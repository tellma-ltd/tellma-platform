// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The resolver is spec 0014 §5.1 and §10 (access/holders); its one round trip is §7.5's row: the
    // prologue, the matching permissions, the current members of their roles, the public-role
    // audience count, and the users' and roles' projections under the caller's core.User × Read.
    /// <summary>
    ///     Resolves who holds a securable across the tenant, on the scope's caller's behalf, in one
    ///     guarded read. One per scope, closed over the distribution's user and role leaves.
    /// </summary>
    /// <typeparam name="TUser">The user leaf.</typeparam>
    /// <typeparam name="TRole">The role leaf.</typeparam>
    /// <param name="accessor">The scope's request context: the tenant's settings and the reader's culture.</param>
    /// <param name="runner">Runs the guarded read.</param>
    /// <param name="registry">The securable registry.</param>
    /// <param name="evaluator">The scope's evaluator, whose decisions and criteria the holders compose alike.</param>
    /// <param name="composer">Composes the holders from what the read returned.</param>
    /// <param name="schemas">The query schemas each filter is validated under.</param>
    /// <param name="metadata">The entity metadata of the leaves.</param>
    /// <param name="options">The access options: the most holders listed.</param>
    /// <param name="time">The host's clock, which the caller's visibility is decided at.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    internal sealed class AccessHoldersResolver<TUser, TRole>(
        IRequestContextAccessor accessor,
        IGuardedBatchRunner runner,
        ISecurableRegistry registry,
        AccessEvaluator evaluator,
        HoldersComposer composer,
        IQueryexSchemaProvider schemas,
        IEntityMetadataProvider metadata,
        IOptions<AccessOptions> options,
        TimeProvider time,
        ILoggerFactory loggerFactory) : IAccessHoldersResolver
        where TUser : User
        where TRole : Role
    {
        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <inheritdoc />
        public async Task<AccessHolders> ResolveAsync(string resource, string action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);

            // --- A pair the registry does not hold: no holder and its problem, with no round trip ---
            if (registry.Find(resource, action) is not SecurableDescriptor securable)
            {
                AccessDecision unknown = evaluator.Unknown(resource, action);
                return new AccessHolders(unknown.Resource, unknown.Action, AccessOutcome.Denied, [], new Dictionary<int, int>(), [], 0, false, [], unknown.Problems, new RelatedEntities());
            }

            // --- One guarded read; a recompose composes afresh under the refreshed context ---
            Plan? plan = null;
            return await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    plan = Compose(user, batch, securable);
                    return ValueTask.CompletedTask;
                },
                (_, _) => Read(plan!, securable),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     The bracketed form a resource or an action name takes in the statement's lists, as
        ///     the permission columns compare it: the platform collation ignores case.
        /// </summary>
        /// <param name="names">The names.</param>
        /// <returns>The list's rows, one per name ignoring case.</returns>
        private static List<StringList> List(IEnumerable<string> names)
        {
            return [.. names.Distinct(StringComparer.OrdinalIgnoreCase).Select(static name => new StringList { Id = name })];
        }

        /// <summary>The projection of a leaf the tenant's schema declares: its related projection, minus the twins the shape does not hold.</summary>
        /// <param name="entity">The leaf's metadata.</param>
        /// <param name="schema">The tenant's query schema.</param>
        /// <returns>The columns, key first.</returns>
        /// <exception cref="InvalidOperationException">The schema does not declare the entity.</exception>
        private static List<string> Projection(EntityMetadata entity, QueryexSchema schema)
        {
            EntityDescriptor descriptor = schema.FindEntity(entity.Name)
                ?? throw new InvalidOperationException($"The query schema declares no entity {entity.Name}, so its holders cannot be read.");
            HashSet<string> declared = new(descriptor.Properties.Select(static property => property.Name), StringComparer.Ordinal) { descriptor.Key.Name };
            return [.. StackDescriptorBuilder.RelatedSelect(entity).Where(declared.Contains)];
        }

        /// <summary>A user's name in the reader's content language, else in the first.</summary>
        /// <param name="user">The user.</param>
        /// <param name="slot">The reader's content-language position.</param>
        /// <returns>The name.</returns>
        private static string NameOf(TUser user, int slot)
        {
            string? twin = slot switch
            {
                2 => user.Name2,
                3 => user.Name3,
                _ => null,
            };

            return string.IsNullOrWhiteSpace(twin) ? user.Name : twin;
        }

        /// <summary>
        ///     Composes the batch: the matching permissions, their roles' current members and the
        ///     public audience in one statement, then the members the caller may read and the roles,
        ///     each with its related projection.
        /// </summary>
        /// <param name="user">The connected caller.</param>
        /// <param name="batch">The batch, its prologue already composed.</param>
        /// <param name="securable">The securable.</param>
        /// <returns>The plan the read takes the results from.</returns>
        private Plan Compose(ConnectedUser user, IDataBatch batch, SecurableDescriptor securable)
        {
            RequestContext context = accessor.Current;
            TenantSettings settings = context.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the holders' filters cannot be validated.");
            QueryexSchema schema = schemas.GetSchema(settings.Shape);

            // The users' visibility filter compiles under the pipeline's ceilings (spec 0014 §12.2).
            batch.Limits = QueryexProfiles.PipelineLimits;

            // --- The names a matching row may store: each level's resource and its former names ---
            List<SecurableDescriptor> levels = [securable];
            while (levels[^1].Owner is SecurableOwner owner && registry.Find(owner.Resource, securable.Action) is SecurableDescriptor next)
            {
                levels.Add(next);
            }

            HashSet<string> resources = new(levels.Select(static level => level.Resource), StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> aliases = (registry as SecurableRegistry)?.Aliases.Where(alias => resources.Contains(alias.Value)).Select(static alias => alias.Key) ?? [];
            List<StringList> names = List(resources.Concat(aliases));

            // --- The actions a matching row may store: the securable's, the wildcard, and for Read on the
            // wildcard resource every action a level registers (the implied read, spec 0014 §5.2 step 2) ---
            bool read = string.Equals(securable.Action, AccessActions.Read, StringComparison.OrdinalIgnoreCase);
            List<StringList> actions = List([securable.Action, AccessActions.Wildcard]);
            List<StringList> wildcardActions = List(read
                ? [securable.Action, AccessActions.Wildcard, .. levels.SelectMany(level => registry.ForResource(level.Resource)).Select(static registered => registered.Action)]
                : [securable.Action, AccessActions.Wildcard]);

            SqlIdentifier permissionIds = batch.DeclareIdTable();
            SqlIdentifier userIds = batch.DeclareIdTable();
            SqlIdentifier roleIds = batch.DeclareIdTable();

            // --- One statement: the matching permissions of active roles (set 0), the current members of
            // their roles that are not public, active users other than the system user (set 1), and the
            // audience every public role reaches (set 2). The membership predicate is the literal form of
            // AccessFilters.CurrentMembershipSql("M"): a text hole would bind as a parameter. ---
            BatchResult<RawResult> found = batch.Sql(
                $"""
                INSERT INTO {permissionIds} ([Id])
                SELECT P.[Id] FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
                WHERE R.[IsActive] = 1
                  AND ((P.[Resource] IN (SELECT N.[Id] FROM {names} AS N) AND ({read} = 1 OR P.[Action] IN (SELECT A.[Id] FROM {actions} AS A)))
                    OR (P.[Resource] = '*' AND P.[Action] IN (SELECT W.[Id] FROM {wildcardActions} AS W)));
                SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterQueryexVersion]
                FROM {permissionIds} AS K JOIN [core].[Permissions] AS P ON P.[Id] = K.[Id] JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
                ORDER BY P.[Id];
                INSERT INTO {userIds} ([Id])
                SELECT DISTINCT M.[UserId] FROM [core].[RoleMemberships] AS M
                JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId] AND R.[IsPublic] = 0
                JOIN [core].[Users] AS U ON U.[Id] = M.[UserId] AND U.[IsActive] = 1 AND U.[Kind] <> 'System'
                WHERE M.[RoleId] IN (SELECT P.[RoleId] FROM {permissionIds} AS K JOIN [core].[Permissions] AS P ON P.[Id] = K.[Id])
                  AND (([M].[StartsAt] IS NULL OR [M].[StartsAt] <= SYSUTCDATETIME()) AND ([M].[EndsAt] IS NULL OR [M].[EndsAt] > SYSUTCDATETIME()));
                SELECT M.[UserId], M.[RoleId], M.[EndsAt] FROM [core].[RoleMemberships] AS M
                JOIN {userIds} AS K ON K.[Id] = M.[UserId]
                JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId] AND R.[IsPublic] = 0
                WHERE M.[RoleId] IN (SELECT P.[RoleId] FROM {permissionIds} AS K2 JOIN [core].[Permissions] AS P ON P.[Id] = K2.[Id])
                  AND (([M].[StartsAt] IS NULL OR [M].[StartsAt] <= SYSUTCDATETIME()) AND ([M].[EndsAt] IS NULL OR [M].[EndsAt] > SYSUTCDATETIME()))
                ORDER BY M.[UserId], M.[RoleId];
                SELECT CASE WHEN EXISTS (SELECT 1 FROM {permissionIds} AS K JOIN [core].[Permissions] AS P ON P.[Id] = K.[Id] JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId] WHERE R.[IsPublic] = 1)
                    THEN (SELECT COUNT(*) FROM [core].[Users] AS U WHERE U.[IsActive] = 1 AND U.[Kind] <> 'System') ELSE 0 END AS [Audience];
                INSERT INTO {roleIds} ([Id]) SELECT DISTINCT P.[RoleId] FROM {permissionIds} AS K JOIN [core].[Permissions] AS P ON P.[Id] = K.[Id];
                """,
                new SqlOptions(resultSets: 3));

            // --- The members the caller may read, under the caller's own core.User × Read decision ---
            EntityMetadata userEntity = metadata.Get(typeof(TUser));
            List<string> userColumns = Projection(userEntity, schema);
            AccessDecision visibility = registry.Find(CoreResources.User, AccessActions.Read) is SecurableDescriptor userRead
                ? evaluator.DecideFrom(user.Access, userRead, time.GetUtcNow())
                : AccessDecision.Deny(CoreResources.User, AccessActions.Read);
            BatchResult<EntityQueryResult<TUser>>? visible = null;
            if (visibility.IsAllowed)
            {
                try
                {
                    visible = batch.Query(new EntityQuery<TUser>
                    {
                        Select = string.Join(", ", userColumns),
                        Filter = visibility.Filter,
                        Restrictions = [new KeySetRestriction(userEntity.Key.Name, userIds.Name)],
                        OrderBy = userEntity.Key.Name,
                    });
                }
                catch (ArgumentException tooComplex) when (visibility.Outcome == AccessOutcome.Filtered)
                {
                    // An administrator's configuration problem: a plain error, never a silent denial.
                    string involved = string.Join(", ", visibility.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.RoleId).Distinct().Order());
                    AccessLog.FilterTooComplex(_logger, tooComplex, context.Tenant?.Id ?? 0, user.UserId, visibility.Resource, visibility.Action, involved);
                    throw new InvalidOperationException("The caller's filter on the users exceeded the compile ceilings, so the holders cannot be listed.", tooComplex);
                }
            }

            // --- The roles of the matching permissions ---
            EntityMetadata roleEntity = metadata.Get(typeof(TRole));
            List<string> roleColumns = Projection(roleEntity, schema);
            BatchResult<EntityQueryResult<TRole>> roles = batch.Query(new EntityQuery<TRole>
            {
                Select = string.Join(", ", roleColumns),
                Restrictions = [new KeySetRestriction(roleEntity.Key.Name, roleIds.Name)],
                OrderBy = roleEntity.Key.Name,
            });

            return new Plan(context, schema, found, visible, roles, userEntity, userColumns, roleEntity, roleColumns);
        }

        /// <summary>Reads the executed batch into the holders: composed, restricted to the visible, ordered and capped.</summary>
        /// <param name="plan">The composed plan.</param>
        /// <param name="securable">The securable.</param>
        /// <returns>The holders.</returns>
        private AccessHolders Read(Plan plan, SecurableDescriptor securable)
        {
            // --- The three result sets of the statement ---
            IReadOnlyList<QueryRowSet> sets = plan.Found.Value.ResultSets;
            List<PermissionRow> rows = Permissions(sets[0]);
            List<HolderMembership> memberships = Memberships(sets[1]);
            int audience = sets[2].RowCount == 0 ? 0 : ((int[])sets[2].GetBuffer(0))[0];

            // --- Every holder, composed as each user's own decision would be ---
            HoldersComposition composition = composer.Compose(securable, rows, memberships, audience, plan.Schema, evaluator.CriteriaOf);

            // --- The holders the caller may read, by display name in the reader's language, then by id ---
            IReadOnlyList<TUser> members = plan.Visible?.Value.Entities ?? [];
            var readable = members.ToDictionary(static user => user.Id);
            CompareInfo compare = plan.Context.CultureInfo.CompareInfo;
            int slot = plan.Context.ContentLanguageIndex;
            List<AccessHolder> visible = [.. composition.Holders.Where(holder => readable.ContainsKey(holder.UserId))];
            visible.Sort((left, right) =>
            {
                int byName = compare.Compare(NameOf(readable[left.UserId], slot), NameOf(readable[right.UserId], slot), CompareOptions.IgnoreCase);
                return byName != 0 ? byName : left.UserId.CompareTo(right.UserId);
            });

            int max = options.Value.MaxHolders;
            List<AccessHolder> listed = [.. visible.Take(max)];

            // --- The listed users' and the involved roles' projections ---
            RelatedEntities related = new();
            if (listed.Count > 0)
            {
                related.Add(plan.UserEntity.Name, [.. listed.Select(holder => readable[holder.UserId])], plan.UserColumns.ToHashSet(StringComparer.Ordinal));
            }

            HashSet<int> involved = [.. composition.Grants.Select(static grant => grant.RoleId), .. composition.Problems.Select(static problem => problem.RoleId)];
            List<TRole> roles = [.. plan.Roles.Value.Entities.Where(role => involved.Contains(role.Id))];
            if (roles.Count > 0)
            {
                related.Add(plan.RoleEntity.Name, roles, plan.RoleColumns.ToHashSet(StringComparer.Ordinal));
            }

            return new AccessHolders(
                securable.Resource,
                securable.Action,
                composition.Everyone,
                composition.Grants,
                composition.MembersByRole,
                listed,
                composition.Holders.Count,
                visible.Count > max,
                composition.Criteria,
                composition.Problems,
                related);
        }

        /// <summary>The matching permissions, as permission rows without a window.</summary>
        /// <param name="set">The statement's first result set.</param>
        /// <returns>The rows.</returns>
        private static List<PermissionRow> Permissions(QueryRowSet set)
        {
            int[] ids = (int[])set.GetBuffer(0);
            int[] roleIds = (int[])set.GetBuffer(1);
            string?[] roleNames = (string?[])set.GetBuffer(2);
            bool[] isPublic = (bool[])set.GetBuffer(3);
            string?[] resources = (string?[])set.GetBuffer(4);
            string?[] actions = (string?[])set.GetBuffer(5);
            string?[] filters = (string?[])set.GetBuffer(6);
            int[] versions = (int[])set.GetBuffer(7);
            List<PermissionRow> rows = new(set.RowCount);
            for (int row = 0; row < set.RowCount; row++)
            {
                rows.Add(new PermissionRow(
                    ids[row],
                    roleIds[row],
                    roleNames[row] ?? string.Empty,
                    isPublic[row],
                    resources[row] ?? string.Empty,
                    actions[row] ?? string.Empty,
                    set.IsNull(6, row) ? null : filters[row],
                    set.IsNull(7, row) ? null : versions[row],
                    StartsAt: null,
                    EndsAt: null));
            }

            return rows;
        }

        /// <summary>The current memberships of the holder candidates.</summary>
        /// <param name="set">The statement's second result set.</param>
        /// <returns>The memberships.</returns>
        private static List<HolderMembership> Memberships(QueryRowSet set)
        {
            int[] userIds = (int[])set.GetBuffer(0);
            int[] roleIds = (int[])set.GetBuffer(1);
            var ends = (DateTimeOffset[])set.GetBuffer(2);
            List<HolderMembership> memberships = new(set.RowCount);
            for (int row = 0; row < set.RowCount; row++)
            {
                memberships.Add(new HolderMembership(userIds[row], roleIds[row], set.IsNull(2, row) ? null : ends[row]));
            }

            return memberships;
        }

        /// <summary>What one composition of the batch hands its read.</summary>
        /// <param name="Context">The context the batch composed under.</param>
        /// <param name="Schema">The tenant's query schema it composed under.</param>
        /// <param name="Found">The statement's three result sets.</param>
        /// <param name="Visible">The members the caller may read; null when the caller may read none.</param>
        /// <param name="Roles">The roles of the matching permissions.</param>
        /// <param name="UserEntity">The user leaf's metadata.</param>
        /// <param name="UserColumns">The users' projection.</param>
        /// <param name="RoleEntity">The role leaf's metadata.</param>
        /// <param name="RoleColumns">The roles' projection.</param>
        private sealed record Plan(
            RequestContext Context,
            QueryexSchema Schema,
            BatchResult<RawResult> Found,
            BatchResult<EntityQueryResult<TUser>>? Visible,
            BatchResult<EntityQueryResult<TRole>> Roles,
            EntityMetadata UserEntity,
            IReadOnlyList<string> UserColumns,
            EntityMetadata RoleEntity,
            IReadOnlyList<string> RoleColumns);
    }
}
