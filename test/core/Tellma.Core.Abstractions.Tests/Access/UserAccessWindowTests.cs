// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;
using static Tellma.Core.Abstractions.Tests.Access.AccessFixture;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>
    ///     Membership windows: a grant reaches a decision only from its start, inclusive, to its end,
    ///     exclusive; the next boundary is the earliest start or end after an instant; the memoised
    ///     direct side never outlives its interval; and the validation instant only moves forward.
    /// </summary>
    public class UserAccessWindowTests
    {
        /// <summary>The start of the window most tests use.</summary>
        private static readonly DateTimeOffset Start = Now.AddHours(1);

        /// <summary>The end of the window most tests use.</summary>
        private static readonly DateTimeOffset End = Now.AddHours(3);

        // --- One window ---

        [Fact]
        public void A_grant_is_current_from_its_start_inclusive_to_its_end_exclusive()
        {
            UserAccess access = Set(Grant(Widget, "Read", startsAt: Start, endsAt: End));

            Assert.Equal(AccessOutcome.Denied, Decide(access, Start.AddTicks(-1)).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, Start).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, End.AddTicks(-1)).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, End).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, End.AddYears(1)).Outcome);
        }

        [Fact]
        public void An_open_start_or_end_never_closes_that_side()
        {
            UserAccess openStart = Set(Grant(Widget, "Read", endsAt: End));
            UserAccess openEnd = Set(Grant(Widget, "Read", startsAt: Start));

            Assert.Equal(AccessOutcome.Unrestricted, Decide(openStart, DateTimeOffset.MinValue).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(openStart, End).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(openEnd, DateTimeOffset.MinValue).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(openEnd, DateTimeOffset.MaxValue).Outcome);
        }

        [Fact]
        public void The_window_is_judged_in_utc_whatever_the_offsets()
        {
            UserAccess access = Set(Grant(Widget, "Read", startsAt: Start.ToOffset(TimeSpan.FromHours(3)), endsAt: End.ToOffset(TimeSpan.FromHours(-5))));

            Assert.Equal(AccessOutcome.Denied, Decide(access, Start.AddTicks(-1).ToOffset(TimeSpan.FromHours(10))).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, Start.ToOffset(TimeSpan.FromHours(-8))).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, End.ToOffset(TimeSpan.FromHours(14))).Outcome);
        }

        [Fact]
        public void Only_the_grants_current_at_the_instant_are_composed_and_listed()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1, startsAt: Start, endsAt: End),
                Grant(Widget, "Read", "Name = 'x'", permissionId: 2),
                Grant(Widget, "Read", null, permissionId: 3, startsAt: End));

            AccessDecision before = Decide(access, Now);
            Assert.Equal([("Name = 'x'", QueryexLanguage.Version)], Leaves(before));
            Assert.Equal([2], PermissionIds(before));

            AccessDecision inside = Decide(access, Start);
            Assert.Equal([("Amount > 10", QueryexLanguage.Version), ("Name = 'x'", QueryexLanguage.Version)], Leaves(inside));
            Assert.Equal([1, 2], PermissionIds(inside));

            // At the end of the first window the unfiltered future grant opens and absorbs the rest.
            AccessDecision after = Decide(access, End);
            Assert.Equal(AccessOutcome.Unrestricted, after.Outcome);
            Assert.Equal([2, 3], PermissionIds(after));
        }

        [Fact]
        public void Two_windows_of_one_role_grant_inside_either_and_nothing_between_them()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", startsAt: Start, endsAt: End),
                Grant(Widget, "Read", startsAt: End.AddHours(2), endsAt: End.AddHours(4)));

            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, Start).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, End.AddHours(1)).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, End.AddHours(3)).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, End.AddHours(4)).Outcome);
        }

        // --- The next boundary ---

        [Fact]
        public void The_next_boundary_is_the_earliest_start_or_end_strictly_after_the_instant()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", permissionId: 1, startsAt: Start, endsAt: End),
                Grant(Widget, "Save", permissionId: 2, startsAt: Now.AddHours(2)),
                Grant(Widget, "Delete", permissionId: 3));

            Assert.Equal(Start, access.NextBoundary(Now));
            Assert.Equal(Now.AddHours(2), access.NextBoundary(Start));
            Assert.Equal(End, access.NextBoundary(Now.AddHours(2)));
            Assert.Null(access.NextBoundary(End));
            Assert.Equal(TimeSpan.Zero, access.NextBoundary(Now.ToOffset(TimeSpan.FromHours(3)))!.Value.Offset);
        }

        [Fact]
        public void A_set_without_windows_has_no_next_boundary()
        {
            Assert.Null(Set(Grant(Widget, "Read")).NextBoundary(Now));
            Assert.Null(Set().NextBoundary(DateTimeOffset.MinValue));
        }

        [Fact]
        public void Moving_the_host_clock_to_each_next_boundary_walks_every_window()
        {
            FakeTimeProvider clock = new(Now);
            UserAccess access = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1, startsAt: Start, endsAt: End),
                Grant(Widget, "Read", null, permissionId: 2, startsAt: End.AddHours(1), endsAt: End.AddHours(2)));
            List<(DateTimeOffset, AccessOutcome)> walk = [(clock.GetUtcNow(), Decide(access, clock.GetUtcNow()).Outcome)];

            // The caller re-decides at each boundary the set reports, and at no other instant.
            while (access.NextBoundary(clock.GetUtcNow()) is DateTimeOffset next)
            {
                clock.SetUtcNow(next);
                walk.Add((clock.GetUtcNow(), Decide(access, clock.GetUtcNow()).Outcome));
            }

            Assert.Equal(
                [
                    (Now, AccessOutcome.Denied),
                    (Start, AccessOutcome.Filtered),
                    (End, AccessOutcome.Denied),
                    (End.AddHours(1), AccessOutcome.Unrestricted),
                    (End.AddHours(2), AccessOutcome.Denied),
                ],
                walk);
        }

        // --- The memo never outlives its interval ---

        [Fact]
        public void A_memoised_decision_is_recomposed_at_the_next_boundary_in_either_direction()
        {
            UserAccess access = Set(Grant(Widget, "Read", startsAt: Start, endsAt: End));

            AccessDecision inside = Decide(access, Start);
            Assert.Same(inside, Decide(access, End.AddTicks(-1)));
            Assert.Equal(AccessOutcome.Denied, Decide(access, End).Outcome);

            // Asked again inside after an instant outside, the decision is current again.
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, Start.AddMinutes(1)).Outcome);
            Assert.Equal(AccessOutcome.Denied, Decide(access, Start.AddTicks(-1)).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, Decide(access, Start).Outcome);
        }

        [Fact]
        public void Decisions_from_many_threads_at_many_instants_agree_with_the_windows()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1, startsAt: Start, endsAt: End),
                Grant(Widget, "Read", null, permissionId: 2, startsAt: End));
            DateTimeOffset[] instants = [Now, Start, Start.AddHours(1), End, End.AddHours(1)];
            AccessOutcome[] expected = [AccessOutcome.Denied, AccessOutcome.Filtered, AccessOutcome.Filtered, AccessOutcome.Unrestricted, AccessOutcome.Unrestricted];

            _ = Parallel.For(0, 2000, index =>
            {
                int pick = index % instants.Length;
                Assert.Equal(expected[pick], Decide(access, instants[pick]).Outcome);
            });
        }

        // --- The validation instant ---

        [Fact]
        public void The_validation_instant_starts_at_the_build_and_only_moves_forward()
        {
            UserAccess access = Set();

            Assert.Equal(Now, access.ValidatedAt);

            access.MarkValidated(Now.AddMinutes(5));
            Assert.Equal(Now.AddMinutes(5), access.ValidatedAt);

            access.MarkValidated(Now.AddMinutes(1));
            Assert.Equal(Now.AddMinutes(5), access.ValidatedAt);

            // An instant before the build never moves it before the build.
            UserAccess fresh = Set();
            fresh.MarkValidated(Now.AddDays(-1));
            Assert.Equal(Now, fresh.ValidatedAt);
            Assert.Equal(TimeSpan.Zero, fresh.ValidatedAt.Offset);
        }

        [Fact]
        public void Concurrent_validations_keep_the_latest_instant()
        {
            UserAccess access = Set();

            _ = Parallel.For(0, 1000, minute => access.MarkValidated(Now.AddMinutes(minute)));

            Assert.Equal(Now.AddMinutes(999), access.ValidatedAt);
        }

        [Fact]
        public void A_copy_keeps_the_validation_instant_but_marks_its_own_from_then_on()
        {
            UserAccess access = Set();
            access.MarkValidated(Now.AddMinutes(5));

            UserAccess copy = access with { };
            copy.MarkValidated(Now.AddMinutes(9));

            Assert.Equal(Now.AddMinutes(9), copy.ValidatedAt);
            Assert.Equal(Now.AddMinutes(5), access.ValidatedAt);
        }

        [Fact]
        public void The_system_set_ignores_validation()
        {
            UserAccess.System.MarkValidated(Now);

            Assert.Equal(DateTimeOffset.UnixEpoch, UserAccess.System.ValidatedAt);
        }

        /// <summary>Decides reading the widgets, without criteria, at an instant.</summary>
        /// <param name="access">The set.</param>
        /// <param name="at">The instant.</param>
        /// <returns>The decision.</returns>
        private static AccessDecision Decide(UserAccess access, DateTimeOffset at)
        {
            return access.Decide(WidgetSecurable("Read"), [], null, at);
        }

        /// <summary>The permission of every listed grant, all of roles.</summary>
        /// <param name="decision">The decision.</param>
        /// <returns>The permissions, in order.</returns>
        private static List<int> PermissionIds(AccessDecision decision)
        {
            return [.. decision.Grants.Cast<RoleAccessGrant>().Select(static grant => grant.PermissionId)];
        }
    }
}
