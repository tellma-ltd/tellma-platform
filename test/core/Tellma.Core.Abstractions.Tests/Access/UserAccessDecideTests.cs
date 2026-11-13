// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;
using static Tellma.Core.Abstractions.Tests.Access.AccessFixture;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>
    ///     The composition contract over hand-built sets: the system set, a set without a user, the
    ///     matching of resources and actions with their wildcards and the implied read, absorption,
    ///     union, the pruning of filtered grants where no filter binds, public roles, bespoke criteria,
    ///     drifted permissions, the leaves' order and stamps, and the arguments refused.
    /// </summary>
    public class UserAccessDecideTests
    {
        // --- The system set and a set without a user ---

        [Fact]
        public void The_system_set_is_unrestricted_on_every_securable_through_one_system_grant()
        {
            AccessDecision decision = UserAccess.System.Decide(WidgetSecurable("Delete"), [Criterion("Read", "Id = 1")], null, Now);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Null(decision.Filter);
            Assert.Equal([new SystemAccessGrant(Widget, "Delete")], decision.Grants);
            Assert.Empty(decision.Problems);
            Assert.True(UserAccess.System.IsSystem);
            Assert.Equal(WellKnownIds.SystemUserId, UserAccess.System.UserId);
        }

        [Fact]
        public void Only_the_system_set_and_its_copies_hold_everything()
        {
            UserAccess copy = UserAccess.System with { TenantId = 3 };

            Assert.True(copy.IsSystem);
            Assert.Equal(AccessOutcome.Unrestricted, copy.Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
            Assert.False(Set(Grant("*", "*")).IsSystem);
            Assert.False(new UserAccess().IsSystem);

            // No initializer outside the set can mark one as the system set.
            Assert.False(typeof(UserAccess).GetProperty(nameof(UserAccess.IsSystem))!.SetMethod!.IsPublic);
        }

        [Fact]
        public void A_set_without_a_user_denies_everything_criteria_and_grants_included()
        {
            UserAccess anonymous = Set(Grant("*", "*")) with { UserId = 0 };

            AccessDecision decision = anonymous.Decide(WidgetSecurable("Read"), [Criterion("*", "Id = 1")], null, Now);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.False(decision.IsAllowed);
            Assert.Empty(decision.Grants);
            Assert.Equal(AccessOutcome.Denied, (Set(Grant("*", "*")) with { UserId = -1 }).Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
        }

        [Fact]
        public void An_empty_set_denies_every_securable_with_no_grant_and_no_problem()
        {
            AccessDecision decision = Set().Decide(WidgetSecurable("Read"), [], null, Now);

            Assert.Equal((Widget, "Read", AccessOutcome.Denied), (decision.Resource, decision.Action, decision.Outcome));
            Assert.Null(decision.Filter);
            Assert.Empty(decision.Grants);
            Assert.Empty(decision.Problems);
        }

        // --- Matching: wildcards and the implied read ---

        [Fact]
        public void A_grant_reaches_its_own_pair_ignoring_case_and_no_other_action_but_read()
        {
            UserAccess access = Set(Grant("FX.widget", "save"));

            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Save"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(WidgetSecurable("Delete"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(Securable(Kind, "Save", Kind), [], null, Now).Outcome);
        }

        [Fact]
        public void A_read_grant_implies_no_other_action()
        {
            UserAccess access = Set(Grant(Widget, "Read"));

            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(WidgetSecurable("Save"), [], null, Now).Outcome);
        }

        [Fact]
        public void A_grant_on_another_action_implies_reading_with_the_same_filter_never_a_wider_one()
        {
            UserAccess access = Set(Grant(Widget, "Save", "Amount > 10"));

            AccessDecision read = access.Decide(WidgetSecurable("Read"), [], null, Now);

            Assert.Equal(AccessOutcome.Filtered, read.Outcome);
            Assert.Equal([("Amount > 10", QueryexLanguage.Version)], Leaves(read));
            Assert.Equal(access.Grants, read.Grants);
            Assert.Equal(AccessOutcome.Denied, access.Decide(WidgetSecurable("Delete"), [], null, Now).Outcome);
        }

        [Fact]
        public void The_wildcard_action_reaches_every_action_of_its_resource_only()
        {
            UserAccess access = Set(Grant(Widget, "*"));

            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Save"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Approve"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(Securable(Kind, "Read", Kind), [], null, Now).Outcome);
        }

        [Fact]
        public void The_wildcard_resource_reaches_its_action_on_every_resource_and_reading_or_everything_everywhere()
        {
            UserAccess save = Set(Grant("*", "Save"));

            Assert.Equal(AccessOutcome.Unrestricted, save.Decide(Securable(Kind, "Save", Kind), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, save.Decide(Securable(Kind, "Delete", Kind), [], null, Now).Outcome);

            UserAccess read = Set(Grant("*", "Read"));
            Assert.Equal(AccessOutcome.Unrestricted, read.Decide(Securable("fx.Anything", "Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, read.Decide(Securable(Kind, "Save", Kind), [], null, Now).Outcome);

            UserAccess everything = Set(Grant("*", "*"));
            Assert.Equal(AccessOutcome.Unrestricted, everything.Decide(Securable("fx.Anything", "Approve"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, everything.Decide(Securable("fx.Anything", "Read"), [], null, Now).Outcome);
        }

        [Fact]
        public void A_wildcard_resource_grant_on_another_action_implies_reading_only_the_resources_that_register_it()
        {
            // Only the widgets register Invite: the grant reads them and nothing else.
            UserAccess access = Set(Grant("*", "Invite"));

            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(Securable(Kind, "Read", Kind), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(Securable("fx.Anything", "Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Denied, access.Decide(WidgetSecurable("Save"), [], null, Now).Outcome);

            // Without the registry's actions a set implies no read through the wildcard resource.
            UserAccess unregistered = access with { ActionResources = ActionResourceMap.Empty };
            Assert.Equal(AccessOutcome.Denied, unregistered.Decide(WidgetSecurable("Read"), [], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, unregistered.Decide(WidgetSecurable("Invite"), [], null, Now).Outcome);
        }

        [Fact]
        public void A_filtered_grant_on_the_wildcard_resource_or_a_public_one_naming_it_cannot_be_built()
        {
            // No one root binds a filter on every resource, so the composition never sees one.
            _ = Assert.Throws<ArgumentException>(() => Grant("*", "Approve", "Amount > 10"));
            _ = Assert.Throws<ArgumentException>(() => Grant("*", "Read", "Amount > 10"));
            _ = Assert.Throws<ArgumentException>(() => Grant("*", "*", "Amount > 10"));

            // A public role reaches every user, so its grant never names every resource.
            _ = Assert.Throws<ArgumentException>(() => Grant("*", "Read", isPublic: true));
            _ = Assert.Throws<ArgumentException>(() => Grant("*", "*", isPublic: true));

            // The same shapes on a concrete resource, and the wildcard resource unfiltered, are grants.
            Assert.Equal("Amount > 10", Grant(Widget, "*", "Amount > 10", isPublic: true).Filter);
            Assert.False(Grant("*", "*").IsPublic);
        }

        [Fact]
        public void A_filtered_grant_on_another_action_carries_its_filter_to_reading_its_own_resource_only()
        {
            UserAccess access = Set(Grant(Widget, "Approve", "Amount > 10"));

            AccessDecision widgets = access.Decide(WidgetSecurable("Read"), [], null, Now);
            Assert.Equal(AccessOutcome.Filtered, widgets.Outcome);
            Assert.Equal([("Amount > 10", QueryexLanguage.Version)], Leaves(widgets));

            AccessDecision kinds = access.Decide(Securable(Kind, "Read", Kind), [], null, Now);
            Assert.Equal(AccessOutcome.Denied, kinds.Outcome);
            Assert.Empty(kinds.Grants);
        }

        [Fact]
        public void A_problem_on_the_wildcard_resource_is_listed_only_where_its_pair_would_have_reached()
        {
            AccessProblem invite = Problem(1, "*", "Invite", AccessProblemCode.WildcardResourceOnPublicRole);
            UserAccess access = Set() with { Problems = [invite] };

            Assert.Equal([invite], access.Decide(WidgetSecurable("Read"), [], null, Now).Problems);
            Assert.Empty(access.Decide(Securable(Kind, "Read", Kind), [], null, Now).Problems);
        }

        // --- Absorption and union ---

        [Fact]
        public void An_unfiltered_grant_absorbs_every_filtered_one_and_every_candidate_is_listed()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1),
                Grant(Widget, "Save", null, permissionId: 2),
                Grant(Widget, "Read", "Name = 'x'", permissionId: 3),
                Grant(Kind, "Read", null, permissionId: 4));

            AccessDecision decision = access.Decide(WidgetSecurable("Read"), [], null, Now);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Null(decision.Filter);
            Assert.Equal([1, 2, 3], decision.Grants.Cast<RoleAccessGrant>().Select(static grant => grant.PermissionId));
        }

        [Fact]
        public void Filters_union_as_one_leaf_per_distinct_text_sorted_ordinally_whatever_the_roles()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", "Name = 'b'", permissionId: 1, roleId: 10),
                Grant(Widget, "Read", "Amount > 10", permissionId: 2, roleId: 11),
                Grant(Widget, "*", "Name = 'b'", permissionId: 3, roleId: 12),
                Grant(Widget, "Save", "amount > 5", permissionId: 4, roleId: 13));

            AccessDecision decision = access.Decide(WidgetSecurable("Read"), [], null, Now);

            // Ordinal: an upper-case letter sorts before every lower-case one.
            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.Equal(
                [("Amount > 10", QueryexLanguage.Version), ("Name = 'b'", QueryexLanguage.Version), ("amount > 5", QueryexLanguage.Version)],
                Leaves(decision));
            Assert.Equal([1, 2, 3, 4], decision.Grants.Cast<RoleAccessGrant>().Select(static grant => grant.PermissionId));
        }

        [Fact]
        public void Every_leaf_carries_the_stamp_of_its_grant()
        {
            UserAccess access = Set(Grant(Widget, "Read", "Amount > 10"));

            FilterTree.LeafNode leaf = Assert.IsType<FilterTree.LeafNode>(Assert.Single(Assert.IsType<FilterTree.OrNode>(access.Decide(WidgetSecurable("Read"), [], null, Now).Filter).Children));

            Assert.Equal(access.Grants[0].FilterQueryexVersion, leaf.QueryexVersion);
        }

        [Fact]
        public void Equal_grant_shapes_compile_to_byte_identical_sql_whatever_their_order_roles_and_ids()
        {
            UserAccess first = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1, roleId: 10),
                Grant(Widget, "Read", "Kind.Name = 'x'", permissionId: 2, roleId: 11),
                Grant(Widget, "Read", "OwnerId = me()", permissionId: 3, roleId: 12));
            UserAccess second = Set(
                Grant(Widget, "*", "OwnerId = me()", permissionId: 30, roleId: 40),
                Grant(Widget, "Save", "Kind.Name = 'x'", permissionId: 31, roleId: 41),
                Grant(Widget, "Read", "Amount > 10", permissionId: 32, roleId: 42),
                Grant(Widget, "Read", "Amount > 10", permissionId: 33, roleId: 43));
            UserAccess third = Set(
                Grant(Widget, "Read", "Amount > 10"),
                Grant(Widget, "Read", "Amount > 11"));

            string firstSql = CompileWidgets(first.Decide(WidgetSecurable("Read"), [], null, Now).Filter!);
            string secondSql = CompileWidgets(second.Decide(WidgetSecurable("Read"), [], null, Now).Filter!);

            Assert.Equal(firstSql, secondSql, StringComparer.Ordinal);
            Assert.NotEqual(firstSql, CompileWidgets(third.Decide(WidgetSecurable("Read"), [], null, Now).Filter!));
        }

        // --- Where no filter binds ---

        [Fact]
        public void A_filtered_grant_on_every_action_never_grants_an_action_that_takes_no_filter()
        {
            UserAccess access = Set(Grant(Widget, "*", "Amount > 10"));

            Assert.Equal(AccessOutcome.Filtered, access.Decide(WidgetSecurable("Save"), [], null, Now).Outcome);
            AccessDecision approve = access.Decide(Securable(Widget, "Approve"), [], null, Now);
            Assert.Equal(AccessOutcome.Denied, approve.Outcome);
            Assert.Empty(approve.Grants);

            // Unfiltered, the same row grants the action that takes no filter.
            Assert.Equal(AccessOutcome.Unrestricted, Set(Grant(Widget, "*")).Decide(Securable(Widget, "Approve"), [], null, Now).Outcome);
        }

        [Fact]
        public void Where_no_filter_binds_a_filtered_grant_is_dropped_and_only_the_unfiltered_ones_are_listed()
        {
            UserAccess access = Set(
                Grant(Widget, "Approve", "Amount > 10", permissionId: 1),
                Grant(Widget, "Approve", null, permissionId: 2));

            AccessDecision decision = access.Decide(Securable(Widget, "Approve"), [], null, Now);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Equal([2], decision.Grants.Cast<RoleAccessGrant>().Select(static grant => grant.PermissionId));
        }

        // --- Public roles ---

        [Fact]
        public void A_public_roles_grant_reaches_the_caller_and_unions_with_their_own()
        {
            UserAccess access = Set(
                Grant(Widget, "Read", "Amount > 10", permissionId: 1, roleId: 10),
                Grant(Widget, "Read", "Name = 'public'", permissionId: 2, roleId: 20, isPublic: true));

            AccessDecision decision = access.Decide(WidgetSecurable("Read"), [], null, Now);

            Assert.Equal([("Amount > 10", QueryexLanguage.Version), ("Name = 'public'", QueryexLanguage.Version)], Leaves(decision));
            Assert.Contains(decision.Grants, static grant => grant is RoleAccessGrant { IsPublic: true, RoleId: 20 });
        }

        // --- Bespoke criteria ---

        [Fact]
        public void A_criterion_allows_access_on_its_own_and_is_listed_as_a_bespoke_grant()
        {
            AccessCriterion self = Criterion("Read", "OwnerId = me()");

            AccessDecision decision = Set().Decide(WidgetSecurable("Read"), [self], null, Now);

            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.Same(self.Filter, Assert.Single(Assert.IsType<FilterTree.OrNode>(decision.Filter).Children));
            Assert.Equal([new BespokeAccessGrant(Widget, "Read", "OwnerId = me()", "self")], decision.Grants);
        }

        [Fact]
        public void A_criterion_contributes_to_its_action_or_with_the_wildcard_to_every_action_and_implies_no_read()
        {
            AccessCriterion save = Criterion("Save", "Amount > 10");
            AccessCriterion every = Criterion("*", "Name = 'x'", "shared");

            Assert.Equal(AccessOutcome.Denied, Set().Decide(WidgetSecurable("Read"), [save], null, Now).Outcome);
            Assert.Equal(AccessOutcome.Filtered, Set().Decide(WidgetSecurable("save"), [save], null, Now).Outcome);
            Assert.Equal(
                [new BespokeAccessGrant(Widget, "*", "Name = 'x'", "shared")],
                Set().Decide(WidgetSecurable("Delete"), [save, every], null, Now).Grants);
        }

        [Fact]
        public void A_criterion_unions_after_the_role_leaves_and_never_absorbs_or_removes_anything()
        {
            AccessCriterion self = Criterion("Read", "OwnerId = me()");

            AccessDecision filtered = Set(Grant(Widget, "Read", "Amount > 10")).Decide(WidgetSecurable("Read"), [self], null, Now);
            FilterTree.OrNode or = Assert.IsType<FilterTree.OrNode>(filtered.Filter);
            Assert.Equal("Amount > 10", Assert.IsType<FilterTree.LeafNode>(or.Children[0]).Text);
            Assert.Same(self.Filter, or.Children[1]);
            Assert.Equal(2, filtered.Grants.Count);

            // Beside an unrestricted grant, the criterion adds nothing and is not listed.
            AccessDecision unrestricted = Set(Grant(Widget, "Read")).Decide(WidgetSecurable("Read"), [self], null, Now);
            Assert.Equal(AccessOutcome.Unrestricted, unrestricted.Outcome);
            _ = Assert.IsType<RoleAccessGrant>(Assert.Single(unrestricted.Grants));
        }

        [Fact]
        public void A_criterion_never_reaches_a_securable_that_takes_no_filter()
        {
            AccessDecision decision = Set().Decide(Securable(Widget, "Approve"), [Criterion("*", "OwnerId = me()")], null, Now);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Empty(decision.Grants);
        }

        [Fact]
        public void A_composite_criterion_is_listed_with_its_tree_rendered_as_text()
        {
            AccessCriterion criterion = new(
                "Read",
                FilterTree.And([FilterTree.Leaf("OwnerId = me()"), FilterTree.Not(FilterTree.Or([])), FilterTree.Via("Kind", FilterTree.Leaf("Name = 'x'"))]),
                "self");

            BespokeAccessGrant grant = Assert.IsType<BespokeAccessGrant>(Assert.Single(Set().Decide(WidgetSecurable("Read"), [criterion], null, Now).Grants));

            Assert.Equal("(OwnerId = me()) and (not (false)) and (via(Kind, Name = 'x'))", grant.Filter);
        }

        // --- Drifted permissions ---

        [Fact]
        public void A_drifted_permission_grants_nothing_and_is_listed_on_the_decisions_it_would_have_reached()
        {
            AccessProblem invalid = Problem(1, Widget, "Read", AccessProblemCode.FilterInvalid);
            AccessProblem unknownAction = Problem(2, Kind, "Approve", AccessProblemCode.UnknownAction);
            AccessProblem wildcard = Problem(3, "*", "Save", AccessProblemCode.WildcardResourceOnPublicRole);
            UserAccess access = Set(Grant(Widget, "Read")) with { Problems = [invalid, unknownAction, wildcard] };

            AccessDecision widgetRead = access.Decide(WidgetSecurable("Read"), [], null, Now);
            Assert.Equal(AccessOutcome.Unrestricted, widgetRead.Outcome);
            Assert.Equal([invalid, wildcard], widgetRead.Problems);

            AccessDecision kindRead = access.Decide(Securable(Kind, "Read", Kind), [], null, Now);
            Assert.Equal(AccessOutcome.Denied, kindRead.Outcome);
            Assert.Empty(kindRead.Grants);
            Assert.Equal([unknownAction, wildcard], kindRead.Problems);

            Assert.Equal([wildcard], access.Decide(WidgetSecurable("Save"), [], null, Now).Problems);
            Assert.Empty(access.Decide(Securable(Kind, "Delete", Kind), [], null, Now).Problems);
        }

        // --- Memoisation ---

        [Fact]
        public void The_direct_decision_is_memoised_between_two_boundaries_and_a_copy_keeps_none()
        {
            UserAccess access = Set(Grant(Widget, "Read", "Amount > 10"));

            AccessDecision first = access.Decide(WidgetSecurable("Read"), [], null, Now);
            AccessDecision again = access.Decide(WidgetSecurable("Read"), [], null, Now.AddDays(300));

            Assert.Same(first, again);
            Assert.NotSame(first, (access with { TenantId = 8 }).Decide(WidgetSecurable("Read"), [], null, Now));

            // With a contributing criterion the decision is composed afresh over the memoised side.
            AccessDecision withCriterion = access.Decide(WidgetSecurable("Read"), [Criterion("Read", "OwnerId = me()")], null, Now);
            Assert.NotSame(first, withCriterion);
            Assert.Same(first.Grants[0], withCriterion.Grants[0]);
        }

        [Fact]
        public void The_memo_holds_at_most_4096_securables_and_composes_any_other_afresh()
        {
            UserAccess access = Set(Grant("*", "Read"));
            List<AccessDecision> firsts = [];
            for (int index = 0; index < 4096; index++)
            {
                firsts.Add(access.Decide(Securable($"fx.Resource{index}", "Read"), [], null, Now));
            }

            // Full, the memo still serves every securable it holds.
            Assert.Same(firsts[0], access.Decide(Securable("fx.Resource0", "Read"), [], null, Now));
            Assert.Same(firsts[^1], access.Decide(Securable("fx.Resource4095", "Read"), [], null, Now));

            // One more is decided as it should be, but composed afresh every time rather than held.
            AccessDecision beyond = access.Decide(Securable("fx.Resource4096", "Read"), [], null, Now);
            AccessDecision again = access.Decide(Securable("fx.Resource4096", "Read"), [], null, Now);
            Assert.Equal((AccessOutcome.Unrestricted, AccessOutcome.Unrestricted), (beyond.Outcome, again.Outcome));
            Assert.Equal(access.Grants, again.Grants);
            Assert.NotSame(beyond, again);
        }

        [Fact]
        public void Sets_compare_by_identity_and_a_copy_is_another_set()
        {
            UserAccess access = Set(Grant(Widget, "Read"));
            UserAccess copy = access with { };

            Assert.Equal(access, access);
            Assert.NotEqual(access, copy);
            Assert.Equal(access.Grants, copy.Grants);
            Assert.NotEqual(access.GetHashCode(), new UserAccess().GetHashCode());
        }

        // --- The arguments refused ---

        [Fact]
        public void A_wildcard_securable_null_criteria_and_a_null_criterion_are_refused()
        {
            UserAccess access = Set(Grant("*", "*"));

            _ = Assert.Throws<ArgumentException>(() => access.Decide(Securable("*", "Read"), [], null, Now));
            _ = Assert.Throws<ArgumentException>(() => access.Decide(Securable(Widget, "*", Widget), [], null, Now));
            _ = Assert.Throws<ArgumentNullException>(() => access.Decide(null!, [], null, Now));
            _ = Assert.Throws<ArgumentNullException>(() => access.Decide(WidgetSecurable("Read"), null!, null, Now));
            _ = Assert.Throws<ArgumentException>(() => access.Decide(WidgetSecurable("Read"), [null!], null, Now));

            // The system set refuses them too: the contract does not depend on the set.
            _ = Assert.Throws<ArgumentException>(() => UserAccess.System.Decide(Securable("*", "Read"), [], null, Now));
        }

        [Fact]
        public void An_owner_decision_is_refused_without_an_owner_and_required_with_one_for_the_owners_pair()
        {
            UserAccess access = Set(Grant("*", "*"));
            SecurableDescriptor child = Securable(Line, "Read", Line, new SecurableOwner(Header, "Header"));

            _ = Assert.Throws<ArgumentException>(() => access.Decide(WidgetSecurable("Read"), [], AccessDecision.Deny(Header, "Read"), Now));
            _ = Assert.Throws<ArgumentException>(() => access.Decide(child, [], null, Now));
            _ = Assert.Throws<ArgumentException>(() => access.Decide(child, [], AccessDecision.Deny(Book, "Read"), Now));
            _ = Assert.Throws<ArgumentException>(() => access.Decide(child, [], AccessDecision.Deny(Header, "Save"), Now));
            Assert.Equal(AccessOutcome.Unrestricted, access.Decide(child, [], AccessDecision.Deny("FX.HEADER", "read"), Now).Outcome);
        }

        /// <summary>A bespoke criterion over one leaf.</summary>
        /// <param name="action">Its action.</param>
        /// <param name="filter">Its leaf's text.</param>
        /// <param name="reason">Its reason key.</param>
        /// <returns>The criterion.</returns>
        private static AccessCriterion Criterion(string action, string filter, string reason = "self")
        {
            return new AccessCriterion(action, FilterTree.Leaf(filter), reason);
        }
    }
}
