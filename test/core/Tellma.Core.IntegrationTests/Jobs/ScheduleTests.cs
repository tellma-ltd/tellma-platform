// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Jobs.JobRunDatabase;
using SaveOptions = Tellma.Core.Abstractions.Crud.SaveOptions;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     The scheduler and the schedules' stack against a fixture database. The tick, riding real
    ///     polls: an uninitialised built-in takes its next due time without firing and then fires as
    ///     the platform, a stopped schedule is never leased, the overlap rule and the skip stamp, the
    ///     pauses with their one write and their notice, several schedules and a replay in one tick,
    ///     a read-only tenant's tick, the lease fence, a lease lost before step 2, the parking of a
    ///     schedule the host cannot evaluate, and the plan of the lease. The stack, through a
    ///     stepped-up user's scope: the rules a save and an activation check, the lease a take-over
    ///     and a save release, the take-over and the activation that withdraws the paused notice, the
    ///     delete that detaches the jobs and withdraws the notice, the run-now action, and the state
    ///     and permissions extras.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class ScheduleTests(JobRunDatabase fixture) : IClassFixture<JobRunDatabase>, IAsyncLifetime
    {
        /// <summary>The namespace of a showplan document.</summary>
        private static readonly XNamespace Showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        /// <summary>The name of the column a returned plan arrives in.</summary>
        private const string ShowplanColumn = "Microsoft SQL Server 2005 XML Showplan";

        /// <summary>An expression that occurs every minute, in every zone alike.</summary>
        private const string EveryMinute = "* * * * *";

        /// <summary>The zone of a schedule pinned to minutes in UTC.</summary>
        private const string Utc = "Etc/UTC";

        /// <summary>An expression that occurs once a day, at 03:00 in the zone it is evaluated in.</summary>
        private const string DailyAtThree = "0 3 * * *";

        /// <summary>A zone three hours ahead of UTC, all year.</summary>
        private const string RiyadhZone = "Asia/Riyadh";

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
            return ValueTask.CompletedTask;
        }

        // --- The tick: the built-ins and the stopped schedules ---

        [Fact]
        public async Task An_uninitialised_built_in_takes_its_next_due_time_without_firing_and_once_due_fires_as_the_platform()
        {
            // --- The first tick initialises the seeded built-in from the database's clock, in the tenant's zone ---
            DateTimeOffset before = await DatabaseNowAsync();
            _ = await TickAsync();
            DateTimeOffset after = await DatabaseNowAsync();

            // The tick read the database's clock between the two reads, so its next due time is the
            // expression's next occurrence after one of them: both give the same one unless the
            // occurrence itself fell between them.
            Assert.Empty(await JobsOfAsync(FixtureFeature.ProbeScheduleId));
            Row state = await StateAsync(FixtureFeature.ProbeScheduleId);
            var builtIn = Cronos.CronExpression.Parse(BuiltInCron);
            TimeZoneInfo zone = await TenantZoneAsync();
            DateTimeOffset[] expected = [.. new[] { before, after }.Select(at => builtIn.GetNextOccurrence(at, zone)!.Value.ToUniversalTime()).Distinct()];
            Assert.Contains(state.Get<DateTimeOffset>("NextDueAt"), expected);
            Assert.Equal((null, null, null, null), (state["LeaseToken"], state["LastFiredAt"], state["LastScheduledFor"], state["LastSkippedAt"]));

            // --- Once its expression names the last three minutes and its state is behind, the next tick fires it as the platform ---
            (DateTimeOffset minute, string cron, DateTimeOffset next) = await PinnedMinutesAsync();
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Sql($"UPDATE [core].[Schedules] SET [CronExpression] = N'{cron}', [TimeZoneId] = N'{Utc}' WHERE [Id] = {FixtureFeature.ProbeScheduleId};"),
                Sql($"UPDATE [core].[ScheduleStates] SET [NextDueAt] = {Literal(minute.AddMinutes(-2))} WHERE [ScheduleId] = {FixtureFeature.ProbeScheduleId};"));
            _ = await TickAsync();

            Row job = Assert.Single(await JobsOfAsync(FixtureFeature.ProbeScheduleId));
            Assert.Equal(
                (FixtureJobKeys.Probe, nameof(JobStatus.Pending), WellKnownIds.SystemUserId, null, minute),
                (job.Get<string>("HandlerKey"), job.Get<string>("Status"), job.Get<int>("RunAsUserId"), job["RequestedById"], job.Get<DateTimeOffset>("ScheduledFor")));
            state = await StateAsync(FixtureFeature.ProbeScheduleId);
            Assert.Equal((minute, next, job.Get<int>("Id")), (state.Get<DateTimeOffset>("LastScheduledFor"), state.Get<DateTimeOffset>("NextDueAt"), state.Get<int>("LastJobId")));
            Assert.Null(state["LeaseToken"]);
        }

        [Fact]
        public async Task A_deactivated_schedule_is_never_leased_whether_a_built_in_or_a_user_schedule()
        {
            // --- A user schedule deactivated through its stack, and a built-in stopped behind the stack's back ---
            int user = await InsertScheduleAsync(FixtureJobKeys.Probe, nextDueAt: (await DatabaseNowAsync()).AddMinutes(-5));
            await using (AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId))
            {
                _ = await Schedules(scope).DeactivateAsync([user], cancellationToken: Token);
            }

            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Sql($"UPDATE [core].[Schedules] SET [IsActive] = 0 WHERE [Id] = {FixtureFeature.ProbeScheduleId};"));

            JobPollResult result = await TickAsync();

            // --- Neither is leased: the deactivation nulled the user schedule's next due time, and the built-in is inactive ---
            Assert.Equal(0, result.Tick?.Leased);
            foreach (int id in new[] { user, FixtureFeature.ProbeScheduleId })
            {
                Row state = await StateAsync(id);
                Assert.Equal((null, null), (state["NextDueAt"], state["LeaseToken"]));
                Assert.Empty(await JobsOfAsync(id));
            }
        }

        // --- The tick: the overlap rule and the skip stamp ---

        [Fact]
        public async Task A_skip_schedule_whose_job_is_held_or_waits_on_a_confirmation_fires_nothing_and_alone_has_its_skip_stamped()
        {
            (DateTimeOffset minute, string cron, DateTimeOffset next) = await PinnedMinutesAsync();
            DateTimeOffset due = minute.AddMinutes(-1);
            int held = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, nextDueAt: due);
            int waiting = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, nextDueAt: due);
            int allowed = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, overlap: OverlapPolicy.Allow, nextDueAt: due);
            int ended = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, nextDueAt: due);
            await InsertScheduleJobAsync(held, JobStatus.Held);
            await InsertScheduleJobAsync(waiting, JobStatus.PendingConfirmation);
            await InsertScheduleJobAsync(allowed, JobStatus.Pending);
            await InsertScheduleJobAsync(ended, JobStatus.Succeeded);

            // A skip-missed schedule whose one occurrence is too old to fire: leased, fires nothing, and no overlap skipped it.
            DateTimeOffset stale = minute.AddMinutes(-10);
            int late = await InsertScheduleAsync(
                FixtureJobKeys.Probe, cron: string.Create(CultureInfo.InvariantCulture, $"{stale.Minute} {stale.Hour} * * *"), zone: Utc, missed: MissedPolicy.Skip, nextDueAt: stale);

            _ = await TickAsync();

            // --- The two schedules a job of theirs still occupies fire nothing; each is advanced with its skip stamped ---
            foreach (int id in new[] { held, waiting })
            {
                Assert.Single(await JobsOfAsync(id));
                Row state = await StateAsync(id);
                Assert.NotNull(state["LastSkippedAt"]);
                Assert.Equal((null, null, next), (state["LastFiredAt"], state["LastJobId"], state.Get<DateTimeOffset>("NextDueAt")));
            }

            // --- The allowing schedule and the one whose job ended fire, unstamped ---
            foreach (int id in new[] { allowed, ended })
            {
                IReadOnlyList<Row> jobs = await JobsOfAsync(id);
                Assert.Equal(2, jobs.Count);
                Row state = await StateAsync(id);
                Assert.Null(state["LastSkippedAt"]);
                Assert.Equal((jobs[1].Get<int>("Id"), minute), (state.Get<int>("LastJobId"), state.Get<DateTimeOffset>("LastScheduledFor")));
            }

            // --- The stale occurrence was dropped by its policy, which stamps no skip ---
            Assert.Empty(await JobsOfAsync(late));
            Row lateState = await StateAsync(late);
            Assert.Equal((null, null, null), (lateState["LastSkippedAt"], lateState["LastFiredAt"], lateState["LeaseToken"]));
            Assert.True(lateState.Get<DateTimeOffset>("NextDueAt") > minute);
        }

        // --- The tick: the pauses ---

        [Fact]
        public async Task A_schedule_whose_owner_is_inactive_pauses_in_one_write_and_its_notice_goes_to_the_administrators()
        {
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe, runAs: InactiveUserId, name: "Nightly", nextDueAt: (await DatabaseNowAsync()).AddMinutes(-1));
            int versions = await HistoryCountAsync(id);

            _ = await TickAsync();

            // --- One update of the schedule: inactive, the reason stamped by the system user, one more version in its history ---
            Row schedule = await ScheduleAsync(id);
            Assert.Equal((false, nameof(SchedulePausedReason.OwnerInactive), WellKnownIds.SystemUserId), (schedule.Get<bool>("IsActive"), schedule.Get<string>("PausedReason"), schedule.Get<int>("ModifiedById")));
            Assert.Equal(versions + 1, await HistoryCountAsync(id));

            // --- Nothing fired, nothing is due, the lease is released ---
            Assert.Empty(await JobsOfAsync(id));
            Row state = await StateAsync(id);
            Assert.Equal((null, null), (state["NextDueAt"], state["LeaseToken"]));

            // --- The notice: to the administrators, deduplicated on the schedule, after the commit ---
            NotificationRequest notice = Assert.Single(fixture.Notices.Raised);
            Assert.Equal(
                (JobNotices.SchedulePausedType, CoreResources.Schedule, id, JobNotices.SchedulePausedKey(id)),
                (notice.Type, notice.TargetResource, notice.TargetId, notice.DedupKey));
            Assert.Equal([AdministratorId], notice.RecipientIds);
            SchedulePausedArguments arguments = Assert.IsType<SchedulePausedArguments>(notice.Arguments);
            Assert.Equal((id, "Nightly", nameof(SchedulePausedReason.OwnerInactive)), (arguments.ScheduleId, arguments.Name.Text, arguments.Reason));
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "SchedulePaused");
        }

        [Fact]
        public async Task A_schedule_whose_key_was_retired_pauses_and_its_notice_goes_to_its_owner()
        {
            int id = await InsertScheduleAsync(JobRunKeys.Retired, nextDueAt: (await DatabaseNowAsync()).AddMinutes(-1));

            _ = await TickAsync();

            Row schedule = await ScheduleAsync(id);
            Assert.Equal((false, nameof(SchedulePausedReason.HandlerRetired)), (schedule.Get<bool>("IsActive"), schedule.Get<string>("PausedReason")));
            Assert.Empty(await JobsOfAsync(id));
            NotificationRequest notice = Assert.Single(fixture.Notices.Raised);
            Assert.Equal([ActiveUserId], notice.RecipientIds);
            Assert.Equal(JobNotices.SchedulePausedKey(id), notice.DedupKey);
        }

        // --- The tick: a step 2 whose commit only the probe proved ---

        [Fact]
        public async Task A_tick_whose_step_two_response_is_lost_after_its_commit_still_wakes_the_poller_for_the_jobs_it_fired()
        {
            DateTimeOffset due = (await DatabaseNowAsync()).AddMinutes(-1);
            int fired = await InsertScheduleAsync(FixtureJobKeys.Probe, zone: Utc, nextDueAt: due);
            int paused = await InsertScheduleAsync(FixtureJobKeys.Probe, runAs: InactiveUserId, nextDueAt: due);

            // Step 2 is the tick's one batch that writes the schedules: the pause's.
            fixture.Audit.LoseResponse(JobStatements.Schedules);
            _ = await TickAsync();

            // --- Step 2 committed: one schedule fired its job, the other paused ---
            Assert.Single(await JobsOfAsync(fired));
            Assert.Equal(nameof(SchedulePausedReason.OwnerInactive), (await ScheduleAsync(paused)).Get<string>("PausedReason"));

            // --- No commit callback ran, yet the poller was woken to claim the fired job and the pause was announced ---
            Assert.Empty(fixture.Notices.Raised);
            Assert.True(fixture.Wakes > 0);
            NotificationRequest notice = Assert.Single(fixture.Notices.Proven);
            Assert.Equal(JobNotices.SchedulePausedKey(paused), notice.DedupKey);
            Assert.DoesNotContain(fixture.Log.GetSnapshot(), static record => record.Id.Name is "SchedulerTickFailed" or "SchedulerTickLeaseLost");
        }

        // --- The tick: several schedules at once ---

        [Fact]
        public async Task One_tick_fires_two_schedules_one_replaying_three_occurrences_with_ids_from_one_range_in_schedule_then_occurrence_order()
        {
            (DateTimeOffset minute, string cron, DateTimeOffset next) = await PinnedMinutesAsync();
            int coalesced = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, nextDueAt: minute.AddMinutes(-1));
            int replayed = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: cron, zone: Utc, missed: MissedPolicy.ReplayAll, overlap: OverlapPolicy.Allow, catchUpWindowMinutes: 60, nextDueAt: minute.AddMinutes(-2));

            JobPollResult result = await TickAsync();

            // --- Three leased, the third the seeded built-in the tick initialises; one job for the coalescing
            // schedule, its latest occurrence, and three for the replaying one, oldest first ---
            Assert.Equal(3, result.Tick?.Leased);
            Row single = Assert.Single(await JobsOfAsync(coalesced));
            IReadOnlyList<Row> replays = await JobsOfAsync(replayed);
            Assert.Equal(minute, single.Get<DateTimeOffset>("ScheduledFor"));
            Assert.Equal([minute.AddMinutes(-2), minute.AddMinutes(-1), minute], replays.Select(static job => job.Get<DateTimeOffset>("ScheduledFor")));

            // --- The four ids are one range, in schedule order and then occurrence order ---
            int first = single.Get<int>("Id");
            Assert.Equal([first + 1, first + 2, first + 3], replays.Select(static job => job.Get<int>("Id")));

            // --- Each job is the owner's own request; each state points at its last job and is due at the expression's next occurrence ---
            Assert.All(replays.Append(single), static job => Assert.Equal((ActiveUserId, ActiveUserId, nameof(JobStatus.Pending)), (job.Get<int>("RunAsUserId"), job.Get<int>("RequestedById"), job.Get<string>("Status"))));
            Row coalescedState = await StateAsync(coalesced);
            Row replayedState = await StateAsync(replayed);
            Assert.Equal((first, minute, next), (coalescedState.Get<int>("LastJobId"), coalescedState.Get<DateTimeOffset>("LastScheduledFor"), coalescedState.Get<DateTimeOffset>("NextDueAt")));
            Assert.Equal((first + 3, minute, next), (replayedState.Get<int>("LastJobId"), replayedState.Get<DateTimeOffset>("LastScheduledFor"), replayedState.Get<DateTimeOffset>("NextDueAt")));
        }

        [Fact]
        public async Task A_read_only_tenants_tick_fires_only_the_schedules_whose_handlers_run_then_and_the_rest_once_it_is_active()
        {
            DateTimeOffset due = (await DatabaseNowAsync()).AddMinutes(-1);
            int probe = await InsertScheduleAsync(FixtureJobKeys.Probe, nextDueAt: due);
            int sweep = await InsertScheduleAsync(JobRunKeys.Sweep, nextDueAt: due);

            // --- Read-only: the sweep fires; the probe is not even leased, so it stays due ---
            JobPollResult readOnly = await TickAsync(readOnly: true);

            Assert.Equal(1, readOnly.Tick?.Leased);
            Assert.Single(await JobsOfAsync(sweep));
            Assert.Empty(await JobsOfAsync(probe));
            Row probeState = await StateAsync(probe);
            Assert.Equal((due, null), (probeState.Get<DateTimeOffset>("NextDueAt"), probeState["LeaseToken"]));

            // --- Active again: the probe fires ---
            _ = await TickAsync();

            Assert.Single(await JobsOfAsync(probe));
        }

        // --- The tick's statements on their own ---

        [Fact]
        public async Task Tick_step_one_run_again_returns_its_rows_and_step_two_fails_on_the_fence_once_a_save_released_the_lease()
        {
            DateTimeOffset now = await DatabaseNowAsync();
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe, nextDueAt: now.AddMinutes(-1));
            var token = Guid.NewGuid();

            // --- Step 1 leases the schedule and the uninitialised seeded built-in; run again under its token, it returns the same rows ---
            Assert.Equal([FixtureFeature.ProbeScheduleId, id], (await MaintenanceAsync(batch => JobStatements.TickStep1(batch, token, readOnlyKeys: null))).Select(static row => row.Id));
            Assert.Equal([FixtureFeature.ProbeScheduleId, id], (await MaintenanceAsync(batch => JobStatements.TickStep1(batch, token, readOnlyKeys: null))).Select(static row => row.Id));
            Assert.Equal(token, (await StateAsync(id)).Get<Guid>("LeaseToken"));

            // --- A save's state upsert releases the lease, as a change of the expression does ---
            DateTimeOffset saved = now.AddHours(1);
            await PersistAsync(batch => JobStatements.UpsertScheduleStates(batch, [new ScheduleNextList { ScheduleId = id, NextDueAt = saved }]));

            // --- Step 2 under the old token fails on its fence, and its transaction inserts nothing ---
            Job job = new() { Id = 1, HandlerKey = FixtureJobKeys.Probe, DueAt = now, RunAsUserId = ActiveUserId, RequestedById = ActiveUserId, ScheduleId = id, ScheduledFor = now, CreatedAt = now };
            BatchAssertionFailedException lost = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => PersistAsync(batch => _ = JobStatements.TickStep2(
                batch, token, [job], [new ScheduleNextList { ScheduleId = id, NextDueAt = now.AddMinutes(1), LastScheduledFor = now }])));

            Assert.Equal((TellmaSqlErrors.Invariant, JobErrorCodes.TickLeaseLost), (lost.Number, lost.Code));
            Assert.Empty(await JobsOfAsync(id));
            Row state = await StateAsync(id);
            Assert.Equal((saved, null, null), (state.Get<DateTimeOffset>("NextDueAt"), state["LastFiredAt"], state["LeaseToken"]));
        }

        [Fact]
        public async Task A_tick_whose_lease_a_save_released_before_step_two_fires_nothing_logs_the_lost_lease_and_returns()
        {
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe, nextDueAt: (await DatabaseNowAsync()).AddMinutes(-1));
            IScheduleTick tick = fixture.Tenant.Services.GetRequiredService<IScheduleTick>();

            DataBatch batch = Batch(BatchPurpose.Maintenance, out AsyncServiceScope scope);
            await using (scope)
            {
                // --- Step 1 leases the schedule and the uninitialised seeded built-in ---
                batch.TransactionMode = TransactionMode.None;
                IScheduleTickRound round = tick.Append(batch, new ScheduleTickContext(FixtureTenant.TenantId, ReadOnly: false, []));
                _ = await batch.ExecuteAsync(Token);
                Assert.Equal(2, round.Leased);

                // --- A save releases the schedule's lease between the steps; step 2 then ends without throwing ---
                await PersistAsync(append => JobStatements.ReleaseTickLeases(append, [id]));
                await round.RunStep2Async(scope.ServiceProvider, Token);
            }

            // --- The fence rolled the whole step back: nothing fired, the built-in is still uninitialised ---
            Assert.Empty(await JobsOfAsync(id));
            Assert.Null((await StateAsync(FixtureFeature.ProbeScheduleId))["NextDueAt"]);
            IReadOnlyList<string> events = [.. fixture.Log.GetSnapshot().Select(static record => record.Id.Name ?? string.Empty)];
            Assert.Contains("SchedulerTickLeaseLost", events);
            Assert.DoesNotContain("SchedulerTickFailed", events);
        }

        [Fact]
        public async Task A_schedule_this_host_cannot_evaluate_fires_nothing_and_is_looked_at_again_an_hour_later_with_its_lease_released()
        {
            // --- Stored behind the rules' back: an expression that does not parse, and a zone no host resolves ---
            DateTimeOffset before = await DatabaseNowAsync();
            int badCron = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: "every minute", nextDueAt: before.AddMinutes(-1));
            int badZone = await InsertScheduleAsync(FixtureJobKeys.Probe, zone: "Mars/Olympus_Mons", nextDueAt: before.AddMinutes(-1));

            _ = await TickAsync();
            DateTimeOffset after = await DatabaseNowAsync();

            // --- Each fires nothing and is parked for an hour from the tick, its lease released and nothing stamped ---
            foreach (int id in new[] { badCron, badZone })
            {
                Assert.Empty(await JobsOfAsync(id));
                Row state = await StateAsync(id);
                DateTimeOffset next = state.Get<DateTimeOffset>("NextDueAt");
                Assert.InRange(next, before.AddHours(1), after.AddHours(1));
                Assert.Equal((null, null, null), (state["LeaseToken"], state["LeaseExpiresAt"], state["LastScheduledFor"]));
                Assert.True((await ScheduleAsync(id)).Get<bool>("IsActive"));
            }

            // --- Each is logged once, by its id ---
            IReadOnlyList<string> unusable = [.. fixture.Log.GetSnapshot().Where(static record => record.Id.Name == "SchedulerScheduleUnusable").Select(static record => record.Message)];
            Assert.Equal(2, unusable.Count);
            Assert.Contains(unusable, message => message.StartsWith($"The schedule {badCron} cannot", StringComparison.Ordinal));
            Assert.Contains(unusable, message => message.StartsWith($"The schedule {badZone} cannot", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Tick_step_one_seeks_the_reserved_band_for_uninitialised_built_ins_and_the_due_index_for_the_rest()
        {
            // --- Far more user schedules than the band holds, a few of them due ---
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Sql($"""
                    INSERT INTO [core].[Schedules] ([Id], [Name], [HandlerKey], [CronExpression], [MissedPolicy], [OverlapPolicy], [CatchUpWindowMinutes], [RunAsUserId], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                    SELECT 100000 + [n].[N], CONCAT(N'Bulk ', [n].[N]), N'{FixtureJobKeys.Probe}', N'0 3 * * *', 'Coalesce', 'Skip', 1440, {ActiveUserId}, 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1
                    FROM (SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [N] FROM sys.all_columns AS [a] CROSS JOIN sys.all_columns AS [b]) AS [n];
                    """),
                "INSERT INTO [core].[ScheduleStates] ([ScheduleId], [NextDueAt]) SELECT [Id], CASE WHEN [Id] % 997 = 0 THEN DATEADD(minute, -1, SYSUTCDATETIME()) ELSE DATEADD(day, 1, SYSUTCDATETIME()) END FROM [core].[Schedules] WHERE [Id] >= 100000;",
                "UPDATE STATISTICS [core].[ScheduleStates] WITH FULLSCAN;");

            // --- The statement as the poll's batch sends it, its actual plans returned after its results ---
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using (SqlCommand on = new("SET STATISTICS XML ON;", connection))
            {
                _ = await on.ExecuteNonQueryAsync(Token);
            }

            List<XDocument> plans = [];
            await using (SqlCommand tick = await TickCommandAsync(connection))
            {
                await using SqlDataReader reader = await tick.ExecuteReaderAsync(Token);
                do
                {
                    bool plan = reader.FieldCount == 1 && reader.GetName(0) == ShowplanColumn;
                    while (await reader.ReadAsync(Token))
                    {
                        if (plan)
                        {
                            plans.Add(XDocument.Parse(reader.GetString(0)));
                        }
                    }
                }
                while (await reader.NextResultAsync(Token));
            }

            // --- A range seek of the clustered key over the band, a seek of the due index, and no scan of the states ---
            XElement[] operators = [.. plans.SelectMany(static plan => plan.Descendants(Showplan + "RelOp"))];
            Assert.Contains(operators, static op => (string?)op.Attribute("PhysicalOp") == "Clustered Index Seek" && Reads(op, "[ScheduleStates]", "[PK_ScheduleStates]"));
            Assert.Contains(operators, static op => (string?)op.Attribute("PhysicalOp") == "Index Seek" && Reads(op, "[ScheduleStates]", "[IX_ScheduleStates_Due]"));
            Assert.DoesNotContain(operators, static op => (string?)op.Attribute("PhysicalOp") is "Clustered Index Scan" or "Table Scan" or "Index Scan" && Reads(op, "[ScheduleStates]", index: null));
        }

        // --- The stack: the rules ---

        [Fact]
        public async Task A_save_refuses_an_expression_that_never_occurs_a_zone_by_an_alias_and_a_key_no_schedule_may_name()
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);
            Schedule[] schedules =
            [
                New("never", FixtureJobKeys.Probe, "0 0 30 2 *"),
                New("alias", FixtureJobKeys.Probe, "0 3 * * *", zone: "Asia/Calcutta"),
                New("parcel", FixtureJobKeys.Parcel, "0 3 * * *"),
            ];

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Schedules(scope).SaveAsync(schedules, new SaveOptions(), Token));

            Assert.Equal(
                [
                    $"[0].{nameof(Schedule.CronExpression)} {ScheduleValidationCodes.CronNeverOccurs}",
                    $"[1].{nameof(Schedule.TimeZoneId)} {ScheduleValidationCodes.TimeZoneUnknown}",
                    $"[2].{nameof(Schedule.HandlerKey)} {ScheduleValidationCodes.HandlerNotSchedulable}",
                ],
                refused.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, Sql($"SELECT COUNT(*) FROM [core].[Schedules] WHERE [Id] > {WellKnownIds.ReservedIdBandEnd};")));
        }

        [Fact]
        public async Task A_save_runs_a_new_schedule_as_its_creator_and_keeps_its_next_due_time_in_step()
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);

            int id = checked((int)Assert.Single((await Schedules(scope).SaveAsync([New("nightly", FixtureJobKeys.Probe, "0 3 * * *", zone: "Asia/Riyadh")], new SaveOptions(), Token)).Ids));

            Row schedule = await ScheduleAsync(id);
            Assert.Equal((ActiveUserId, true, null), (schedule.Get<int>("RunAsUserId"), schedule.Get<bool>("IsActive"), schedule["PausedReason"]));
            DateTimeOffset next = (await StateAsync(id)).Get<DateTimeOffset>("NextDueAt");
            Assert.Equal(TimeSpan.Zero, next.Offset);
            Assert.Equal((0, 0, 0), (next.Hour, next.Minute, next.Second));
        }

        // --- The stack: the take-over, the activation and the delete ---

        [Fact]
        public async Task A_take_over_and_a_save_that_moves_no_due_time_release_a_ticks_lease_and_leave_the_next_due_time_alone()
        {
            DateTimeOffset due = await DatabaseNowAsync();
            int taken = await InsertScheduleAsync(FixtureJobKeys.Probe, runAs: AdministratorId, nextDueAt: due.AddMinutes(-1));
            int edited = await InsertScheduleAsync(FixtureJobKeys.Probe, nextDueAt: due.AddMinutes(-1));

            // --- A tick's step 1 leases both ---
            var token = Guid.NewGuid();
            _ = await MaintenanceAsync(batch => JobStatements.TickStep1(batch, token, readOnlyKeys: null));
            foreach (int id in new[] { taken, edited })
            {
                Assert.Equal(token, (await StateAsync(id)).Get<Guid>("LeaseToken"));
            }

            // --- A take-over, and a save that changes a policy alone ---
            await using (AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId))
            {
                EntityService<Schedule, int> schedules = Schedules(scope);
                _ = await schedules.ExecuteActionAsync(ScheduleService<Schedule>.TakeOverAction, [taken], null, new ActionOptions(), Token);
                Schedule update = new()
                {
                    Id = edited,
                    Name = "Scheduled",
                    HandlerKey = FixtureJobKeys.Probe,
                    CronExpression = EveryMinute,
                    OverlapPolicy = OverlapPolicy.Allow,
                    ModifiedAt = (await ScheduleAsync(edited)).Get<DateTimeOffset>("ModifiedAt"),
                };
                _ = await schedules.SaveAsync([update], new SaveOptions(), Token);
            }

            // --- Both leases are released, so the tick's step 2 would fail its fence; both stay due as they were ---
            foreach (int id in new[] { taken, edited })
            {
                Row state = await StateAsync(id);
                Assert.Equal((null, null, due.AddMinutes(-1)), (state["LeaseToken"], state["LeaseExpiresAt"], state.Get<DateTimeOffset>("NextDueAt")));
            }

            Assert.Equal((ActiveUserId, nameof(OverlapPolicy.Allow)), ((await ScheduleAsync(taken)).Get<int>("RunAsUserId"), (await ScheduleAsync(edited)).Get<string>("OverlapPolicy")));
        }

        [Fact]
        public async Task A_schedule_paused_for_its_inactive_owner_is_taken_over_then_activated_and_its_notice_withdrawn_with_the_commit()
        {
            // --- A daily schedule in the tenant's zone, which is not UTC, so its next occurrence depends on the zone it is evaluated in ---
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe, cron: DailyAtThree, runAs: DeactivatedUserId, nextDueAt: (await DatabaseNowAsync()).AddMinutes(-1));
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Sql($"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {DeactivatedUserId};"),
                Sql($"UPDATE [core].[Settings] SET [TimeZone] = N'{RiyadhZone}'; UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Settings}';"));
            _ = await TickAsync();
            Assert.Equal(nameof(SchedulePausedReason.OwnerInactive), (await ScheduleAsync(id)).Get<string>("PausedReason"));

            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);
            EntityService<Schedule, int> schedules = Schedules(scope);

            // --- Its owner is still inactive, so it cannot be activated yet ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => schedules.ActivateAsync([id], cancellationToken: Token));
            Assert.Equal(ScheduleValidationCodes.OwnerInactive, Assert.Single(refused.Errors).Code);

            // --- The take-over changes the owner alone: the schedule stays paused ---
            _ = await schedules.ExecuteActionAsync(ScheduleService<Schedule>.TakeOverAction, [id], null, new ActionOptions(), Token);
            Row taken = await ScheduleAsync(id);
            Assert.Equal((ActiveUserId, false, nameof(SchedulePausedReason.OwnerInactive)), (taken.Get<int>("RunAsUserId"), taken.Get<bool>("IsActive"), taken.Get<string>("PausedReason")));
            Assert.Empty(fixture.Notices.Withdrawn);

            // --- The activation: active, the reason cleared, due again from the request's instant, and the notice withdrawn once it committed ---
            _ = await schedules.ActivateAsync([id], cancellationToken: Token);
            Row active = await ScheduleAsync(id);
            Assert.Equal((true, null), (active.Get<bool>("IsActive"), active["PausedReason"]));

            // The next due time is the expression's first occurrence after the request's instant, which
            // the scope stamped when it began (spec 0020 §7.8: the one place the application clock
            // enters), in the tenant's zone. It is held to that instant, not to a later read of the
            // database clock: an occurrence that falls between the two leaves it in the past. The same
            // expression evaluated in UTC gives another instant, so an activation that ignored the
            // tenant's zone would fail here.
            DateTimeOffset requested = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current.Now;
            var expression = Cronos.CronExpression.Parse(DailyAtThree);
            DateTimeOffset due = expression.GetNextOccurrence(requested, TimeZoneInfo.FindSystemTimeZoneById(RiyadhZone))!.Value.ToUniversalTime();
            Assert.NotEqual(expression.GetNextOccurrence(requested, TimeZoneInfo.Utc)!.Value, due);
            Assert.Equal(due, (await StateAsync(id)).Get<DateTimeOffset>("NextDueAt"));
            Assert.Equal([JobNotices.SchedulePausedKey(id)], fixture.Notices.Withdrawn.Select(static withdrawal => withdrawal.DedupKey));
        }

        [Fact]
        public async Task A_delete_removes_each_schedules_state_detaches_its_jobs_and_withdraws_its_paused_notice_after_it_committed()
        {
            // --- The first schedule fired a job; the fixture's audit refuses a batch that writes a table it did not declare ---
            int first = await InsertScheduleAsync(FixtureJobKeys.Probe);
            int second = await InsertScheduleAsync(FixtureJobKeys.Probe);
            await InsertScheduleJobAsync(first, JobStatus.Succeeded);
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);

            AffectedResult deleted = await Schedules(scope).DeleteByIdsAsync([first, second], null, Token);

            // --- The states are gone, and the job stays, no longer of a schedule ---
            Assert.Equal(2, deleted.Count);
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, Sql($"SELECT COUNT(*) FROM [core].[ScheduleStates] WHERE [ScheduleId] IN ({first}, {second});")));
            Assert.Equal((1, 0), (
                await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs] WHERE [ScheduleId] IS NULL;"),
                await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs] WHERE [ScheduleId] IS NOT NULL;")));
            Assert.Equal(
                [JobNotices.SchedulePausedKey(first), JobNotices.SchedulePausedKey(second)],
                fixture.Notices.Withdrawn.Select(static withdrawal => withdrawal.DedupKey).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task A_built_in_refuses_a_delete_and_a_deactivation_whatever_the_callers_grant()
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);
            EntityService<Schedule, int> schedules = Schedules(scope);

            ValidationException delete = await Assert.ThrowsAsync<ValidationException>(() => schedules.DeleteByIdsAsync([FixtureFeature.ProbeScheduleId], null, Token));
            ValidationException deactivate = await Assert.ThrowsAsync<ValidationException>(() => schedules.DeactivateAsync([FixtureFeature.ProbeScheduleId], cancellationToken: Token));

            Assert.Equal(ScheduleValidationCodes.BuiltInImmutable, Assert.Single(delete.Errors).Code);
            Assert.Equal(ScheduleValidationCodes.BuiltInAlwaysActive, Assert.Single(deactivate.Errors).Code);
            Assert.True((await ScheduleAsync(FixtureFeature.ProbeScheduleId)).Get<bool>("IsActive"));
        }

        // --- The stack: run-now ---

        [Fact]
        public async Task Run_now_inserts_one_job_due_now_with_no_occurrence_as_the_caller_or_for_a_built_in_as_the_platform()
        {
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe, runAs: AdministratorId, overlap: OverlapPolicy.Allow);
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);
            EntityService<Schedule, int> schedules = Schedules(scope);

            JobAccepted user = await schedules.ExecuteActionAsync<JobAccepted>(ScheduleService<Schedule>.RunNowAction, [id], null, new ActionOptions(), Token);
            JobAccepted builtIn = await schedules.ExecuteActionAsync<JobAccepted>(ScheduleService<Schedule>.RunNowAction, [FixtureFeature.ProbeScheduleId], null, new ActionOptions(), Token);

            // --- A user schedule's job runs as the caller; a built-in's as the platform; the caller asked for both ---
            Row userJob = Assert.Single(await JobsOfAsync(id));
            Row builtInJob = Assert.Single(await JobsOfAsync(FixtureFeature.ProbeScheduleId));
            Assert.Equal((user.JobId, builtIn.JobId), (userJob.Get<int>("Id"), builtInJob.Get<int>("Id")));
            Assert.Equal((nameof(JobStatus.Pending), null, ActiveUserId, ActiveUserId), (userJob.Get<string>("Status"), userJob["ScheduledFor"], userJob.Get<int>("RunAsUserId"), userJob.Get<int>("RequestedById")));
            Assert.Equal((nameof(JobStatus.Pending), null, WellKnownIds.SystemUserId, ActiveUserId), (builtInJob.Get<string>("Status"), builtInJob["ScheduledFor"], builtInJob.Get<int>("RunAsUserId"), builtInJob.Get<int>("RequestedById")));

            // --- The schedules' own state is left alone ---
            Row state = await StateAsync(id);
            Assert.Equal((null, null), (state["LastJobId"], state["LastFiredAt"]));
        }

        [Fact]
        public async Task Run_now_of_a_skip_schedule_whose_job_has_not_ended_is_refused_as_in_progress()
        {
            int id = await InsertScheduleAsync(FixtureJobKeys.Probe);
            await InsertScheduleJobAsync(id, JobStatus.Held);
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => Schedules(scope).ExecuteActionAsync<JobAccepted>(ScheduleService<Schedule>.RunNowAction, [id], null, new ActionOptions(), Token));

            Assert.Equal(ScheduleValidationCodes.RunInProgress, Assert.Single(refused.Errors).Code);
            Assert.Single(await JobsOfAsync(id));
        }

        // --- The stack: the extras ---

        [Fact]
        public async Task The_state_extra_gives_each_visible_schedules_state_and_nothing_of_one_the_caller_cannot_see()
        {
            DateTimeOffset due = (await DatabaseNowAsync()).AddHours(2);
            int seen = await InsertScheduleAsync(FixtureJobKeys.Probe, code: ViewerScheduleCode, nextDueAt: due);
            int hidden = await InsertScheduleAsync(FixtureJobKeys.Probe, code: "hidden", nextDueAt: due);
            DetailsRequest request = new() { Include = [ScheduleService<Schedule>.StateExtra] };
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ViewerUserId);
            EntityService<Schedule, int> schedules = Schedules(scope);

            EntitiesResult<Schedule> visible = await schedules.GetByIdsAsync([seen], request, Token);
            EntitiesResult<Schedule> invisible = await schedules.GetByIdsAsync([hidden], request, Token);

            Assert.Equal([seen], visible.Ids);
            Assert.Empty(invisible.Ids);

            // --- The visible schedule's one state row, its columns named; nothing for the invisible one ---
            JsonElement extra = visible.Extras![ScheduleService<Schedule>.StateExtra];
            Assert.Equal(
                ["ScheduleId", "NextDueAt", "LastFiredAt", "LastScheduledFor", "LastSkippedAt", "LastJobId"],
                extra.GetProperty("columns").EnumerateArray().Select(static column => column.GetProperty("name").GetString()));
            JsonElement state = Assert.Single(extra.GetProperty("rows").EnumerateArray());
            Assert.Equal(seen, state[0].GetInt32());
            Assert.Equal(due, state[1].GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, state[2].ValueKind);
            Assert.Empty(invisible.Extras![ScheduleService<Schedule>.StateExtra].GetProperty("rows").EnumerateArray());
        }

        [Fact]
        public async Task The_permissions_extra_tells_the_client_what_a_built_in_refuses_whatever_the_callers_grant()
        {
            int user = await InsertScheduleAsync(FixtureJobKeys.Probe);
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(ActiveUserId);

            EntitiesResult<Schedule> result = await Schedules(scope).GetByIdsAsync([FixtureFeature.ProbeScheduleId, user], new DetailsRequest { Include = ["permissions"] }, Token);

            var refusals = result.Extras!["permissions"].EnumerateArray().ToDictionary(
                static row => row.GetProperty("id").GetInt32(),
                static row => row.GetProperty("refused").EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.GetString()!, StringComparer.Ordinal));
            Assert.Equal(ScheduleValidationCodes.BuiltInImmutable, refusals[FixtureFeature.ProbeScheduleId]["delete"]);
            Assert.Equal(ScheduleValidationCodes.BuiltInAlwaysActive, refusals[FixtureFeature.ProbeScheduleId]["activate"]);
            Assert.Equal(ScheduleValidationCodes.BuiltInImmutable, refusals[FixtureFeature.ProbeScheduleId]["action:" + ScheduleService<Schedule>.TakeOverAction]);
            Assert.DoesNotContain("save", refusals[FixtureFeature.ProbeScheduleId].Keys);
            Assert.Empty(refusals[user]);
        }

        // --- Helpers: the tick ---

        /// <summary>Runs one poll of the fixture tenant that claims nothing, so it carries the tick alone.</summary>
        /// <param name="readOnly">Whether the tenant is read-only.</param>
        /// <returns>What the poll returned, once its tick's step 2 ran.</returns>
        private Task<JobPollResult> TickAsync(bool readOnly = false)
        {
            return fixture.Tenant.Services.GetRequiredService<IJobPoll>().PollAsync(
                new JobPollPlan(FixtureTenant.TenantId, ReadOnly: readOnly, Stopping: false, [], SampleBacklog: false, SampleOrphans: false, JobLeaseOwner.Current),
                static _ => { },
                Token);
        }

        /// <summary>Tick step 1 as the poll's batch sends it, as a command of a connection of the test's own.</summary>
        /// <param name="connection">The connection.</param>
        /// <returns>The command.</returns>
        private async Task<SqlCommand> TickCommandAsync(SqlConnection connection)
        {
            DataBatch draft = Batch(BatchPurpose.Maintenance, out AsyncServiceScope scope);
            await using (scope)
            {
                _ = JobStatements.TickStep1(draft, Guid.NewGuid(), readOnlyKeys: null);
                BatchStatement statement = Assert.Single(draft.Statements, static statement => statement.Writes.Count > 0);
                SqlCommand command = new(statement.Text, connection);
                foreach (BatchParameter parameter in statement.Parameters)
                {
                    _ = command.Parameters.Add(parameter.Create());
                }

                return command;
            }
        }

        /// <summary>Whether a plan operator reads a table, through an index when one is named.</summary>
        /// <param name="op">The operator.</param>
        /// <param name="table">The table, delimited.</param>
        /// <param name="index">The index, delimited; null for any.</param>
        /// <returns>Whether it does.</returns>
        private static bool Reads(XElement op, string table, string? index)
        {
            // An operator's own object is its first: a nested operator's objects belong to the nested one.
            XElement? target = op.Descendants(Showplan + "Object").FirstOrDefault();
            return target is not null && (string?)target.Attribute("Table") == table && (index is null || (string?)target.Attribute("Index") == index);
        }

        /// <summary>Runs one statement in a maintenance batch of the system scope, committing on its own.</summary>
        /// <typeparam name="T">The statement's result.</typeparam>
        /// <param name="append">Appends the statement.</param>
        /// <returns>The result.</returns>
        private async Task<T> MaintenanceAsync<T>(Func<DataBatch, BatchResult<T>> append)
        {
            DataBatch batch = Batch(BatchPurpose.Maintenance, out AsyncServiceScope scope);
            await using (scope)
            {
                batch.TransactionMode = TransactionMode.None;
                BatchResult<T> result = append(batch);
                _ = await batch.ExecuteAsync(Token);
                return result.Value;
            }
        }

        /// <summary>Runs statements in one transaction of a persist batch of the system scope.</summary>
        /// <param name="append">Appends the statements.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task PersistAsync(Action<DataBatch> append)
        {
            DataBatch batch = Batch(BatchPurpose.Persist, out AsyncServiceScope scope);
            await using (scope)
            {
                append(batch);
                _ = await batch.ExecuteAsync(Token);
            }
        }

        /// <summary>A batch of a fresh system scope.</summary>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="scope">The scope; the caller disposes it.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(BatchPurpose purpose, out AsyncServiceScope scope)
        {
            scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            return (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(purpose);
        }

        // --- Helpers: the stack ---

        /// <summary>The schedules' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Schedule, int> Schedules(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Schedule, int>>();
        }

        /// <summary>A new schedule as a client sends one.</summary>
        /// <param name="code">Its code.</param>
        /// <param name="key">Its handler key.</param>
        /// <param name="cron">Its expression.</param>
        /// <param name="zone">Its zone; null for the tenant's.</param>
        /// <returns>The schedule.</returns>
        private static Schedule New(string code, string key, string cron, string? zone = null)
        {
            return new Schedule { Code = code, Name = code, HandlerKey = key, CronExpression = cron, TimeZoneId = zone };
        }

        // --- Helpers: the rows ---

        /// <summary>Stores a user schedule and its state, active unless told otherwise.</summary>
        /// <param name="key">Its handler key.</param>
        /// <param name="cron">Its expression.</param>
        /// <param name="zone">Its zone; null for the tenant's.</param>
        /// <param name="missed">Its missed policy.</param>
        /// <param name="overlap">Its overlap policy.</param>
        /// <param name="catchUpWindowMinutes">Its catch-up window.</param>
        /// <param name="runAs">Its owner.</param>
        /// <param name="code">Its code; null for none.</param>
        /// <param name="name">Its name.</param>
        /// <param name="nextDueAt">Its next due time; null for none.</param>
        /// <returns>Its id.</returns>
        private async Task<int> InsertScheduleAsync(
            string key,
            string cron = EveryMinute,
            string? zone = null,
            MissedPolicy missed = MissedPolicy.Coalesce,
            OverlapPolicy overlap = OverlapPolicy.Skip,
            int catchUpWindowMinutes = 1440,
            int runAs = ActiveUserId,
            string? code = null,
            string name = "Scheduled",
            DateTimeOffset? nextDueAt = null)
        {
            string due = nextDueAt is DateTimeOffset value ? Literal(value) : "NULL";
            return await SqlServerFixture.ScalarAsync<int>(
                fixture.ConnectionString,
                Sql($"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Schedules];
                    INSERT INTO [core].[Schedules] ([Id], [Code], [Name], [HandlerKey], [CronExpression], [TimeZoneId], [MissedPolicy], [OverlapPolicy], [CatchUpWindowMinutes], [RunAsUserId], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                    VALUES (@id, {JobRunDatabase.Literal(code)}, {JobRunDatabase.Literal(name)}, N'{key}', N'{cron}', {JobRunDatabase.Literal(zone)}, '{missed}', '{overlap}', {catchUpWindowMinutes}, {runAs}, 1, SYSDATETIMEOFFSET(), {runAs}, SYSDATETIMEOFFSET(), {runAs});
                    INSERT INTO [core].[ScheduleStates] ([ScheduleId], [NextDueAt]) VALUES (@id, {due});
                    SELECT @id;
                    """));
        }

        /// <summary>Stores a job a schedule fired earlier, in a status.</summary>
        /// <param name="scheduleId">The schedule.</param>
        /// <param name="status">The job's status; never Running, which needs a lease.</param>
        /// <returns>A task that completes when the job is stored.</returns>
        private Task InsertScheduleJobAsync(int scheduleId, JobStatus status)
        {
            string warnings = status == JobStatus.PendingConfirmation ? "N'[]'" : "NULL";
            string completed = status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled ? "SYSUTCDATETIME()" : "NULL";
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Sql($"""
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [PendingConfirmationsJson], [CompletedAt], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
                    VALUES (NEXT VALUE FOR [core].[sq_Jobs], N'{FixtureJobKeys.Probe}', '{status}', DATEADD(hour, -1, SYSUTCDATETIME()), 1, {warnings}, {completed}, {ActiveUserId}, {ActiveUserId}, {scheduleId}, DATEADD(hour, -1, SYSUTCDATETIME()), DATEADD(hour, -1, SYSUTCDATETIME()));
                    """));
        }

        /// <summary>A schedule's row.</summary>
        /// <param name="id">The schedule.</param>
        /// <returns>The row.</returns>
        private Task<Row> ScheduleAsync(int id)
        {
            return RowAsync(Sql($"SELECT * FROM [core].[Schedules] WHERE [Id] = {id};"));
        }

        /// <summary>A schedule's state.</summary>
        /// <param name="id">The schedule.</param>
        /// <returns>The row.</returns>
        private Task<Row> StateAsync(int id)
        {
            return RowAsync(Sql($"SELECT * FROM [core].[ScheduleStates] WHERE [ScheduleId] = {id};"));
        }

        /// <summary>The jobs of a schedule, in id order.</summary>
        /// <param name="id">The schedule.</param>
        /// <returns>The rows.</returns>
        private Task<IReadOnlyList<Row>> JobsOfAsync(int id)
        {
            return RowsAsync(Sql($"SELECT * FROM [core].[Jobs] WHERE [ScheduleId] = {id} ORDER BY [Id];"));
        }

        /// <summary>How many earlier versions of a schedule its history holds.</summary>
        /// <param name="id">The schedule.</param>
        /// <returns>The count.</returns>
        private async Task<int> HistoryCountAsync(int id)
        {
            return await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, Sql($"SELECT COUNT(*) FROM [core].[SchedulesHistory] WHERE [Id] = {id};"));
        }

        /// <summary>The database's clock.</summary>
        /// <returns>The instant, in UTC.</returns>
        private async Task<DateTimeOffset> DatabaseNowAsync()
        {
            return await SqlServerFixture.ScalarAsync<DateTimeOffset>(fixture.ConnectionString, "SELECT CAST(SYSUTCDATETIME() AS datetimeoffset(3));");
        }

        // The expression names the current minute and the two before it by their minutes and hours,
        // so it may match other minutes too; none lies between the earliest of the three and the
        // expression's next occurrence after the current one, which is most of an hour away, so a tick
        // that runs into a later minute still fires exactly the pinned occurrences.
        /// <summary>
        ///     The database's current minute, an expression in UTC naming it and the two minutes before
        ///     it, and the expression's next occurrence after it.
        /// </summary>
        /// <returns>The minute's start, the expression and its next occurrence, in UTC.</returns>
        private async Task<(DateTimeOffset Minute, string Cron, DateTimeOffset Next)> PinnedMinutesAsync()
        {
            DateTimeOffset now = await DatabaseNowAsync();
            DateTimeOffset minute = new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
            DateTimeOffset[] pinned = [minute.AddMinutes(-2), minute.AddMinutes(-1), minute];
            string cron = string.Create(
                CultureInfo.InvariantCulture,
                $"{string.Join(',', pinned.Select(static at => at.Minute).Distinct())} {string.Join(',', pinned.Select(static at => at.Hour).Distinct())} * * *");
            DateTimeOffset next = Cronos.CronExpression.Parse(cron).GetNextOccurrence(minute, TimeZoneInfo.Utc)!.Value;
            return (minute, cron, next);
        }

        /// <summary>The zone the tenant's settings name, which a schedule without its own follows.</summary>
        /// <returns>The zone.</returns>
        private async Task<TimeZoneInfo> TenantZoneAsync()
        {
            string? id = await SqlServerFixture.ScalarAsync<string>(fixture.ConnectionString, "SELECT [TimeZone] FROM [core].[Settings];");
            return id is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
        }

        /// <summary>Reads one row, every column by name.</summary>
        /// <param name="sql">The query.</param>
        /// <returns>The row.</returns>
        private async Task<Row> RowAsync(string sql)
        {
            return Assert.Single(await RowsAsync(sql));
        }

        /// <summary>Reads rows, every column by name.</summary>
        /// <param name="sql">The query.</param>
        /// <returns>The rows.</returns>
        private async Task<IReadOnlyList<Row>> RowsAsync(string sql)
        {
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = new(sql, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<Row> rows = [];
            while (await reader.ReadAsync(Token))
            {
                Dictionary<string, object?> row = new(StringComparer.Ordinal);
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
                }

                rows.Add(new Row(row));
            }

            return rows;
        }

        /// <summary>An instant as a SQL literal.</summary>
        /// <param name="value">The instant.</param>
        /// <returns>The literal.</returns>
        private static string Literal(DateTimeOffset value)
        {
            return string.Create(CultureInfo.InvariantCulture, $"CAST('{value.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffffffzzz}' AS datetimeoffset(7))");
        }

        /// <summary>Formats SQL text with the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Sql(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A stored row, every column by name.</summary>
        /// <param name="Columns">The columns.</param>
        private sealed record Row(IReadOnlyDictionary<string, object?> Columns)
        {
            /// <summary>A column's value.</summary>
            /// <param name="column">The column.</param>
            /// <returns>The value; null for <c>NULL</c>.</returns>
            public object? this[string column] => Columns[column];

            /// <summary>A column's value, which is not <c>NULL</c>.</summary>
            /// <typeparam name="T">The value's type.</typeparam>
            /// <param name="column">The column.</param>
            /// <returns>The value.</returns>
            public T Get<T>(string column)
            {
                return Assert.IsType<T>(Columns[column]);
            }
        }
    }
}
