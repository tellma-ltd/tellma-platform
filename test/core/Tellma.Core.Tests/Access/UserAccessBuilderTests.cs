// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     Building a caller's set from stored permissions: every row of the drift table grants nothing
    ///     and is kept as a problem, every resolvable row becomes a grant spelt as registered, windows
    ///     are normalised, the set never depends on the rows' order, filters are interned, and each
    ///     problem is counted and logged once per version of the tenant's permissions.
    /// </summary>
    public sealed class UserAccessBuilderTests : IDisposable
    {
        private const string Widget = "fx.Widget";

        private const string Kind = "fx.Kind";

        private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

        private static readonly Guid FirstTag = new("00000001-0000-0000-0000-000000000000");

        private static readonly Guid SecondTag = new("00000002-0000-0000-0000-000000000000");

        /// <summary>The fixture's schema: widgets with a kind.</summary>
        private static readonly QueryexSchema Schema = BuildSchema();

        /// <summary>
        ///     The fixture's securables: widgets with filterable actions and one that takes no filter,
        ///     kinds, a resource whose securables take two roots, a report with no root, and the
        ///     widgets' former name.
        /// </summary>
        private static readonly SecurableRegistry Registry = SecurableRegistryTests.Compose(("fixture", static builder => builder
            .Add(Widget, "Read", Widget)
            .Add(Widget, "Save", Widget)
            .Add(Widget, "Delete", Widget)
            .Add(Widget, "Approve", null)
            .Add(Kind, "Read", Kind)
            .Add("fx.Mixed", "Read", Widget)
            .Add("fx.Mixed", "Save", Kind)
            .Add("fx.Report", "Run", null)
            .Alias("fx.Gadget", Widget)));

        /// <summary>The meter factory and the logs, Debug included.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>The host's clock.</summary>
        private readonly FakeTimeProvider _time = new(Start);

        /// <summary>The builder under test.</summary>
        private readonly UserAccessBuilder _builder;

        /// <summary>Creates the builder over the fixture.</summary>
        public UserAccessBuilderTests()
        {
            _builder = new UserAccessBuilder(
                new PermissionClassifier(Registry, new QueryexEngine()),
                new AccessMetrics(_services.GetRequiredService<IMeterFactory>()),
                _services.GetRequiredService<ILoggerFactory>(),
                _time);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        // --- The drift table: each row grants nothing and is one problem ---

        [Theory]
        [InlineData("fx.Nothing", "Read", null, false, AccessProblemCode.UnknownResource)]
        [InlineData("", "Read", null, false, AccessProblemCode.UnknownResource)]
        [InlineData("fx", "Read", null, false, AccessProblemCode.UnknownResource)]
        [InlineData(Widget, "Fly", null, false, AccessProblemCode.UnknownAction)]
        [InlineData(Widget, "", null, false, AccessProblemCode.UnknownAction)]
        [InlineData(Widget, "Run", null, false, AccessProblemCode.UnknownAction)]
        [InlineData("*", "Fly", null, false, AccessProblemCode.UnknownAction)]
        [InlineData("*", "", null, false, AccessProblemCode.UnknownAction)]
        [InlineData(Widget, "Approve", "Amount > 1", false, AccessProblemCode.FilterUnsupported)]
        [InlineData("fx.Report", "*", "Id > 1", false, AccessProblemCode.FilterUnsupported)]
        [InlineData("fx.Mixed", "*", "Id > 1", false, AccessProblemCode.FilterUnsupported)]
        [InlineData("*", "Read", "Id > 1", false, AccessProblemCode.FilterUnsupported)]
        [InlineData("*", "*", "Id > 1", false, AccessProblemCode.FilterUnsupported)]
        [InlineData("*", "*", null, true, AccessProblemCode.WildcardResourceOnPublicRole)]
        [InlineData("*", "Read", null, true, AccessProblemCode.WildcardResourceOnPublicRole)]
        [InlineData("*", "Fly", "Id > 1", true, AccessProblemCode.WildcardResourceOnPublicRole)]
        [InlineData(Widget, "Read", "Amount >", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Widget, "Read", "Nothing = 1", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Widget, "Read", "Amount + 1", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Widget, "Read", "Amount > @limit", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Widget, "Read", " ", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Widget, "*", "Kind.Nothing = 1", false, AccessProblemCode.FilterInvalid)]
        [InlineData(Kind, "Read", "Amount > 1", false, AccessProblemCode.FilterInvalid)]
        public void A_row_of_the_drift_table_grants_nothing_and_is_kept_as_one_problem(
            string resource,
            string action,
            string? filter,
            bool isPublic,
            AccessProblemCode code)
        {
            UserAccess access = Build(Row(7, resource, action, filter, isPublic: isPublic));

            Assert.Empty(access.Grants);
            AccessProblem problem = Assert.Single(access.Problems);
            Assert.Equal((7, 10, code), (problem.PermissionId, problem.RoleId, problem.Code));

            // Fail closed: nothing the row could have reached is allowed.
            foreach (SecurableDescriptor securable in Registry.All)
            {
                Assert.Equal(AccessOutcome.Denied, access.Decide(securable, [], null, Start).Outcome);
            }
        }

        [Fact]
        public void An_invalid_filter_keeps_the_engines_diagnostics()
        {
            AccessProblem problem = Assert.Single(Build(Row(1, Widget, "Read", "Nothing = 1")).Problems);

            Assert.NotEmpty(problem.Diagnostics);
            Assert.All(problem.Diagnostics, static diagnostic => Assert.StartsWith("QX", diagnostic.Code, StringComparison.Ordinal));
        }

        [Fact]
        public void A_filter_without_a_stamp_or_with_one_this_engine_does_not_compile_is_a_version_problem()
        {
            UserAccess access = Build(
                new PermissionRow(1, 10, "R", false, Widget, "Read", "Amount > 1", null, null, null),
                new PermissionRow(2, 10, "R", false, Widget, "Read", "Amount > 1", QueryexLanguage.Version + 1, null, null),
                new PermissionRow(3, 10, "R", false, Widget, "Read", "Amount > 1", 0, null, null));

            Assert.Empty(access.Grants);
            Assert.All(access.Problems, static problem => Assert.Equal(AccessProblemCode.VersionUnsupported, problem.Code));
            Assert.Equal([1, 2, 3], access.Problems.Select(static problem => problem.PermissionId));
        }

        [Fact]
        public void The_first_failing_check_decides_the_problem()
        {
            UserAccess access = Build(
                new PermissionRow(1, 10, "R", false, "fx.Nothing", "Fly", "Amount >", null, null, null),
                new PermissionRow(2, 10, "R", false, Widget, "Fly", "Amount >", null, null, null),
                new PermissionRow(3, 10, "R", false, Widget, "Approve", "Amount >", null, null, null),
                new PermissionRow(4, 10, "R", false, Widget, "Read", "Amount >", null, null, null));

            Assert.Equal(
                [AccessProblemCode.UnknownResource, AccessProblemCode.UnknownAction, AccessProblemCode.FilterUnsupported, AccessProblemCode.VersionUnsupported],
                access.Problems.Select(static problem => problem.Code));
        }

        // --- Rows that resolve ---

        [Fact]
        public void A_grant_carries_its_pair_as_registered_whatever_the_rows_spelling_or_former_name()
        {
            UserAccess access = Build(
                Row(1, "FX.WIDGET", "read"),
                Row(2, "fx.gadget", "SAVE"),
                Row(3, "*", "delete"),
                Row(4, "*", "*"),
                Row(5, "fx.Gadget", "*", "Amount > 1"));

            Assert.Empty(access.Problems);
            Assert.Equal(
                [(Widget, "Read"), (Widget, "Save"), ("*", "Delete"), ("*", "*"), (Widget, "*")],
                access.Grants.Select(static grant => (grant.Resource, grant.Action)));
        }

        [Fact]
        public void A_filter_validates_against_the_securables_root_with_a_user_and_the_current_time()
        {
            UserAccess access = Build(
                Row(1, Widget, "Read", "OwnerId = me() and Kind.Name = 'x'"),
                Row(2, Widget, "*", "Amount > 1"),
                Row(3, "fx.Mixed", "Read", "Amount > 1"),
                Row(4, "fx.Mixed", "Save", "Name = 'x'"),
                Row(5, Kind, "Read", "Name = 'x'"));

            Assert.Empty(access.Problems);
            Assert.Equal([1, 2, 3, 4, 5], access.Grants.Select(static grant => grant.PermissionId));
            Assert.All(access.Grants, static grant => Assert.Equal(QueryexLanguage.Version, grant.FilterQueryexVersion));
        }

        [Fact]
        public void An_unfiltered_row_carries_no_stamp_even_when_one_is_stored()
        {
            RoleAccessGrant grant = Assert.Single(Build(new PermissionRow(1, 10, "R", false, Widget, "Read", null, 99, null, null)).Grants);

            Assert.Null(grant.Filter);
            Assert.Null(grant.FilterQueryexVersion);
        }

        // --- Windows ---

        [Fact]
        public void A_public_roles_grant_has_no_window_and_an_ended_membership_grants_nothing()
        {
            UserAccess access = Build(
                Row(1, Widget, "Read", isPublic: true, startsAt: Start.AddDays(1), endsAt: Start.AddDays(-1)),
                Row(2, Widget, "Save", endsAt: Start),
                Row(3, Widget, "Delete", endsAt: Start.AddTicks(-1)),
                Row(4, Kind, "Read", startsAt: Start.AddDays(1), endsAt: Start.AddDays(2)),
                Row(5, Widget, "Approve", endsAt: Start.AddTicks(1)));

            Assert.Equal([1, 4, 5], access.Grants.Select(static grant => grant.PermissionId));
            RoleAccessGrant publicGrant = access.Grants[0];
            Assert.True(publicGrant.IsPublic);
            Assert.Null(publicGrant.StartsAt);
            Assert.Null(publicGrant.EndsAt);
            Assert.Equal(Start.AddDays(1), access.Grants[1].StartsAt);
            Assert.Equal(Start, access.ComputedAt);
            Assert.Equal(Start, access.ValidatedAt);
        }

        [Fact]
        public void The_clock_decides_which_memberships_have_ended()
        {
            _time.Advance(TimeSpan.FromHours(1));

            UserAccess access = Build(
                Row(1, Widget, "Read", endsAt: Start.AddMinutes(30)),
                Row(2, Widget, "Save", endsAt: Start.AddHours(2)));

            Assert.Equal([2], access.Grants.Select(static grant => grant.PermissionId));
            Assert.Equal(Start.AddHours(1), access.ComputedAt);
        }

        // --- Order, duplicates and problems per permission ---

        [Fact]
        public void The_set_is_ordered_by_role_permission_and_window_whatever_the_rows_order()
        {
            PermissionRow[] rows =
            [
                Row(5, Widget, "Read", roleId: 20),
                Row(2, Widget, "Read", roleId: 10, startsAt: Start.AddDays(2), endsAt: Start.AddDays(3)),
                Row(2, Widget, "Read", roleId: 10, startsAt: Start.AddDays(2)),
                Row(2, Widget, "Read", roleId: 10),
                Row(1, Widget, "Save", roleId: 10, endsAt: Start.AddDays(1)),
                Row(9, "fx.Nothing", "Read", roleId: 5),
                Row(3, Widget, "Fly", roleId: 30),
            ];

            UserAccess forward = Build(rows);
            UserAccess backward = Build([.. Enumerable.Reverse(rows)]);

            List<(int, int, DateTimeOffset?, DateTimeOffset?)> expected =
            [
                (10, 1, null, Start.AddDays(1)),
                (10, 2, null, null),
                (10, 2, Start.AddDays(2), Start.AddDays(3)),
                (10, 2, Start.AddDays(2), null),
                (20, 5, null, null),
            ];
            Assert.Equal(expected, forward.Grants.Select(static grant => (grant.RoleId, grant.PermissionId, grant.StartsAt, grant.EndsAt)));
            Assert.Equal(forward.Grants, backward.Grants);
            Assert.Equal([3, 9], forward.Problems.Select(static problem => problem.PermissionId));
            Assert.Equal(forward.Problems.Select(static problem => (problem.PermissionId, problem.Code)), backward.Problems.Select(static problem => (problem.PermissionId, problem.Code)));
        }

        [Fact]
        public void An_exact_duplicate_row_is_one_grant_and_a_problem_is_one_per_permission_however_many_windows_bring_it()
        {
            UserAccess access = Build(
                Row(1, Widget, "Read", "Amount > 1"),
                Row(1, Widget, "Read", "Amount > 1"),
                Row(2, Widget, "Fly", startsAt: Start.AddDays(1)),
                Row(2, Widget, "Fly", startsAt: Start.AddDays(5)),
                Row(2, Widget, "Fly", isPublic: true));

            Assert.Single(access.Grants);
            AccessProblem problem = Assert.Single(access.Problems);
            Assert.Equal((2, AccessProblemCode.UnknownAction, Widget, "Fly"), (problem.PermissionId, problem.Code, problem.Resource, problem.Action));
        }

        [Fact]
        public void A_problem_names_the_pair_as_far_as_it_resolved()
        {
            UserAccess access = Build(
                Row(1, "fx.Gadget", "Fly"),
                Row(2, "fx.gadget", "approve", "Amount > 1"),
                Row(3, "*", "read", "Amount > 1"));

            Assert.Equal(
                [(Widget, "Fly"), (Widget, "Approve"), ("*", "Read")],
                access.Problems.Select(static problem => (problem.Resource, problem.Action)));

            // A problem is listed on the decisions its pair would have reached.
            Assert.Equal([1, 2, 3], access.Decide(new SecurableDescriptor(Widget, "Read", Widget, false, "fixture", null), [], null, Start).Problems.Select(static problem => problem.PermissionId));
        }

        // --- Interning and validation once ---

        [Fact]
        public void Filters_and_role_names_are_one_object_across_sets()
        {
            UserAccess first = Build(Row(1, Widget, "Read", new string("Amount > 1".AsSpan())));
            UserAccess second = Build(Row(1, Widget, "Read", new string("Amount > 1".AsSpan())));

            Assert.Same(first.Grants[0].Filter, second.Grants[0].Filter);
            Assert.Same(first.Grants[0].RoleName, second.Grants[0].RoleName);
        }

        [Fact]
        public void A_filter_is_validated_once_per_root_text_and_stamp_whatever_the_resource_spelling()
        {
            PermissionClassifier classifier = new(Registry, new QueryexEngine());
            Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];

            _ = classifier.Classify(Widget, "Read", "Amount > 1", QueryexLanguage.Version, false, Schema, validations);
            _ = classifier.Classify("fx.Gadget", "*", "Amount > 1", QueryexLanguage.Version, false, Schema, validations);
            _ = classifier.Classify("fx.Mixed", "Read", "Amount > 1", QueryexLanguage.Version, false, Schema, validations);
            Assert.Equal([(Widget, "Amount > 1", QueryexLanguage.Version)], validations.Keys);

            _ = classifier.Classify(Kind, "Read", "Amount > 1", QueryexLanguage.Version, false, Schema, validations);
            Assert.Equal(2, validations.Count);
            Assert.False(validations[(Kind, "Amount > 1", QueryexLanguage.Version)].IsValid);
        }

        [Fact]
        public void A_root_the_tenants_schema_does_not_hold_validates_nothing()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[fx].[Kinds]").Key("Id", QueryexType.QxNumeric, "Id");

            UserAccess access = _builder.Build(7, 42, FirstTag, Guid.Empty, [Row(1, Widget, "Read", "Id > 1"), Row(2, Widget, "Read")], builder.Build());

            Assert.Equal(AccessProblemCode.FilterInvalid, Assert.Single(access.Problems).Code);
            Assert.Equal([2], access.Grants.Select(static grant => grant.PermissionId));
        }

        // --- Reporting ---

        [Fact]
        public void Each_problem_is_counted_and_logged_once_per_version_of_the_tenants_permissions()
        {
            using MetricCollector<long> unresolved = Collector<long>(AccessTelemetryNames.PermissionsUnresolved);
            PermissionRow[] rows = [Row(1, Widget, "Fly"), Row(2, Widget, "Read", "Nothing = 1"), Row(3, Widget, "Read")];

            _ = _builder.Build(7, 42, FirstTag, Guid.Empty, rows, Schema);
            _ = _builder.Build(7, 43, FirstTag, Guid.Empty, rows, Schema);
            Assert.Equal(
                [AccessTelemetryNames.ProblemUnknownAction, AccessTelemetryNames.ProblemFilterInvalid],
                unresolved.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.ProblemTag]!));
            Assert.Equal(2, Drift().Count);

            // A new version of the permissions, or another tenant, reports them again.
            _ = _builder.Build(7, 42, SecondTag, Guid.Empty, rows, Schema);
            _ = _builder.Build(8, 42, SecondTag, Guid.Empty, rows, Schema);
            Assert.Equal(6, unresolved.GetMeasurementSnapshot().Count);
            Assert.All(unresolved.GetMeasurementSnapshot(), static measurement => Assert.Equal(1, measurement.Value));
            Assert.Equal(6, Drift().Count);
        }

        [Fact]
        public void The_drift_warning_names_the_permission_role_tenant_user_pair_problem_and_diagnostics()
        {
            _ = _builder.Build(7, 42, FirstTag, Guid.Empty, [Row(5, Widget, "Read", "Nothing = 1", roleId: 11)], Schema);

            FakeLogRecord record = Assert.Single(Drift());
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.Equal(AccessEvents.Category, record.Category);
            Assert.StartsWith(
                "Permission 5 of role 11 in tenant 7 grants nothing (filter_invalid) on fx.Widget:Read; found building the permissions of user 42. Diagnostics: QX",
                record.Message,
                StringComparison.Ordinal);
            Assert.Null(record.Exception);
        }

        [Fact]
        public void A_build_records_its_duration_and_grants_and_logs_at_debug()
        {
            using MetricCollector<double> duration = Collector<double>(AccessTelemetryNames.SetBuildDuration);
            using MetricCollector<long> grants = Collector<long>(AccessTelemetryNames.SetGrants);

            _ = Build(Row(1, Widget, "Read"), Row(2, Widget, "Save"), Row(3, Widget, "Fly"));

            Assert.Equal(2, Assert.Single(grants.GetMeasurementSnapshot()).Value);
            Assert.True(Assert.Single(duration.GetMeasurementSnapshot()).Value >= 0);
            FakeLogRecord built = Assert.Single(_services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Id == AccessEvents.AccessSetBuilt);
            Assert.Equal(LogLevel.Debug, built.Level);
            Assert.Contains("2 grants and 1 permissions that grant nothing", built.Message, StringComparison.Ordinal);
        }

        // --- The arguments refused ---

        [Fact]
        public void A_build_refuses_a_set_without_a_user_and_null_rows()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _builder.Build(7, 0, FirstTag, Guid.Empty, [], Schema));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _builder.Build(7, -1, FirstTag, Guid.Empty, [], Schema));
            _ = Assert.Throws<ArgumentNullException>(() => _builder.Build(7, 42, FirstTag, Guid.Empty, null!, Schema));
            _ = Assert.Throws<ArgumentNullException>(() => _builder.Build(7, 42, FirstTag, Guid.Empty, [], null!));
            _ = Assert.Throws<ArgumentException>(() => _builder.Build(7, 42, FirstTag, Guid.Empty, [null!], Schema));
        }

        [Fact]
        public void The_set_carries_its_tenant_user_and_tags()
        {
            var userTag = Guid.NewGuid();

            UserAccess access = _builder.Build(7, 42, FirstTag, userTag, [], Schema);

            Assert.Equal((7, 42, FirstTag, userTag, false), (access.TenantId, access.UserId, access.Tag, access.UserTag, access.IsSystem));
        }

        /// <summary>A stored permission of a membership.</summary>
        /// <param name="permissionId">The permission.</param>
        /// <param name="resource">Its resource.</param>
        /// <param name="action">Its action.</param>
        /// <param name="filter">Its filter, stamped with the current version.</param>
        /// <param name="roleId">Its role.</param>
        /// <param name="isPublic">Whether the role is public.</param>
        /// <param name="startsAt">The membership's start.</param>
        /// <param name="endsAt">The membership's end.</param>
        /// <returns>The row.</returns>
        private static PermissionRow Row(
            int permissionId,
            string resource,
            string action,
            string? filter = null,
            int roleId = 10,
            bool isPublic = false,
            DateTimeOffset? startsAt = null,
            DateTimeOffset? endsAt = null)
        {
            return new PermissionRow(
                permissionId,
                roleId,
                $"Role {roleId}",
                isPublic,
                resource,
                action,
                filter,
                filter is null ? null : QueryexLanguage.Version,
                startsAt,
                endsAt);
        }

        /// <summary>Builds the fixture's schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[fx].[Kinds]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity(Widget, "[fx].[Widgets]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true)
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Property("OwnerId", QueryexType.QxNumeric, "OwnerId", isNotNull: true)
                .Property("KindId", QueryexType.QxNumeric, "KindId", isNotNull: true)
                .Navigation("Kind", Kind, "KindId");
            return builder.Build();
        }

        /// <summary>Builds user 42's set in tenant 7 under the first tag.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The set.</returns>
        private UserAccess Build(params PermissionRow[] rows)
        {
            return _builder.Build(7, 42, FirstTag, Guid.Empty, rows, Schema);
        }

        /// <summary>The drift warnings logged so far.</summary>
        /// <returns>The records.</returns>
        private List<FakeLogRecord> Drift()
        {
            return [.. _services.GetFakeLogCollector().GetSnapshot().Where(static record => record.Id.Id == AccessEvents.PermissionDrift)];
        }

        /// <summary>A collector of one of the access instruments.</summary>
        /// <typeparam name="T">The instrument's type.</typeparam>
        /// <param name="instrument">The instrument.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, instrument);
        }
    }
}
