// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Queryex;
using static Tellma.Core.Abstractions.Tests.Access.AccessFixture;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>
    ///     The access contracts refuse every inconsistent shape at construction, the grants travel as
    ///     JSON under a kind discriminator, the options hold their bounds, and the wire names of the
    ///     telemetry, the events and the limits stay pinned.
    /// </summary>
    public class AccessContractTests
    {
        // --- Decisions ---

        [Fact]
        public void A_decision_carries_a_filter_exactly_when_it_is_filtered()
        {
            var filter = FilterTree.Or([FilterTree.Leaf("Amount > 10")]);

            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Filtered, null, [], []));
            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Unrestricted, filter, [], []));
            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Denied, filter, [], []));
            Assert.Same(filter, new AccessDecision(Widget, "Read", AccessOutcome.Filtered, filter, [], []).Filter);
        }

        [Fact]
        public void A_denial_lists_no_grant_and_an_undefined_outcome_is_refused()
        {
            AccessGrant grant = Grant(Widget, "Read");

            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Denied, null, [grant], []));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AccessDecision(Widget, "Read", (AccessOutcome)3, null, [], []));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new AccessDecision(Widget, "Read", (AccessOutcome)(-1), null, [], []));

            // An outcome nobody set is a denial.
            AccessOutcome unset = default;
            Assert.Equal(AccessOutcome.Denied, unset);
        }

        [Fact]
        public void A_decision_refuses_null_parts_and_copies_its_lists()
        {
            List<AccessGrant> grants = [Grant(Widget, "Read")];
            List<AccessProblem> problems = [Problem(1, Widget, "Read", AccessProblemCode.FilterInvalid)];

            AccessDecision decision = new(Widget, "Read", AccessOutcome.Unrestricted, null, grants, problems);
            grants.Clear();
            problems.Clear();

            Assert.Single(decision.Grants);
            Assert.Single(decision.Problems);
            _ = Assert.Throws<ArgumentNullException>(() => new AccessDecision(null!, "Read", AccessOutcome.Denied, null, [], []));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessDecision(Widget, null!, AccessOutcome.Denied, null, [], []));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Unrestricted, null, null!, []));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Denied, null, [], null!));
            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Unrestricted, null, [null!], []));
            _ = Assert.Throws<ArgumentException>(() => new AccessDecision(Widget, "Read", AccessOutcome.Denied, null, [], [null!]));
        }

        [Fact]
        public void A_denial_is_not_allowed_and_every_other_outcome_is()
        {
            var denied = AccessDecision.Deny(Widget, "Read");

            Assert.False(denied.IsAllowed);
            Assert.Equal((Widget, "Read", AccessOutcome.Denied), (denied.Resource, denied.Action, denied.Outcome));
            Assert.True(new AccessDecision(Widget, "Read", AccessOutcome.Unrestricted, null, [], []).IsAllowed);
            Assert.True(new AccessDecision(Widget, "Read", AccessOutcome.Filtered, FilterTree.Or([FilterTree.Leaf("Id = 1")]), [], []).IsAllowed);
        }

        // --- Grants ---

        [Fact]
        public void A_role_grant_is_stamped_exactly_when_it_is_filtered_and_only_with_a_version_this_engine_compiles()
        {
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, "Amount > 10", null, null, null));
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, "Amount > 10", QueryexLanguage.Version + 1, null, null));
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, "Amount > 10", 0, null, null));
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, null, QueryexLanguage.Version, null, null));
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, " ", QueryexLanguage.Version, null, null));
            _ = Assert.Throws<ArgumentException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, "R", false, string.Empty, QueryexLanguage.Version, null, null));
            Assert.Equal(QueryexLanguage.Version, Grant(Widget, "Read", "Amount > 10").FilterQueryexVersion);
        }

        [Fact]
        public void A_grant_refuses_a_null_resource_action_role_name_or_reason()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new RoleAccessGrant(null!, "Read", 1, 10, "R", false, null, null, null, null));
            _ = Assert.Throws<ArgumentNullException>(() => new RoleAccessGrant(Widget, null!, 1, 10, "R", false, null, null, null, null));
            _ = Assert.Throws<ArgumentNullException>(() => new RoleAccessGrant(Widget, "Read", 1, 10, null!, false, null, null, null, null));
            _ = Assert.Throws<ArgumentNullException>(() => new BespokeAccessGrant(Widget, "Read", "Id = 1", null!));
            _ = Assert.Throws<ArgumentNullException>(() => new SystemAccessGrant(null!, "Read"));
        }

        [Fact]
        public void A_window_holds_from_its_start_inclusive_to_its_end_exclusive()
        {
            RoleAccessGrant grant = Grant(Widget, "Read", startsAt: Now, endsAt: Now.AddHours(1));

            Assert.False(grant.IsCurrentAt(Now.AddTicks(-1)));
            Assert.True(grant.IsCurrentAt(Now));
            Assert.True(grant.IsCurrentAt(Now.AddHours(1).AddTicks(-1)));
            Assert.False(grant.IsCurrentAt(Now.AddHours(1)));

            // An inverted or empty window holds at no instant.
            Assert.False(Grant(Widget, "Read", startsAt: Now, endsAt: Now).IsCurrentAt(Now));
            Assert.False(Grant(Widget, "Read", startsAt: Now, endsAt: Now.AddHours(-1)).IsCurrentAt(Now));
        }

        // --- The registered actions ---

        [Fact]
        public void The_registered_actions_answer_ignoring_case_and_the_empty_map_registers_nothing()
        {
            var map = ActionResourceMap.From([Securable(Widget, "Approve"), Securable(Kind, "Read")]);

            Assert.True(map.Registers("FX.WIDGET", "approve"));
            Assert.True(map.Registers(Kind, "Read"));
            Assert.False(map.Registers(Kind, "Approve"));
            Assert.False(map.Registers(Widget, "Read"));
            Assert.False(map.Registers(null!, "Read"));
            Assert.False(map.Registers(Kind, null!));
            Assert.False(ActionResourceMap.Empty.Registers(Widget, "Approve"));
        }

        [Fact]
        public void The_registered_actions_refuse_null_securables_and_a_set_refuses_none()
        {
            _ = Assert.Throws<ArgumentNullException>(() => ActionResourceMap.From(null!));
            _ = Assert.Throws<ArgumentException>(() => ActionResourceMap.From([null!]));
            _ = Assert.Throws<ArgumentNullException>(() => new UserAccess { ActionResources = null! });
            Assert.Same(ActionResourceMap.Empty, new UserAccess().ActionResources);
            Assert.Same(Actions, (Set() with { }).ActionResources);
        }

        // --- Criteria, problems and the membership filter ---

        [Fact]
        public void A_criterion_names_an_action_a_filter_and_a_reason()
        {
            var filter = FilterTree.Leaf("OwnerId = me()");

            _ = Assert.Throws<ArgumentException>(() => new AccessCriterion(" ", filter, "self"));
            _ = Assert.Throws<ArgumentException>(() => new AccessCriterion(null!, filter, "self"));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessCriterion("Read", null!, "self"));
            _ = Assert.Throws<ArgumentException>(() => new AccessCriterion("Read", filter, string.Empty));
            Assert.Equal("*", new AccessCriterion("*", filter, "self").Action);
        }

        [Fact]
        public void A_problem_copies_its_diagnostics_and_refuses_null_parts()
        {
            List<QueryexDiagnostic> diagnostics = [];

            AccessProblem problem = new(1, 10, Widget, "Read", AccessProblemCode.FilterInvalid, diagnostics);

            Assert.NotSame(diagnostics, problem.Diagnostics);
            _ = Assert.Throws<ArgumentNullException>(() => new AccessProblem(1, 10, null!, "Read", AccessProblemCode.FilterInvalid, []));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessProblem(1, 10, Widget, null!, AccessProblemCode.FilterInvalid, []));
            _ = Assert.Throws<ArgumentNullException>(() => new AccessProblem(1, 10, Widget, "Read", AccessProblemCode.FilterInvalid, null!));
            _ = Assert.Throws<ArgumentException>(() => new AccessProblem(1, 10, Widget, "Read", AccessProblemCode.FilterInvalid, [null!]));
        }

        [Fact]
        public void The_current_membership_filter_reads_the_window_against_now()
        {
            FilterTree.LeafNode leaf = Assert.IsType<FilterTree.LeafNode>(AccessFilters.CurrentMembership);

            Assert.Equal("(StartsAt is null or StartsAt <= now()) and (EndsAt is null or EndsAt > now())", leaf.Text);
            Assert.Equal(
                "(([m].[StartsAt] IS NULL OR [m].[StartsAt] <= SYSUTCDATETIME()) AND ([m].[EndsAt] IS NULL OR [m].[EndsAt] > SYSUTCDATETIME()))",
                AccessFilters.CurrentMembershipSql("m"));
            Assert.Contains("[_Rm9].[EndsAt]", AccessFilters.CurrentMembershipSql("_Rm9"), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("9m")]
        [InlineData("m]")]
        [InlineData("m].[x")]
        [InlineData("m ")]
        [InlineData("m;--")]
        [InlineData("é")]
        public void The_membership_sql_refuses_an_alias_that_is_not_a_plain_identifier(string alias)
        {
            _ = Assert.Throws<ArgumentException>(() => AccessFilters.CurrentMembershipSql(alias));
        }

        [Fact]
        public void The_membership_sql_refuses_a_null_or_overlong_alias()
        {
            _ = Assert.Throws<ArgumentException>(() => AccessFilters.CurrentMembershipSql(null!));
            _ = Assert.Throws<ArgumentException>(() => AccessFilters.CurrentMembershipSql(new string('m', 129)));
            Assert.NotEmpty(AccessFilters.CurrentMembershipSql(new string('m', 128)));
        }

        // --- Rendering a criterion's tree ---

        [Fact]
        public void A_filter_tree_renders_every_node_with_its_children_parenthesised()
        {
            var tree = FilterTree.Or(
            [
                FilterTree.And([]),
                FilterTree.Or([]),
                FilterTree.Exists("fx.Line", "Header", FilterTree.Leaf("Quantity > 1")),
                FilterTree.Via("Header.Book", FilterTree.Not(FilterTree.Leaf("Code = 'A'"))),
            ]);

            Assert.Equal(
                "(true) or (false) or (exists(fx.Line, Header, Quantity > 1)) or (via(Header.Book, not (Code = 'A')))",
                FilterTreeText.Render(tree));
        }

        // --- Grants as JSON ---

        [Fact]
        public void Every_grant_round_trips_as_json_under_its_kind_written_first()
        {
            AccessGrant[] grants =
            [
                Grant(Widget, "Read", "Amount > 10", permissionId: 3, roleId: 4, startsAt: Now, endsAt: Now.AddDays(1)),
                Grant(Widget, "*", isPublic: true),
                Grant("*", "Save"),
                new BespokeAccessGrant(Widget, "Read", "OwnerId = me()", "self"),
                new SystemAccessGrant(Widget, "Delete"),
            ];
            string[] kinds = [AccessGrant.RoleKind, AccessGrant.RoleKind, AccessGrant.RoleKind, AccessGrant.BespokeKind, AccessGrant.SystemKind];

            for (int index = 0; index < grants.Length; index++)
            {
                string json = JsonSerializer.Serialize(grants[index], TellmaJson.Options);

                Assert.StartsWith($$"""{"kind":"{{kinds[index]}}",""", json, StringComparison.Ordinal);
                Assert.Equal(grants[index], JsonSerializer.Deserialize<AccessGrant>(json, TellmaJson.Options));
            }
        }

        [Fact]
        public void A_grant_list_round_trips_as_json_with_each_kind()
        {
            IReadOnlyList<AccessGrant> grants = [Grant(Widget, "Read"), new SystemAccessGrant(Widget, "Read")];

            string json = JsonSerializer.Serialize(grants, TellmaJson.Options);

            Assert.Equal(grants, JsonSerializer.Deserialize<List<AccessGrant>>(json, TellmaJson.Options));
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"kind":"admin","resource":"fx.Widget","action":"Read"}""")]
        [InlineData(/*lang=json,strict*/ """{"kind":"role","resource":"fx.Widget","action":"Read","permissionId":1,"roleId":1,"roleName":"R","isPublic":false,"filter":"Id = 1"}""")]
        [InlineData(/*lang=json,strict*/ """{"kind":"role","resource":"fx.Widget","action":"Read","permissionId":1,"roleId":1,"roleName":"R","isPublic":false,"filterQueryexVersion":1}""")]
        [InlineData(/*lang=json,strict*/ """{"kind":"role","resource":"*","action":"Read","permissionId":1,"roleId":1,"roleName":"R","isPublic":false,"filter":"Id = 1","filterQueryexVersion":1}""")]
        [InlineData(/*lang=json,strict*/ """{"kind":"role","resource":"*","action":"Read","permissionId":1,"roleId":1,"roleName":"R","isPublic":true}""")]
        [InlineData(/*lang=json,strict*/ """{"kind":"system","action":"Read"}""")]
        public void A_grant_of_an_unknown_kind_or_an_inconsistent_shape_is_refused(string json)
        {
            Exception exception = Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<AccessGrant>(json, TellmaJson.Options));

            Assert.True(exception is JsonException or ArgumentException, exception.GetType().Name);
        }

        [Fact]
        public void A_grant_without_a_kind_is_refused()
        {
            Exception exception = Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<AccessGrant>("""{"resource":"fx.Widget","action":"Read"}""", TellmaJson.Options));

            Assert.True(exception is JsonException or NotSupportedException, exception.GetType().Name);
        }

        [Fact]
        public void A_grant_of_a_kind_the_contract_does_not_declare_is_never_written()
        {
            _ = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize<AccessGrant>(new ForeignGrant(Widget, "Read"), TellmaJson.Options));
        }

        // --- Options ---

        [Fact]
        public void The_options_default_to_the_documented_values_and_pass_their_own_rules()
        {
            AccessOptions options = new();

            Assert.Equal("Tellma:Access", AccessOptions.SectionPath);
            Assert.Equal(
                (300, 32, 2048, TimeSpan.FromHours(2), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 1024, 32768, TimeSpan.FromSeconds(5), 500),
                (options.MaxPermissionsPerRole, options.MaxRolesPerUser, options.MaxFilterLength, options.PermissionsMaxAge, options.ActivityStampInterval,
                    options.FastDenyWindow, options.MaxPreferenceKeys, options.MaxPreferenceValueBytes, options.SecurityLockTimeout, options.MaxHolders));
            Assert.Empty(Validate(options));
        }

        [Fact]
        public void Every_count_must_be_positive_and_the_filter_length_fits_the_column()
        {
            Assert.Equal([nameof(AccessOptions.MaxPermissionsPerRole)], Validate(new AccessOptions { MaxPermissionsPerRole = 0 }));
            Assert.Equal([nameof(AccessOptions.MaxRolesPerUser)], Validate(new AccessOptions { MaxRolesPerUser = -1 }));
            Assert.Equal([nameof(AccessOptions.MaxFilterLength)], Validate(new AccessOptions { MaxFilterLength = 2049 }));
            Assert.Equal([nameof(AccessOptions.MaxFilterLength)], Validate(new AccessOptions { MaxFilterLength = 0 }));
            Assert.Equal([nameof(AccessOptions.MaxPreferenceKeys)], Validate(new AccessOptions { MaxPreferenceKeys = 0 }));
            Assert.Equal([nameof(AccessOptions.MaxPreferenceValueBytes)], Validate(new AccessOptions { MaxPreferenceValueBytes = 0 }));
            Assert.Equal([nameof(AccessOptions.MaxHolders)], Validate(new AccessOptions { MaxHolders = 0 }));
            Assert.Empty(Validate(new AccessOptions { MaxFilterLength = 1 }));
        }

        [Fact]
        public void Every_span_must_be_positive_the_deny_window_within_the_max_age_and_the_stamp_interval_at_least_a_minute()
        {
            Assert.Equal([nameof(AccessOptions.PermissionsMaxAge)], Validate(new AccessOptions { PermissionsMaxAge = TimeSpan.Zero }));
            Assert.Equal([nameof(AccessOptions.FastDenyWindow)], Validate(new AccessOptions { FastDenyWindow = TimeSpan.FromSeconds(-1) }));
            Assert.Equal([nameof(AccessOptions.SecurityLockTimeout)], Validate(new AccessOptions { SecurityLockTimeout = TimeSpan.Zero }));
            Assert.Equal([nameof(AccessOptions.FastDenyWindow)], Validate(new AccessOptions { FastDenyWindow = TimeSpan.FromHours(3) }));
            Assert.Equal([nameof(AccessOptions.ActivityStampInterval)], Validate(new AccessOptions { ActivityStampInterval = TimeSpan.FromSeconds(59) }));
            Assert.Empty(Validate(new AccessOptions { FastDenyWindow = TimeSpan.FromHours(2) }));
        }

        [Fact]
        public void A_broken_span_names_its_key_and_bounds_in_its_message()
        {
            ValidationResult result = Assert.Single(new AccessOptions { ActivityStampInterval = TimeSpan.FromSeconds(30) }.Validate(new ValidationContext(new AccessOptions())));

            Assert.Equal("Tellma:Access:ActivityStampInterval is 00:00:30; it must be at least 60 seconds.", result.ErrorMessage);
        }

        // --- The pinned names ---

        [Fact]
        public void The_telemetry_names_and_values_are_pinned()
        {
            Assert.Equal(
                ["Tellma.Core", "tellma.access.decisions", "tellma.access.set.build.duration", "tellma.access.set.grants",
                    "tellma.access.permissions.unresolved", "tellma.access.guards.triggered", "tellma.access.witness.missing",
                    "tellma.access.connect.results", "tellma.access.connect.prologue.duration"],
                [AccessTelemetryNames.MeterName, AccessTelemetryNames.Decisions, AccessTelemetryNames.SetBuildDuration, AccessTelemetryNames.SetGrants,
                    AccessTelemetryNames.PermissionsUnresolved, AccessTelemetryNames.GuardsTriggered, AccessTelemetryNames.WitnessMissing,
                    AccessTelemetryNames.ConnectResults, AccessTelemetryNames.ConnectPrologueDuration]);
            Assert.Equal(
                ["denied", "filtered", "unrestricted", "unknown_resource", "unknown_action", "filter_unsupported", "filter_invalid",
                    "version_unsupported", "wildcard_resource_on_public_role"],
                [AccessTelemetryNames.OutcomeDenied, AccessTelemetryNames.OutcomeFiltered, AccessTelemetryNames.OutcomeUnrestricted,
                    AccessTelemetryNames.ProblemUnknownResource, AccessTelemetryNames.ProblemUnknownAction, AccessTelemetryNames.ProblemFilterUnsupported,
                    AccessTelemetryNames.ProblemFilterInvalid, AccessTelemetryNames.ProblemVersionUnsupported,
                    AccessTelemetryNames.ProblemWildcardResourceOnPublicRole]);
        }

        [Fact]
        public void The_access_events_are_pinned_to_their_category_and_ids()
        {
            Assert.Equal("Tellma.Core.Access", AccessEvents.Category);
            Assert.Equal(
                [3100, 3101, 3102, 3103, 3104, 3105, 3106, 3107],
                [AccessEvents.MissingWitness, AccessEvents.PermissionDrift, AccessEvents.GuardThrash, AccessEvents.GuardTriggered,
                    AccessEvents.EscalationRejected, AccessEvents.ConnectRejected, AccessEvents.AccessSetBuilt, AccessEvents.FilterTooComplex]);
        }

        [Fact]
        public void The_queryex_profiles_tighten_the_defaults_for_a_stored_filter_and_widen_them_for_a_composed_query()
        {
            QueryexLimits access = QueryexProfiles.AccessLimits;
            QueryexLimits pipeline = QueryexProfiles.PipelineLimits;

            Assert.Equal((2048, 8, 64, 512), (access.MaxInputLength, access.MaxJoins, access.MaxParameters, access.MaxTypedNodes));
            Assert.Equal((1024, 64, 512), (pipeline.MaxParameters, pipeline.MaxJoins, pipeline.MaxListItems));
            Assert.Equal(QueryexLimits.Default with { MaxParameters = 1024, MaxJoins = 64, MaxListItems = 512 }, pipeline);
        }

        /// <summary>Validates options as the startup gate does, returning the member named by each failure.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The members, in order.</returns>
        private static List<string> Validate(AccessOptions options)
        {
            List<ValidationResult> results = [];
            _ = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return [.. results.SelectMany(static result => result.MemberNames)];
        }

        /// <summary>A grant of a kind the contract does not declare.</summary>
        /// <param name="Resource">The resource.</param>
        /// <param name="Action">The action.</param>
        private sealed record ForeignGrant(string Resource, string Action) : AccessGrant(Resource, Action);
    }
}
