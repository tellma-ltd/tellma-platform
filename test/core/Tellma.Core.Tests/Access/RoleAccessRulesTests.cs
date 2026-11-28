// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The roles' access rules: each permission's securable, its filter's root, stamp, length and
    ///     validation; the public role's members and wildcard; the administrator role; the cap; the
    ///     escalation test; and the deletes and actions they validate.
    /// </summary>
    public sealed class RoleAccessRulesTests : IDisposable
    {
        /// <summary>The caller of every test.</summary>
        private const int CallerId = 50;

        /// <summary>The wildcard action.</summary>
        private const string Any = AccessActions.Wildcard;

        /// <summary>A filter over the widgets that validates.</summary>
        private const string Valid = "Amount > 5";

        /// <summary>The instruments, logs and clock.</summary>
        private readonly AccessRulesHarness _harness = new();

        /// <summary>The loader, whose roles have the members each test gives them.</summary>
        private readonly RulesLoader _loader = new();

        /// <summary>The engine every filter validates on.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>Each validation the rules asked of the engine, in order.</summary>
        private readonly List<(string Text, ValidationOptions Options)> _validations = [];

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- Rule 1: a registered securable ---

        [Theory]
        [InlineData(Widget, AccessActions.Read)]
        [InlineData(Widget, Any)]
        [InlineData("FX.WIDGET", "read")]
        [InlineData("fx.Gadget", AccessActions.Save)]
        [InlineData("*", Any)]
        [InlineData("*", "Approve")]
        [InlineData("*", "Run")]
        public async Task A_registered_pair_an_alias_or_a_wildcard_names_a_securable(string resource, string action)
        {
            Assert.Empty(await SaveAsync(Admin(), (Private(5, Permission(resource, action)), null)));
        }

        [Theory]
        [InlineData("fx.Nope", AccessActions.Read, "Resource")]
        [InlineData(Widget, "Run", "Action")]
        [InlineData(Report, AccessActions.Read, "Action")]
        [InlineData("*", "Bogus", "Action")]
        public async Task A_pair_no_securable_registers_is_unknown_and_nothing_more_is_said_of_it(string resource, string action, string property)
        {
            // A filter on the pair would be refused too, if the rule went on.
            IReadOnlyList<ValidationError> errors = await SaveErrorsAsync(Caller(CallerId), (Private(5, Permission(resource, action, Valid)), null));

            ValidationError error = Assert.Single(errors);
            Assert.Equal(($"[0].Permissions[0].{property}", AccessCodes.UnknownSecurable), (error.Path.ToString(), error.Code));
            Assert.Equal(resource, error.Arguments["resource"]);
            Assert.Equal(action, error.Arguments["action"]);
        }

        // --- Rule 2: a filter binds where the securable takes one, on the root they share ---

        [Theory]
        [InlineData(Widget, AccessActions.Read, Valid)]
        [InlineData(Widget, AccessActions.Save, Valid)]
        [InlineData(Widget, Any, Valid)]
        [InlineData("fx.Gadget", AccessActions.Read, Valid)]
        [InlineData(Kind, AccessActions.Read, "Name = 'a'")]
        public async Task A_filter_binds_on_the_securable_s_root_and_on_the_root_a_resource_s_securables_share(string resource, string action, string filter)
        {
            Permission permission = Permission(resource, action, filter);

            Assert.Empty(await SaveAsync(Admin(), (Private(5, permission), null)));
            (string text, ValidationOptions options) = Assert.Single(_validations);
            Assert.Equal(filter, text);
            Assert.Equal(resource == Kind ? Kind : Widget, options.Root?.Name);
        }

        [Theory]
        [InlineData(Widget, "Approve")]
        [InlineData(Report, "Run")]
        [InlineData("*", AccessActions.Read)]
        [InlineData("*", Any)]
        [InlineData(Mixed, AccessActions.Read)]
        [InlineData(Mixed, Any)]
        public async Task A_filter_is_refused_where_the_securable_takes_none_or_the_resource_s_securables_share_no_root(string resource, string action)
        {
            // The mixed resource's two roots fail the composition's own check; the rule refuses it all the same.
            Assert.Equal([("[0].Permissions[0].Filter", AccessCodes.FilterNotSupported)], await SaveAsync(Admin(), (Private(5, Permission(resource, action, Valid)), null)));
            Assert.Empty(_validations);
        }

        // --- Rules 3, 4 and 7: the stamp follows the text ---

        [Fact]
        public async Task A_new_or_rewritten_filter_takes_the_running_version_and_an_unchanged_one_keeps_its_stamp()
        {
            Permission fresh = Permission(Widget, AccessActions.Read, Valid, version: 99);
            Permission rewritten = Permission(Widget, AccessActions.Save, "Amount > 6", id: 11, version: QueryexLanguage.Minimum - 1);
            Permission kept = Permission(Widget, Any, Valid, id: 12, version: null);
            Permission unstamped = Permission(Kind, AccessActions.Read, "Name = 'a'", id: 13, version: null);
            Role stored = Private(5, Permission(Widget, AccessActions.Save, Valid, id: 11, version: QueryexLanguage.Minimum - 1), Permission(Widget, Any, Valid, id: 12, version: QueryexLanguage.Version), Permission(Kind, AccessActions.Read, "Name = 'a'", id: 13, version: null));

            Assert.Empty(await SaveAsync(Admin(), (Private(5, fresh, rewritten, kept, unstamped), stored)));
            Assert.Equal([QueryexLanguage.Version, QueryexLanguage.Version, QueryexLanguage.Version, QueryexLanguage.Version], [fresh.FilterQueryexVersion, rewritten.FilterQueryexVersion, kept.FilterQueryexVersion, unstamped.FilterQueryexVersion]);
            Assert.All(_validations, static validation => Assert.Equal(QueryexLanguage.Version, validation.Options.QueryexVersion));
        }

        [Theory]
        [InlineData(QueryexLanguage.Minimum - 1)]
        [InlineData(QueryexLanguage.Version + 1)]
        public async Task An_unchanged_filter_whose_stamp_this_engine_does_not_compile_is_refused(int version)
        {
            Permission kept = Permission(Widget, AccessActions.Read, Valid, id: 11, version: version);
            Role stored = Private(5, Permission(Widget, AccessActions.Read, Valid, id: 11, version: version));

            ValidationError error = Assert.Single(await SaveErrorsAsync(Admin(), (Private(5, kept), stored)));
            Assert.Equal(("[0].Permissions[0].Filter", AccessCodes.FilterVersionUnsupported), (error.Path.ToString(), error.Code));
            Assert.Equal(version, error.Arguments["version"]);
            Assert.Equal(QueryexLanguage.Minimum, error.Arguments["minimum"]);
            Assert.Empty(_validations);
        }

        [Fact]
        public async Task A_permission_without_a_filter_carries_no_stamp()
        {
            Permission permission = Permission(Widget, AccessActions.Read, version: QueryexLanguage.Version);

            Assert.Empty(await SaveAsync(Admin(), (Private(5, permission), null)));
            Assert.Null(permission.FilterQueryexVersion);
        }

        // --- Rules 5 and 6: the length, then the engine's validation ---

        [Fact]
        public async Task A_filter_longer_than_the_cap_is_refused_before_the_engine_reads_it()
        {
            _harness.Options.MaxFilterLength = Valid.Length;
            Permission permission = Permission(Widget, AccessActions.Read, Valid + " ");

            ValidationError error = Assert.Single(await SaveErrorsAsync(Admin(), (Private(5, Permission(Widget, AccessActions.Save, Valid), permission), null)));
            Assert.Equal(("[0].Permissions[1].Filter", AccessCodes.FilterTooLong), (error.Path.ToString(), error.Code));
            Assert.Equal(Valid.Length, error.Arguments["maxLength"]);
            Assert.Equal([Valid], [.. _validations.Select(static validation => validation.Text)]);
        }

        [Fact]
        public async Task The_engine_validates_a_predicate_over_the_root_with_a_user_no_parameters_and_the_stored_filters_limits()
        {
            Assert.Empty(await SaveAsync(Admin(), (Private(5, Permission(Widget, AccessActions.Read, Valid)), null)));

            ValidationOptions options = Assert.Single(_validations).Options;
            Assert.Same(Schema, options.Schema);
            Assert.Equal(QueryexMode.Filter, options.Mode);
            Assert.True(options.HasUser);
            Assert.Empty(options.Parameters);
            Assert.Equal(QueryexProfiles.AccessLimits, options.Limits);
        }

        [Fact]
        public async Task Each_of_the_engine_s_diagnostics_is_one_invalid_filter_error_with_the_diagnostic_s_code_and_arguments()
        {
            Permission permission = Permission(Widget, AccessActions.Read, "Nope > 5");

            ValidationError error = Assert.Single(await SaveErrorsAsync(Admin(), (Private(5, permission), null)));
            Assert.Equal(("[0].Permissions[0].Filter", AccessCodes.FilterInvalid), (error.Path.ToString(), error.Code));
            Assert.Equal("QX3001", error.Arguments["code"]);
            Assert.Contains("Nope", error.Arguments.Values);
            Assert.Null(permission.FilterQueryexVersion);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Amount +")]
        public async Task A_blank_or_malformed_filter_is_invalid(string filter)
        {
            IReadOnlyList<(string Path, string Code)> errors = await SaveAsync(Admin(), (Private(5, Permission(Widget, AccessActions.Read, filter)), null));

            Assert.NotEmpty(errors);
            Assert.All(errors, static error => Assert.Equal(("[0].Permissions[0].Filter", AccessCodes.FilterInvalid), error));
        }

        [Fact]
        public async Task A_schema_without_the_root_validates_no_filter_over_it()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Kind, "[fx].[Kinds]").Key("Id", QueryexType.QxNumeric, "Id");

            Assert.Equal([("[0].Permissions[0].Filter", AccessCodes.FilterInvalid)], await SaveAsync(Admin(), builder.Build(), (Private(5, Permission(Widget, AccessActions.Read, Valid)), null)));
            Assert.Empty(_validations);
        }

        // --- The public role, the cap ---

        [Fact]
        public async Task A_stored_role_saved_public_with_members_is_refused_and_one_without_or_a_new_one_passes()
        {
            _loader.WithMembers.Add(5);

            Assert.Equal(
                [("[0].IsPublic", AccessCodes.PublicRoleHasMembers)],
                await SaveAsync(Admin(), (Public(5), Private(5)), (Public(6), Private(6)), (Public(0), null), (Private(7), Private(7))));
            Assert.Equal(1, _loader.SqlLoads);
        }

        [Fact]
        public async Task A_public_role_holds_no_permission_on_the_wildcard_resource()
        {
            Assert.Equal(
                [("[0].Permissions[1].Resource", AccessCodes.WildcardResourceOnPublicRole)],
                await SaveAsync(Admin(), (Public(0, Permission(Widget, AccessActions.Read), Permission("*", AccessActions.Read)), null), (Private(0, Permission("*", AccessActions.Read)), null)));
        }

        [Fact]
        public async Task More_permissions_than_the_cap_are_refused()
        {
            _harness.Options.MaxPermissionsPerRole = 2;

            ValidationError error = Assert.Single(await SaveErrorsAsync(Admin(), (Private(5, Permission(Widget, AccessActions.Read), Permission(Widget, AccessActions.Save), Permission(Kind, AccessActions.Read)), null), (Private(6, Permission(Widget, AccessActions.Read), Permission(Kind, AccessActions.Read)), null)));
            Assert.Equal(("[0].Permissions", AccessCodes.TooManyPermissions), (error.Path.ToString(), error.Code));
            Assert.Equal(2, error.Arguments["max"]);
        }

        // --- The administrator role ---

        [Fact]
        public async Task The_administrator_role_is_never_public_and_keeps_its_unrestricted_wildcard_grant()
        {
            Role published = Administrators(isPublic: true, Permission("*", Any, id: WellKnownIds.AdministratorPermissionId));
            Role narrowed = Administrators(isPublic: false, Permission(Widget, AccessActions.Read, id: WellKnownIds.AdministratorPermissionId));
            Role filtered = Administrators(isPublic: false, Permission("*", Any, Valid, id: WellKnownIds.AdministratorPermissionId), Permission("*", Any));
            Role renamed = Administrators(isPublic: false, Permission("*", Any, id: WellKnownIds.AdministratorPermissionId), Permission(Widget, AccessActions.Read));
            Role headerOnly = new() { Id = WellKnownIds.AdministratorRoleId, Name = "Administrators" };

            // Made public, its wildcard grant would reach every user, which the public-role rule refuses too.
            Assert.Equal(
                [("[0].IsPublic", AccessCodes.AdministratorImmutable), ("[0].Permissions[0].Resource", AccessCodes.WildcardResourceOnPublicRole)],
                await SaveAsync(Admin(), (published, published)));
            Assert.Equal(
                [("[0].Permissions", AccessCodes.AdministratorImmutable), ("[0].Permissions[0]", AccessCodes.AdministratorImmutable)],
                await SaveAsync(Admin(), (narrowed, narrowed)));
            Assert.Equal(
                [("[0].Permissions[0]", AccessCodes.AdministratorImmutable), ("[0].Permissions[0].Filter", AccessCodes.FilterNotSupported)],
                await SaveAsync(Admin(), (filtered, filtered)));
            Assert.Empty(await SaveAsync(Admin(), (renamed, renamed), (headerOnly, headerOnly)));
        }

        [Fact]
        public async Task The_administrator_role_and_a_role_with_members_are_not_deleted()
        {
            _loader.WithMembers.Add(5);

            // The ids name the targets in another order than they loaded in.
            Assert.Equal(
                [("Ids[2]", AccessCodes.AdministratorImmutable), ("Ids[0]", AccessCodes.RoleHasMembers)],
                await DeleteAsync([5, 6, WellKnownIds.AdministratorRoleId], Private(WellKnownIds.AdministratorRoleId), Private(5), Private(6)));
            Assert.Equal(1, _loader.SqlLoads);
        }

        [Fact]
        public async Task The_administrator_role_is_not_deactivated_and_accepts_the_other_actions()
        {
            Role administrators = Private(WellKnownIds.AdministratorRoleId);

            Assert.Equal([("Ids[1]", AccessCodes.AdministratorImmutable)], await ActionAsync("deactivate", [5, WellKnownIds.AdministratorRoleId], Private(5), administrators));
            Assert.Empty(await ActionAsync("activate", [WellKnownIds.AdministratorRoleId], administrators));
        }

        // --- Rule 9: what the caller hands out ---

        [Theory]
        [InlineData(Widget, Any, Widget, AccessActions.Save, true)]
        [InlineData(Widget, Any, Widget, Any, true)]
        [InlineData(Widget, Any, "*", AccessActions.Save, false)]
        [InlineData(Widget, Any, Kind, AccessActions.Read, false)]
        [InlineData("*", AccessActions.Save, Widget, AccessActions.Save, true)]
        [InlineData("*", AccessActions.Save, "*", AccessActions.Save, true)]
        [InlineData("*", AccessActions.Save, Widget, Any, false)]
        [InlineData("*", AccessActions.Save, Kind, AccessActions.Read, false)]
        [InlineData("*", Any, "*", Any, true)]
        [InlineData("*", Any, Kind, AccessActions.Read, true)]
        [InlineData(Widget, AccessActions.Save, Widget, AccessActions.Read, true)]
        [InlineData(Widget, AccessActions.Save, "fx.Gadget", AccessActions.Read, true)]
        [InlineData(Widget, AccessActions.Read, Widget, AccessActions.Save, false)]
        public async Task A_permission_is_saved_only_when_the_caller_holds_it_unrestricted(string heldResource, string heldAction, string resource, string action, bool saved)
        {
            IReadOnlyList<ValidationError> errors = await SaveErrorsAsync(Caller(CallerId, (heldResource, heldAction)), (Private(5, Permission(resource, action)), null));

            if (saved)
            {
                Assert.Empty(errors);
                Assert.Empty(_harness.GuardTags);
                return;
            }

            ValidationError error = Assert.Single(errors);
            Assert.Equal(("[0].Permissions[0].Resource", AccessCodes.EscalationBeyondSelf), (error.Path.ToString(), error.Code));
            Assert.Equal(resource, error.Arguments["resource"]);
            Assert.Equal(action, error.Arguments["action"]);
            Assert.Equal([AccessTelemetryNames.GuardEscalation], _harness.GuardTags);
            Assert.Single(_harness.Logs(AccessEvents.EscalationRejected));
        }

        [Fact]
        public async Task An_unchanged_permission_is_not_tested_and_a_changed_one_is()
        {
            UserAccess caller = Caller(CallerId, (Widget, AccessActions.Read));
            Role stored = Private(5, Permission(Kind, AccessActions.Read, id: 11), Permission(Kind, AccessActions.Read, "Name = 'a'", id: 12, version: QueryexLanguage.Version));
            Role saved = Private(5, Permission("FX.KIND", "read", id: 11), Permission(Kind, AccessActions.Read, "Name = 'b'", id: 12, version: QueryexLanguage.Version));

            Assert.Equal([("[0].Permissions[1].Resource", AccessCodes.EscalationBeyondSelf)], await SaveAsync(caller, (saved, stored)));
        }

        [Fact]
        public async Task Every_permission_of_a_role_made_or_created_public_is_tested()
        {
            UserAccess caller = Caller(CallerId, (Widget, AccessActions.Read));
            Role stored = Private(5, Permission(Widget, AccessActions.Read, id: 11), Permission(Kind, AccessActions.Read, id: 12));
            Role published = Public(5, Permission(Widget, AccessActions.Read, id: 11), Permission(Kind, AccessActions.Read, id: 12));
            Role kept = Public(6, Permission(Kind, AccessActions.Read, id: 13));

            Assert.Equal(
                [("[0].Permissions[1].Resource", AccessCodes.EscalationBeyondSelf), ("[1].Permissions[0].Resource", AccessCodes.EscalationBeyondSelf)],
                await SaveAsync(caller, (published, stored), (Public(0, Permission(Kind, AccessActions.Read)), null), (kept, Public(6, Permission(Kind, AccessActions.Read, id: 13)))));
        }

        // --- Fixtures ---

        /// <summary>The caller holding the unrestricted wildcard pair.</summary>
        /// <returns>The caller's set.</returns>
        private static UserAccess Admin()
        {
            return Caller(CallerId, ("*", Any));
        }

        /// <summary>A permission.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="filter">The filter; null for none.</param>
        /// <param name="id">The id; 0 for a new one.</param>
        /// <param name="version">The stamp it carries.</param>
        /// <returns>The permission.</returns>
        private static Permission Permission(string resource, string action, string? filter = null, int id = 0, int? version = null)
        {
            return new Permission { Id = id, Resource = resource, Action = action, Filter = filter, FilterQueryexVersion = version };
        }

        /// <summary>A private role.</summary>
        /// <param name="id">The id; 0 for a new one.</param>
        /// <param name="permissions">The permissions.</param>
        /// <returns>The role.</returns>
        private static Role Private(int id, params Permission[] permissions)
        {
            return new Role { Id = id, Name = "Role", Permissions = permissions };
        }

        /// <summary>A public role.</summary>
        /// <param name="id">The id; 0 for a new one.</param>
        /// <param name="permissions">The permissions.</param>
        /// <returns>The role.</returns>
        private static Role Public(int id, params Permission[] permissions)
        {
            return new Role { Id = id, Name = "Everyone", IsPublic = true, Permissions = permissions };
        }

        /// <summary>The administrator role.</summary>
        /// <param name="isPublic">Whether it is saved public.</param>
        /// <param name="permissions">The permissions.</param>
        /// <returns>The role.</returns>
        private static Role Administrators(bool isPublic, params Permission[] permissions)
        {
            return new Role { Id = WellKnownIds.AdministratorRoleId, Name = "Administrators", IsPublic = isPublic, Permissions = permissions };
        }

        /// <summary>The rules under a caller's set, recording what they ask of the engine.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <returns>The rules.</returns>
        private RoleAccessRules<Role> Rules(UserAccess caller, QueryexSchema schema)
        {
            return new RoleAccessRules<Role>(
                new FixedEvaluator(caller),
                Registry,
                new SchemaDatabase(schema),
                (text, options) =>
                {
                    _validations.Add((text, options));
                    return _engine.Validate(text, options);
                },
                _harness.AccessOptions(),
                _harness.Time,
                _harness.Metrics,
                _harness.LoggerFactory);
        }

        /// <summary>Validates a save over the fixture's schema.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="rows">The payload with the before images.</param>
        /// <returns>The errors, as paths and codes.</returns>
        private Task<IReadOnlyList<(string Path, string Code)>> SaveAsync(UserAccess caller, params (Role Row, Role? Before)[] rows)
        {
            return SaveAsync(caller, Schema, rows);
        }

        /// <summary>Validates a save.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="rows">The payload with the before images.</param>
        /// <returns>The errors, as paths and codes.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> SaveAsync(UserAccess caller, QueryexSchema schema, params (Role Row, Role? Before)[] rows)
        {
            SaveContext<Role> context = Save(_loader, CallerId, rows);
            await Rules(caller, schema).ValidateAsync(context);
            return Errors(context.Errors);
        }

        /// <summary>Validates a save over the fixture's schema.</summary>
        /// <param name="caller">The caller's set.</param>
        /// <param name="rows">The payload with the before images.</param>
        /// <returns>The errors.</returns>
        private async Task<IReadOnlyList<ValidationError>> SaveErrorsAsync(UserAccess caller, params (Role Row, Role? Before)[] rows)
        {
            SaveContext<Role> context = Save(_loader, CallerId, rows);
            await Rules(caller, Schema).ValidateAsync(context);
            return context.Errors.Items;
        }

        /// <summary>Validates a delete.</summary>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets.</param>
        /// <returns>The errors, as paths and codes.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> DeleteAsync(IReadOnlyList<int> ids, params Role[] targets)
        {
            DeleteContext<Role, int> context = Delete(_loader, CallerId, ids, targets);
            await Rules(Admin(), Schema).ValidateDeleteAsync(context);
            return Errors(context.Errors);
        }

        /// <summary>Validates an action.</summary>
        /// <param name="action">The action.</param>
        /// <param name="ids">The ids asked.</param>
        /// <param name="targets">The targets.</param>
        /// <returns>The errors, as paths and codes.</returns>
        private async Task<IReadOnlyList<(string Path, string Code)>> ActionAsync(string action, IReadOnlyList<int> ids, params Role[] targets)
        {
            ActionContext<Role, int> context = Action(_loader, CallerId, ids, targets);
            await Rules(Admin(), Schema).ValidateActionAsync(action, context);
            return Errors(context.Errors);
        }
    }
}
