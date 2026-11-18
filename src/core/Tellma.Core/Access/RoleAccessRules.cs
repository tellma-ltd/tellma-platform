// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The rules are spec 0014 §3.5, §3.7 rules 1-9, §3.9 and §8.4 (RoleAccessRules). The filter-root
    // rule is the drift classifier's, so a permission this rule accepts never drifts on the
    // deployment that saved it. The guards of §8.3 back the role-level rules inside the transaction.
    /// <summary>
    ///     The access rules of the roles' stack: each permission names a registered securable, binds
    ///     its filter only where the securable takes one, under a query-language stamp this rule mints
    ///     and validated against the tenant's current schema; a public role has no members and no
    ///     wildcard-resource permission; the administrator role keeps its unrestricted wildcard grant
    ///     and is never made public, deactivated or deleted; a role with members is not deleted; no
    ///     role holds more permissions than the cap; and no caller saves a permission they lack
    ///     unrestricted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A filter's stamp follows its text: a new row, a changed text or a missing stamp takes
    ///         the running language version, and unchanged text keeps the stamp it was affirmed
    ///         under. The rule sets <see cref="Permission.FilterQueryexVersion" /> after the save's
    ///         first round, since the stamp reads the before image.
    ///     </para>
    ///     <para>
    ///         The escalation test applies to each permission a save adds or changes, and to every
    ///         permission of a role the save makes public, which then reaches every user.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TRole">The stack's role leaf.</typeparam>
    internal sealed class RoleAccessRules<TRole> : IEntityValidator<TRole>
        where TRole : Role
    {
        /// <summary>The built-in action that deactivates a role.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>The caller's access evaluator, whose set the escalation test reads.</summary>
        private readonly IAccessEvaluator _evaluator;

        /// <summary>The securable registry.</summary>
        private readonly ISecurableRegistry _registry;

        /// <summary>The tenant's database, whose current schema a filter validates against.</summary>
        private readonly ITenantDatabase _database;

        /// <summary>Validates one filter text; the engine's own validation but in tests.</summary>
        private readonly Func<string, ValidationOptions, QueryexResult<ValidatedExpression>> _validate;

        /// <summary>The access options, which cap the permissions and the filters.</summary>
        private readonly IOptions<AccessOptions> _options;

        /// <summary>The host's clock, which the caller's grants are judged against.</summary>
        private readonly TimeProvider _time;

        /// <summary>The access instruments.</summary>
        private readonly AccessMetrics _metrics;

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the rule over the engine's validation.</summary>
        /// <param name="evaluator">The caller's access evaluator.</param>
        /// <param name="registry">The securable registry.</param>
        /// <param name="database">The tenant's database.</param>
        /// <param name="engine">The query engine that validates filters.</param>
        /// <param name="options">The access options.</param>
        /// <param name="time">The host's clock.</param>
        /// <param name="metrics">The access instruments.</param>
        /// <param name="loggerFactory">Creates the access logger.</param>
        public RoleAccessRules(
            IAccessEvaluator evaluator,
            ISecurableRegistry registry,
            ITenantDatabase database,
            QueryexEngine engine,
            IOptions<AccessOptions> options,
            TimeProvider time,
            AccessMetrics metrics,
            ILoggerFactory loggerFactory)
            : this(evaluator, registry, database, (engine ?? throw new ArgumentNullException(nameof(engine))).Validate, options, time, metrics, loggerFactory)
        {
        }

        /// <summary>Creates the rule over a validation of its own, for tests.</summary>
        /// <param name="evaluator">The caller's access evaluator.</param>
        /// <param name="registry">The securable registry.</param>
        /// <param name="database">The tenant's database.</param>
        /// <param name="validate">Validates one filter text under the given options.</param>
        /// <param name="options">The access options.</param>
        /// <param name="time">The host's clock.</param>
        /// <param name="metrics">The access instruments.</param>
        /// <param name="loggerFactory">Creates the access logger.</param>
        internal RoleAccessRules(
            IAccessEvaluator evaluator,
            ISecurableRegistry registry,
            ITenantDatabase database,
            Func<string, ValidationOptions, QueryexResult<ValidatedExpression>> validate,
            IOptions<AccessOptions> options,
            TimeProvider time,
            AccessMetrics metrics,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(evaluator);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(validate);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _evaluator = evaluator;
            _registry = registry;
            _database = database;
            _validate = validate;
            _options = options;
            _time = time;
            _metrics = metrics;
            _logger = loggerFactory.CreateLogger(AccessEvents.Category);
        }

        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<TRole> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            DateTimeOffset now = _time.GetUtcNow();

            // --- The caller's set, in memory; the members of every stored role the save leaves public, with the first round ---
            UserAccess caller = await _evaluator.GetAccessAsync(context.CancellationToken).ConfigureAwait(false);
            bool exempt = AccessDelegation.IsExempt(caller, now);
            int[] publicIds = [.. context.Entities.Where(static role => role.IsPublic && role.Id > 0).Select(static role => role.Id).Distinct().Order()];
            ContextRef<RawResult>? members = publicIds.Length > 0 ? MembersOf(context.Loader, publicIds) : null;

            // --- The before images and the members, then every row's rules ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            HashSet<int> withMembers = members is null ? [] : RoleIds(members.Value);
            QueryexSchema schema = _database.Schema;
            for (int index = 0; index < context.Entities.Count; index++)
            {
                CheckRow(context, index, withMembers, schema, caller, exempt, now);
            }
        }

        /// <inheritdoc />
        public async Task ValidateDeleteAsync(DeleteContext<TRole> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The targets' members, with the targets in the first round when the ids are known ---
            IReadOnlyList<int>? ids = (context as DeleteContext<TRole, int>)?.Ids;
            ContextRef<RawResult>? members = ids is { Count: > 0 } ? MembersOf(context.Loader, [.. ids.Distinct().Order()]) : null;
            await context.Loader.LoadAsync().ConfigureAwait(false);
            if (members is null && context.Entities.Count > 0)
            {
                members = MembersOf(context.Loader, [.. context.Entities.Select(static role => role.Id).Distinct().Order()]);
                await context.Loader.LoadAsync().ConfigureAwait(false);
            }

            // --- The administrator role is never deleted, nor a role with members ---
            HashSet<int> withMembers = members is null ? [] : RoleIds(members.Value);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TRole role = context.Entities[index];
                ValidationPath at = AccessRefusals.Target(ids, role.Id, index);
                if (role.Id == WellKnownIds.AdministratorRoleId)
                {
                    context.Errors.Add(at, AccessCodes.AdministratorImmutable);
                }
                else if (withMembers.Contains(role.Id))
                {
                    context.Errors.Add(at, AccessCodes.RoleHasMembers);
                }
            }
        }

        /// <inheritdoc />
        public async Task ValidateActionAsync(string action, ActionContext<TRole> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);

            // --- The targets load with the first round; the administrator role is never deactivated ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            IReadOnlyList<int>? ids = (context as ActionContext<TRole, int>)?.Ids;
            if (!string.Equals(action, Deactivate, StringComparison.Ordinal))
            {
                return;
            }

            for (int index = 0; index < context.Entities.Count; index++)
            {
                TRole role = context.Entities[index];
                if (role.Id == WellKnownIds.AdministratorRoleId)
                {
                    context.Errors.Add(AccessRefusals.Target(ids, role.Id, index), AccessCodes.AdministratorImmutable);
                }
            }
        }

        /// <summary>Requests the roles among some that have a membership.</summary>
        /// <param name="loader">The context loader.</param>
        /// <param name="roleIds">The roles, distinct and ordered, so one set of roles is one load.</param>
        /// <returns>The pending load: one result set of role ids.</returns>
        private static ContextRef<RawResult> MembersOf(IContextLoader loader, int[] roleIds)
        {
            List<IdList> keys = [.. roleIds.Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"SELECT DISTINCT M.[RoleId] FROM [core].[RoleMemberships] AS M WHERE M.[RoleId] IN (SELECT K.[Id] FROM {keys} AS K);",
                new SqlOptions(resultSets: 1));
        }

        /// <summary>The role ids a load of <see cref="MembersOf" /> returned.</summary>
        /// <param name="result">The load's result.</param>
        /// <returns>The roles that have a membership.</returns>
        private static HashSet<int> RoleIds(RawResult result)
        {
            QueryRowSet set = result.ResultSets[0];
            int[] ids = (int[])set.GetBuffer(0);
            return [.. ids.Take(set.RowCount)];
        }

        /// <summary>Whether a stored permission reads as the administrator role's unrestricted wildcard grant.</summary>
        /// <param name="permission">The permission.</param>
        /// <returns>True when it is <c>*</c>/<c>*</c> without a filter.</returns>
        private static bool IsWildcardGrant(Permission permission)
        {
            return permission.Resource == CoreResources.Wildcard && permission.Action == AccessActions.Wildcard && permission.Filter is null;
        }

        /// <summary>The rules of one payload row: the role's own, then each of its permissions'.</summary>
        /// <param name="context">The save's context, after its first round.</param>
        /// <param name="index">The row.</param>
        /// <param name="withMembers">The stored roles saved public that have a membership.</param>
        /// <param name="schema">The tenant's current schema.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="exempt">Whether the caller may save every permission.</param>
        /// <param name="now">The save's instant.</param>
        private void CheckRow(SaveContext<TRole> context, int index, HashSet<int> withMembers, QueryexSchema schema, UserAccess caller, bool exempt, DateTimeOffset now)
        {
            TRole role = context.Entities[index];
            TRole? before = context.Before(index);
            ValidationPath row = ValidationPath.Root.Index(index);
            ValidationPath list = row.Property(nameof(Role.Permissions));
            IReadOnlyList<Permission>? permissions = role.Permissions;

            // --- The administrator role: never public, and its unrestricted wildcard grant stays ---
            if (role.Id == WellKnownIds.AdministratorRoleId)
            {
                if (role.IsPublic)
                {
                    context.Errors.Add(index, static row => row.IsPublic, AccessCodes.AdministratorImmutable);
                }

                if (permissions is not null && !permissions.Any(IsWildcardGrant))
                {
                    context.Errors.Add(list, AccessCodes.AdministratorImmutable);
                }

                for (int position = 0; position < (permissions?.Count ?? 0); position++)
                {
                    if (permissions![position].Id == WellKnownIds.AdministratorPermissionId && !IsWildcardGrant(permissions[position]))
                    {
                        context.Errors.Add(list.Index(position), AccessCodes.AdministratorImmutable);
                    }
                }
            }

            // --- A public role reaches every user, so it has no members ---
            if (role.IsPublic && withMembers.Contains(role.Id))
            {
                context.Errors.Add(index, static row => row.IsPublic, AccessCodes.PublicRoleHasMembers);
            }

            if (permissions is null)
            {
                return;
            }

            // --- At most so many permissions on one role ---
            int max = _options.Value.MaxPermissionsPerRole;
            if (permissions.Count > max)
            {
                context.Errors.Add(list, AccessCodes.TooManyPermissions, AccessRefusals.Arguments(("max", max)));
            }

            // --- Each permission, against its stored row when it updates one ---
            var stored = (before?.Permissions ?? []).Where(static permission => permission.Id > 0).ToDictionary(static permission => permission.Id);
            bool becomesPublic = role.IsPublic && before is not { IsPublic: true };
            for (int position = 0; position < permissions.Count; position++)
            {
                Permission permission = permissions[position];
                Permission? was = permission.Id > 0 && stored.TryGetValue(permission.Id, out Permission? found) ? found : null;
                CheckPermission(context, role, list.Index(position), permission, was, becomesPublic, schema, caller, exempt, now);
            }
        }

        /// <summary>The rules of one permission, in the order of the role save's rules; the filter's stamp is set when it validates.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="role">The permission's role.</param>
        /// <param name="at">The permission's path.</param>
        /// <param name="permission">The saved permission.</param>
        /// <param name="was">The stored permission it updates; null for a new one.</param>
        /// <param name="becomesPublic">Whether the save makes the role public, or creates it public.</param>
        /// <param name="schema">The tenant's current schema.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="exempt">Whether the caller may save every permission.</param>
        /// <param name="now">The save's instant.</param>
        private void CheckPermission(
            SaveContext<TRole> context,
            TRole role,
            ValidationPath at,
            Permission permission,
            Permission? was,
            bool becomesPublic,
            QueryexSchema schema,
            UserAccess caller,
            bool exempt,
            DateTimeOffset now)
        {
            string resource = permission.Resource ?? string.Empty;
            string action = permission.Action ?? string.Empty;

            // --- Rule 1: a registered pair, aliases considered, or a wildcard in either position;
            // the wildcard resource takes an action some resource registers ---
            string? registered = resource == CoreResources.Wildcard ? CoreResources.Wildcard : _registry.FindResource(resource);
            if (registered is null)
            {
                context.Errors.Add(at.Property(nameof(Permission.Resource)), AccessCodes.UnknownSecurable, AccessRefusals.Arguments(("resource", resource), ("action", action)));
                return;
            }

            bool knownAction = action == AccessActions.Wildcard
                || (registered == CoreResources.Wildcard
                    ? _registry.All.Any(securable => string.Equals(securable.Action, action, StringComparison.OrdinalIgnoreCase))
                    : _registry.Find(registered, action) is not null);
            if (!knownAction)
            {
                context.Errors.Add(at.Property(nameof(Permission.Action)), AccessCodes.UnknownSecurable, AccessRefusals.Arguments(("resource", resource), ("action", action)));
                return;
            }

            // --- A public role never holds the wildcard resource ---
            if (role.IsPublic && registered == CoreResources.Wildcard)
            {
                context.Errors.Add(at.Property(nameof(Permission.Resource)), AccessCodes.WildcardResourceOnPublicRole);
            }

            // --- Rule 9: a permission added or changed, or on a role made public, is the caller's to hand out ---
            bool changed = was is null
                || !string.Equals(was.Resource, permission.Resource, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(was.Action, permission.Action, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(was.Filter, permission.Filter, StringComparison.Ordinal);
            if (!exempt && (changed || becomesPublic) && !AccessDelegation.Covers(caller, registered, action, now))
            {
                context.Errors.Add(at.Property(nameof(Permission.Resource)), AccessCodes.EscalationBeyondSelf, AccessRefusals.Arguments(("resource", resource), ("action", action)));
                AccessRefusals.Escalation(_metrics, _logger, context.Context, Math.Max(role.Id, 0), resource, action, AccessCodes.EscalationBeyondSelf);
            }

            // --- Without a filter there is nothing to stamp (CK_Permissions_FilterVersion) ---
            if (permission.Filter is not string filter)
            {
                permission.FilterQueryexVersion = null;
                return;
            }

            ValidationPath filterPath = at.Property(nameof(Permission.Filter));
            CheckFilter(context, filterPath, permission, was, registered, action, filter, schema);
        }

        /// <summary>Rules 2 to 7: the filter's root, its stamp, its length and its validation; the stamp is set on success.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="at">The filter's path.</param>
        /// <param name="permission">The saved permission.</param>
        /// <param name="was">The stored permission it updates; null for a new one.</param>
        /// <param name="resource">The permission's resource, as registered, or the wildcard.</param>
        /// <param name="action">The permission's action, or the wildcard.</param>
        /// <param name="filter">The filter's text.</param>
        /// <param name="schema">The tenant's current schema.</param>
        private void CheckFilter(
            SaveContext<TRole> context,
            ValidationPath at,
            Permission permission,
            Permission? was,
            string resource,
            string action,
            string filter,
            QueryexSchema schema)
        {
            // --- Rule 2: a filter binds only where the resource's filterable securables share the pair's root ---
            string? root = resource == CoreResources.Wildcard ? null : SecurableFilterRoots.Of(_registry, resource, action);
            string? shared = root is null ? null : SecurableFilterRoots.Shared(_registry.ForResource(resource));
            if (root is null || shared is null || !string.Equals(root, shared, StringComparison.OrdinalIgnoreCase))
            {
                context.Errors.Add(at, AccessCodes.FilterNotSupported);
                return;
            }

            // --- Rule 3: the stamp follows the text; rule 4: an unchanged text keeps a stamp this engine compiles ---
            bool rewritten = was is null || !string.Equals(was.Filter, filter, StringComparison.Ordinal) || was.FilterQueryexVersion is null;
            int stamp = rewritten ? QueryexLanguage.Version : was!.FilterQueryexVersion!.Value;
            if (!QueryexLanguage.IsSupported(stamp))
            {
                context.Errors.Add(at, AccessCodes.FilterVersionUnsupported, AccessRefusals.Arguments(("version", stamp), ("minimum", QueryexLanguage.Minimum)));
                return;
            }

            // --- Rule 5: the length, before the engine reads the text ---
            int maxLength = _options.Value.MaxFilterLength;
            if (filter.Length > maxLength)
            {
                context.Errors.Add(at, AccessCodes.FilterTooLong, AccessRefusals.Arguments(("maxLength", maxLength)));
                return;
            }

            // --- Rule 6: a predicate over the root, with a user and no parameters, within one stored filter's profile;
            // a schema without the root validates nothing over it, as the drift classifier reads it ---
            if (schema.FindEntity(root) is not EntityDescriptor entity)
            {
                context.Errors.Add(at, AccessCodes.FilterInvalid);
                return;
            }

            QueryexResult<ValidatedExpression> result = _validate(filter, new ValidationOptions
            {
                Schema = schema,
                Root = entity,
                Mode = QueryexMode.Filter,
                QueryexVersion = stamp,
                HasUser = true,
                Parameters = [],
                Limits = QueryexProfiles.AccessLimits,
            });
            if (!result.Succeeded || result.Diagnostics.Count > 0 || string.IsNullOrWhiteSpace(filter))
            {
                // One error per diagnostic, each carrying the diagnostic's code and its own arguments.
                foreach (QueryexDiagnostic diagnostic in result.Diagnostics)
                {
                    Dictionary<string, object?> arguments = AccessRefusals.Arguments(("code", diagnostic.Code));
                    foreach (KeyValuePair<string, string> argument in diagnostic.Arguments)
                    {
                        _ = arguments.TryAdd(argument.Key, argument.Value);
                    }

                    context.Errors.Add(at, AccessCodes.FilterInvalid, arguments);
                }

                if (result.Diagnostics.Count == 0)
                {
                    context.Errors.Add(at, AccessCodes.FilterInvalid);
                }

                return;
            }

            // --- Rule 7: the stamp is minted here and nowhere else ---
            permission.FilterQueryexVersion = stamp;
        }
    }
}
