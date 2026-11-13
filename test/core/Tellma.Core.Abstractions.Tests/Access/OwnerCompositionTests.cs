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
    ///     The owner rule: a child's decision unions its direct grants with its owner's decision on
    ///     the same action, rebased through the owner's navigation, chained up to the top; either side
    ///     unrestricted is unrestricted, both denied deny, and the stateless composition agrees with
    ///     the set's.
    /// </summary>
    public class OwnerCompositionTests
    {
        /// <summary>Reading the books: the top of the chain.</summary>
        private static readonly SecurableDescriptor BookRead = Securable(Book, "Read", Book);

        /// <summary>Reading the headers, owned by a book.</summary>
        private static readonly SecurableDescriptor HeaderRead = Securable(Header, "Read", Header, new SecurableOwner(Book, "Book"));

        /// <summary>Reading the lines, owned by a header.</summary>
        private static readonly SecurableDescriptor LineRead = Securable(Line, "Read", Line, new SecurableOwner(Header, "Header"));

        // --- One owner ---

        [Fact]
        public void A_direct_grant_alone_filters_the_child_by_its_own_leaves()
        {
            UserAccess access = Set(Grant(Header, "Read", "Amount > 10"));

            AccessDecision decision = DecideHeader(access);

            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.Equal([("Amount > 10", QueryexLanguage.Version)], Leaves(decision));
        }

        [Fact]
        public void An_owner_grant_alone_filters_the_child_through_the_owners_navigation()
        {
            RoleAccessGrant book = Grant(Book, "Read", "Code = 'A'", permissionId: 2);
            UserAccess access = Set(book);

            AccessDecision decision = DecideHeader(access);

            FilterTree.ViaNode via = Assert.IsType<FilterTree.ViaNode>(Assert.Single(Assert.IsType<FilterTree.OrNode>(decision.Filter).Children));
            Assert.Equal("Book", via.Navigation);
            Assert.Equal("Code = 'A'", Assert.IsType<FilterTree.LeafNode>(Assert.Single(Assert.IsType<FilterTree.OrNode>(via.Inner).Children)).Text);
            Assert.Equal([book], decision.Grants);
            _ = Compile(Header, decision.Filter!);
        }

        [Fact]
        public void Both_sides_filtered_union_the_direct_leaves_first_and_the_owner_last()
        {
            RoleAccessGrant header = Grant(Header, "Read", "Amount > 10", permissionId: 1);
            RoleAccessGrant book = Grant(Book, "Read", "Code = 'A'", permissionId: 2);
            UserAccess access = Set(header, book);

            AccessDecision decision = DecideHeader(access);

            FilterTree.OrNode or = Assert.IsType<FilterTree.OrNode>(decision.Filter);
            Assert.Equal(2, or.Children.Count);
            Assert.Equal("Amount > 10", Assert.IsType<FilterTree.LeafNode>(or.Children[0]).Text);
            _ = Assert.IsType<FilterTree.ViaNode>(or.Children[1]);
            Assert.Equal([header, book], decision.Grants);
            _ = Compile(Header, decision.Filter!);
        }

        [Fact]
        public void Neither_side_granting_denies_and_lists_the_problems_of_both()
        {
            AccessProblem headerProblem = Problem(1, Header, "Read", AccessProblemCode.FilterInvalid);
            AccessProblem bookProblem = Problem(2, Book, "Read", AccessProblemCode.FilterInvalid);
            UserAccess access = Set(Grant(Kind, "Read")) with { Problems = [headerProblem, bookProblem] };

            AccessDecision decision = DecideHeader(access);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Null(decision.Filter);
            Assert.Empty(decision.Grants);
            Assert.Equal([headerProblem, bookProblem], decision.Problems);
        }

        [Fact]
        public void Either_side_unrestricted_makes_the_child_unrestricted_and_lists_both_sides()
        {
            RoleAccessGrant header = Grant(Header, "Read", "Amount > 10", permissionId: 1);
            RoleAccessGrant book = Grant(Book, "Read", null, permissionId: 2);

            AccessDecision ownerUnrestricted = DecideHeader(Set(header, book));
            Assert.Equal(AccessOutcome.Unrestricted, ownerUnrestricted.Outcome);
            Assert.Null(ownerUnrestricted.Filter);
            Assert.Equal([header, book], ownerUnrestricted.Grants);

            RoleAccessGrant headerAll = Grant(Header, "Read", null, permissionId: 3);
            RoleAccessGrant bookFiltered = Grant(Book, "Read", "Code = 'A'", permissionId: 4);
            AccessDecision directUnrestricted = DecideHeader(Set(headerAll, bookFiltered));
            Assert.Equal(AccessOutcome.Unrestricted, directUnrestricted.Outcome);
            Assert.Equal([headerAll, bookFiltered], directUnrestricted.Grants);
        }

        [Fact]
        public void The_owner_decision_is_on_the_same_action_so_another_actions_grant_on_the_owner_implies_only_reading()
        {
            SecurableDescriptor headerSave = Securable(Header, "Save", Header, new SecurableOwner(Book, "Book"));
            UserAccess access = Set(Grant(Book, "Save", "Code = 'A'"));

            AccessDecision save = access.Decide(headerSave, [], access.Decide(Securable(Book, "Save", Book), [], null, Now), Now);
            Assert.Equal(AccessOutcome.Filtered, save.Outcome);

            SecurableDescriptor headerDelete = Securable(Header, "Delete", Header, new SecurableOwner(Book, "Book"));
            AccessDecision delete = access.Decide(headerDelete, [], access.Decide(Securable(Book, "Delete", Book), [], null, Now), Now);
            Assert.Equal(AccessOutcome.Denied, delete.Outcome);
        }

        // --- Chains and criteria ---

        [Fact]
        public void A_chain_nests_each_owners_filter_through_its_own_navigation()
        {
            UserAccess access = Set(Grant(Book, "Read", "Code = 'A'"));

            AccessDecision line = DecideLine(access);

            FilterTree.ViaNode header = Assert.IsType<FilterTree.ViaNode>(Assert.Single(Assert.IsType<FilterTree.OrNode>(line.Filter).Children));
            Assert.Equal("Header", header.Navigation);
            FilterTree.ViaNode book = Assert.IsType<FilterTree.ViaNode>(Assert.Single(Assert.IsType<FilterTree.OrNode>(header.Inner).Children));
            Assert.Equal("Book", book.Navigation);
            Assert.Equal(AccessOutcome.Filtered, line.Outcome);

            string sql = Compile(Line, line.Filter!);
            Assert.Contains("[fx].[Books]", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unrestricted_grant_at_the_top_of_a_chain_reaches_the_bottom()
        {
            Assert.Equal(AccessOutcome.Unrestricted, DecideLine(Set(Grant(Book, "Read"))).Outcome);
            Assert.Equal(AccessOutcome.Unrestricted, DecideLine(Set(Grant("*", "Read"))).Outcome);
            Assert.Equal(AccessOutcome.Denied, DecideLine(Set(Grant(Kind, "Read"))).Outcome);
        }

        [Fact]
        public void A_criterion_of_the_child_unions_before_the_owners_filter()
        {
            AccessCriterion criterion = new("Read", FilterTree.Leaf("Amount > 100"), "large");
            UserAccess access = Set(Grant(Book, "Read", "Code = 'A'"));

            AccessDecision decision = access.Decide(HeaderRead, [criterion], access.Decide(BookRead, [], null, Now), Now);

            FilterTree.OrNode or = Assert.IsType<FilterTree.OrNode>(decision.Filter);
            Assert.Same(criterion.Filter, or.Children[0]);
            _ = Assert.IsType<FilterTree.ViaNode>(or.Children[1]);
            _ = Assert.IsType<BespokeAccessGrant>(decision.Grants[0]);
        }

        [Fact]
        public void A_filtered_owner_never_reaches_a_child_that_takes_no_filter()
        {
            SecurableDescriptor unfilterable = Securable(Header, "Approve", null, new SecurableOwner(Book, "Book"));
            UserAccess access = Set(Grant(Book, "Approve", "Code = 'A'"));

            AccessDecision owner = access.Decide(Securable(Book, "Approve", Book), [], null, Now);
            Assert.Equal(AccessOutcome.Filtered, owner.Outcome);

            AccessDecision decision = access.Decide(unfilterable, [], owner, Now);
            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Empty(decision.Grants);
        }

        [Fact]
        public void A_denied_owner_pair_the_registry_does_not_hold_leaves_the_direct_side_alone()
        {
            UserAccess access = Set(Grant(Header, "Read", "Amount > 10"));

            AccessDecision decision = access.Decide(HeaderRead, [], AccessDecision.Deny(Book, "Read"), Now);

            Assert.Equal([("Amount > 10", QueryexLanguage.Version)], Leaves(decision));
        }

        // --- The stateless composition agrees with the set ---

        [Fact]
        public void Composing_the_current_grants_decides_as_the_set_does()
        {
            UserAccess access = Set(
                Grant(Header, "Read", "Amount > 10", permissionId: 1),
                Grant(Header, "Save", "Amount > 5", permissionId: 2, roleId: 11),
                Grant(Book, "Read", "Code = 'A'", permissionId: 3, startsAt: Now.AddHours(-1), endsAt: Now.AddHours(1)),
                Grant(Book, "Read", null, permissionId: 4, startsAt: Now.AddHours(1))) with
            {
                Problems = [Problem(9, Header, "Read", AccessProblemCode.FilterInvalid)],
            };
            AccessCriterion criterion = new("*", FilterTree.Leaf("Amount > 100"), "large");
            DateTimeOffset[] instants = [Now.AddHours(-2), Now, Now.AddHours(1)];

            foreach (DateTimeOffset at in instants)
            {
                AccessDecision owner = access.Decide(BookRead, [], null, at);
                AccessDecision byTheSet = access.Decide(HeaderRead, [criterion], owner, at);

                List<RoleAccessGrant> current = [.. access.Grants.Where(grant => grant.IsCurrentAt(at))];
                AccessDecision composedOwner = AccessComposition.Compose(BookRead, current, access.Problems, [], null);
                AccessDecision composed = AccessComposition.Compose(HeaderRead, current, access.Problems, [criterion], composedOwner);

                Assert.Equal(byTheSet.Outcome, composed.Outcome);
                Assert.Equal(byTheSet.Grants, composed.Grants);
                Assert.Equal(byTheSet.Problems, composed.Problems);
                Assert.Equal(
                    byTheSet.Filter is null ? null : Compile(Header, byTheSet.Filter),
                    composed.Filter is null ? null : Compile(Header, composed.Filter));
            }
        }

        [Fact]
        public void Composition_refuses_null_lists_and_null_items()
        {
            _ = Assert.Throws<ArgumentNullException>(() => AccessComposition.Compose(BookRead, null!, [], [], null));
            _ = Assert.Throws<ArgumentNullException>(() => AccessComposition.Compose(BookRead, [], null!, [], null));
            _ = Assert.Throws<ArgumentNullException>(() => AccessComposition.Compose(BookRead, [], [], null!, null));
            _ = Assert.Throws<ArgumentException>(() => AccessComposition.Compose(BookRead, [null!], [], [], null));
            _ = Assert.Throws<ArgumentException>(() => AccessComposition.Compose(BookRead, [], [null!], [], null));
            _ = Assert.Throws<ArgumentException>(() => AccessComposition.Compose(HeaderRead, [], [], [], null));
            _ = Assert.Throws<ArgumentException>(() => AccessComposition.Compose(Securable(Book, "*", Book), [], [], [], null));
        }

        /// <summary>Decides reading the headers, the book's decision taken first.</summary>
        /// <param name="access">The set.</param>
        /// <returns>The decision.</returns>
        private static AccessDecision DecideHeader(UserAccess access)
        {
            return access.Decide(HeaderRead, [], access.Decide(BookRead, [], null, Now), Now);
        }

        /// <summary>Decides reading the lines, the chain's decisions taken top down.</summary>
        /// <param name="access">The set.</param>
        /// <returns>The decision.</returns>
        private static AccessDecision DecideLine(UserAccess access)
        {
            return access.Decide(LineRead, [], DecideHeader(access), Now);
        }
    }
}
