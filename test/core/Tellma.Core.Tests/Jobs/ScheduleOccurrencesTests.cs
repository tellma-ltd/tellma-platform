// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Jobs;
using Cron = Cronos.CronExpression;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     What a tick does with a leased schedule, with fixed zones and across daylight-saving
    ///     transitions: each missed policy's pick, the replay window, the skip window, the gap
    ///     degradation, the pauses and the exhaustion of an expression that never occurs again.
    /// </summary>
    public sealed class ScheduleOccurrencesTests
    {
        /// <summary>The default gap threshold.</summary>
        private static readonly TimeSpan Gap = TimeSpan.FromHours(6);

        /// <summary>The default longest poll interval.</summary>
        private static readonly TimeSpan MaxPoll = TimeSpan.FromSeconds(30);

        // --- The missed policies within the gap threshold ---

        [Fact]
        public void Coalesce_fires_the_latest_due_occurrence_and_counts_the_rest_as_missed()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.Coalesce, due: Utc(10, 0), now: Utc(10, 47, 30));

            ScheduleFiring firing = Compute(row);

            Assert.Null(firing.Pause);
            Assert.Equal([Utc(10, 45)], firing.Occurrences);
            Assert.Equal(3, firing.Missed);
            Assert.Equal(Utc(11, 0), firing.Next);
            Assert.Equal(Utc(10, 45), firing.LastScheduledFor);
        }

        [Fact]
        public void Coalesce_with_one_due_occurrence_fires_it_and_misses_nothing()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.Coalesce, due: Utc(10, 45), now: Utc(10, 45, 2));

            ScheduleFiring firing = Compute(row);

            Assert.Equal([Utc(10, 45)], firing.Occurrences);
            Assert.Equal(0, firing.Missed);
            Assert.Equal(Utc(11, 0), firing.Next);
        }

        [Fact]
        public void Replay_all_fires_every_occurrence_inside_the_catch_up_window_and_misses_the_older_ones()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.ReplayAll, due: Utc(10, 0), now: Utc(10, 47, 30), catchUpWindowMinutes: 20);

            ScheduleFiring firing = Compute(row);

            Assert.Equal([Utc(10, 30), Utc(10, 45)], firing.Occurrences);
            Assert.Equal(2, firing.Missed);
            Assert.Equal(Utc(10, 45), firing.LastScheduledFor);
        }

        [Fact]
        public void Replay_all_with_a_wide_window_fires_every_due_occurrence_oldest_first()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.ReplayAll, due: Utc(10, 15), now: Utc(10, 45));

            ScheduleFiring firing = Compute(row);

            Assert.Equal([Utc(10, 15), Utc(10, 30), Utc(10, 45)], firing.Occurrences);
            Assert.Equal(0, firing.Missed);
        }

        [Fact]
        public void Skip_fires_the_latest_occurrence_only_within_twice_the_longest_poll_interval()
        {
            ScheduleTickRow fresh = Row("*/15 * * * *", MissedPolicy.Skip, due: Utc(10, 0), now: Utc(10, 46));
            ScheduleTickRow late = Row("*/15 * * * *", MissedPolicy.Skip, due: Utc(10, 0), now: Utc(10, 46, 1));

            ScheduleFiring firing = Compute(fresh);
            ScheduleFiring skipped = Compute(late);

            Assert.Equal([Utc(10, 45)], firing.Occurrences);
            Assert.Equal(3, firing.Missed);
            Assert.Empty(skipped.Occurrences);
            Assert.Equal(4, skipped.Missed);
            Assert.Equal(Utc(11, 0), skipped.Next);
            Assert.Null(skipped.LastScheduledFor);
        }

        [Fact]
        public void A_due_time_with_no_occurrence_before_now_fires_nothing_and_takes_the_next()
        {
            // A due time computed in another zone may fall between two occurrences of this one.
            ScheduleTickRow row = Row("0 * * * *", MissedPolicy.Coalesce, due: Utc(10, 5), now: Utc(10, 30));

            ScheduleFiring firing = Compute(row);

            Assert.Empty(firing.Occurrences);
            Assert.Equal(0, firing.Missed);
            Assert.Equal(Utc(11, 0), firing.Next);
        }

        // --- The gap degradation ---

        [Theory]
        [InlineData(MissedPolicy.Coalesce)]
        [InlineData(MissedPolicy.ReplayAll)]
        [InlineData(MissedPolicy.Skip)]
        public void Past_the_gap_threshold_every_policy_fires_the_latest_occurrence_alone_and_counts_nothing_missed(MissedPolicy policy)
        {
            // Every minute for sixty days: enumerating them would materialise 86,400 occurrences.
            DateTimeOffset now = Utc(10, 47, 30);
            ScheduleTickRow row = Row("* * * * *", policy, due: now.AddDays(-60), now: now);

            ScheduleFiring firing = Compute(row);

            Assert.Equal([Utc(10, 47)], firing.Occurrences);
            Assert.Equal(0, firing.Missed);
            Assert.Equal(Utc(10, 48), firing.Next);
        }

        [Fact]
        public void Exactly_at_the_gap_threshold_the_policy_still_applies()
        {
            DateTimeOffset now = Utc(10, 0);
            ScheduleTickRow row = Row("0 * * * *", MissedPolicy.ReplayAll, due: now - Gap, now: now, catchUpWindowMinutes: 1440);

            ScheduleFiring firing = Compute(row);

            Assert.Equal(7, firing.Occurrences.Count);
            Assert.Equal(now - Gap, firing.Occurrences[0]);
        }

        // --- Pauses, initialisation and exhaustion ---

        [Fact]
        public void An_inactive_owner_pauses_the_schedule_and_fires_nothing()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.Coalesce, due: Utc(10, 0), now: Utc(10, 47), ownerIsActive: false);

            ScheduleFiring firing = Compute(row, retired: true);

            Assert.Equal(SchedulePausedReason.OwnerInactive, firing.Pause);
            Assert.Empty(firing.Occurrences);
            Assert.Null(firing.Next);
        }

        [Fact]
        public void A_retired_key_pauses_the_schedule_and_fires_nothing()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.Coalesce, due: Utc(10, 0), now: Utc(10, 47));

            ScheduleFiring firing = Compute(row, retired: true);

            Assert.Equal(SchedulePausedReason.HandlerRetired, firing.Pause);
            Assert.Empty(firing.Occurrences);
            Assert.Null(firing.Next);
        }

        [Fact]
        public void An_uninitialised_built_in_fires_nothing_and_takes_its_next_due_time_strictly_after_now()
        {
            ScheduleTickRow row = Row("*/15 * * * *", MissedPolicy.Coalesce, due: null, now: Utc(10, 45), id: 1);

            ScheduleFiring firing = Compute(row);

            Assert.Null(firing.Pause);
            Assert.Empty(firing.Occurrences);
            Assert.Equal(Utc(11, 0), firing.Next);
        }

        [Fact]
        public void An_expression_that_never_occurs_again_exhausts_the_schedule()
        {
            ScheduleTickRow row = Row("0 0 30 2 *", MissedPolicy.Coalesce, due: null, now: Utc(10, 45));

            ScheduleFiring firing = Compute(row);

            Assert.Equal(SchedulePausedReason.Exhausted, firing.Pause);
            Assert.Null(firing.Next);
        }

        // --- Zones ---

        [Fact]
        public void A_fixed_offset_zone_shifts_every_occurrence_by_its_offset()
        {
            // Riyadh is UTC+3 all year: nine every morning there is six in UTC.
            ScheduleTickRow row = Row("0 9 * * *", MissedPolicy.Coalesce, due: Utc(6, 0), now: Utc(6, 0, 5), zone: "Asia/Riyadh");

            ScheduleFiring firing = Compute(row);

            Assert.Equal([Utc(6, 0)], firing.Occurrences);
            Assert.Equal(Utc(6, 0).AddDays(1), firing.Next);
            Assert.Equal(TimeSpan.Zero, firing.Next!.Value.Offset);
        }

        [Fact]
        public void A_local_time_the_spring_transition_skips_fires_at_the_next_valid_instant()
        {
            // 2026-03-08 in New York: 02:00 EST jumps to 03:00 EDT, so 02:30 never happens and
            // fires at 03:00 EDT, 07:00 UTC; the next day's 02:30 EDT is 06:30 UTC.
            DateTimeOffset due = new(2026, 3, 8, 6, 0, 0, TimeSpan.Zero);
            DateTimeOffset now = new(2026, 3, 8, 7, 0, 30, TimeSpan.Zero);
            ScheduleTickRow row = Row("30 2 * * *", MissedPolicy.Coalesce, due: due, now: now, zone: "America/New_York");

            ScheduleFiring firing = Compute(row);

            Assert.Equal([new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero)], firing.Occurrences);
            Assert.Equal(new DateTimeOffset(2026, 3, 9, 6, 30, 0, TimeSpan.Zero), firing.Next);
        }

        [Fact]
        public void A_local_time_the_autumn_transition_repeats_fires_once()
        {
            // 2026-11-01 in New York: 01:30 happens at 05:30 UTC (EDT) and again at 06:30 UTC (EST);
            // the expression fires the first, and next at 01:30 EST the following day.
            DateTimeOffset due = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
            DateTimeOffset now = new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
            ScheduleTickRow row = Row("30 1 * * *", MissedPolicy.ReplayAll, due: due, now: now, zone: "America/New_York");

            ScheduleFiring firing = Compute(row);

            Assert.Equal([new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero)], firing.Occurrences);
            Assert.Equal(0, firing.Missed);
            Assert.Equal(new DateTimeOffset(2026, 11, 2, 6, 30, 0, TimeSpan.Zero), firing.Next);
        }

        // --- The dialect ---

        [Theory]
        [InlineData("0 3 * * *", true)]
        [InlineData("@daily", true)]
        [InlineData("0 0 L * *", true)]
        [InlineData("0 0 * * MON#2", true)]
        [InlineData("0 0 0 3 * *", false)]
        [InlineData("not cron", false)]
        [InlineData(null, false)]
        public void The_dialect_is_five_field_unix_cron_with_macros_and_the_special_characters(string? expression, bool parses)
        {
            Assert.Equal(parses, ScheduleCron.TryParse(expression, out _));
        }

        [Fact]
        public void A_zone_the_host_does_not_resolve_is_refused_and_a_null_zone_is_the_tenants()
        {
            var tenant = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

            Assert.False(ScheduleCron.TryZone("Mars/Olympus_Mons", tenant, out _));
            Assert.True(ScheduleCron.TryZone(null, tenant, out TimeZoneInfo? zone));
            Assert.Same(tenant, zone);
        }

        /// <summary>Computes a row's firing in its zone under the default timings.</summary>
        /// <param name="row">The row.</param>
        /// <param name="retired">Whether its key is retired.</param>
        /// <returns>The firing.</returns>
        private static ScheduleFiring Compute(ScheduleTickRow row, bool retired = false)
        {
            Assert.True(ScheduleCron.TryParse(row.CronExpression, out Cron? cron));
            var zone = TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId ?? "UTC");
            return ScheduleOccurrences.Compute(row, cron, zone, retired, Gap, MaxPoll);
        }

        /// <summary>A leased schedule as tick step 1 returns it.</summary>
        /// <param name="cron">Its expression.</param>
        /// <param name="policy">Its missed policy.</param>
        /// <param name="due">Its stored next due time.</param>
        /// <param name="now">The database's clock.</param>
        /// <param name="catchUpWindowMinutes">Its catch-up window.</param>
        /// <param name="ownerIsActive">Whether its owner is active.</param>
        /// <param name="zone">Its zone; null for UTC here.</param>
        /// <param name="id">Its id.</param>
        /// <returns>The row.</returns>
        private static ScheduleTickRow Row(
            string cron, MissedPolicy policy, DateTimeOffset? due, DateTimeOffset now, int catchUpWindowMinutes = 1440, bool ownerIsActive = true, string? zone = null, int id = 1000)
        {
            return new ScheduleTickRow(
                id,
                "fixture.scheduled",
                ArgumentsJson: null,
                cron,
                zone,
                policy,
                policy == MissedPolicy.ReplayAll ? OverlapPolicy.Allow : OverlapPolicy.Skip,
                catchUpWindowMinutes,
                RunAsUserId: 101,
                new LocalizedText("Nightly"),
                due,
                LastScheduledFor: null,
                ownerIsActive,
                now);
        }

        /// <summary>An instant on 2026-03-02 in UTC.</summary>
        /// <param name="hour">The hour.</param>
        /// <param name="minute">The minute.</param>
        /// <param name="second">The second.</param>
        /// <returns>The instant.</returns>
        private static DateTimeOffset Utc(int hour, int minute, int second = 0)
        {
            return new DateTimeOffset(2026, 3, 2, hour, minute, second, TimeSpan.Zero);
        }
    }
}
