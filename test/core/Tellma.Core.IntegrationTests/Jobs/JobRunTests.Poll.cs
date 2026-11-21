// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Tenancy;
using static Tellma.Core.IntegrationTests.Jobs.JobRunDatabase;

namespace Tellma.Core.IntegrationTests.Jobs
{
    // The worker's poll against the database: the gap check and its hold with the notices that
    // follow, the heartbeat stamp and its throttle, the samples, the retired-key cancel, a poll that
    // re-runs after a transient failure, a poll whose instruments throw, the instruments each of
    // them records, and the worker itself polling, claiming, running two batches at once, retiring a
    // tenant's poller and draining its batches at shutdown.
    public abstract partial class JobRunTests
    {
        /// <summary>A key no handler of the fixture registers or retires.</summary>
        private const string UnknownKey = "test.gone";

        /// <summary>How long a gap the tests open: past the six-hour default threshold.</summary>
        private const int GapSeconds = 7 * 3600;

        // --- The gap check and its hold ---

        [Fact]
        public async Task A_poll_after_a_gap_holds_every_overdue_unleased_job_before_its_claims_and_tells_the_administrators_and_each_requester()
        {
            await StampAsync(secondsAgo: GapSeconds);
            int pending = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: GapSeconds + 3600);
            int inbox = await fixture.InsertJobAsync(JobRunKeys.Inbox, dueSecondsAgo: GapSeconds);
            int unrequested = await fixture.InsertJobAsync(JobRunKeys.Probe, requestedBy: null, dueSecondsAgo: GapSeconds);
            int lapsed = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Steps, dueSecondsAgo: GapSeconds), leaseSeconds: -60);
            int leased = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: GapSeconds), leaseSeconds: 300);
            int recent = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 60);
            using JobInstruments instruments = new(fixture);

            JobPollResult result = await PollAsync(Plan(claims: [JobRunKeys.Probe]));

            // --- The overdue rows no live lease holds are held; the claim after the hold leases the recent row alone ---
            Assert.Equal([recent], Assert.Single(result.Claims).Claim.Rows.Value.Select(static row => row.Job.Id));
            foreach (int id in new[] { pending, inbox, unrequested, lapsed })
            {
                JobRow row = await fixture.JobAsync(id);
                Assert.Equal((nameof(JobStatus.Held), JobErrorCodes.HeldAfterGap, null), (row.Status, row.ErrorCode, row.LeaseToken));
                Assert.Null(row.Columns["LeaseOwner"]);
            }

            Assert.Equal(nameof(JobStatus.Running), (await fixture.JobAsync(leased)).Status);

            // --- What the check read: the stamp before the gap, the database's clock, and the count per requester ---
            Assert.True(result.Gap.GapDetected);
            Assert.Equal(4, result.Gap.HeldCount);
            Assert.Equal([(null, 1), (ActiveUserId, 3)], result.Gap.Held.Select(static held => (held.RequestedById, held.Count)).OrderBy(static held => held.RequestedById));
            DateTimeOffset previous = Assert.NotNull(result.Gap.PreviousTickAt);
            Assert.InRange(result.Gap.Now - previous, TimeSpan.FromSeconds(GapSeconds - 5), TimeSpan.FromSeconds(GapSeconds + 60));

            // --- The administrators hear of the gap and its total; each requester of their own held rows ---
            Assert.Equal(
                [
                    new NotificationRequest(JobNotices.GapType, [AdministratorId], new SchedulerGapArguments(previous, result.Gap.Now, 4), null, null, null, JobNotices.GapType),
                    new NotificationRequest(JobNotices.HeldType, [ActiveUserId], new JobHeldArguments(3), null, null, null, JobNotices.HeldType),
                ],
                fixture.Notices.Raised,
                NoticeComparer.Instance);
            FakeLogRecord detected = Assert.Single(fixture.Log.GetSnapshot(), static record => record.Id.Name == "SchedulerGapDetected");
            Assert.Equal(LogLevel.Warning, detected.Level);
            Assert.Equal(1L, Assert.Single(instruments.GapDetected.GetMeasurementSnapshot()).Value);

            // --- The heartbeat closed the gap: the next poll holds nothing and tells no one ---
            Assert.NotNull(result.Stamped);
            JobPollResult next = await PollAsync(Plan());
            Assert.False(next.Gap.GapDetected);
            Assert.Empty(next.Gap.Held);
            Assert.Equal(2, fixture.Notices.Raised.Count);
        }

        [Fact]
        public async Task A_tenant_never_polled_has_no_gap_so_its_overdue_jobs_are_claimed()
        {
            int overdue = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: GapSeconds * 2);
            using JobInstruments instruments = new(fixture);

            JobPollResult result = await PollAsync(Plan(claims: [JobRunKeys.Probe, JobRunKeys.Inbox]));

            Assert.False(result.Gap.GapDetected);
            Assert.Null(result.Gap.PreviousTickAt);
            Assert.Equal([overdue], result.Claims.Single(static polled => polled.Claim.Handler.Key == JobRunKeys.Probe).Claim.Rows.Value.Select(static row => row.Job.Id));
            Assert.Empty(fixture.Notices.Raised);
            Assert.Empty(instruments.GapDetected.GetMeasurementSnapshot());

            // --- The claim that leased a row is counted, its fill and its first claim's wait recorded;
            // the inbox claim that leased nothing records nothing; the round trip is timed once ---
            Assert.Equal([(JobRunKeys.Probe, 1L)], JobInstruments.ByHandler(instruments.Claimed));
            Assert.Equal([(JobRunKeys.Probe, 0.1)], JobInstruments.ByHandler(instruments.BatchFill));
            (string? latencyKey, double latency) = Assert.Single(JobInstruments.ByHandler(instruments.QueueLatency));
            Assert.Equal(JobRunKeys.Probe, latencyKey);
            Assert.InRange(latency, (GapSeconds * 2) - 5, (GapSeconds * 2) + 60);
            CollectedMeasurement<double> duration = Assert.Single(instruments.PollDuration.GetMeasurementSnapshot());
            Assert.True(duration.Value > 0);
            Assert.Empty(duration.Tags);
        }

        [Fact]
        public async Task A_poll_whose_instruments_throw_still_hands_its_claims_over_and_logs_the_failure()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);

            // --- An exporter of this host's claim counter that throws on every measurement ---
            IMeterFactory meters = fixture.Tenant.Services.GetRequiredService<IMeterFactory>();
            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, subscriber) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, meters) && instrument.Name == JobsTelemetryNames.Claimed)
                {
                    subscriber.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => throw new InvalidOperationException("The exporter broke."));
            listener.Start();

            JobPollResult? handed = null;
            JobPollResult result = await fixture.Tenant.Services.GetRequiredService<IJobPoll>().PollAsync(Plan(claims: [JobRunKeys.Probe]), polled => handed = polled, Token);

            // --- The claims were handed over before the instruments ran, and the poll ended as usual ---
            Assert.Same(result, handed);
            Assert.Equal([id], Assert.Single(result.Claims).Claim.Rows.Value.Select(static row => row.Job.Id));
            FakeLogRecord failed = Assert.Single(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobPollNoticesFailed");
            Assert.Equal(LogLevel.Warning, failed.Level);
            Assert.IsType<InvalidOperationException>(failed.Exception);
        }

        // --- The heartbeat stamp ---

        [Fact]
        public async Task The_heartbeat_is_stamped_with_the_owner_at_most_once_a_minute_and_each_stamp_is_logged()
        {
            // --- The first poll stamps the row ---
            JobPollResult first = await PollAsync(Plan());
            DateTimeOffset stamped = Assert.NotNull(first.Stamped);
            Assert.Equal((stamped, JobLeaseOwner.Current), await WorkerStateAsync());

            // --- Within the minute the stamp is left as it is ---
            JobPollResult second = await PollAsync(Plan());
            Assert.Null(second.Stamped);
            Assert.Equal((stamped, JobLeaseOwner.Current), await WorkerStateAsync());

            // --- A minute on, the next poll stamps it again ---
            await StampAsync(secondsAgo: 61);
            JobPollResult third = await PollAsync(Plan());
            Assert.True(Assert.NotNull(third.Stamped) > stamped);
            Assert.Equal(2, fixture.Log.GetSnapshot().Count(static record => record.Id.Name == "JobWorkerHeartbeat"));
        }

        [Fact]
        public async Task A_poll_that_fails_short_of_its_end_leaves_the_heartbeat_unstamped()
        {
            await StampAsync(secondsAgo: GapSeconds);
            int overdue = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: GapSeconds);
            ScriptedTick tick = new(TickFault.Final);

            _ = await Assert.ThrowsAnyAsync<Exception>(() => PollAsync(Plan(), tick));

            // --- The hold before the failure committed on its own; the stamp after it never ran ---
            Assert.Equal(nameof(JobStatus.Held), (await fixture.JobAsync(overdue)).Status);
            (DateTimeOffset? lastTickAt, _) = await WorkerStateAsync();
            Assert.True(lastTickAt < DateTimeOffset.UtcNow.AddHours(-6));
            Assert.Equal(0, tick.SecondSteps);
            Assert.Empty(fixture.Notices.Raised);
        }

        // --- Re-runs ---

        [Fact]
        public async Task A_poll_that_fails_transiently_re_runs_whole_with_its_claims_returned_once_and_the_stamp_still_last()
        {
            await StampAsync(secondsAgo: GapSeconds);
            int held = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: GapSeconds);
            int recent = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 60);
            ScriptedTick tick = new(TickFault.Transient);

            JobPollResult result = await PollAsync(Plan(claims: [JobRunKeys.Probe]), tick);

            // --- The tick ran twice and saw the stamp unwritten both times: the stamp comes after it ---
            Assert.Equal(["tick: stale", "tick: stale"], await fixture.RunLogAsync());
            Assert.Equal(1, tick.SecondSteps);

            // --- The re-run's hold and claim returned what the first attempt wrote, once ---
            Assert.Equal([(recent, 1)], Assert.Single(result.Claims).Claim.Rows.Value.Select(static row => (row.Job.Id, row.Job.Attempts)));
            Assert.Equal(1, (await fixture.JobAsync(recent)).Attempts);
            Assert.Equal(nameof(JobStatus.Held), (await fixture.JobAsync(held)).Status);
            Assert.Equal(1, result.Gap.HeldCount);
            Assert.NotNull(result.Stamped);
            Assert.Equal([JobNotices.GapType, JobNotices.HeldType], fixture.Notices.Raised.Select(static notice => notice.Type));
        }

        [Fact]
        public async Task A_poll_whose_connection_is_killed_re_runs_whole_and_returns_every_row_it_leased_with_one_attempt_counted()
        {
            int first = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 20);
            int second = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 10);
            ScriptedTick tick = new(TickFault.Killed);

            // --- The test's session holds the gate the tick's first run waits for; unpooled, so closing it releases the gate ---
            Microsoft.Data.SqlClient.SqlConnectionStringBuilder unpooled = new(fixture.ConnectionString) { Pooling = false };
            await using Microsoft.Data.SqlClient.SqlConnection gate = new(unpooled.ConnectionString);
            await gate.OpenAsync(Token);
            int holder;
            await using (Microsoft.Data.SqlClient.SqlCommand hold = gate.CreateCommand())
            {
                hold.CommandText = $"EXEC sp_getapplock @Resource = N'{ScriptedTick.Gate}', @LockMode = 'Exclusive', @LockOwner = 'Session'; SELECT @@SPID;";
                holder = Convert.ToInt32(await hold.ExecuteScalarAsync(Token), System.Globalization.CultureInfo.InvariantCulture);
            }

            Task<JobPollResult> polling = PollAsync(Plan(claims: [JobRunKeys.Probe]), tick);

            // --- Once the poll's session waits behind the gate, it is killed: its claim committed, its stamp never ran ---
            try
            {
                int victim = 0;
                await EventuallyAsync(async () =>
                {
                    victim = await SqlServerFixture.ScalarAsync<int>(
                        fixture.ConnectionString, Invariant($"SELECT CAST(ISNULL(MAX([session_id]), 0) AS int) FROM sys.dm_exec_requests WHERE [blocking_session_id] = {holder};"));
                    return victim != 0;
                });
                await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"KILL {victim};"));
            }
            finally
            {
                // Whatever happened, the poll ends here rather than claim the next test's rows.
                await gate.CloseAsync();
                await ((Task)polling).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            JobPollResult result = await polling;

            // --- The re-run returned the rows the first run leased, each attempt counted once ---
            Assert.Equal(["tick: stale", "tick: stale"], await fixture.RunLogAsync());
            Assert.Equal([(first, 1), (second, 1)], Assert.Single(result.Claims).Claim.Rows.Value.Select(static row => (row.Job.Id, row.Job.Attempts)));
            Assert.Equal(1, (await fixture.JobAsync(first)).Attempts);
            Assert.Equal(1, (await fixture.JobAsync(second)).Attempts);
            Assert.NotNull(result.Stamped);
            Assert.Equal(1, tick.SecondSteps);
        }

        // --- The samples and the retired-key cancel ---

        [Fact]
        public async Task The_backlog_sample_ages_each_registered_keys_oldest_available_job_and_skips_the_rest()
        {
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 120);
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 30);
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: -600);
            _ = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 3600), leaseSeconds: 300);
            _ = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Ship, dueSecondsAgo: 300), leaseSeconds: -1);
            _ = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Steps, dueSecondsAgo: 300), leaseSeconds: 300);
            using JobInstruments instruments = new(fixture);

            JobPollResult result = await PollAsync(Plan(sampleBacklog: true));

            // --- The oldest due row free to claim, per key: a live lease hides its row, a lapsed one does not ---
            Assert.NotNull(result.Backlog);
            IReadOnlyList<JobBacklogSample> backlog = result.Backlog;
            Assert.Equal([JobRunKeys.Probe, JobRunKeys.Ship], backlog.Select(static sample => sample.HandlerKey).Order(StringComparer.Ordinal));
            Assert.InRange(backlog.Single(static sample => sample.HandlerKey == JobRunKeys.Probe).Age, TimeSpan.FromSeconds(118), TimeSpan.FromSeconds(180));
            Assert.InRange(backlog.Single(static sample => sample.HandlerKey == JobRunKeys.Ship).Age, TimeSpan.FromSeconds(298), TimeSpan.FromSeconds(360));
            Assert.Null(result.Orphaned);

            // --- Each key's age is recorded under its handler, in seconds ---
            Assert.Equal(
                [(JobRunKeys.Probe, backlog.Single(static sample => sample.HandlerKey == JobRunKeys.Probe).Age.TotalSeconds), (JobRunKeys.Ship, backlog.Single(static sample => sample.HandlerKey == JobRunKeys.Ship).Age.TotalSeconds)],
                JobInstruments.ByHandler(instruments.BacklogAge).OrderBy(static sample => sample.Handler, StringComparer.Ordinal));
        }

        [Fact]
        public async Task The_hourly_sweep_cancels_a_retired_keys_unfinished_jobs_once_no_live_lease_holds_them_and_withdraws_their_confirmation_notices()
        {
            int pending = await fixture.InsertJobAsync(JobRunKeys.Retired, dueSecondsAgo: 7200);
            int held = await StatusAsync(await fixture.InsertJobAsync(JobRunKeys.Retired), JobStatus.Held);
            int waiting = await StatusAsync(await fixture.InsertJobAsync(JobRunKeys.Retired), JobStatus.PendingConfirmation);
            int lapsed = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Retired), leaseSeconds: -60);
            int leased = await LeaseAsync(await fixture.InsertJobAsync(JobRunKeys.Retired), leaseSeconds: 300);
            int succeeded = await StatusAsync(await fixture.InsertJobAsync(JobRunKeys.Retired), JobStatus.Succeeded);
            int registered = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 7200);
            _ = await fixture.InsertJobAsync(UnknownKey, dueSecondsAgo: 7200);
            _ = await fixture.InsertJobAsync(UnknownKey, dueSecondsAgo: 600);
            using JobInstruments instruments = new(fixture);

            JobPollResult result = await PollAsync(Plan(sampleOrphans: true));

            // --- Every unfinished row of the retired key ends cancelled, but the one a live lease holds ---
            Assert.Equal(
                [(pending, JobStatus.Pending), (held, JobStatus.Held), (waiting, JobStatus.PendingConfirmation), (lapsed, JobStatus.Running)],
                result.Retired.Select(static row => (row.Id, row.PreviousStatus)).OrderBy(static row => row.Id));
            foreach (int id in new[] { pending, held, waiting, lapsed })
            {
                JobRow row = await fixture.JobAsync(id);
                Assert.Equal((nameof(JobStatus.Cancelled), JobErrorCodes.HandlerRetired, null, null), (row.Status, row.ErrorCode, row.LeaseToken, row.PendingConfirmationsJson));
                Assert.NotNull(row.CompletedAt);
            }

            Assert.Equal(nameof(JobStatus.Running), (await fixture.JobAsync(leased)).Status);
            Assert.Equal(nameof(JobStatus.Succeeded), (await fixture.JobAsync(succeeded)).Status);
            Assert.Equal(nameof(JobStatus.Pending), (await fixture.JobAsync(registered)).Status);

            // --- The confirmation notice of the row that waited for one is withdrawn; the orphans are the unknown key's hour-overdue rows ---
            Assert.Equal([new WithdrawRequest(JobNotices.PendingConfirmationKey(waiting))], fixture.Notices.Withdrawn);
            Assert.Equal(1, result.Orphaned);
            Assert.Equal(1L, instruments.OrphanedNow());
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobRetiredKeysCancelled");

            // --- Once its lease lapsed, the next sweep cancels the last row too ---
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseExpiresAt] = DATEADD(second, -1, SYSUTCDATETIME()) WHERE [Id] = {leased};"));
            JobPollResult next = await PollAsync(Plan(sampleOrphans: true));
            Assert.Equal([(leased, JobStatus.Running)], next.Retired.Select(static row => (row.Id, row.PreviousStatus)));
            Assert.Equal(nameof(JobStatus.Cancelled), (await fixture.JobAsync(leased)).Status);
        }

        // --- Spans ---

        [Fact]
        public async Task A_poll_runs_under_its_own_root_span_with_one_receive_span_per_claim()
        {
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe);
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe);
            ConcurrentQueue<Activity> stopped = new();
            using ActivityListener listener = new()
            {
                ShouldListenTo = static source => source.Name == JobsTelemetryNames.ActivitySourceName,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            using Activity caller = new("caller");
            _ = caller.Start();
            Activity? poll = null;

            _ = await fixture.Tenant.Services.GetRequiredService<IJobPoll>().PollAsync(Plan(claims: [JobRunKeys.Probe, JobRunKeys.Inbox]), _ => poll = Activity.Current, Token);

            // --- The poll's span is a root of its own, never the caller's child, and the caller is current again ---
            Assert.NotNull(poll);
            Assert.Equal((JobPoll.PollActivityName, ActivityKind.Internal, default), (poll.OperationName, poll.Kind, poll.ParentSpanId));
            Assert.NotEqual(caller.TraceId, poll.TraceId);
            Assert.Same(caller, Activity.Current);

            // --- Each claim's receive span is the poll's child and counts the rows its claim leased ---
            Activity[] receives = [.. stopped.Where(activity => activity.ParentSpanId == poll.SpanId && activity.OperationName.StartsWith("receive ", StringComparison.Ordinal))];
            Assert.Equal(
                [($"receive {JobRunKeys.Inbox}", 0), ($"receive {JobRunKeys.Probe}", 2)],
                receives.Select(static activity => (activity.DisplayName, (int)activity.GetTagItem(JobsActivities.MessageCountTag)!)).OrderBy(static span => span.DisplayName, StringComparer.Ordinal));
            Assert.All(receives, static activity => Assert.Equal(ActivityKind.Client, activity.Kind));
        }

        // --- The worker ---

        [Fact]
        public async Task The_worker_polls_an_active_tenant_runs_two_batches_at_once_and_stops_polling_it_once_frozen()
        {
            // --- Two keys due before the worker starts, and an orphan its first poll samples. The
            // probe's handler returns only once the inbox's began, so both batches run at once ---
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int failing = await fixture.InsertJobAsync(JobRunKeys.Inbox);
            _ = await fixture.InsertJobAsync(UnknownKey, dueSecondsAgo: 7200);
            TaskCompletionSource inboxBegan = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Script.Probe = (_, _) => inboxBegan.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            fixture.Script.Inbox = (_, _) =>
            {
                inboxBegan.TrySetResult();
                throw new InvalidOperationException("The test handler breaks.");
            };
            using JobInstruments instruments = new(fixture);
            await using var worker = Worker.Create(fixture, TenantState.Active);

            await worker.StartAsync();

            // --- One poll claimed both keys, the dispatch ran both batches, and the heartbeat names this instance ---
            await EventuallyAsync(async () => (await fixture.JobAsync(id)).Status == nameof(JobStatus.Succeeded)
                && (await fixture.JobAsync(failing)).Status == nameof(JobStatus.Failed)
                && fixture.Events.Published.Count == 4
                && fixture.Notices.Raised.Count == 1);
            Assert.Contains(worker.Polls.Results, static result => result.Claims.Count(static polled => polled.Claim.Rows.Value.Count > 0) == 2);
            Assert.Equal([FixtureTenantId], worker.Instance.Polled);
            Assert.Equal(JobLeaseOwner.Current, (await WorkerStateAsync()).Owner);

            // --- Each requester heard of the claim and of the end; the failure reached the inbox alone ---
            Assert.Equal([id, id, failing, failing], ChangedJobs().Order());
            NotificationRequest failed = Assert.Single(fixture.Notices.Raised);
            Assert.Equal((JobNotices.FailedType, ActiveUserId, true), (failed.Type, Assert.Single(failed.RecipientIds), failed.InboxOnly));

            // --- The instruments: each job claimed, run, timed and completed under its handler and outcome ---
            await EventuallyAsync(() => Task.FromResult(worker.Dispatcher.Running == 0));
            Assert.Equal([(JobRunKeys.Inbox, 1L), (JobRunKeys.Probe, 1L)], JobInstruments.ByHandler(instruments.Claimed).Order());
            Assert.Equal([(JobRunKeys.Inbox, 0.1), (JobRunKeys.Probe, 0.1)], JobInstruments.ByHandler(instruments.BatchFill).Order());
            Assert.Equal([JobRunKeys.Inbox, JobRunKeys.Probe], JobInstruments.ByHandler(instruments.QueueLatency).Select(static measurement => measurement.Handler).Order(StringComparer.Ordinal));
            Assert.Equal([(JobRunKeys.Inbox, -1L), (JobRunKeys.Inbox, 1L), (JobRunKeys.Probe, -1L), (JobRunKeys.Probe, 1L)], JobInstruments.ByHandler(instruments.InFlight).Order());
            Assert.Equal([JobRunKeys.Inbox, JobRunKeys.Probe], JobInstruments.ByHandler(instruments.Duration).Select(static measurement => measurement.Handler).Order(StringComparer.Ordinal));
            Assert.Equal(
                [(JobRunKeys.Inbox, JobsMetrics.Failed, 1L), (JobRunKeys.Probe, JobsMetrics.Succeeded, 1L)],
                instruments.Completed.GetMeasurementSnapshot()
                    .Select(static measurement => ((string?)measurement.Tags[JobsTelemetryNames.HandlerTag], (string?)measurement.Tags[JobsTelemetryNames.OutcomeTag], measurement.Value))
                    .Order());
            Assert.NotEmpty(instruments.PollDuration.GetMeasurementSnapshot());
            Assert.Equal(1L, instruments.OrphanedNow());

            // --- Frozen: the poller is retired, its orphan sample dropped once it ended, and a job enqueued afterwards waits ---
            await worker.Instance.OnStateChangedAsync(worker.Registry.Set(TenantState.Frozen), TenantState.Active);
            Assert.Empty(worker.Instance.Polled);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobPollerRetired");
            await EventuallyAsync(() => Task.FromResult(instruments.OrphanedNow() == 0));
            int later = await fixture.InsertJobAsync(JobRunKeys.Probe);
            await Task.Delay(TimeSpan.FromSeconds(2), Token);
            JobRow waiting = await fixture.JobAsync(later);
            Assert.Equal((nameof(JobStatus.Pending), 0), (waiting.Status, waiting.Attempts));
        }

        [Fact]
        public async Task The_worker_polls_a_read_only_tenant_for_the_keys_that_run_then_alone()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            await using var worker = Worker.Create(fixture, TenantState.ReadOnly);

            await worker.StartAsync();

            // --- The poll ran, stamped, and claimed nothing: no fixture key runs while read-only ---
            await EventuallyAsync(async () => (await WorkerStateAsync()).LastTickAt is not null);
            Assert.Equal([FixtureTenantId], worker.Instance.Polled);
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Pending), 0, null), (row.Status, row.Attempts, row.LeaseToken));
            Assert.Empty(fixture.Script.Invocations);
        }

        [Fact]
        public async Task A_frozen_tenant_is_never_polled()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            await using var worker = Worker.Create(fixture, TenantState.Frozen);

            await worker.StartAsync();
            await Task.Delay(TimeSpan.FromSeconds(2), Token);

            Assert.Empty(worker.Instance.Polled);
            Assert.Equal((null, null), await WorkerStateAsync());
            Assert.Equal(nameof(JobStatus.Pending), (await fixture.JobAsync(id)).Status);
        }

        // --- The drain ---

        [Fact]
        public async Task A_drain_stops_a_handler_that_observes_the_host_token_and_its_untouched_job_is_released_uncounted()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Script.Probe = async (batch, _) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, batch.Items[0].CancellationToken);
            };
            // A drain long enough for the batch's own end on a loaded server, its release completion
            // included: the handler returns at the host token, and the test waits no longer than that.
            await using var worker = Worker.Create(fixture, TenantState.Active, drainTimeout: TimeSpan.FromSeconds(20));
            await worker.StartAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

            await worker.StopAsync();

            // --- The handler returned within the drain: the row is free again, its attempt not counted ---
            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Pending), 0, null), (row.Status, row.Attempts, row.LeaseToken));
            Assert.DoesNotContain(fixture.Log.GetSnapshot(), static record => record.Id.Name is "WorkerDrainTimedOut" or "JobHandlerAbandoned");
        }

        [Fact]
        public async Task A_drain_that_times_out_renews_the_running_lease_once_by_the_shutdown_timeout_and_abandons_the_handler()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Script.Probe = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
            };
            await using var worker = Worker.Create(fixture, TenantState.Active);
            await worker.StartAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

            await worker.StopAsync();

            // --- The row is still leased, now until the shutdown timeout from the final renewal ---
            Assert.Equal(nameof(JobStatus.Running), (await fixture.JobAsync(id)).Status);
            int remaining = await SqlServerFixture.ScalarAsync<int>(
                fixture.ConnectionString, Invariant($"SELECT DATEDIFF(millisecond, SYSUTCDATETIME(), [LeaseExpiresAt]) FROM [core].[Jobs] WHERE [Id] = {id};"));
            Assert.InRange(remaining, 1, (int)Worker.ShutdownTimeout.TotalMilliseconds);
            FakeLogRecord abandoned = Assert.Single(fixture.Log.GetSnapshot(), static record => record.Id.Name == "JobHandlerAbandoned");
            Assert.Equal(LogLevel.Warning, abandoned.Level);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "WorkerDrainTimedOut");

            // --- The abandoned handler ends on its own; the dispatch then holds nothing ---
            release.SetResult();
            await EventuallyAsync(() => Task.FromResult(worker.Dispatcher.Running == 0));
        }

        /// <summary>The fixture tenant's id.</summary>
        private static int FixtureTenantId => Tellma.Testing.Entities.FixtureTenant.TenantId;

        /// <summary>A poll plan of the fixture tenant, active and not stopping.</summary>
        /// <param name="claims">The keys to claim, each with no slot reserved.</param>
        /// <param name="sampleBacklog">Whether the poll carries the backlog sample.</param>
        /// <param name="sampleOrphans">Whether the poll carries the orphan sample and the retired-key cancel.</param>
        /// <returns>The plan.</returns>
        private JobPollPlan Plan(IReadOnlyList<string>? claims = null, bool sampleBacklog = false, bool sampleOrphans = false)
        {
            JobHandlerRegistry registry = fixture.Tenant.Services.GetRequiredService<JobHandlerRegistry>();
            JobPollClaimRequest[] requests = [.. (claims ?? []).Select(key => new JobPollClaimRequest(
                registry.TryFind(key, out JobHandlerRegistration? handler) ? handler : throw new InvalidOperationException($"The fixture registers {key}."),
                Reservation: null))];
            return new JobPollPlan(FixtureTenantId, ReadOnly: false, Stopping: false, requests, sampleBacklog, sampleOrphans, JobLeaseOwner.Current);
        }

        /// <summary>Runs one poll on the host's poll, or on a poll with the given tick.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="tick">The tick the poll carries; null for the host's own poll.</param>
        /// <returns>What the poll returned.</returns>
        private Task<JobPollResult> PollAsync(JobPollPlan plan, IScheduleTick? tick = null)
        {
            IServiceProvider services = fixture.Tenant.Services;
            IJobPoll poll = tick is null
                ? services.GetRequiredService<IJobPoll>()
                : new JobPoll(
                    services.GetRequiredService<ITenantScopeFactory>(),
                    services.GetRequiredService<JobHandlerRegistry>(),
                    [tick],
                    services.GetRequiredService<IOptions<JobsOptions>>(),
                    services.GetRequiredService<IOptions<NotificationsOptions>>(),
                    services.GetRequiredService<JobsMetrics>(),
                    TimeProvider.System,
                    services.GetRequiredService<ILoggerFactory>());
            return poll.PollAsync(plan, static _ => { }, Token);
        }

        /// <summary>Sets the worker's heartbeat stamp in the past.</summary>
        /// <param name="secondsAgo">How long ago the stamp is.</param>
        /// <returns>A task that completes when the stamp is set.</returns>
        private Task StampAsync(int secondsAgo)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"UPDATE [core].[JobWorkerState] SET [LastTickAt] = DATEADD(second, -{secondsAgo}, SYSUTCDATETIME()), [LastTickOwner] = N'elsewhere/1';"));
        }

        /// <summary>Reads the worker's heartbeat stamp and its owner.</summary>
        /// <returns>The stamp and owner; nulls before the first stamp.</returns>
        private async Task<(DateTimeOffset? LastTickAt, string? Owner)> WorkerStateAsync()
        {
            await using Microsoft.Data.SqlClient.SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [LastTickAt], [LastTickOwner] FROM [core].[JobWorkerState];";
            await using Microsoft.Data.SqlClient.SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token), "The worker state row is seeded.");
            return (reader.IsDBNull(0) ? null : reader.GetDateTimeOffset(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        }

        /// <summary>Leases a stored job to another instance.</summary>
        /// <param name="id">The job.</param>
        /// <param name="leaseSeconds">How long the lease has left; negative for a lapsed one.</param>
        /// <returns>The job's id.</returns>
        private async Task<int> LeaseAsync(int id, int leaseSeconds)
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"UPDATE [core].[Jobs] SET [Status] = 'Running', [Attempts] = 1, [LeaseToken] = NEWID(), [LeaseExpiresAt] = DATEADD(second, {leaseSeconds}, SYSUTCDATETIME()), [LeaseOwner] = N'elsewhere/1' WHERE [Id] = {id};"));
            return id;
        }

        /// <summary>Moves a stored job to a status no lease goes with.</summary>
        /// <param name="id">The job.</param>
        /// <param name="status">The status.</param>
        /// <returns>The job's id.</returns>
        private async Task<int> StatusAsync(int id, JobStatus status)
        {
            string warnings = status == JobStatus.PendingConfirmation ? "N'[]'" : "NULL";
            string completed = status == JobStatus.Succeeded ? "SYSUTCDATETIME()" : "NULL";
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"UPDATE [core].[Jobs] SET [Status] = '{status}', [PendingConfirmationsJson] = {warnings}, [CompletedAt] = {completed} WHERE [Id] = {id};"));
            return id;
        }

        /// <summary>Waits until a condition holds, thirty seconds at most.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes once it held.</returns>
        private static async Task EventuallyAsync(Func<Task<bool>> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!await condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }

        /// <summary>How a scripted tick's statement fails.</summary>
        private enum TickFault
        {
            /// <summary>Its first run fails with a transient error; the re-run succeeds.</summary>
            Transient,

            /// <summary>Every run fails with an error no re-run answers.</summary>
            Final,

            /// <summary>Its first run waits for a gate the test holds, until the test kills its session.</summary>
            Killed,
        }

        /// <summary>
        ///     A tick that notes each run of its statement, with whether the heartbeat was stamped in the
        ///     last hour, and then fails as scripted.
        /// </summary>
        /// <param name="fault">How its statement fails.</param>
        private sealed class ScriptedTick(TickFault fault) : IScheduleTick, IScheduleTickRound
        {
            /// <summary>The application lock the first run of a killed tick waits for.</summary>
            public const string Gate = "fx-tick-gate";

            /// <summary>The raw table the statement notes its runs in.</summary>
            private static readonly IReadOnlySet<TableName> RunLog = new HashSet<TableName> { new("test", "RunLog") };

            /// <summary>How many times step 2 ran.</summary>
            private int _secondSteps;

            /// <summary>How many times step 2 ran.</summary>
            public int SecondSteps => Volatile.Read(ref _secondSteps);

            /// <inheritdoc />
            public int Leased => 0;

            /// <inheritdoc />
            public bool Full => false;

            /// <inheritdoc />
            public IScheduleTickRound Append(DataBatch batch, ScheduleTickContext context)
            {
                ArgumentNullException.ThrowIfNull(batch);
                Assert.Equal(FixtureTenantId, context.TenantId);
                _ = batch.Sql(
                    $"""
                    INSERT INTO [test].[RunLog] ([JobId], [Note])
                    SELECT 0, CASE WHEN [LastTickAt] >= DATEADD(hour, -1, SYSUTCDATETIME()) THEN N'tick: fresh' ELSE N'tick: stale' END
                    FROM [core].[JobWorkerState];
                    DECLARE @fx_tick_runs int = (SELECT COUNT(*) FROM [test].[RunLog] WHERE [Note] LIKE N'tick:%');
                    IF {fault == TickFault.Final} = 1
                        THROW 50600, N'Fixture.Stop', 1;
                    IF {fault == TickFault.Transient} = 1 AND @fx_tick_runs = 1
                        THROW {TellmaSqlErrors.Transient}, N'Fixture.Busy', 1;
                    IF {fault == TickFault.Killed} = 1 AND @fx_tick_runs = 1
                        EXEC sp_getapplock @Resource = {Gate}, @LockMode = 'Shared', @LockOwner = 'Session', @LockTimeout = 60000;
                    """,
                    new SqlOptions(writes: RunLog, idempotent: true));
                return this;
            }

            /// <inheritdoc />
            public Task RunStep2Async(IServiceProvider services, CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _secondSteps);
                return Task.CompletedTask;
            }
        }

        /// <summary>Compares notification requests by value, their recipients and arguments included.</summary>
        private sealed class NoticeComparer : IEqualityComparer<NotificationRequest>
        {
            /// <summary>The one instance.</summary>
            public static NoticeComparer Instance { get; } = new();

            /// <inheritdoc />
            public bool Equals(NotificationRequest? x, NotificationRequest? y)
            {
                return x is not null && y is not null
                    && x.Type == y.Type
                    && x.RecipientIds.SequenceEqual(y.RecipientIds)
                    && Equals(x.Arguments, y.Arguments)
                    && x.DedupKey == y.DedupKey
                    && (x.TargetResource, x.TargetId, x.ActorUserId, x.OnDuplicate, x.InboxOnly) == (y.TargetResource, y.TargetId, y.ActorUserId, y.OnDuplicate, y.InboxOnly);
            }

            /// <inheritdoc />
            public int GetHashCode(NotificationRequest obj)
            {
                ArgumentNullException.ThrowIfNull(obj);
                return obj.Type.GetHashCode(StringComparison.Ordinal);
            }
        }

        /// <summary>A tenant registry over the fixture tenant whose state a test sets.</summary>
        /// <param name="inner">The fixture host's registry.</param>
        /// <param name="state">The tenant's state at first.</param>
        private sealed class StateRegistry(ITenantRegistry inner, TenantState state) : ITenantRegistry
        {
            /// <summary>The tenant as the registry holds it now.</summary>
            private TenantInfo _tenant = WithState(inner.Get(FixtureTenantId), state);

            /// <inheritdoc />
            public IReadOnlyList<TenantInfo> Tenants => [Volatile.Read(ref _tenant)];

            /// <inheritdoc />
            public Guid SnapshotVersion => inner.SnapshotVersion;

            /// <inheritdoc />
            public DateTimeOffset? SnapshotLoadedAt => inner.SnapshotLoadedAt;

            /// <inheritdoc />
            public bool IsAvailable => true;

            /// <summary>Sets the tenant's state.</summary>
            /// <param name="next">The state.</param>
            /// <returns>The tenant's descriptor in that state.</returns>
            public TenantDescriptor Set(TenantState next)
            {
                TenantInfo tenant = WithState(Volatile.Read(ref _tenant), next);
                Volatile.Write(ref _tenant, tenant);
                return tenant.Descriptor;
            }

            /// <inheritdoc />
            public TenantInfo? Find(int tenantId)
            {
                return tenantId == FixtureTenantId ? Volatile.Read(ref _tenant) : null;
            }

            /// <inheritdoc />
            public TenantInfo Get(int tenantId)
            {
                return Find(tenantId) ?? throw new TenantNotFoundException(tenantId);
            }

            /// <inheritdoc />
            public Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <summary>A tenant in another state.</summary>
            /// <param name="tenant">The tenant.</param>
            /// <param name="next">The state.</param>
            /// <returns>The tenant in that state.</returns>
            private static TenantInfo WithState(TenantInfo tenant, TenantState next)
            {
                return tenant with { Descriptor = tenant.Descriptor with { State = next } };
            }
        }

        /// <summary>
        ///     A worker of its own over the fixture host's job machinery: its own dispatch, renewal
        ///     schedule, registry and recorded poll, a one-second drain (or a longer one a test asks
        ///     for) and a shutdown timeout three seconds past the drain. The fixture's audit is
        ///     suspended while it runs, since its batches run side by side. It is stopped as the host
        ///     would stop it, once, when the test stops it or disposes it.
        /// </summary>
        private sealed class Worker : IAsyncDisposable
        {
            /// <summary>The host's shutdown timeout the worker is given.</summary>
            public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

            /// <summary>The class's database, whose audit is suspended while the worker runs.</summary>
            private readonly JobRunDatabase _fixture;

            /// <summary>Whether the worker was stopped.</summary>
            private bool _stopped;

            /// <summary>Wraps a composed worker.</summary>
            /// <param name="fixture">The class's database.</param>
            /// <param name="instance">The worker.</param>
            /// <param name="registry">Its registry.</param>
            /// <param name="dispatcher">Its dispatch.</param>
            /// <param name="renewals">Its renewal schedule.</param>
            /// <param name="polls">Its poll, recorded.</param>
            private Worker(JobRunDatabase fixture, JobWorker instance, StateRegistry registry, JobDispatcher dispatcher, JobRenewalScheduler renewals, RecordedPoll polls)
            {
                _fixture = fixture;
                Instance = instance;
                Registry = registry;
                Dispatcher = dispatcher;
                Renewals = renewals;
                Polls = polls;
            }

            /// <summary>The worker.</summary>
            public JobWorker Instance { get; }

            /// <summary>The registry the worker follows.</summary>
            public StateRegistry Registry { get; }

            /// <summary>The worker's dispatch.</summary>
            public JobDispatcher Dispatcher { get; }

            /// <summary>The worker's poll, which records what each round trip returned.</summary>
            public RecordedPoll Polls { get; }

            /// <summary>The worker's renewal schedule.</summary>
            private JobRenewalScheduler Renewals { get; }

            /// <summary>Composes a worker over the fixture host.</summary>
            /// <param name="fixture">The class's database.</param>
            /// <param name="state">The tenant's state at first.</param>
            /// <param name="drainTimeout">How long the drain waits; one second when null.</param>
            /// <returns>The worker, not started.</returns>
            public static Worker Create(JobRunDatabase fixture, TenantState state, TimeSpan? drainTimeout = null)
            {
                IServiceProvider services = fixture.Tenant.Services;
                StateRegistry registry = new(services.GetRequiredService<ITenantRegistry>(), state);
                JobDispatcher dispatcher = ActivatorUtilities.CreateInstance<JobDispatcher>(services);
                JobRenewalScheduler renewals = ActivatorUtilities.CreateInstance<JobRenewalScheduler>(services);
                RecordedPoll polls = new(services.GetRequiredService<IJobPoll>());
                JobsOptions options = services.GetRequiredService<IOptions<JobsOptions>>().Value;
                options = new JobsOptions
                {
                    Enabled = true,
                    MinPollInterval = options.MinPollInterval,
                    MaxPollInterval = TimeSpan.FromSeconds(1),
                    MaxParallelBatches = options.MaxParallelBatches,
                    DrainTimeout = drainTimeout ?? TimeSpan.FromSeconds(1),
                    GapThreshold = options.GapThreshold,
                };
                JobWorker worker = ActivatorUtilities.CreateInstance<JobWorker>(
                    services,
                    registry,
                    dispatcher,
                    renewals,
                    polls,
                    Options.Create(options),
                    Options.Create(new HostOptions { ShutdownTimeout = drainTimeout is TimeSpan drain ? drain + ShutdownTimeout : ShutdownTimeout }));
                return new Worker(fixture, worker, registry, dispatcher, renewals, polls);
            }

            /// <summary>Starts the worker as the host starts a hosted service, the fixture's audit suspended.</summary>
            /// <returns>A task that completes once the worker started.</returns>
            public async Task StartAsync()
            {
                _fixture.Audit.Suspended = true;
                await Instance.StartingAsync(Token);
                await Instance.StartAsync(Token);
                await Instance.StartedAsync(Token);
            }

            /// <summary>Stops the worker as the host stops a hosted service: its drain, then its stop.</summary>
            /// <returns>A task that completes once the worker stopped.</returns>
            public async Task StopAsync()
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                await Instance.StoppingAsync(Token);
                await Instance.StopAsync(Token);
                await Instance.StoppedAsync(Token);
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                await StopAsync();
                Instance.Dispose();
                Dispatcher.Dispose();
                Renewals.Dispose();
            }
        }

        /// <summary>A poll that records what each round trip returned, before the worker takes its claims.</summary>
        /// <param name="inner">The host's poll.</param>
        private sealed class RecordedPoll(IJobPoll inner) : IJobPoll
        {
            /// <summary>What each round trip returned, in order.</summary>
            public ConcurrentQueue<JobPollResult> Results { get; } = new();

            /// <inheritdoc />
            public Task<JobPollResult> PollAsync(JobPollPlan plan, Action<JobPollResult> onExecuted, CancellationToken cancellationToken)
            {
                return inner.PollAsync(
                    plan,
                    result =>
                    {
                        Results.Enqueue(result);
                        onExecuted(result);
                    },
                    cancellationToken);
            }
        }

        /// <summary>Collects every job instrument the fixture host records while a test runs.</summary>
        private sealed class JobInstruments : IDisposable
        {
            /// <summary>Starts collecting from the fixture host's meters.</summary>
            /// <param name="fixture">The class's database.</param>
            public JobInstruments(JobRunDatabase fixture)
            {
                IMeterFactory meters = fixture.Tenant.Services.GetRequiredService<IMeterFactory>();
                Claimed = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.Claimed);
                Completed = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.Completed);
                Duration = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.Duration);
                QueueLatency = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.QueueLatency);
                BatchFill = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.BatchFill);
                InFlight = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.InFlight);
                LeaseLost = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.LeaseLost);
                BacklogAge = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.BacklogAge);
                PollDuration = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.PollDuration);
                Orphaned = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.Orphaned);
                GapDetected = new(meters, JobsTelemetryNames.MeterName, JobsTelemetryNames.SchedulesGapDetected);
            }

            /// <summary>Jobs claimed.</summary>
            public MetricCollector<long> Claimed { get; }

            /// <summary>Jobs completed.</summary>
            public MetricCollector<long> Completed { get; }

            /// <summary>Handler invocations' durations.</summary>
            public MetricCollector<double> Duration { get; }

            /// <summary>The waits from due to first claim.</summary>
            public MetricCollector<double> QueueLatency { get; }

            /// <summary>The claims' fill.</summary>
            public MetricCollector<double> BatchFill { get; }

            /// <summary>The jobs in flight.</summary>
            public MetricCollector<long> InFlight { get; }

            /// <summary>The batches whose lease was lost.</summary>
            public MetricCollector<long> LeaseLost { get; }

            /// <summary>The keys' backlog ages.</summary>
            public MetricCollector<double> BacklogAge { get; }

            /// <summary>The poll round trips' durations.</summary>
            public MetricCollector<double> PollDuration { get; }

            /// <summary>The orphaned jobs.</summary>
            public MetricCollector<long> Orphaned { get; }

            /// <summary>The polls that found a gap.</summary>
            public MetricCollector<long> GapDetected { get; }

            /// <summary>A collector's measurements as their handler tag and value, in the order recorded.</summary>
            /// <typeparam name="T">The measurements' type.</typeparam>
            /// <param name="collector">The collector.</param>
            /// <returns>The handler tag, null when absent, and the value of each measurement.</returns>
            public static IReadOnlyList<(string? Handler, T Value)> ByHandler<T>(MetricCollector<T> collector)
                where T : struct
            {
                return [.. collector.GetMeasurementSnapshot().Select(static measurement => (measurement.Tags.TryGetValue(JobsTelemetryNames.HandlerTag, out object? handler) ? handler as string : null, measurement.Value))];
            }

            /// <summary>The orphan gauge's value now.</summary>
            /// <returns>The value.</returns>
            public long OrphanedNow()
            {
                Orphaned.RecordObservableInstruments();
                return Orphaned.LastMeasurement?.Value ?? throw new InvalidOperationException("The orphan gauge reported no value.");
            }

            /// <inheritdoc />
            public void Dispose()
            {
                Claimed.Dispose();
                Completed.Dispose();
                Duration.Dispose();
                QueueLatency.Dispose();
                BatchFill.Dispose();
                InFlight.Dispose();
                LeaseLost.Dispose();
                BacklogAge.Dispose();
                PollDuration.Dispose();
                Orphaned.Dispose();
                GapDetected.Dispose();
            }
        }
    }
}
