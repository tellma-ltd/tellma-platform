// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Jobs.JobRunDatabase;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     Jobs run end to end against a fixture database, one poll at a time through the test-only
    ///     poll driver: the outcomes each handler ending records, the completion transaction that
    ///     carries the handler's statements and enlisted writes with the outcomes or loses them all,
    ///     the completions no handler runs, the stops, the verdicts a flush's renewal reaches, the
    ///     checkpoints, the warnings a job waits on, and the notices and change events that follow a
    ///     commit. The tests run once with read-committed snapshot on and once with it off.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    public abstract partial class JobRunTests(JobRunDatabase fixture) : IAsyncLifetime
    {
        /// <summary>The first shipment id the tests store.</summary>
        private const long FirstShipment = 7_000;

        /// <summary>A job id no job carries.</summary>
        private const int MissingJob = 999_999;

        /// <summary>The raw table the handlers' statements write, as a declared write.</summary>
        private static readonly IReadOnlySet<TableName> RunLogWrites = new HashSet<TableName> { new("test", "RunLog") };

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task The_database_reads_committed_rows_as_the_class_requires()
        {
            Assert.Equal(
                fixture.ReadCommittedSnapshot,
                await SqlServerFixture.ScalarAsync<bool>(fixture.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
        }

        // --- End to end ---

        [Fact]
        public async Task A_fixture_handlers_job_runs_from_its_enqueue_through_one_poll_to_success_and_tells_its_requester()
        {
            int id = Assert.Single(await EnqueueAsync(new JobRequest(FixtureJobKeys.Probe, null, null, ActiveUserId, ActiveUserId, null)));

            PolledJob polled = Assert.Single(await PollAsync(FixtureJobKeys.Probe));

            Assert.Equal(new PolledJob(id, FixtureJobKeys.Probe, PolledJobEnd.Completed, JobStatus.Succeeded, null), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Succeeded), 1, 100, null), (row.Status, row.Attempts, row.ProgressPercent, row.LeaseToken));
            Assert.NotNull(row.CompletedAt);
            Assert.Equal([id], ChangedJobs());

            // --- A second poll finds nothing due ---
            Assert.Empty(await PollAsync(FixtureJobKeys.Probe));
        }

        [Fact]
        public async Task A_handler_reports_progress_flushes_it_and_succeeds_with_its_success_notice_and_one_change_per_write()
        {
            int id = Assert.Single(await EnqueueAsync(new JobRequest(JobRunKeys.Probe, new Pages(3), null, ActiveUserId, ActiveUserId, null)));
            JobRow? flushed = null;
            int pages = 0;
            NotificationRequest shipped = new("test.shipped", [ActiveUserId], null, null, null, null, null);
            fixture.Script.Probe = async (batch, _) =>
            {
                JobItem<Job> item = batch.Single;
                pages = item.Arguments<Pages>().Count;
                item.Progress.Report(40, new JobMessage("Test_Reading"), new Checkpoint(2));
                await item.Progress.FlushAsync(Token);
                flushed = await fixture.JobAsync(id);
                item.NotifyOnSuccess(shipped);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            // --- The flush wrote the report while the job ran ---
            Assert.Equal(3, pages);
            Assert.NotNull(flushed);
            Assert.Equal((nameof(JobStatus.Running), (byte?)40, "Test_Reading", /*lang=json,strict*/ """{"page":2}"""), (flushed.Status, flushed.ProgressPercent, flushed.ProgressKey, flushed.StateJson));

            // --- The completion kept the checkpoint and raised the success notice with the commit ---
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Succeeded), (byte?)100, /*lang=json,strict*/ """{"page":2}"""), (row.Status, row.ProgressPercent, row.StateJson));
            Assert.Equal([shipped], fixture.Notices.Raised);
            Assert.Equal([id, id], ChangedJobs());
        }

        [Fact]
        public async Task A_flush_after_the_final_renewal_extends_the_lease_only_to_the_end_that_renewal_set()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int? remaining = null;
            fixture.Script.Probe = async (batch, context) =>
            {
                // --- The host's final renewal binds the lease to five seconds; the handler then reports and flushes ---
                _ = await context.Services.GetRequiredService<IJobRenewal>().RenewFinalAsync(FixtureTenantId, leaseSeconds: 5, Token);
                batch.Single.Progress.Report(50, null, null);
                await batch.Single.Progress.FlushAsync(Token);
                remaining = await SqlServerFixture.ScalarAsync<int>(
                    fixture.ConnectionString, Invariant($"SELECT DATEDIFF(millisecond, SYSUTCDATETIME(), [LeaseExpiresAt]) FROM [core].[Jobs] WHERE [Id] = {id};"));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            // --- The flush carried the report but left the lease ending within the five seconds, not its own five minutes ---
            Assert.InRange(Assert.NotNull(remaining), 1, 5_000);
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal((byte?)100, (await fixture.JobAsync(id)).ProgressPercent);
        }

        [Fact]
        public async Task A_last_attempt_that_throws_fails_its_jobs_and_raises_the_failure_notice_in_the_inbox_alone()
        {
            int requested = await fixture.InsertJobAsync(JobRunKeys.Inbox);
            int unrequested = await fixture.InsertJobAsync(JobRunKeys.Inbox, requestedBy: null);
            fixture.Script.Inbox = static (_, _) => throw new InvalidOperationException("The test handler breaks.");

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Inbox);

            // --- One attempt allowed: the retry an exception earns fails the jobs instead ---
            Assert.Equal([(requested, JobStatus.Failed, JobErrorCodes.Internal), (unrequested, JobStatus.Failed, JobErrorCodes.Internal)], polled.Select(static job => (job.JobId, job.Status, job.ErrorCode)));
            Assert.Equal(nameof(JobStatus.Failed), (await fixture.JobAsync(requested)).Status);

            // --- The requester is told, else the administrators, in the inbox alone ---
            Assert.Equal(
                [(JobNotices.FailedType, ActiveUserId, true), (JobNotices.FailedType, AdministratorId, true)],
                fixture.Notices.Raised.Select(static notice => (notice.Type, Assert.Single(notice.RecipientIds), notice.InboxOnly)));
            Assert.Equal(new JobFailedArguments(JobRunKeys.Inbox, JobErrorCodes.Internal), fixture.Notices.Raised[0].Arguments);
            Assert.Equal([requested], ChangedJobs());
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobHandlerFailed" && record.Exception is InvalidOperationException);
        }

        [Fact]
        public async Task An_unexpected_exception_retries_with_the_internal_code_and_the_trace_id_of_the_invocation()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            string? traceId = null;
            using ActivityListener listener = new()
            {
                ShouldListenTo = static source => source.Name == JobsTelemetryNames.ActivitySourceName,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);
            fixture.Script.Probe = (_, _) =>
            {
                traceId = Activity.Current?.TraceId.ToHexString();
                throw new InvalidOperationException("The test handler breaks.");
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            // --- The first of three attempts: a retry under the backoff, the error traced to the invocation ---
            Assert.Equal((JobStatus.Pending, JobErrorCodes.Internal), (polled.Status, polled.ErrorCode));
            JobRow row = await fixture.JobAsync(id);
            Assert.NotNull(traceId);
            Assert.Equal((1, JobErrorCodes.Internal, traceId), (row.Attempts, row.ErrorCode, row.ErrorTraceId));
            Assert.Null(row.CompletedAt);
            Assert.Empty(fixture.Notices.Raised);

            // --- The failure's log record carries the trace the row names ---
            FakeLogRecord failed = Assert.Single(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobHandlerFailed");
            Assert.Equal(row.ErrorTraceId, TraceIdOf(failed));
        }

        // --- The completions no handler runs ---

        [Fact]
        public async Task Rows_past_their_last_attempt_cancelled_without_their_entity_or_of_an_inactive_user_complete_without_the_handler()
        {
            int cancelled = await fixture.InsertJobAsync(JobRunKeys.Probe, cancelRequested: true);
            int exhausted = await fixture.InsertJobAsync(JobRunKeys.Probe, attempts: 3);
            int exhaustedWithError = await fixture.InsertJobAsync(JobRunKeys.Probe, attempts: 3, errorCode: "Test.Earlier");
            int inactive = await fixture.InsertJobAsync(JobRunKeys.Probe, runAs: InactiveUserId);
            int missing = await fixture.InsertJobAsync(JobRunKeys.Ship);

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe, JobRunKeys.Ship);

            // --- Each row ends by the first rule that applies; no handler ran ---
            Assert.Empty(fixture.Script.Invocations);
            var ends = polled.ToDictionary(static job => job.JobId, static job => (job.Status, job.ErrorCode));
            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), ends[cancelled]);
            Assert.Equal((JobStatus.Failed, JobErrorCodes.AttemptsExhausted), ends[exhausted]);
            Assert.Equal((JobStatus.Failed, "Test.Earlier"), ends[exhaustedWithError]);
            Assert.Equal((JobStatus.Failed, JobErrorCodes.UserInactive), ends[inactive]);
            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.EntityMissing), ends[missing]);

            // --- The rows as stored: ended, unleased, and the attempt the claim counted kept ---
            JobRow exhaustedRow = await fixture.JobAsync(exhausted);
            Assert.Equal((nameof(JobStatus.Failed), 4, null), (exhaustedRow.Status, exhaustedRow.Attempts, exhaustedRow.LeaseToken));
            Assert.NotNull(exhaustedRow.CompletedAt);
            JobRow missingRow = await fixture.JobAsync(missing);
            Assert.Equal((nameof(JobStatus.Cancelled), JobErrorCodes.EntityMissing), (missingRow.Status, missingRow.ErrorCode));

            // --- Each failure tells the requester; a cancel tells nobody ---
            Assert.Equal(
                [exhausted, exhaustedWithError, inactive],
                fixture.Notices.Raised.Where(static notice => notice.Type == JobNotices.FailedType).Select(static notice => (int)notice.TargetId!.Value).Order());
        }

        [Fact]
        public async Task A_claim_reports_its_jobs_in_claim_order_whichever_completion_ended_them()
        {
            // Two run-as users interleaved, and a row past its last attempt between them.
            int first = await fixture.InsertJobAsync(JobRunKeys.Probe, runAs: ActiveUserId);
            int second = await fixture.InsertJobAsync(JobRunKeys.Probe, runAs: AdministratorId);
            int exhausted = await fixture.InsertJobAsync(JobRunKeys.Probe, attempts: 3);
            int fourth = await fixture.InsertJobAsync(JobRunKeys.Probe, runAs: ActiveUserId);
            List<(int JobId, PrincipalKind Kind, int? ScopeUser)> ran = [];
            fixture.Script.Probe = (batch, context) =>
            {
                RequestContext scope = context.Services.GetRequiredService<IRequestContextAccessor>().Current;
                ran.AddRange(batch.Items.Select(item => (item.Job.Id, scope.Kind, scope.UserId)));
                return Task.CompletedTask;
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);

            Assert.Equal(
                [(first, JobStatus.Succeeded), (second, JobStatus.Succeeded), (exhausted, JobStatus.Failed), (fourth, JobStatus.Succeeded)],
                polled.Select(static job => (job.JobId, job.Status)));

            // --- Each partition ran in a scope of its own run-as user, in the order of its first row ---
            Assert.Equal<(int, PrincipalKind, int?)>(
                [(first, PrincipalKind.User, ActiveUserId), (fourth, PrincipalKind.User, ActiveUserId), (second, PrincipalKind.User, AdministratorId)],
                ran);
        }

        [Fact]
        public async Task Jobs_of_one_user_that_carry_different_consent_run_in_scopes_of_their_own()
        {
            int confirmed = await fixture.InsertJobAsync(JobRunKeys.Probe, confirmedKeys: JobRunKeys.Backdated);
            int unconfirmed = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = static async (batch, context) =>
            {
                context.Host.Confirmations.Require(JobRunKeys.Backdated, [1]);
                _ = await context.Host.PersistAsync(Token);
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);

            // --- The consent one job carries passes its warning and never covers its neighbour's ---
            Assert.Equal([[confirmed], [unconfirmed]], fixture.Script.Invocations.Select(static invocation => invocation.JobIds));
            Assert.Equal([(confirmed, JobStatus.Succeeded), (unconfirmed, JobStatus.PendingConfirmation)], polled.Select(static job => (job.JobId, job.Status)));
        }

        // --- The completion transaction ---

        [Fact]
        public async Task An_enlisted_write_and_the_handlers_statement_commit_with_the_outcomes()
        {
            (int first, int second) = (await ShipmentJobAsync(0, "stored-0"), await ShipmentJobAsync(1, "stored-1"));
            fixture.Script.Ship = static async (batch, context, writer) =>
            {
                foreach (JobItem<Shipment> item in batch.Items)
                {
                    item.Item.Payload = $"shipped-{item.Job.Id}";
                    Note(batch.Batch, item.Job.Id, "shipped");
                }

                _ = await writer.EnlistSaveAsync(context.Host, [.. batch.Items.Select(static item => item.Item)]);
                _ = await context.Host.PersistAsync(Token);
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Ship);

            Assert.Equal([(first, JobStatus.Succeeded), (second, JobStatus.Succeeded)], polled.Select(static job => (job.JobId, job.Status)));
            Assert.Equal([$"shipped-{first}", $"shipped-{second}"], await PayloadsAsync());
            Assert.Equal(["shipped", "shipped"], await fixture.RunLogAsync());
            Assert.Equal([first, second], ChangedJobs());
        }

        [Fact]
        public async Task A_handler_receives_each_owning_row_with_every_column_as_stored()
        {
            int id = await ShipmentJobAsync(0, "stored-0");
            List<(int? JobId, string? Payload)> seen = [];
            fixture.Script.Ship = (batch, _, _) =>
            {
                seen.AddRange(batch.Items.Select(static item => (item.Item.JobId, item.Item.Payload)));
                return Task.CompletedTask;
            };

            _ = Assert.Single(await PollAsync(JobRunKeys.Ship));

            Assert.Equal([(id, "stored-0")], seen);
        }

        [Fact]
        public async Task A_lease_taken_by_another_instance_rolls_back_the_handlers_statements_and_enlisted_writes_and_records_nothing()
        {
            int id = await ShipmentJobAsync(0, "stored-0");
            var other = Guid.NewGuid();
            fixture.Script.Ship = async (batch, context, writer) =>
            {
                JobItem<Shipment> item = batch.Single;
                item.Item.Payload = "shipped";
                Note(batch.Batch, item.Job.Id, "shipped");
                _ = await writer.EnlistSaveAsync(context.Host, [item.Item]);
                _ = await context.Host.PersistAsync(Token);

                // Another instance takes the row once this one's lease lapsed.
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseToken] = '{other}' WHERE [Id] = {id};"));
            };
            using JobInstruments instruments = new(fixture);

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Ship));

            Assert.Equal(new PolledJob(id, JobRunKeys.Ship, PolledJobEnd.LostLease, null, null), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Running), (Guid?)other, 1), (row.Status, row.LeaseToken, row.Attempts));
            Assert.Equal(["stored-0"], await PayloadsAsync());
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Empty(fixture.Events.Published);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobLeaseLost");

            // --- The lost batch is counted once, under its handler; nothing completed ---
            Assert.Equal([(JobRunKeys.Ship, 1L)], JobInstruments.ByHandler(instruments.LeaseLost));
            Assert.Empty(instruments.Completed.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_constraint_an_enlisted_write_breaks_fails_every_item_with_its_translated_error_and_writes_nothing()
        {
            (int first, int second) = (await ShipmentJobAsync(0, "stored-0"), await ShipmentJobAsync(1, "stored-1"));
            fixture.Script.Ship = static async (batch, context, writer) =>
            {
                // A new shipment names a job that does not exist; the database refuses it. An update
                // never writes a server-owned member, so the reference rides an insert.
                Note(batch.Batch, batch.Items[0].Job.Id, "shipped");
                _ = await writer.EnlistSaveAsync(
                    context.Host,
                    [new Shipment { Payload = "orphan", JobId = MissingJob }],
                    new EnlistSaveOptions { ServerOwned = new HashSet<string>(StringComparer.Ordinal) { nameof(Shipment.JobId) } });
                _ = await context.Host.PersistAsync(Token);
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Ship);

            // --- Both items fail at once with the translated first error; the handler's work is gone ---
            Assert.Equal([(first, JobStatus.Failed, ValidationCodes.FkNotFound), (second, JobStatus.Failed, ValidationCodes.FkNotFound)], polled.Select(static job => (job.JobId, job.Status, job.ErrorCode)));
            Assert.Equal(["stored-0", "stored-1"], await PayloadsAsync());
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Equal(2, fixture.Notices.Raised.Count(static notice => notice.Type == JobNotices.FailedType));
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobCompletionFailed");
        }

        [Fact]
        public async Task A_guard_that_fails_at_the_completion_retries_every_item_at_once_without_the_handlers_statements()
        {
            int first = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int second = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = async (batch, _) =>
            {
                foreach (JobItem<Job> item in batch.Items)
                {
                    Note(batch.Batch, item.Job.Id, "probed");
                }

                // The tenant's permissions move while the handler runs.
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'permissions';");
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);

            Assert.Equal([(first, JobStatus.Pending, null), (second, JobStatus.Pending, null)], polled.Select(static job => (job.JobId, job.Status, job.ErrorCode)));
            JobRow row = await fixture.JobAsync(first);
            Assert.Equal((1, null, null), (row.Attempts, row.ErrorCode, row.LeaseToken));
            Assert.InRange(row.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Empty(await fixture.RunLogAsync());
        }

        [Fact]
        public async Task A_tag_the_transactions_recheck_finds_moved_retries_every_item_at_once_without_the_handlers_statements()
        {
            int first = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int second = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = static (batch, context) =>
            {
                foreach (JobItem<Job> item in batch.Items)
                {
                    Note(batch.Batch, item.Job.Id, "probed");
                }

                // The work rests on a settings tag the tenant has moved since: the prologue's guard
                // passes, and the transaction's re-check of the batch's dependencies refuses the persist.
                batch.Batch.DependsOn([new VersionTagDependency(VersionTagNames.Settings, new VersionTag(Guid.NewGuid()), VersionTagMismatchPolicy.Rerun)]);
                return Task.CompletedTask;
            };
            using MetricCollector<long> stale = new(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.Stale);

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);

            Assert.Equal([(first, JobStatus.Pending, null), (second, JobStatus.Pending, null)], polled.Select(static job => (job.JobId, job.Status, job.ErrorCode)));
            JobRow row = await fixture.JobAsync(first);
            Assert.Equal((1, null, null), (row.Attempts, row.ErrorCode, row.LeaseToken));
            Assert.InRange(row.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Empty(await fixture.RunLogAsync());

            // --- The refusal came from the transaction's re-check, not from the prologue's guard ---
            CollectedMeasurement<long> measured = Assert.Single(stale.GetMeasurementSnapshot());
            Assert.Equal("write-guard", measured.Tags[CacheTelemetryNames.StalePhaseTag]);
        }

        [Fact]
        public async Task A_completion_whose_fresh_batch_fails_too_records_nothing_and_leaves_the_lease_to_lapse()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Notices.Refuses = true;
            fixture.Script.Probe = (batch, _) =>
            {
                Note(batch.Batch, id, "probed");
                throw new JobFailedException(new JobError("Test.Broken"));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            // --- The failure's notice can be appended neither to the composed batch nor to the fresh one ---
            Assert.Equal(new PolledJob(id, JobRunKeys.Probe, PolledJobEnd.Lapsed, null, null), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Running), 1, null), (row.Status, row.Attempts, row.ErrorCode));
            Assert.NotNull(row.LeaseToken);
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Empty(fixture.Events.Published);
            Assert.Equal(2, fixture.Log.GetSnapshot().Count(static record => record.Id.Name == "JobCompletionFailed"));
        }

        [Fact]
        public async Task A_handler_that_executes_its_invocations_batch_is_refused_and_the_worker_sends_it_with_the_outcomes()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            InvalidOperationException? refused = null;
            fixture.Script.Probe = async (batch, _) =>
            {
                Note(batch.Batch, id, "probed");
                refused = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.Batch.ExecuteAsync(Token));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.NotNull(refused);
            Assert.Contains("sent by the worker", refused.Message, StringComparison.Ordinal);
            Assert.Equal(new PolledJob(id, JobRunKeys.Probe, PolledJobEnd.Completed, JobStatus.Succeeded, null), polled);
            Assert.Equal(["probed"], await fixture.RunLogAsync());
        }

        [Fact]
        public async Task A_caller_statement_writing_the_job_table_is_refused_while_the_completions_own_write_passes()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            InvalidOperationException? refused = null;
            fixture.Script.Probe = (batch, _) =>
            {
                // The test writes the job table from a type that owns no stack, deliberately, as a
                // handler's own statement would; the batch refuses it at append.
#pragma warning disable TELLMA0006
                refused = Assert.Throws<InvalidOperationException>(() => batch.Batch.Sql(
                    $"UPDATE [core].[Jobs] SET [LabelKey] = NULL WHERE [Id] = {id};",
                    new SqlOptions(writes: new HashSet<TableName> { JobStatements.Jobs })));
#pragma warning restore TELLMA0006
                return Task.CompletedTask;
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.NotNull(refused);
            Assert.Contains("owns", refused.Message, StringComparison.Ordinal);
            Assert.Equal(JobStatus.Succeeded, polled.Status);
        }

        [Fact]
        public async Task A_run_as_user_deactivated_while_the_handler_runs_fails_the_jobs_as_inactive_without_the_work_and_keeps_the_checkpoint()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe, runAs: DeactivatedUserId);
            fixture.Script.Probe = async (batch, _) =>
            {
                JobItem<Job> item = batch.Single;
                item.Progress.Report(30, null, new Checkpoint(3));
                Note(batch.Batch, id, "probed");

                // The user leaves the tenant while the handler runs; the completion's connect finds them inactive.
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {DeactivatedUserId};"));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            _ = Assert.Single(fixture.Script.Invocations);
            Assert.Equal(new PolledJob(id, JobRunKeys.Probe, PolledJobEnd.Completed, JobStatus.Failed, JobErrorCodes.UserInactive), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Failed), JobErrorCodes.UserInactive, /*lang=json,strict*/ """{"page":3}""", null), (row.Status, row.ErrorCode, row.StateJson, row.LeaseToken));
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Equal(
                [(JobNotices.FailedType, ActiveUserId)],
                fixture.Notices.Raised.Select(static notice => (notice.Type, Assert.Single(notice.RecipientIds))));
        }

        [Fact]
        public async Task A_handler_statement_failing_with_no_translation_retries_with_the_internal_code_and_writes_nothing()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = (batch, context) =>
            {
                Note(batch.Batch, id, "probed");
                _ = batch.Batch.Sql($"DECLARE @zero int = 0, @broken int; SET @broken = 1 / @zero;", SqlOptions.None);
                return Task.CompletedTask;
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            // --- The work is gone, and the outcomes alone record a retry of the unexpected failure ---
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Pending, JobErrorCodes.Internal), (polled.End, polled.Status, polled.ErrorCode));
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Pending), 1, JobErrorCodes.Internal, null), (row.Status, row.Attempts, row.ErrorCode, row.LeaseToken));
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobCompletionFailed");
        }

        // --- Releases and stops ---

        [Fact]
        public async Task A_host_already_stopping_releases_the_claimed_rows_uncounted_without_running_the_handler()
        {
            int first = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int second = await fixture.InsertJobAsync(JobRunKeys.Probe);
            using CancellationTokenSource host = new();
            await host.CancelAsync();

            IReadOnlyList<PolledJob> polled = await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], host.Token, Token);

            Assert.Equal([(first, JobStatus.Pending), (second, JobStatus.Pending)], polled.Select(static job => (job.JobId, job.Status)));
            Assert.Empty(fixture.Script.Invocations);
            JobRow row = await fixture.JobAsync(first);
            Assert.Equal((0, null, null, null), (row.Attempts, row.LeaseToken, row.ErrorCode, row.StateJson));
            Assert.Empty(fixture.Notices.Raised);
        }

        [Fact]
        public async Task A_stop_while_the_handler_runs_releases_an_untouched_item_and_queues_an_observed_one_again_at_once()
        {
            int observed = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int untouched = await fixture.InsertJobAsync(JobRunKeys.Probe);
            using CancellationTokenSource host = new();
            fixture.Script.Probe = async (batch, _) =>
            {
                batch.Items[0].Progress.Report(10, null, new Checkpoint(1));
                await host.CancelAsync();
                batch.Items[0].CancellationToken.ThrowIfCancellationRequested();
            };

            IReadOnlyList<PolledJob> polled = await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], host.Token, Token);

            Assert.Equal([(observed, JobStatus.Pending), (untouched, JobStatus.Pending)], polled.Select(static job => (job.JobId, job.Status)));
            JobRow retried = await fixture.JobAsync(observed);
            Assert.Equal((1, /*lang=json,strict*/ """{"page":1}""", null), (retried.Attempts, retried.StateJson, retried.ErrorCode));
            Assert.Equal(0, (await fixture.JobAsync(untouched)).Attempts);
        }

        // --- Renewals ---

        [Fact]
        public async Task A_flush_that_finds_the_row_claimed_elsewhere_stops_the_item_and_the_scope_and_records_nothing()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            (bool Cancelled, JobCancellationReason Reason)? seen = null;
            Exception? scopeStatement = null;
            fixture.Script.Probe = async (batch, context) =>
            {
                JobItem<Job> item = batch.Single;
                Note(batch.Batch, id, "probed");

                // Another instance takes the row once this one's lease lapsed; the flush's renewal finds it gone.
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseToken] = NEWID() WHERE [Id] = {id};"));
                await item.Progress.FlushAsync(Token);
                seen = (item.CancellationToken.IsCancellationRequested, item.CancellationReason);

                // The scope stops with the lease: its next statement is cancelled before it is sent.
                IDataBatch chunk = context.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                Note(chunk, id, "chunk");
                scopeStatement = await Record.ExceptionAsync(() => chunk.ExecuteAsync(Token));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal((true, JobCancellationReason.LeaseLost), seen);
            _ = Assert.IsAssignableFrom<OperationCanceledException>(scopeStatement);
            Assert.Equal(new PolledJob(id, JobRunKeys.Probe, PolledJobEnd.LostLease, null, null), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Running), 1, null), (row.Status, row.Attempts, row.ErrorCode));
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Empty(fixture.Events.Published);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobLeaseLost");
        }

        [Fact]
        public async Task A_flush_that_finds_a_cancel_requested_stops_the_item_alone_and_the_job_ends_cancelled()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            (bool Cancelled, JobCancellationReason Reason)? seen = null;
            fixture.Script.Probe = async (batch, context) =>
            {
                JobItem<Job> item = batch.Single;
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [CancelRequestedAt] = SYSUTCDATETIME() WHERE [Id] = {id};"));
                await item.Progress.FlushAsync(Token);
                seen = (item.CancellationToken.IsCancellationRequested, item.CancellationReason);

                // The scope still runs: the handler's own chunk commits before it honours the stop.
                IDataBatch chunk = context.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                Note(chunk, id, "chunk");
                _ = await chunk.ExecuteAsync(Token);
                item.CancellationToken.ThrowIfCancellationRequested();
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal((true, JobCancellationReason.CancelRequested), seen);
            Assert.Equal(["chunk"], await fixture.RunLogAsync());
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Cancelled, JobErrorCodes.Cancelled), (polled.End, polled.Status, polled.ErrorCode));
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Cancelled), null), (row.Status, row.LeaseToken));
            Assert.Empty(fixture.Notices.Raised);
        }

        // --- Checkpoints ---

        [Fact]
        public async Task A_resumed_item_reads_the_checkpoint_its_claim_returned_and_the_completion_keeps_it()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe, attempts: 1, stateJson: /*lang=json,strict*/ """{"page":7}""");
            int? resumedAt = null;
            fixture.Script.Probe = (batch, _) =>
            {
                resumedAt = batch.Single.State<Checkpoint>()?.Page;
                return Task.CompletedTask;
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal(7, resumedAt);
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((2, /*lang=json,strict*/ """{"page":7}"""), (row.Attempts, row.StateJson));
        }

        [Fact]
        public async Task An_appended_checkpoint_counts_and_tells_the_requester_only_once_its_chunk_committed()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe, stateJson: /*lang=json,strict*/ """{"page":1}""");
            List<int?> seen = [];
            int changesBeforeRollback = -1;
            fixture.Script.Probe = async (batch, context) =>
            {
                JobItem<Job> item = batch.Single;
                ITenantDatabase database = context.Services.GetRequiredService<ITenantDatabase>();

                // --- The first chunk commits with its checkpoint ---
                IDataBatch first = database.CreateBatch(BatchPurpose.Persist);
                Note(first, id, "chunk-1");
                item.Progress.Append(first, 50, null, new Checkpoint(2));
                seen.Add(item.State<Checkpoint>()?.Page);
                _ = await first.ExecuteAsync(Token);
                seen.Add(item.State<Checkpoint>()?.Page);
                changesBeforeRollback = fixture.Events.Published.Count;

                // --- The second chunk breaks, and its checkpoint rolls back with it ---
                IDataBatch second = database.CreateBatch(BatchPurpose.Persist);
                Note(second, id, "chunk-2");
                item.Progress.Append(second, 75, null, new Checkpoint(3));
                _ = second.Sql($"DECLARE @zero int = 0, @broken int; SET @broken = 1 / @zero;", SqlOptions.None);
                _ = await Assert.ThrowsAnyAsync<Exception>(() => second.ExecuteAsync(Token));
                seen.Add(item.State<Checkpoint>()?.Page);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal([1, 2, 2], seen);
            Assert.Equal(1, changesBeforeRollback);
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal(/*lang=json,strict*/ """{"page":2}""", (await fixture.JobAsync(id)).StateJson);
            Assert.Equal(["chunk-1"], await fixture.RunLogAsync());
            Assert.Equal([id, id], ChangedJobs());
        }

        [Fact]
        public async Task A_checkpoint_appended_under_a_lost_lease_rolls_its_chunk_back()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            Exception? refused = null;
            fixture.Script.Probe = async (batch, context) =>
            {
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseToken] = NEWID() WHERE [Id] = {id};"));
                IDataBatch chunk = context.Services.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                Note(chunk, id, "chunk-1");
                batch.Single.Progress.Append(chunk, 50, null, new Checkpoint(2));
                refused = await Record.ExceptionAsync(() => chunk.ExecuteAsync(Token));
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.True(refused is not null && JobCompletionFailures.IsLeaseLost(refused), $"The chunk fails on the fence: {refused}");
            Assert.Equal(PolledJobEnd.LostLease, polled.End);
            Assert.Empty(await fixture.RunLogAsync());
            Assert.Empty(fixture.Events.Published);
            Assert.Null((await fixture.JobAsync(id)).StateJson);
        }

        [Fact]
        public async Task A_checkpoint_appended_to_the_completion_batch_is_refused_and_a_reported_one_completes_with_the_job()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            ArgumentException? refused = null;
            fixture.Script.Probe = (batch, _) =>
            {
                JobItem<Job> item = batch.Single;
                Note(batch.Batch, id, "probed");
                refused = Assert.Throws<ArgumentException>(() => item.Progress.Append(batch.Batch, 100, null, new Checkpoint(4)));

                // The final checkpoint is reported instead; the completion records it.
                item.Progress.Report(100, null, new Checkpoint(4));
                return Task.CompletedTask;
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.NotNull(refused);
            Assert.Equal("batch", refused.ParamName);
            Assert.Equal(new PolledJob(id, JobRunKeys.Probe, PolledJobEnd.Completed, JobStatus.Succeeded, null), polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Succeeded), /*lang=json,strict*/ """{"page":4}"""), (row.Status, row.StateJson));
            Assert.Equal(["probed"], await fixture.RunLogAsync());
        }

        // --- Warnings ---

        [Fact]
        public async Task A_warning_the_enqueuing_scope_confirmed_is_stored_with_the_job_and_passes_its_run()
        {
            Shipment shipment = new() { Id = FirstShipment };
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"INSERT INTO [fixture].[Shipments] ([Id], [State], [Payload], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({FirstShipment}, 'Pending', N'stored-0', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);"));

            // --- The user's scope confirmed the warning, and enqueues ---
            TenantScope scope = await fixture.Tenant.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(
                JobScopes.RunAs(FixtureTenant.TenantId, ActiveUserId, traceParent: null), cancellationToken: Token);
            int id;
            await using (scope)
            {
                scope.Services.GetRequiredService<Confirmations>().Seed([JobRunKeys.Backdated]);
                id = Assert.Single(await scope.Services.GetRequiredService<IJobQueue>().EnqueueAsync([new JobRequest(JobRunKeys.Ship, null, null, ActiveUserId, null, shipment)], Token));
            }

            Assert.Equal(JobRunKeys.Backdated, (await fixture.JobAsync(id)).ConfirmedKeys);
            fixture.Script.Ship = static async (batch, context, writer) =>
            {
                batch.Single.Item.Payload = "backdated";
                _ = await writer.EnlistSaveAsync(context.Host, [batch.Single.Item]);
                context.Host.Confirmations.Require(JobRunKeys.Backdated, [batch.Single.Item.Id]);
                _ = await context.Host.PersistAsync(Token);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Ship));

            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal(["backdated"], await PayloadsAsync());
        }

        [Fact]
        public async Task An_unconfirmed_warning_leaves_its_jobs_waiting_with_their_checkpoint_and_tells_the_requester_or_the_administrators()
        {
            int requested = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int unrequested = await fixture.InsertJobAsync(JobRunKeys.Probe, requestedBy: null);
            fixture.Script.Probe = static async (batch, context) =>
            {
                foreach (JobItem<Job> item in batch.Items)
                {
                    item.Progress.Report(null, null, new Checkpoint(4));
                }

                context.Host.Confirmations.Require(JobRunKeys.Backdated, [1, 2], new Dictionary<string, object?> { ["date"] = "2026-01-31" });
                context.Host.Confirmations.Require(JobRunKeys.Negative, [3]);
                _ = await context.Host.PersistAsync(Token);
            };

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);

            // --- Waiting, without an error, the checkpoint kept and due at once ---
            Assert.Equal([JobStatus.PendingConfirmation, JobStatus.PendingConfirmation], polled.Select(static job => job.Status));
            JobRow row = await fixture.JobAsync(requested);
            Assert.Equal((nameof(JobStatus.PendingConfirmation), null, null, null, /*lang=json,strict*/ """{"page":4}"""), (row.Status, row.ErrorCode, row.ErrorArgumentsJson, row.ErrorTraceId, row.StateJson));
            Assert.InRange(row.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

            // --- Every warning and the total count ---
            using var pending = JsonDocument.Parse(row.PendingConfirmationsJson!);
            Assert.Equal(2, pending.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(
                [JobRunKeys.Backdated, JobRunKeys.Negative],
                pending.RootElement.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("key").GetString()));

            // --- The notice, once per job, to the requester or to the administrators ---
            Assert.Equal(
                [(JobNotices.PendingConfirmationType, ActiveUserId), (JobNotices.PendingConfirmationType, AdministratorId)],
                fixture.Notices.Raised.Select(static notice => (notice.Type, Assert.Single(notice.RecipientIds))));
            Assert.Equal(new JobPendingConfirmationArguments(JobRunKeys.Probe, 2), fixture.Notices.Raised[0].Arguments);
            Assert.Equal(Invariant($"{JobNotices.PendingConfirmationType}:{unrequested}"), fixture.Notices.Raised[1].DedupKey);
        }

        [Fact]
        public async Task Seventeen_distinct_warnings_fail_the_job_as_too_many_to_confirm()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = static async (batch, context) =>
            {
                foreach (string key in JobRunKeys.Numbered)
                {
                    context.Host.Confirmations.Require(key, [1]);
                }

                _ = await context.Host.PersistAsync(Token);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal((JobStatus.Failed, JobErrorCodes.TooManyConfirmations), (polled.Status, polled.ErrorCode));
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((/*lang=json,strict*/ """{"count":17}""", null), (row.ErrorArgumentsJson, row.PendingConfirmationsJson));
        }

        [Fact]
        public async Task A_warning_that_would_take_the_confirmed_keys_past_their_column_fails_the_job()
        {
            // The stored keys take 1,994 characters; one more key takes them past 2,000.
            string stored = string.Join(',', Enumerable.Range(0, 133).Select(static index => string.Create(CultureInfo.InvariantCulture, $"Test.Filler{index:D3}")));
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe, confirmedKeys: stored);
            fixture.Script.Probe = static async (batch, context) =>
            {
                context.Host.Confirmations.Require(JobRunKeys.Negative, [1]);
                _ = await context.Host.PersistAsync(Token);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal(1_994, stored.Length);
            Assert.Equal((JobStatus.Failed, JobErrorCodes.TooManyConfirmations), (polled.Status, polled.ErrorCode));
            Assert.Equal(/*lang=json,strict*/ """{"count":1}""", (await fixture.JobAsync(id)).ErrorArgumentsJson);
        }

        [Fact]
        public async Task Warnings_past_sixty_four_kilobytes_keep_the_first_of_every_key_and_the_total_count()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            fixture.Script.Probe = static async (batch, context) =>
            {
                // Sixty warnings of about 2 KB each take the record to some 120 KB.
                Dictionary<string, object?> bulky = new(StringComparer.Ordinal) { ["text"] = new string('x', 2_000) };
                for (int index = 0; index < 60; index++)
                {
                    context.Host.Confirmations.Require(JobRunKeys.Backdated, [index], bulky);
                }

                context.Host.Confirmations.Require(JobRunKeys.Negative, [1], bulky);
                _ = await context.Host.PersistAsync(Token);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Probe));

            Assert.Equal(JobStatus.PendingConfirmation, polled.Status);
            string record = (await fixture.JobAsync(id)).PendingConfirmationsJson!;
            Assert.InRange(Encoding.UTF8.GetByteCount(record), 0, JobLimits.MaxPendingConfirmationsBytes);
            using var pending = JsonDocument.Parse(record);
            Assert.Equal(61, pending.RootElement.GetProperty("count").GetInt32());
            string?[] keys = [.. pending.RootElement.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("key").GetString())];
            Assert.True(keys.Length < 61);
            Assert.Equal((JobRunKeys.Backdated, JobRunKeys.Negative), (keys[0], keys[^1]));
        }

        [Fact]
        public async Task A_cancelled_row_of_a_handler_that_commits_in_steps_runs_with_its_token_cancelled_records_its_stop_and_ends_cancelled()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Steps, attempts: 1, stateJson: /*lang=json,strict*/ """{"page":5}""", cancelRequested: true);
            await fixture.InsertShipmentAsync(FirstShipment, id, "stored-0");
            (bool Cancelled, JobCancellationReason Reason)? seen = null;
            fixture.Script.Steps = async (batch, context, writer) =>
            {
                JobItem<Shipment> item = batch.Single;
                seen = (item.CancellationToken.IsCancellationRequested, item.CancellationReason);

                // The stop is recorded on the owning row, under the scope's own token, which still runs.
                item.Item.Payload = "stopped";
                _ = await writer.EnlistSaveAsync(context.Host, [item.Item]);
                _ = await context.Host.PersistAsync(Token);
            };

            PolledJob polled = Assert.Single(await PollAsync(JobRunKeys.Steps));

            Assert.Equal((true, JobCancellationReason.CancelRequested), seen);
            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), (polled.Status, polled.ErrorCode));
            Assert.Equal(["stopped"], await PayloadsAsync());
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Cancelled), /*lang=json,strict*/ """{"page":5}"""), (row.Status, row.StateJson));
        }

        /// <summary>Enqueues jobs in a system scope.</summary>
        /// <param name="requests">The requests.</param>
        /// <returns>The new jobs' ids.</returns>
        private async Task<IReadOnlyList<int>> EnqueueAsync(params JobRequest[] requests)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            return await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(requests, Token);
        }

        /// <summary>Runs one poll of the given handlers.</summary>
        /// <param name="keys">The handler keys.</param>
        /// <returns>The polled jobs.</returns>
        private Task<IReadOnlyList<PolledJob>> PollAsync(params string[] keys)
        {
            return JobPollDriver.PollOnceAsync(fixture.Tenant, keys, cancellationToken: Token);
        }

        /// <summary>Stores a job of the shipment handler and the shipment that owns it.</summary>
        /// <param name="offset">The shipment's offset past the first id.</param>
        /// <param name="payload">The shipment's payload.</param>
        /// <returns>The job's id.</returns>
        private async Task<int> ShipmentJobAsync(int offset, string payload)
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Ship, dueSecondsAgo: 30 - offset);
            await fixture.InsertShipmentAsync(FirstShipment + offset, id, payload);
            return id;
        }

        /// <summary>The shipments' payloads, in id order.</summary>
        /// <returns>The payloads.</returns>
        private async Task<IReadOnlyList<string?>> PayloadsAsync()
        {
            List<string?> payloads = [];
            await using Microsoft.Data.SqlClient.SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Payload] FROM [fixture].[Shipments] ORDER BY [Id];";
            await using Microsoft.Data.SqlClient.SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                payloads.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
            }

            return payloads;
        }

        /// <summary>Appends a handler's own statement: a note in the raw table.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="jobId">The job the note is for.</param>
        /// <param name="note">The note.</param>
        private static void Note(IDataBatch batch, int jobId, string note)
        {
            _ = batch.Sql($"INSERT INTO [test].[RunLog] ([JobId], [Note]) VALUES ({jobId}, {note});", new SqlOptions(writes: RunLogWrites));
        }

        /// <summary>The jobs the change events published so far name, in order, each event checked to reach the requester alone.</summary>
        /// <returns>The jobs' ids.</returns>
        private IReadOnlyList<int> ChangedJobs()
        {
            return [.. fixture.Events.Published.Select(static published =>
            {
                Assert.Equal((JobNotices.ChangedEvent, ActiveUserId), (published.Name, Assert.Single(published.UserIds)));
                return Assert.IsType<JobChangedPayload>(published.Payload).JobId;
            })];
        }

        /// <summary>The trace id a log record's scopes carry, as the logger factory adds it from the current activity.</summary>
        /// <param name="record">The record.</param>
        /// <returns>The trace id; null when the record was logged outside any activity.</returns>
        private static string? TraceIdOf(FakeLogRecord record)
        {
            return record.Scopes
                .OfType<IEnumerable<KeyValuePair<string, object?>>>()
                .SelectMany(static scope => scope)
                .Where(static pair => pair.Key == "TraceId")
                .Select(static pair => pair.Value?.ToString())
                .FirstOrDefault();
        }

        /// <summary>Formats a string with the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The string.</returns>
        private static string Invariant(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }

        /// <summary>A probe job's arguments.</summary>
        /// <param name="Count">The pages to read.</param>
        private sealed record Pages(int Count);

        /// <summary>A handler's checkpoint.</summary>
        /// <param name="Page">The page reached.</param>
        private sealed record Checkpoint(int Page);
    }

    /// <summary>Jobs run with read-committed snapshot on, as provisioning creates a tenant database.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class JobRunSnapshotTests(JobRunDatabase fixture) : JobRunTests(fixture), IClassFixture<JobRunDatabase>;

    /// <summary>Jobs run with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class JobRunLockingTests(LockingJobRunDatabase fixture) : JobRunTests(fixture), IClassFixture<LockingJobRunDatabase>;
}
