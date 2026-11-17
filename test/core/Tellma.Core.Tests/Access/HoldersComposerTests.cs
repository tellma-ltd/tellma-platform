// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     "Who holds this" over a seeded tenant: for every registered securable the holders are
    ///     exactly the users whose decision over their own memberships allows it, with that
    ///     decision's outcome and permissions; a user holds it at all exactly when they are a holder
    ///     or everyone does; the grants and problems listed are those a member of every role sees;
    ///     and each role behind them counts its members, a public role its audience.
    /// </summary>
    public sealed class HoldersComposerTests : IDisposable
    {
        /// <summary>The documents' resource.</summary>
        private const string Doc = "acme.Doc";

        /// <summary>The document lines' resource, owned by the documents.</summary>
        private const string Line = "acme.DocLine";

        /// <summary>The notes' resource, owned by the document lines.</summary>
        private const string Note = "acme.Note";

        /// <summary>The reports' resource, with no filter root.</summary>
        private const string Report = "acme.Report";

        /// <summary>The kinds' resource, which only a public role grants.</summary>
        private const string Kind = "acme.Kind";

        /// <summary>The audience: the active users other than the system user, 101 to 110.</summary>
        private const int Audience = 10;

        /// <summary>The instant every decision is taken at.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        /// <summary>
        ///     The fixture's securables: documents with an action that takes no filter, lines owned by
        ///     their document, notes owned by their line, a report, kinds, and the documents' former name.
        /// </summary>
        private static readonly SecurableRegistry Registry = SecurableRegistryTests.Compose(("acme", static builder => builder
            .Add(Doc, "Read", Doc)
            .Add(Doc, "Save", Doc)
            .Add(Doc, "Delete", Doc)
            .Add(Doc, "Approve", null)
            .Add(Line, "Read", Line, owner: new SecurableOwner(Doc, "Doc"))
            .Add(Line, "Save", Line, owner: new SecurableOwner(Doc, "Doc"))
            .Add(Note, "Read", Note, owner: new SecurableOwner(Line, "Line"))
            .Add(Report, "Run", null)
            .Add(Kind, "Read", Kind)
            .Alias("acme.Document", Doc)));

        /// <summary>The fixture's schema.</summary>
        private static readonly QueryexSchema Schema = BuildSchema();

        /// <summary>
        ///     The stored permissions of the tenant's active roles: filtered and unfiltered grants,
        ///     implied reads, wildcard resources and actions, a former name, drift, public roles, and
        ///     a filtered wildcard action that takes no filter where an action takes none.
        /// </summary>
        private static readonly PermissionRow[] Rows =
        [
            Row(1, 10, Doc, "Read", "Amount > 1"),
            Row(2, 10, Doc, "Save", "OwnerId = 5"),
            Row(3, 11, Doc, "*"),
            Row(4, 11, Report, "Run"),
            Row(5, 12, "*", "Read"),
            Row(6, 12, "acme.Nothing", "Read"),
            Row(7, 12, Doc, "Read", "Nothing = 1"),
            Row(8, 13, Line, "Save", "Qty > 0"),
            Row(9, 13, Note, "Read", "Id > 3"),
            Row(10, 14, Kind, "Read", isPublic: true),
            Row(11, 14, Doc, "Read", "Amount < 0", isPublic: true),
            Row(12, 15, Doc, "Approve"),
            Row(13, 15, "acme.Document", "Delete", "Amount > 100"),
            Row(14, 17, Report, "Run", isPublic: true),
            Row(15, 18, Doc, "*", "Amount > 7"),
            Row(16, 18, Line, "Read", "Qty > 1"),
        ];

        /// <summary>The current memberships: user 108 has none, role 16 grants nothing.</summary>
        private static readonly HolderMembership[] Memberships =
        [
            new(101, 10, new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new(102, 10, null),
            new(102, 13, new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            new(103, 11, null),
            new(104, 12, new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero)),
            new(105, 13, null),
            new(106, 15, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)),
            new(106, 10, null),
            new(107, 16, null),
            new(109, 12, new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero)),
            new(109, 15, new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new(110, 18, null),
            new(110, 13, new DateTimeOffset(2027, 3, 1, 0, 0, 0, TimeSpan.Zero)),
        ];

        /// <summary>The users of the tenant other than the system user.</summary>
        private static readonly int[] Users = [.. Enumerable.Range(101, Audience)];

        /// <summary>The bespoke criteria: documents read by their owner, lines of no quantity for anything.</summary>
        private static readonly Dictionary<string, IReadOnlyList<AccessCriterion>> Criteria = new(StringComparer.OrdinalIgnoreCase)
        {
            [Doc] = [new AccessCriterion("Read", FilterTree.Leaf("OwnerId = 1"), "self")],
            [Line] = [new AccessCriterion("*", FilterTree.Leaf("Qty = 0"), "empty")],
        };

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().AddLogging().BuildServiceProvider();

        /// <summary>Builds the oracle's sets.</summary>
        private readonly UserAccessBuilder _builder;

        /// <summary>The composer under test.</summary>
        private readonly HoldersComposer _composer;

        /// <summary>Creates the composer and the oracle's builder over the fixture.</summary>
        public HoldersComposerTests()
        {
            PermissionClassifier classifier = new(Registry, new QueryexEngine());
            _builder = new UserAccessBuilder(
                classifier,
                new AccessMetrics(_services.GetRequiredService<IMeterFactory>()),
                _services.GetRequiredService<ILoggerFactory>(),
                new FakeTimeProvider(Now));
            _composer = new HoldersComposer(Registry, classifier);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        // --- Conformance with the caller's own decision, over every securable ---

        [Fact]
        public void The_holders_of_every_securable_are_the_users_whose_own_memberships_decision_holds_a_role_grant()
        {
            int nonEmpty = 0;
            foreach (SecurableDescriptor securable in Registry.All)
            {
                HoldersComposition composition = Compose(securable);
                var holders = composition.Holders.ToDictionary(static holder => holder.UserId);
                Assert.Equal(composition.Holders.Select(static holder => holder.UserId).Order(), composition.Holders.Select(static holder => holder.UserId));

                foreach (int user in Users)
                {
                    // The oracle: the user's own memberships alone, no public role and no criterion.
                    AccessDecision own = Decide(Own(user), securable, withCriteria: false);
                    if (!own.IsAllowed)
                    {
                        Assert.False(holders.ContainsKey(user), $"User {user} is listed as a holder of {securable}.");
                        continue;
                    }

                    Assert.True(holders.TryGetValue(user, out AccessHolder? holder), $"User {user} is not listed as a holder of {securable}.");
                    Assert.Equal(own.Outcome, holder.Outcome);
                    Assert.Equal(own.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.PermissionId).Distinct().Order(), holder.PermissionIds);
                }

                nonEmpty += composition.Holders.Count > 0 ? 1 : 0;
            }

            // Every securable of the fixture has a holder, so no comparison above was vacuous.
            Assert.Equal(Registry.All.Count, nonEmpty);
        }

        [Fact]
        public void A_user_holds_a_securable_at_all_exactly_when_they_are_a_holder_or_everyone_does()
        {
            foreach (SecurableDescriptor securable in Registry.All)
            {
                HoldersComposition composition = Compose(securable);
                HashSet<int> holders = [.. composition.Holders.Select(static holder => holder.UserId)];

                foreach (int user in Users)
                {
                    AccessDecision full = Decide(Full(user), securable, withCriteria: true);
                    Assert.Equal(full.IsAllowed, holders.Contains(user) || composition.Everyone != AccessOutcome.Denied);
                }
            }
        }

        [Fact]
        public void Everyone_and_the_criteria_are_what_a_user_without_memberships_is_decided_by()
        {
            foreach (SecurableDescriptor securable in Registry.All)
            {
                HoldersComposition composition = Compose(securable);
                AccessDecision nobody = Decide(Full(108), securable, withCriteria: true);

                Assert.Equal(nobody.Outcome, composition.Everyone);
                Assert.Equal(
                    Decide(Set(108, []), securable, withCriteria: true).Grants.OfType<BespokeAccessGrant>().Select(static grant => grant.Reason).Order(StringComparer.Ordinal),
                    composition.Criteria.Select(static grant => grant.Reason).Order(StringComparer.Ordinal));
            }
        }

        [Fact]
        public void The_grants_and_problems_listed_are_those_a_member_of_every_role_is_decided_by()
        {
            foreach (SecurableDescriptor securable in Registry.All)
            {
                HoldersComposition composition = Compose(securable);
                AccessDecision every = Decide(Set(108, [.. Rows]), securable, withCriteria: false);

                Assert.Equal(every.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.PermissionId).Order(), composition.Grants.Select(static grant => grant.PermissionId).Order());
                Assert.All(composition.Grants, static grant => Assert.True(grant.StartsAt is null && grant.EndsAt is null));
                Assert.Equal(
                    every.Problems.Select(static problem => (problem.PermissionId, problem.Code)).Order(),
                    composition.Problems.Select(static problem => (problem.PermissionId, problem.Code)).Order());
            }
        }

        // --- One securable in full ---

        [Fact]
        public void The_holders_of_reading_documents_in_full()
        {
            HoldersComposition composition = Compose(Registry.Find(Doc, "Read")!);

            Assert.Equal(AccessOutcome.Filtered, composition.Everyone);
            Assert.Equal(["self"], composition.Criteria.Select(static grant => grant.Reason));
            Assert.Equal(
                [(10, 1), (10, 2), (11, 3), (12, 5), (14, 11), (15, 12), (15, 13), (18, 15)],
                composition.Grants.Select(static grant => (grant.RoleId, grant.PermissionId)));
            Assert.Equal([(7, 12, AccessProblemCode.FilterInvalid)], composition.Problems.Select(static problem => (problem.PermissionId, problem.RoleId, problem.Code)));
            Assert.Equal(
                [(10, 3), (11, 1), (12, 2), (14, Audience), (15, 2), (18, 1)],
                composition.MembersByRole.OrderBy(static pair => pair.Key).Select(static pair => (pair.Key, pair.Value)));
            Assert.Equal(
                [
                    (101, AccessOutcome.Filtered, "1,2", (DateTimeOffset?)new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                    (102, AccessOutcome.Filtered, "1,2", null),
                    (103, AccessOutcome.Unrestricted, "3", null),
                    (104, AccessOutcome.Unrestricted, "5", new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero)),
                    (106, AccessOutcome.Unrestricted, "1,2,12,13", null),
                    (109, AccessOutcome.Unrestricted, "5,12,13", new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
                    (110, AccessOutcome.Filtered, "15", null),
                ],
                composition.Holders.Select(static holder => (holder.UserId, holder.Outcome, string.Join(',', holder.PermissionIds), holder.EndsAt)));
        }

        [Fact]
        public void A_filtered_grant_never_makes_a_holder_of_an_action_that_takes_no_filter()
        {
            HoldersComposition composition = Compose(Registry.Find(Doc, "Approve")!);

            Assert.Equal([103, 106, 109], composition.Holders.Select(static holder => holder.UserId));
            Assert.DoesNotContain(composition.Grants, static grant => grant.PermissionId == 15);
            Assert.Equal(AccessOutcome.Denied, composition.Everyone);
            Assert.Empty(composition.Criteria);
        }

        [Fact]
        public void A_child_securables_holders_include_its_owners_through_the_whole_chain()
        {
            HoldersComposition composition = Compose(Registry.Find(Note, "Read")!);

            // Documents' readers reach notes through lines; the line editors through their implied line read.
            Assert.Equal([101, 102, 103, 104, 105, 106, 109, 110], composition.Holders.Select(static holder => holder.UserId));
            Assert.Equal("8,9", string.Join(',', composition.Holders.Single(static holder => holder.UserId == 105).PermissionIds));
            Assert.Equal("8,9,15,16", string.Join(',', composition.Holders.Single(static holder => holder.UserId == 110).PermissionIds));
            Assert.Equal(AccessOutcome.Filtered, composition.Everyone);
        }

        [Fact]
        public void A_public_role_makes_no_holder_and_counts_the_audience()
        {
            HoldersComposition composition = Compose(Registry.Find(Report, "Run")!);

            Assert.Equal(AccessOutcome.Unrestricted, composition.Everyone);
            Assert.Equal([103], composition.Holders.Select(static holder => holder.UserId));
            Assert.Equal([(11, 1), (17, Audience)], composition.MembersByRole.OrderBy(static pair => pair.Key).Select(static pair => (pair.Key, pair.Value)));
        }

        [Fact]
        public void Nothing_read_composes_nobody()
        {
            HoldersComposition composition = _composer.Compose(Registry.Find(Doc, "Read")!, [], [], 0, Schema, static _ => []);

            Assert.Equal(AccessOutcome.Denied, composition.Everyone);
            Assert.Empty(composition.Grants);
            Assert.Empty(composition.Holders);
            Assert.Empty(composition.MembersByRole);
            Assert.Empty(composition.Criteria);
            Assert.Empty(composition.Problems);
        }

        [Fact]
        public void The_arguments_are_checked()
        {
            SecurableDescriptor securable = Registry.Find(Doc, "Read")!;

            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(null!, Rows, Memberships, Audience, Schema, CriteriaOf));
            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(securable, null!, Memberships, Audience, Schema, CriteriaOf));
            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(securable, Rows, null!, Audience, Schema, CriteriaOf));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _composer.Compose(securable, Rows, Memberships, -1, Schema, CriteriaOf));
            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(securable, Rows, Memberships, Audience, null!, CriteriaOf));
            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(securable, Rows, Memberships, Audience, Schema, null!));
            _ = Assert.Throws<ArgumentNullException>(() => _composer.Compose(securable, [null!], Memberships, Audience, Schema, CriteriaOf));
        }

        // --- Helpers ---

        /// <summary>A stored permission without a window, as the holders' round trip reads it.</summary>
        /// <param name="permissionId">The permission's id.</param>
        /// <param name="roleId">The role's id.</param>
        /// <param name="resource">The resource as stored.</param>
        /// <param name="action">The action as stored.</param>
        /// <param name="filter">The filter; null for an unrestricted permission.</param>
        /// <param name="isPublic">Whether the role is public.</param>
        /// <returns>The row.</returns>
        private static PermissionRow Row(int permissionId, int roleId, string resource, string action, string? filter = null, bool isPublic = false)
        {
            return new PermissionRow(permissionId, roleId, $"Role {roleId}", isPublic, resource, action, filter, filter is null ? null : QueryexLanguage.Version, null, null);
        }

        /// <summary>The bespoke criteria of a resource.</summary>
        /// <param name="resource">The registered resource.</param>
        /// <returns>The criteria.</returns>
        private static IReadOnlyList<AccessCriterion> CriteriaOf(string resource)
        {
            return Criteria.GetValueOrDefault(resource) ?? [];
        }

        /// <summary>Builds the fixture's schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[acme].[Kinds]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity(Doc, "[acme].[Docs]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Property("OwnerId", QueryexType.QxNumeric, "OwnerId", isNotNull: true);
            _ = builder.Entity(Line, "[acme].[DocLines]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("DocId", QueryexType.QxNumeric, "DocId", isNotNull: true)
                .Property("Qty", QueryexType.QxNumeric, "Qty", isNotNull: true)
                .Navigation("Doc", Doc, "DocId");
            _ = builder.Entity(Note, "[acme].[Notes]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("LineId", QueryexType.QxNumeric, "LineId", isNotNull: true)
                .Navigation("Line", Line, "LineId");
            return builder.Build();
        }

        /// <summary>A decision on a securable from a set, its owners decided first, as the evaluator decides.</summary>
        /// <param name="access">The set.</param>
        /// <param name="securable">The securable.</param>
        /// <param name="withCriteria">Whether the criteria join.</param>
        /// <returns>The decision.</returns>
        private static AccessDecision Decide(UserAccess access, SecurableDescriptor securable, bool withCriteria)
        {
            AccessDecision? owner = securable.Owner is SecurableOwner declared
                ? Registry.Find(declared.Resource, securable.Action) is SecurableDescriptor next
                    ? Decide(access, next, withCriteria)
                    : AccessDecision.Deny(declared.Resource, securable.Action)
                : null;
            return access.Decide(securable, withCriteria ? CriteriaOf(securable.Resource) : [], owner, Now);
        }

        /// <summary>The rows a user's memberships bring, each with its membership's end.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The rows.</returns>
        private static IEnumerable<PermissionRow> MembershipRows(int userId)
        {
            return Memberships
                .Where(membership => membership.UserId == userId)
                .SelectMany(static membership => Rows
                    .Where(row => !row.IsPublic && row.RoleId == membership.RoleId)
                    .Select(row => row with { EndsAt = membership.EndsAt }));
        }

        /// <summary>The composition of a securable over the fixture.</summary>
        /// <param name="securable">The securable.</param>
        /// <returns>The composition.</returns>
        private HoldersComposition Compose(SecurableDescriptor securable)
        {
            return _composer.Compose(securable, Rows, Memberships, Audience, Schema, CriteriaOf);
        }

        /// <summary>A user's set from their own current memberships alone, each row with its membership's window.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The set.</returns>
        private UserAccess Own(int userId)
        {
            return Set(userId, [.. MembershipRows(userId)]);
        }

        /// <summary>A user's whole set: their own memberships' rows and every public role's.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The set.</returns>
        private UserAccess Full(int userId)
        {
            return Set(userId, [.. MembershipRows(userId), .. Rows.Where(static row => row.IsPublic)]);
        }

        /// <summary>A set built from rows at the fixture's instant.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The set.</returns>
        private UserAccess Set(int userId, PermissionRow[] rows)
        {
            return _builder.Build(1, userId, Guid.Empty, Guid.Empty, rows, Schema);
        }
    }
}
