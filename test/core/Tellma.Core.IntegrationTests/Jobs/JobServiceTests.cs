// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Jobs.JobRunDatabase;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     The jobs' stack and the platform's maintenance handlers against a fixture database. The
    ///     actions, through a stepped-up user's scope: each precondition and its repeat, the three
    ///     kinds of cancel and the next claim of a re-queued row, the withdrawals of the paused, gap
    ///     and confirmation notices, the change event to each requester, the confirmation's
    ///     requester and keys, the self-scope of a member without a grant, a read-only tenant, and
    ///     a job's projection read from the row that owns it. The handlers, through real polls: the
    ///     retention's pages, its stop and the keys that lose a deleted job, and the tree verify.
    ///     The tests run once with read-committed snapshot on and once with it off.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    public abstract class JobServiceTests(JobRunDatabase fixture) : IAsyncLifetime
    {
        /// <summary>The test's cancellation.</summary>
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

        // --- Retry ---

        [Fact]
        public async Task A_retry_runs_a_failed_job_again_from_its_first_attempt_tells_its_requester_and_refuses_its_repeat()
        {
            int failed = await InsertJobAsync(JobRunKeys.Probe, attempts: 3);
            await fixture.SetJobAsync(failed, "[Status] = 'Failed', [CompletedAt] = SYSUTCDATETIME(), [ErrorCode] = N'Jobs.Internal', [ErrorTraceId] = 'trace', [DueAt] = DATEADD(day, -1, SYSUTCDATETIME())");

            await ActAsync(AdministratorId, JobService.RetryAction, [failed]);

            // --- Pending, due now, its attempts and error gone; its requester told ---
            JobRow row = await fixture.JobAsync(failed);
            Assert.Equal((nameof(JobStatus.Pending), 0, null, null, null), (row.Status, row.Attempts, row.ErrorCode, row.ErrorTraceId, row.CompletedAt));
            Assert.InRange(row.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Equal([Changed(failed, ActiveUserId)], Events());
            Assert.Equal(1, fixture.Wakes);

            // --- The repeat finds a job that has not failed ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(AdministratorId, JobService.RetryAction, [failed]));
            Assert.Equal(JobValidationCodes.NotRetryable, Assert.Single(refused.Errors).Code);
        }

        // --- Cancel ---

        [Fact]
        public async Task A_cancel_flags_a_running_job_re_queues_a_checkpointed_or_waiting_job_and_cancels_any_other_outright()
        {
            int running = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(running, "[Status] = 'Running', [LeaseToken] = NEWID(), [LeaseExpiresAt] = DATEADD(minute, 5, SYSUTCDATETIME()), [LeaseOwner] = 'elsewhere'");
            int checkpointed = await InsertJobAsync(JobRunKeys.Probe, stateJson: /*lang=json,strict*/ """{"page":2}""", dueSecondsAgo: -3_600);
            int heldCheckpointed = await InsertJobAsync(JobRunKeys.Probe, stateJson: /*lang=json,strict*/ """{"page":4}""");
            await fixture.SetJobAsync(heldCheckpointed, "[Status] = 'Held'");
            int waiting = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(waiting, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated))}");
            int pending = await InsertJobAsync(JobRunKeys.Probe);
            int held = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(held, "[Status] = 'Held'");

            await ActAsync(AdministratorId, JobService.CancelAction, [running, checkpointed, heldCheckpointed, waiting, pending, held]);

            // --- F: the running job is flagged and stops at its next renewal ---
            JobRow flagged = await fixture.JobAsync(running);
            Assert.Equal((nameof(JobStatus.Running), true, null), (flagged.Status, flagged.Columns["CancelRequestedAt"] is not null, flagged.CompletedAt));

            // --- Q: each checkpointed or waiting job is pending again, due now and flagged, so its next claim records the stop ---
            foreach (int id in new[] { checkpointed, heldCheckpointed, waiting })
            {
                JobRow queued = await fixture.JobAsync(id);
                Assert.Equal((nameof(JobStatus.Pending), true, null, null), (queued.Status, queued.Columns["CancelRequestedAt"] is not null, queued.PendingConfirmationsJson, queued.CompletedAt));
                Assert.InRange(queued.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            }

            // --- C: any other is cancelled at once ---
            foreach (int id in new[] { pending, held })
            {
                JobRow cancelled = await fixture.JobAsync(id);
                Assert.Equal((nameof(JobStatus.Cancelled), JobErrorCodes.Cancelled, true), (cancelled.Status, cancelled.ErrorCode, cancelled.CompletedAt is not null));
            }

            // --- The waiting job's notice goes; the last held jobs' notices go, and the gap with them; every requester is told ---
            Assert.Equal(
                [
                    new WithdrawRequest(JobNotices.HeldType, [ActiveUserId]),
                    new WithdrawRequest(JobNotices.GapType),
                    new WithdrawRequest(JobNotices.PendingConfirmationKey(waiting)),
                ],
                fixture.Notices.Withdrawn,
                WithdrawalComparer.Instance);
            Assert.Equal(6, Events().Count);
            Assert.Equal(0, fixture.Wakes);

            // --- A second cancel of a re-queued job before its claim only flags it again: it stays queued ---
            JobRow before = await fixture.JobAsync(checkpointed);
            await ActAsync(AdministratorId, JobService.CancelAction, [checkpointed]);
            JobRow again = await fixture.JobAsync(checkpointed);
            Assert.Equal((nameof(JobStatus.Pending), before.DueAt, /*lang=json,strict*/ """{"page":2}"""), (again.Status, again.DueAt, again.StateJson));
            Assert.True((DateTimeOffset)again.Columns["CancelRequestedAt"]! >= (DateTimeOffset)before.Columns["CancelRequestedAt"]!);

            // --- The re-queued jobs' next claim records the stop: the handler never runs ---
            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);
            Assert.Equal(
                [(checkpointed, JobStatus.Cancelled), (heldCheckpointed, JobStatus.Cancelled), (waiting, JobStatus.Cancelled)],
                polled.Select(static job => (job.JobId, job.Status)).Order());
            Assert.Empty(fixture.Script.Invocations);
        }

        [Fact]
        public async Task A_cancel_of_an_ended_job_is_refused_and_a_repeat_on_a_running_job_flags_it_again()
        {
            int ended = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(ended, "[Status] = 'Succeeded', [CompletedAt] = SYSUTCDATETIME()");
            int running = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(running, "[Status] = 'Running', [LeaseToken] = NEWID(), [LeaseExpiresAt] = DATEADD(minute, 5, SYSUTCDATETIME()), [LeaseOwner] = 'elsewhere'");

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(AdministratorId, JobService.CancelAction, [running, ended]));
            Assert.Equal(JobValidationCodes.NotCancellable, Assert.Single(refused.Errors).Code);
            Assert.Null((await fixture.JobAsync(running)).Columns["CancelRequestedAt"]);

            await ActAsync(AdministratorId, JobService.CancelAction, [running]);
            await ActAsync(AdministratorId, JobService.CancelAction, [running]);

            Assert.Equal(nameof(JobStatus.Running), (await fixture.JobAsync(running)).Status);
            Assert.Empty(fixture.Notices.Withdrawn);
        }

        // --- Resume ---

        [Fact]
        public async Task A_resume_withdraws_a_requesters_paused_notice_only_with_their_last_held_job_and_the_gap_only_with_the_last_of_all()
        {
            int first = await InsertJobAsync(JobRunKeys.Probe);
            int second = await InsertJobAsync(JobRunKeys.Probe);
            int viewers = await InsertJobAsync(JobRunKeys.Probe, requestedBy: ViewerUserId);
            int unrequested = await InsertJobAsync(JobRunKeys.Probe, requestedBy: null);
            foreach (int id in new[] { first, second, viewers, unrequested })
            {
                await fixture.SetJobAsync(id, "[Status] = 'Held', [ErrorCode] = N'Jobs.HeldAfterGap'");
            }

            // --- One of the requester's two held jobs: nothing is withdrawn ---
            await ActAsync(AdministratorId, JobService.ResumeAction, [first]);
            JobRow resumed = await fixture.JobAsync(first);
            Assert.Equal((nameof(JobStatus.Pending), null), (resumed.Status, resumed.ErrorCode));
            Assert.Empty(fixture.Notices.Withdrawn);
            Assert.Equal([Changed(first, ActiveUserId)], Events());
            Assert.Equal(1, fixture.Wakes);

            // --- Their last: their notice goes; another requester's and an unrequested job stay held ---
            await ActAsync(AdministratorId, JobService.ResumeAction, [second]);
            Assert.Equal([new WithdrawRequest(JobNotices.HeldType, [ActiveUserId])], fixture.Notices.Withdrawn, WithdrawalComparer.Instance);

            // --- The last of all: the gap goes with the viewer's notice; the job no user asked for tells nobody ---
            fixture.Notices.Clear();
            fixture.Events.Clear();
            await ActAsync(AdministratorId, JobService.ResumeAction, [viewers, unrequested]);
            Assert.Equal(
                [new WithdrawRequest(JobNotices.HeldType, [ViewerUserId]), new WithdrawRequest(JobNotices.GapType)],
                fixture.Notices.Withdrawn,
                WithdrawalComparer.Instance);
            Assert.Equal([Changed(viewers, ViewerUserId)], Events());

            // --- The repeat finds a job that is not held ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(AdministratorId, JobService.ResumeAction, [first]));
            Assert.Equal(JobValidationCodes.NotHeld, Assert.Single(refused.Errors).Code);
        }

        // --- Confirm ---

        [Fact]
        public async Task A_confirm_by_the_requester_merges_the_recorded_keys_and_runs_the_job_again_and_its_repeat_is_refused()
        {
            int waiting = await InsertJobAsync(JobRunKeys.Probe, attempts: 2, confirmedKeys: "Test.Earlier");
            await fixture.SetJobAsync(waiting, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated, JobRunKeys.Negative))}, [DueAt] = DATEADD(day, -1, SYSUTCDATETIME())");

            await ActAsync(ActiveUserId, JobService.ConfirmAction, [waiting], new ConfirmJobArguments([JobRunKeys.Negative, JobRunKeys.Backdated]));

            // --- Pending, due now, from its first attempt, with every recorded key confirmed ---
            JobRow row = await fixture.JobAsync(waiting);
            Assert.Equal((nameof(JobStatus.Pending), 0, null), (row.Status, row.Attempts, row.PendingConfirmationsJson));
            Assert.Equal([JobRunKeys.Backdated, "Test.Earlier", JobRunKeys.Negative], PendingConfirmations.Split(row.ConfirmedKeys).Order(StringComparer.Ordinal));
            Assert.InRange(row.DueAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Equal([new WithdrawRequest(JobNotices.PendingConfirmationKey(waiting))], fixture.Notices.Withdrawn);
            Assert.Equal([Changed(waiting, ActiveUserId)], Events());
            Assert.Equal(1, fixture.Wakes);

            // --- The repeat finds a job that waits on nothing ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => ActAsync(ActiveUserId, JobService.ConfirmAction, [waiting], new ConfirmJobArguments([JobRunKeys.Backdated, JobRunKeys.Negative])));
            Assert.Equal(JobValidationCodes.NotPendingConfirmation, Assert.Single(refused.Errors).Code);
        }

        [Fact]
        public async Task A_confirm_is_refused_to_anyone_but_the_requester_short_of_a_recorded_key_and_with_a_blank_key()
        {
            int waiting = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(waiting, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated, JobRunKeys.Negative))}");

            // --- An administrator who holds the grant did not start the job ---
            ValidationException other = await Assert.ThrowsAsync<ValidationException>(
                () => ActAsync(AdministratorId, JobService.ConfirmAction, [waiting], new ConfirmJobArguments([JobRunKeys.Backdated, JobRunKeys.Negative])));
            Assert.Equal(JobValidationCodes.NotInitiator, Assert.Single(other.Errors).Code);

            // --- The requester leaves a recorded key out ---
            ValidationException partial = await Assert.ThrowsAsync<ValidationException>(
                () => ActAsync(ActiveUserId, JobService.ConfirmAction, [waiting], new ConfirmJobArguments([JobRunKeys.Backdated])));
            Assert.Equal(JobValidationCodes.NotPendingConfirmation, Assert.Single(partial.Errors).Code);

            // --- A blank key and an overlong key, each at its position among the arguments ---
            ValidationException blank = await Assert.ThrowsAsync<ValidationException>(
                () => ActAsync(ActiveUserId, JobService.ConfirmAction, [waiting], new ConfirmJobArguments([JobRunKeys.Backdated, " ", new string('k', JobLimits.MaxConfirmationKeyLength + 1)])));
            Assert.Equal(
                [JobValidationCodes.NotPendingConfirmation, ValidationCodes.MaxLength, ValidationCodes.Required],
                blank.Errors.Select(static error => error.Code).Order(StringComparer.Ordinal));

            JobRow row = await fixture.JobAsync(waiting);
            Assert.Equal(nameof(JobStatus.PendingConfirmation), row.Status);
            Assert.Empty(fixture.Notices.Withdrawn);
            Assert.Empty(Events());
        }

        // --- The self-scope ---

        [Fact]
        public async Task A_member_without_a_grant_reads_and_cancels_their_own_jobs_alone_and_is_refused_the_machinerys_recovery()
        {
            int own = await InsertJobAsync(JobRunKeys.Probe);
            int others = await InsertJobAsync(JobRunKeys.Probe, requestedBy: ViewerUserId);
            int failed = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(failed, "[Status] = 'Failed', [CompletedAt] = SYSUTCDATETIME()");

            await using (AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId))
            {
                // --- "My jobs": the query reads the caller's alone ---
                RowPage page = await Jobs(scope).QueryAsync(new QueryRequest { Select = "Id" }, Token);
                Assert.Equal([own, failed], Ints(page.Rows, 0).Order());

                // --- Their own job cancels; another's is hidden; a retry takes a grant ---
                _ = await Jobs(scope).ExecuteActionAsync(JobService.CancelAction, [own], null, new ActionOptions(), Token);
                _ = await Assert.ThrowsAsync<NotFoundException>(() => Jobs(scope).ExecuteActionAsync(JobService.CancelAction, [others], null, new ActionOptions(), Token));
                _ = await Assert.ThrowsAsync<ForbiddenException>(() => Jobs(scope).ExecuteActionAsync(JobService.RetryAction, [failed], null, new ActionOptions(), Token));
            }

            Assert.Equal(nameof(JobStatus.Cancelled), (await fixture.JobAsync(own)).Status);
            Assert.Equal((nameof(JobStatus.Pending), nameof(JobStatus.Failed)), ((await fixture.JobAsync(others)).Status, (await fixture.JobAsync(failed)).Status));
        }

        [Fact]
        public async Task A_reader_of_an_owning_row_reads_its_jobs_projection_through_the_navigation_but_no_other_column()
        {
            int job = await InsertJobAsync(JobRunKeys.Ship, requestedBy: ViewerUserId);
            await fixture.InsertShipmentAsync(1, job, "boxed");

            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);
            EntityService<Shipment, long> shipments = scope.ServiceProvider.GetRequiredService<EntityService<Shipment, long>>();

            // --- The status is the job's related projection, open to any reader of the shipment ---
            RowPage page = await shipments.QueryAsync(new QueryRequest { Select = "Id,Job.Status" }, Token);
            Assert.Equal([nameof(JobStatus.Pending)], Strings(page.Rows, 1));

            // --- A column beyond it takes a read of the job, which the caller's self-scope does not give ---
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => shipments.QueryAsync(new QueryRequest { Select = "Id,Job.ErrorCode" }, Token));
            Assert.Equal(CoreResources.Job, refused.Resource);
        }

        // --- A read-only tenant ---

        [Fact]
        public async Task A_read_only_tenant_serves_a_cancel_and_refuses_a_retry()
        {
            int pending = await InsertJobAsync(JobRunKeys.Probe);
            int failed = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(failed, "[Status] = 'Failed', [CompletedAt] = SYSUTCDATETIME()");

            await using (AsyncServiceScope scope = await fixture.UserScopeAsync(AdministratorId, TenantState.ReadOnly))
            {
                _ = await Jobs(scope).ExecuteActionAsync(JobService.CancelAction, [pending], null, new ActionOptions(), Token);
                TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                    () => Jobs(scope).ExecuteActionAsync(JobService.RetryAction, [failed], null, new ActionOptions(), Token));
                Assert.Equal(TenantProblemCodes.ReadOnly, refused.Code);
            }

            Assert.Equal((nameof(JobStatus.Cancelled), nameof(JobStatus.Failed)), ((await fixture.JobAsync(pending)).Status, (await fixture.JobAsync(failed)).Status));
        }

        // --- The retention ---

        [Fact]
        public async Task The_retention_deletes_ended_jobs_past_their_periods_page_by_page_and_the_rows_referencing_them_lose_them()
        {
            // --- 1,005 succeeded jobs past their 30 days, one within them; failed and cancelled jobs past and within their 180 ---
            await InsertEndedAsync(nameof(JobStatus.Succeeded), 1_005, daysAgo: 31);
            int recentSucceeded = await InsertEndedAsync(nameof(JobStatus.Succeeded), 1, daysAgo: 29);
            int oldFailed = await InsertEndedAsync(nameof(JobStatus.Failed), 1, daysAgo: 181);
            _ = await InsertEndedAsync(nameof(JobStatus.Cancelled), 1, daysAgo: 181);
            int recentFailed = await InsertEndedAsync(nameof(JobStatus.Failed), 1, daysAgo: 179);

            // --- A job that has not ended is never deleted, however old ---
            int old = await InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 400 * 86_400);
            await fixture.SetJobAsync(old, "[CreatedAt] = DATEADD(day, -400, SYSUTCDATETIME())");

            // --- A shipment and a schedule state reference deleted jobs ---
            await fixture.InsertShipmentAsync(1, oldFailed, "aged");
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[ScheduleStates] SET [LastJobId] = {oldFailed} WHERE [ScheduleId] = {FixtureFeature.ProbeScheduleId};"));

            int retention = await InsertJobAsync(JobRetentionHandler.Key, runAs: WellKnownIds.SystemUserId, requestedBy: null);
            IReadOnlyList<PolledJob> polled = await PollAsync(JobRetentionHandler.Key);

            // --- The run succeeded and left the jobs within their periods, the unended one and itself ---
            Assert.Equal([(retention, JobStatus.Succeeded)], polled.Select(static job => (job.JobId, job.Status)));

            // --- One progress report per page: the empty stop, the succeeded band's full and short pages, the failed band's one ---
            Assert.Equal(4, fixture.Log.GetSnapshot().Count(static record => record.Id.Name == "JobProgress"));
            Assert.Equal(
                [recentSucceeded, recentFailed, old, retention],
                await IdsAsync("SELECT [Id] FROM [core].[Jobs] ORDER BY [Id];"));

            // --- The keys lost the deleted job ---
            Assert.Equal(
                (0, 1),
                (await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [fixture].[Shipments] WHERE [JobId] IS NOT NULL;"),
                 await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[ScheduleStates] WHERE [ScheduleId] = {FixtureFeature.ProbeScheduleId} AND [LastJobId] IS NULL;"))));
        }

        [Fact]
        public async Task The_retention_stops_a_job_that_waited_for_confirmation_past_the_failed_period_and_withdraws_its_notice()
        {
            int stale = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(stale, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated))}, [DueAt] = DATEADD(day, -181, SYSUTCDATETIME())");
            int fresh = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(fresh, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated))}, [DueAt] = DATEADD(day, -179, SYSUTCDATETIME())");
            int held = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(held, "[Status] = 'Held', [DueAt] = DATEADD(day, -181, SYSUTCDATETIME())");

            _ = await InsertJobAsync(JobRetentionHandler.Key, runAs: WellKnownIds.SystemUserId, requestedBy: null);
            _ = await PollAsync(JobRetentionHandler.Key);

            // --- The stale job is queued flagged, so its next claim records the stop; the fresh one still waits ---
            JobRow stopped = await fixture.JobAsync(stale);
            Assert.Equal((nameof(JobStatus.Pending), true, null), (stopped.Status, stopped.Columns["CancelRequestedAt"] is not null, stopped.PendingConfirmationsJson));
            Assert.Equal((nameof(JobStatus.PendingConfirmation), nameof(JobStatus.Held)), ((await fixture.JobAsync(fresh)).Status, (await fixture.JobAsync(held)).Status));
            Assert.Equal([new WithdrawRequest(JobNotices.PendingConfirmationKey(stale))], fixture.Notices.Withdrawn);

            IReadOnlyList<PolledJob> polled = await PollAsync(JobRunKeys.Probe);
            Assert.Equal([(stale, JobStatus.Cancelled)], polled.Select(static job => (job.JobId, job.Status)));
        }

        [Fact]
        public async Task A_stop_cancelled_while_its_notices_go_still_withdraws_every_stopped_jobs_notice_and_runs_no_later_page()
        {
            // --- 101 jobs waiting past the failed period, two withdrawal calls' worth, and an old succeeded job ---
            List<int> stale = await IdsAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"""
                DECLARE @due datetimeoffset(3) = DATEADD(day, -181, SYSUTCDATETIME());
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [RunAsUserId], [RequestedById], [CreatedAt], [PendingConfirmationsJson])
                OUTPUT [inserted].[Id]
                SELECT NEXT VALUE FOR [core].[sq_Jobs], N'{JobRunKeys.Probe}', 'PendingConfirmation', @due, 0, {ActiveUserId}, {ActiveUserId}, @due, {Literal(Record(JobRunKeys.Backdated))}
                FROM (SELECT TOP (101) 1 AS [n] FROM sys.all_columns) AS [numbers];
                """));
            int succeeded = await InsertEndedAsync(nameof(JobStatus.Succeeded), 1, daysAgo: 31);

            // --- The host stops as the first withdrawal arrives, which cancels the item's token ---
            using CancellationTokenSource host = new();
            fixture.Notices.Withdrawing = host.Cancel;
            int retention = await InsertJobAsync(JobRetentionHandler.Key, runAs: WellKnownIds.SystemUserId, requestedBy: null);
            IReadOnlyList<PolledJob> polled = await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRetentionHandler.Key], host.Token, Token);

            // --- The committed page's notices all went; the cancel stopped the run before the next page, and queued it again ---
            Assert.Equal(
                [.. stale.Select(static id => JobNotices.PendingConfirmationKey(id)).Order(StringComparer.Ordinal)],
                fixture.Notices.Withdrawn.Select(static request => request.DedupKey).Order(StringComparer.Ordinal));
            Assert.Equal([(retention, JobStatus.Pending)], polled.Select(static job => (job.JobId, job.Status)));
            Assert.Equal(nameof(JobStatus.Succeeded), (await fixture.JobAsync(succeeded)).Status);
        }

        // --- The statements' repeats ---

        [Fact]
        public async Task Each_actions_statement_run_again_matches_no_row_but_a_running_jobs_cancel_which_flags_it_again()
        {
            int failed = await InsertJobAsync(JobRunKeys.Probe, attempts: 3);
            await fixture.SetJobAsync(failed, "[Status] = 'Failed', [CompletedAt] = SYSUTCDATETIME(), [ErrorCode] = N'Jobs.Internal'");
            int held = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(held, "[Status] = 'Held'");
            int pending = await InsertJobAsync(JobRunKeys.Probe);
            int running = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(running, "[Status] = 'Running', [LeaseToken] = NEWID(), [LeaseExpiresAt] = DATEADD(minute, 5, SYSUTCDATETIME()), [LeaseOwner] = 'elsewhere'");
            int waiting = await InsertJobAsync(JobRunKeys.Probe, attempts: 2);
            await fixture.SetJobAsync(waiting, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated))}");

            void Append(DataBatch batch)
            {
                JobStatements.Retry(batch, [failed]);
                JobStatements.Resume(batch, [held]);
                JobStatements.Cancel(batch, [pending, running]);
                JobStatements.Confirm(batch, [waiting], [JobRunKeys.Backdated], ActiveUserId);
            }

            // --- The first run matches each row; then each row carries a value its statement's repeat would overwrite ---
            await StatementsAsync(Append);
            Assert.Equal(
                [nameof(JobStatus.Pending), nameof(JobStatus.Pending), nameof(JobStatus.Cancelled), nameof(JobStatus.Running), nameof(JobStatus.Pending)],
                [(await fixture.JobAsync(failed)).Status, (await fixture.JobAsync(held)).Status, (await fixture.JobAsync(pending)).Status, (await fixture.JobAsync(running)).Status, (await fixture.JobAsync(waiting)).Status]);
            await fixture.SetJobAsync(failed, "[Attempts] = 2");
            await fixture.SetJobAsync(held, "[ErrorCode] = N'Test.Mark'");
            await fixture.SetJobAsync(pending, "[CompletedAt] = DATEADD(day, -1, SYSUTCDATETIME())");
            await fixture.SetJobAsync(running, "[CancelRequestedAt] = DATEADD(day, -1, SYSUTCDATETIME())");
            await fixture.SetJobAsync(waiting, "[Attempts] = 2");

            // --- The repeat leaves every mark, but sets the running job's cancel flag again ---
            await StatementsAsync(Append);
            DateTimeOffset dayAgo = DateTimeOffset.UtcNow.AddHours(-23);
            Assert.Equal(2, (await fixture.JobAsync(failed)).Attempts);
            Assert.Equal("Test.Mark", (await fixture.JobAsync(held)).ErrorCode);
            Assert.True((await fixture.JobAsync(pending)).CompletedAt < dayAgo);
            Assert.True((DateTimeOffset)(await fixture.JobAsync(running)).Columns["CancelRequestedAt"]! > dayAgo);
            Assert.Equal(2, (await fixture.JobAsync(waiting)).Attempts);
        }

        [Fact]
        public async Task The_confirm_statement_matches_no_job_another_user_requested_nor_one_a_recorded_key_of_which_was_not_confirmed()
        {
            int partial = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(partial, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated, JobRunKeys.Negative))}");
            int others = await InsertJobAsync(JobRunKeys.Probe);
            await fixture.SetJobAsync(others, $"[Status] = 'PendingConfirmation', [PendingConfirmationsJson] = {Literal(Record(JobRunKeys.Backdated))}");

            await StatementsAsync(batch =>
            {
                JobStatements.Confirm(batch, [partial], [JobRunKeys.Backdated], ActiveUserId);
                JobStatements.Confirm(batch, [others], [JobRunKeys.Backdated], ViewerUserId);
            });

            foreach (int id in new[] { partial, others })
            {
                JobRow row = await fixture.JobAsync(id);
                Assert.Equal((nameof(JobStatus.PendingConfirmation), null), (row.Status, row.ConfirmedKeys));
            }
        }

        // --- The tree verify ---

        [Fact]
        public async Task The_tree_verify_repairs_a_tree_tables_drifted_counts_through_its_own_round_trip_and_warns_of_the_repair()
        {
            // --- A root and its child, the root's counts drifted behind the stacks' backs ---
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                """
                INSERT INTO [fixture].[Nodes] ([Id], [Code], [Name], [Node], [ParentId], [SubtreeCount], [ActiveSubtreeCount], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (1, 'R', N'Root', hierarchyid::Parse('/1/'), NULL, 9, 0, 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1),
                       (2, 'C', N'Child', hierarchyid::Parse('/1/2/'), 1, 1, 1, 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);
                """);

            // --- The handler reports a percent after each table, whose jump renews the lease in the
            // background while the next table's round trip runs; the audit attributes every change in
            // a batch's window to that batch, so it is suspended while the two overlap ---
            fixture.Audit.Suspended = true;
            int verify = await InsertJobAsync(TreeVerifyHandler.Key, runAs: WellKnownIds.SystemUserId, requestedBy: null);
            IReadOnlyList<PolledJob> polled = await PollAsync(TreeVerifyHandler.Key);

            Assert.Equal([(verify, JobStatus.Succeeded)], polled.Select(static job => (job.JobId, job.Status)));
            Assert.Equal(
                (2, 2),
                (await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT [SubtreeCount] FROM [fixture].[Nodes] WHERE [Id] = 1;"),
                 await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT [ActiveSubtreeCount] FROM [fixture].[Nodes] WHERE [Id] = 1;")));
            FakeLogRecord repaired = Assert.Single(fixture.Log.GetSnapshot(), static record => record.Id.Name == "TreeVerify.Repaired");
            Assert.Equal(LogLevel.Warning, repaired.Level);
        }

        // --- Helpers ---

        /// <summary>Stores a job of a handler, as <see cref="JobRunDatabase.InsertJobAsync" /> does.</summary>
        /// <param name="key">The handler key.</param>
        /// <param name="runAs">The run-as user.</param>
        /// <param name="requestedBy">The requester; null for none.</param>
        /// <param name="attempts">The attempts already made.</param>
        /// <param name="stateJson">The checkpoint; null for none.</param>
        /// <param name="confirmedKeys">The confirmed keys as stored; null for none.</param>
        /// <param name="dueSecondsAgo">How long ago the job became due.</param>
        /// <returns>The job's id.</returns>
        private Task<int> InsertJobAsync(
            string key, int runAs = ActiveUserId, int? requestedBy = ActiveUserId, int attempts = 0, string? stateJson = null, string? confirmedKeys = null, int dueSecondsAgo = 5)
        {
            return fixture.InsertJobAsync(key, runAs, requestedBy, attempts, stateJson, confirmedKeys, dueSecondsAgo: dueSecondsAgo);
        }

        /// <summary>Stores ended jobs of the probe in one statement, each ended some days ago.</summary>
        /// <param name="status">The status they ended in.</param>
        /// <param name="count">How many.</param>
        /// <param name="daysAgo">How many days ago they ended.</param>
        /// <returns>The last job's id.</returns>
        private Task<int> InsertEndedAsync(string status, int count, int daysAgo)
        {
            return SqlServerFixture.ScalarAsync<int>(
                fixture.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @ended datetimeoffset(3) = DATEADD(day, -{daysAgo}, SYSUTCDATETIME());
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [CompletedAt], [RunAsUserId], [RequestedById], [CreatedAt])
                    SELECT NEXT VALUE FOR [core].[sq_Jobs], N'{JobRunKeys.Probe}', '{status}', @ended, 1, @ended, {ActiveUserId}, {ActiveUserId}, @ended
                    FROM (SELECT TOP ({count}) 1 AS [n] FROM sys.all_columns AS [a] CROSS JOIN sys.all_columns AS [b]) AS [numbers];
                    SELECT MAX([Id]) FROM [core].[Jobs];
                    """));
        }

        /// <summary>Polls the tenant once for some handlers' due jobs, and runs and completes what it claimed.</summary>
        /// <param name="keys">The handler keys.</param>
        /// <returns>Each claimed job and how its attempt ended.</returns>
        private Task<IReadOnlyList<PolledJob>> PollAsync(params string[] keys)
        {
            return JobPollDriver.PollOnceAsync(fixture.Tenant, keys, cancellationToken: Token);
        }

        /// <summary>Runs statements in one transaction of a maintenance batch of the system scope, outside any stack.</summary>
        /// <param name="append">Appends the statements.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task StatementsAsync(Action<DataBatch> append)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            append(batch);
            _ = await batch.ExecuteAsync(Token);
        }

        /// <summary>Runs one of the jobs' actions as a user, through a stepped-up scope of theirs.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="action">The action.</param>
        /// <param name="ids">The target jobs.</param>
        /// <param name="arguments">The action's arguments; null for none.</param>
        /// <returns>A task that completes when the action committed.</returns>
        private async Task ActAsync(int userId, string action, IReadOnlyList<int> ids, object? arguments = null)
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(userId);
            _ = await Jobs(scope).ExecuteActionAsync(action, ids, arguments, new ActionOptions(), Token);
        }

        /// <summary>The jobs' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Job, int> Jobs(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Job, int>>();
        }

        /// <summary>The record of the warnings a job waits on, one warning per key.</summary>
        /// <param name="keys">The keys.</param>
        /// <returns>The record, as the completion writes it.</returns>
        private static string Record(params string[] keys)
        {
            return PendingConfirmations.Serialize([.. keys.Select(static key => new ConfirmationRequest(key, [1], new Dictionary<string, object?>()))]);
        }

        /// <summary>The change event a requester is told of a job by.</summary>
        /// <param name="jobId">The job.</param>
        /// <param name="requesterId">The requester.</param>
        /// <returns>The event's name, its one user and its payload.</returns>
        private static (string Name, int UserId, object? Payload) Changed(int jobId, int requesterId)
        {
            ClientEvent changed = JobNotices.Changed(jobId, requesterId);
            return (changed.Name, Assert.Single(changed.UserIds), changed.Payload);
        }

        /// <summary>The client events published so far, each as its name, its one user and its payload.</summary>
        /// <returns>The events.</returns>
        private List<(string Name, int UserId, object? Payload)> Events()
        {
            return [.. fixture.Events.Published.Select(static published => (published.Name, Assert.Single(published.UserIds), published.Payload))];
        }

        /// <summary>The ids a query returns.</summary>
        /// <param name="sql">The query, of one int column.</param>
        /// <returns>The ids.</returns>
        private async Task<List<int>> IdsAsync(string sql)
        {
            List<int> ids = [];
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await using Microsoft.Data.SqlClient.SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }

        /// <summary>A column of int values of a row set.</summary>
        /// <param name="rows">The row set.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static List<int> Ints(QueryRowSet rows, int column)
        {
            int[] values = (int[])rows.GetBuffer(column);
            return [.. values.Take(rows.RowCount)];
        }

        /// <summary>A column of string values of a row set.</summary>
        /// <param name="rows">The row set.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static List<string?> Strings(QueryRowSet rows, int column)
        {
            string?[] values = (string?[])rows.GetBuffer(column);
            return [.. values.Take(rows.RowCount).Select((value, row) => rows.IsNull(column, row) ? null : value)];
        }

        /// <summary>Compares withdrawals by their key and their recipients' values.</summary>
        private sealed class WithdrawalComparer : IEqualityComparer<WithdrawRequest>
        {
            /// <summary>The comparer.</summary>
            public static WithdrawalComparer Instance { get; } = new();

            /// <inheritdoc />
            public bool Equals(WithdrawRequest? x, WithdrawRequest? y)
            {
                return x is not null && y is not null
                    && string.Equals(x.DedupKey, y.DedupKey, StringComparison.Ordinal)
                    && (x.RecipientIds ?? []).SequenceEqual(y.RecipientIds ?? []) && (x.RecipientIds is null) == (y.RecipientIds is null);
            }

            /// <inheritdoc />
            public int GetHashCode(WithdrawRequest obj)
            {
                return StringComparer.Ordinal.GetHashCode(obj.DedupKey);
            }
        }
    }

    /// <summary>The jobs' stack and the platform's maintenance handlers with read-committed snapshot on, as provisioning creates a tenant database.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class JobServiceSnapshotTests(JobRunDatabase fixture) : JobServiceTests(fixture), IClassFixture<JobRunDatabase>;

    /// <summary>The jobs' stack and the platform's maintenance handlers with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class JobServiceLockingTests(LockingJobRunDatabase fixture) : JobServiceTests(fixture), IClassFixture<LockingJobRunDatabase>;
}
