// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     What a caller may hand out: a pair is covered only by an unrestricted, current grant of the
    ///     caller's that holds it, through either wildcard, and reading also through any action that
    ///     implies it.
    /// </summary>
    public class AccessDelegationTests
    {
        /// <summary>The wildcard action.</summary>
        private const string Any = AccessActions.Wildcard;

        // --- The matrix of the two wildcards ---

        [Theory]
        [InlineData(Widget, "Save", Widget, "Save", true)]
        [InlineData(Widget, Any, Widget, "Save", true)]
        [InlineData(Widget, Any, Widget, Any, true)]
        [InlineData(Widget, Any, "*", "Save", false)]
        [InlineData(Widget, Any, "*", Any, false)]
        [InlineData("*", "Save", Widget, "Save", true)]
        [InlineData("*", "Save", "*", "Save", true)]
        [InlineData("*", "Save", Widget, Any, false)]
        [InlineData("*", "Save", "*", Any, false)]
        [InlineData(Widget, "Save", Widget, Any, false)]
        [InlineData(Widget, "Save", "*", "Save", false)]
        [InlineData(Widget, "Save", Kind, "Save", false)]
        public void A_grant_covers_a_pair_through_the_same_wildcard_only(string heldResource, string heldAction, string resource, string action, bool covered)
        {
            UserAccess caller = Caller(5, (heldResource, heldAction));

            Assert.Equal(covered, AccessDelegation.Covers(caller, resource, action, Now));
        }

        [Theory]
        [InlineData(Widget, "Save")]
        [InlineData(Widget, Any)]
        [InlineData("*", Any)]
        [InlineData("*", "Read")]
        [InlineData(Kind, "Read")]
        public void The_unrestricted_wildcard_pair_and_the_system_set_cover_every_pair(string resource, string action)
        {
            Assert.True(AccessDelegation.Covers(Caller(5, ("*", Any)), resource, action, Now));
            Assert.True(AccessDelegation.Covers(UserAccess.System, resource, action, Now));
            Assert.True(AccessDelegation.IsExempt(Caller(5, ("*", Any)), Now));
            Assert.False(AccessDelegation.IsExempt(Caller(5, (Widget, Any)), Now));
        }

        // --- Reading, implied by any other action ---

        [Fact]
        public void Reading_a_resource_is_covered_by_any_action_on_it()
        {
            Assert.True(AccessDelegation.Covers(Caller(5, (Widget, "Save")), Widget, AccessActions.Read, Now));
            Assert.False(AccessDelegation.Covers(Caller(5, (Kind, "Save")), Widget, AccessActions.Read, Now));
        }

        [Fact]
        public void Reading_a_resource_is_covered_by_an_action_on_the_wildcard_resource_only_where_the_resource_registers_it()
        {
            // The widgets register Approve; the kinds do not.
            UserAccess caller = Caller(5, ("*", "Approve"));

            Assert.True(AccessDelegation.Covers(caller, Widget, AccessActions.Read, Now));
            Assert.False(AccessDelegation.Covers(caller, Kind, AccessActions.Read, Now));
            Assert.False(AccessDelegation.Covers(caller, Widget, "Save", Now));
        }

        // --- Grants that never qualify ---

        [Fact]
        public void A_filtered_grant_covers_nothing()
        {
            UserAccess caller = Caller(5, [Grant(Widget, Any, filter: "Amount > 5")]);

            Assert.False(AccessDelegation.Covers(caller, Widget, "Save", Now));
            Assert.False(AccessDelegation.Covers(caller, Widget, AccessActions.Read, Now));
        }

        [Fact]
        public void A_grant_that_has_not_started_or_has_ended_covers_nothing()
        {
            UserAccess future = Caller(5, [Grant(Widget, "Save", startsAt: Now.AddDays(1))]);
            UserAccess ended = Caller(5, [Grant(Widget, "Save", startsAt: Now.AddDays(-2), endsAt: Now)]);
            UserAccess current = Caller(5, [Grant(Widget, "Save", startsAt: Now.AddDays(-2), endsAt: Now.AddDays(1))]);

            Assert.False(AccessDelegation.Covers(future, Widget, "Save", Now));
            Assert.False(AccessDelegation.Covers(ended, Widget, "Save", Now));
            Assert.True(AccessDelegation.Covers(current, Widget, "Save", Now));
            Assert.False(AccessDelegation.IsExempt(Caller(5, [Grant("*", Any, startsAt: Now.AddDays(1))]), Now));
        }

        [Fact]
        public void Names_compare_ignoring_case()
        {
            Assert.True(AccessDelegation.Covers(Caller(5, ("FX.WIDGET", "save")), Widget, "Save", Now));
        }
    }
}
