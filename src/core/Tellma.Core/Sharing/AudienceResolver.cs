// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Crud;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §6.2. The first round trip reads, under the caller's prologue: the row under the
    // caller's Share decision (§6.2 step 1); for the row and for each row seen through, its owner,
    // its grants and the stored permissions that may reach it; the current members of every role
    // they name; the audience size; the first users by name when a grant may reach everyone; per
    // data securable, the roles whose stored permissions read it, its readers' count and which of
    // the named users read it; and the users', roles' and seen-through rows' projections. A stored
    // permission's filter is Queryex text, which only C# compiles, so the filtered permissions the
    // first trip found are evaluated against their rows in a second round trip, one statement per
    // distinct text and version; a row with no filtered permission costs one round trip, whatever
    // it is seen through. The membership predicate in the statements is the literal form of
    // AccessFilters.CurrentMembershipSql("m"): a text hole would bind as a parameter.
    /// <summary>
    ///     Resolves the audience of a shareable row on the scope's caller's behalf: one guarded read
    ///     for the row, its grants, the stored permissions, the members, the data readers and the
    ///     projections, and one more for the filtered permissions, when any reach the row. One per
    ///     scope, closed over the distribution's user and role leaves.
    /// </summary>
    /// <typeparam name="TUser">The user leaf.</typeparam>
    /// <typeparam name="TRole">The role leaf.</typeparam>
    /// <param name="accessor">The scope's request context: the tenant's settings and the reader's culture.</param>
    /// <param name="runner">Runs the guarded reads.</param>
    /// <param name="registry">The securable registry.</param>
    /// <param name="evaluator">The scope's evaluator, whose decisions and criteria the audience reads.</param>
    /// <param name="classifier">Classifies each stored permission, as the permission set build does.</param>
    /// <param name="schemas">The query schemas the reads are rooted in.</param>
    /// <param name="metadata">The entity metadata of the leaves.</param>
    /// <param name="stacks">The stack registry, whose related selects project the users, roles and rows.</param>
    /// <param name="shapes">The composition's shareable entities.</param>
    /// <param name="options">The sharing options: the most members listed.</param>
    /// <param name="metrics">The sharing instruments.</param>
    /// <param name="time">The host's clock, which the caller's decisions are taken at.</param>
    internal sealed class AudienceResolver<TUser, TRole>(
        IRequestContextAccessor accessor,
        IGuardedBatchRunner runner,
        ISecurableRegistry registry,
        AccessEvaluator evaluator,
        PermissionClassifier classifier,
        QueryexSchemaProvider schemas,
        IEntityMetadataProvider metadata,
        IStackRegistry stacks,
        ShareableShapes shapes,
        IOptions<SharingOptions> options,
        SharingMetrics metrics,
        TimeProvider time) : IAudienceResolver
        where TUser : User
        where TRole : Role
    {
        /// <summary>The generic definition of the projection of one shareable entity's rows.</summary>
        private static readonly MethodInfo RowsDefinition = typeof(AudienceResolver<TUser, TRole>)
            .GetMethod(nameof(QueryRows), BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <inheritdoc />
        public async Task<ShareAudience> ResolveAsync(AudienceQuery query, CancellationToken cancellationToken = default)
        {
            // --- The query's shape: shareable resources, and through rows only of a resource ---
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(query.Data, nameof(query));
            var main = Group.Of(Shape(query.Resource), [query.Id]);
            Group? through = Through(query, main);
            List<DataTarget> data = [.. query.Data.Select((securable, index) => Target(securable ?? throw new ArgumentException("A data securable is null.", nameof(query)), index))];
            if (query.Id <= 0)
            {
                throw new NotFoundException(main.Shape.Entity.Name, [query.Id.ToString(CultureInfo.InvariantCulture)]);
            }

            // --- The first round trip; a recompose composes afresh under the refreshed context ---
            First? first = null;
            Read read = await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    first = Compose(user, batch, main, through, data);
                    return ValueTask.CompletedTask;
                },
                (_, _) => Take(first!),
                cancellationToken).ConfigureAwait(false);

            // --- The second, only for the filtered permissions that may reach the rows ---
            IReadOnlyDictionary<Pending, IReadOnlySet<int>> satisfied = read.Pending.Count == 0
                ? new Dictionary<Pending, IReadOnlySet<int>>()
                : await EvaluateAsync(read, cancellationToken).ConfigureAwait(false);

            // --- The audience, composed, with its projections ---
            ShareAudience audience = Finish(read, satisfied);
            metrics.RecordAudience(main.Shape.Entity.Name, audience.Total);
            return audience;
        }

        /// <summary>The projection of a leaf's rows among a key table, as a closure that adds them to the related entities.</summary>
        /// <typeparam name="T">The leaf.</typeparam>
        /// <param name="batch">The batch.</param>
        /// <param name="name">The entity's name.</param>
        /// <param name="columns">The projection, key first.</param>
        /// <param name="filter">The caller's filter on the rows; null for none.</param>
        /// <param name="keys">The key table.</param>
        /// <returns>Adds the rows read to related entities, once the batch executed.</returns>
        private static Func<IReadOnlySet<int>?, RelatedEntities, int> QueryRows<T>(IDataBatch batch, string name, IReadOnlyList<string> columns, FilterTree? filter, string keys)
            where T : Entity<int>
        {
            BatchResult<EntityQueryResult<T>> result = batch.Query(new EntityQuery<T>
            {
                Select = string.Join(", ", columns),
                Filter = filter,
                Restrictions = [new KeySetRestriction(nameof(Entity<>.Id), keys)],
                OrderBy = nameof(Entity<>.Id),
            });
            HashSet<string> projection = new(columns, StringComparer.Ordinal);
            return (only, related) =>
            {
                List<T> rows = [.. result.Value.Entities.Where(row => only is null || only.Contains(row.Id))];
                if (rows.Count > 0)
                {
                    related.Add(name, rows, projection);
                }

                return rows.Count;
            };
        }

        /// <summary>The bracketed list rows of names, each once ignoring case, as the permission columns compare them.</summary>
        /// <param name="names">The names.</param>
        /// <returns>The rows.</returns>
        private static List<StringList> List(IEnumerable<string> names)
        {
            return [.. names.Distinct(StringComparer.OrdinalIgnoreCase).Select(static name => new StringList { Id = name })];
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

        /// <summary>The shareable entity a resource names.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>Its shape.</returns>
        /// <exception cref="ArgumentException">The resource is not a shareable one.</exception>
        private ShareableShape Shape(string? resource)
        {
            return (resource is null ? null : shapes.Find(resource))
                ?? throw new ArgumentException($"The resource '{resource}' is not shareable, so it has no audience.", nameof(resource));
        }

        /// <summary>The rows seen through, other than the row itself, each once in the query's order.</summary>
        /// <param name="query">The query.</param>
        /// <param name="main">The row itself.</param>
        /// <returns>Their group; null when there are none.</returns>
        /// <exception cref="ArgumentException">The query names rows seen through but no resource for them.</exception>
        private Group? Through(AudienceQuery query, Group main)
        {
            // No row seen through needs no resource for it, nor one that is shareable yet.
            IReadOnlyList<int> ids = query.ThroughIds ?? [];
            if (ids.Count == 0)
            {
                return null;
            }

            if (query.ThroughResource is null)
            {
                throw new ArgumentException("The query names rows seen through but not their resource.", nameof(query));
            }

            ShareableShape shape = Shape(query.ThroughResource);
            bool same = string.Equals(shape.Entity.Name, main.Shape.Entity.Name, StringComparison.Ordinal);
            int[] distinct = [.. ids.Where(id => id > 0 && !(same && id == query.Id)).Distinct()];
            return distinct.Length == 0 ? null : Group.Of(shape, distinct);
        }

        /// <summary>
        ///     A data securable's readers' terms: the names a stored permission on it, or on an owner up
        ///     its chain, may store; the actions such a permission on the wildcard resource may store;
        ///     and whether a bespoke criterion lets every user read it.
        /// </summary>
        /// <param name="securable">The data securable.</param>
        /// <param name="index">Its position in the query.</param>
        /// <returns>The terms.</returns>
        private DataTarget Target(SecurableRef securable, int index)
        {
            // --- The securable and its owners on the same action; an unregistered pair is itself alone ---
            SecurableDescriptor? found = registry.Find(securable.Resource, securable.Action)
                ?? (registry.FindResource(securable.Resource) is string registered ? registry.Find(registered, AccessActions.Read) : null);
            if (found is null)
            {
                return new DataTarget(index, List([securable.Resource]), List([securable.Action, AccessActions.Wildcard]), Everyone: false);
            }

            List<SecurableDescriptor> levels = Levels(found);
            HashSet<string> actions = new(StringComparer.OrdinalIgnoreCase) { securable.Action, AccessActions.Read, AccessActions.Wildcard };
            actions.UnionWith(levels.SelectMany(level => registry.ForResource(level.Resource)).Select(static registered => registered.Action));

            // A bespoke criterion with the action Read, the wildcard or the securable's own, on any
            // level, applies to every user alike: the test runs once, and when it passes no user lacks it.
            bool everyone = levels.Any(level => evaluator.CriteriaOf(level.Resource).Any(criterion =>
                criterion.Action == AccessActions.Wildcard
                || string.Equals(criterion.Action, AccessActions.Read, StringComparison.OrdinalIgnoreCase)
                || string.Equals(criterion.Action, securable.Action, StringComparison.OrdinalIgnoreCase)));
            return new DataTarget(index, Names(levels), List(actions), everyone);
        }

        /// <summary>A securable and its registered owners on the same action, nearest first.</summary>
        /// <param name="securable">The securable.</param>
        /// <returns>The levels.</returns>
        private List<SecurableDescriptor> Levels(SecurableDescriptor securable)
        {
            // The registry validated at startup that every owner chain ends.
            List<SecurableDescriptor> levels = [securable];
            while (levels[^1].Owner is SecurableOwner owner && registry.Find(owner.Resource, securable.Action) is SecurableDescriptor next)
            {
                levels.Add(next);
            }

            return levels;
        }

        /// <summary>The names a stored permission on some levels may store: each level's resource and its former names.</summary>
        /// <param name="levels">The levels.</param>
        /// <returns>The list rows.</returns>
        private List<StringList> Names(IEnumerable<SecurableDescriptor> levels)
        {
            HashSet<string> resources = new(levels.Select(static level => level.Resource), StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> aliases = (registry as SecurableRegistry)?.Aliases.Where(alias => resources.Contains(alias.Value)).Select(static alias => alias.Key) ?? [];
            return List(resources.Concat(aliases));
        }

        /// <summary>Composes the first round trip.</summary>
        /// <param name="user">The connected caller.</param>
        /// <param name="batch">The batch, its prologue already composed.</param>
        /// <param name="main">The row itself.</param>
        /// <param name="through">The rows seen through; null for none.</param>
        /// <param name="data">The data securables.</param>
        /// <returns>The plan the read takes the results from.</returns>
        private First Compose(ConnectedUser user, IDataBatch batch, Group main, Group? through, IReadOnlyList<DataTarget> data)
        {
            RequestContext context = accessor.Current;
            TenantSettings settings = context.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the audience has no schema to compose under.");
            QueryexSchemaMap map = schemas.Map(settings.Shape);
            DateTimeOffset now = time.GetUtcNow();

            // The caller's filters compile under the pipeline's ceilings (spec 0014 §12.2).
            batch.Limits = QueryexProfiles.PipelineLimits;

            // --- The caller's decisions: sharing the row, reading the roles, reading the rows seen through ---
            AccessDecision share = Decide(user, main.Shape.Entity.Name, AccessActions.Share, now);
            bool membersVisible = Decide(user, CoreResources.Role, AccessActions.Read, now).IsAllowed;
            AccessDecision? throughRead = through is null ? null : Decide(user, through.Shape.Entity.Name, AccessActions.Read, now);
            int cap = options.Value.MaxAudienceMembers;

            // --- The row under the caller's Share decision: no row admitted is not found ---
            SqlIdentifier mainIds = batch.Tvp(main.Rows);
            BatchResult<RowPage>? admitted = share.IsAllowed
                ? batch.Rows(
                    new QuerySpec
                    {
                        Root = map.Descriptor(main.Shape.Entity),
                        Select = nameof(Entity<>.Id),
                        Filter = share.Filter,
                        Restrictions = [new KeySetRestriction(nameof(Entity<>.Id), mainIds.Name)],
                    },
                    null,
                    null)
                : null;

            // --- The batch-local tables the statements fill and read ---
            SqlIdentifier people = batch.DeclareIdTable();
            SqlIdentifier candidates = batch.DeclareIdTable();
            SqlIdentifier roleIds = batch.DeclareIdTable();
            SqlIdentifier reach = batch.DeclareIdTable();
            SqlIdentifier listed = batch.DeclareIdTable();
            int visible = membersVisible ? 1 : 0;

            // --- Per group: its rows, grants and the stored permissions that may reach them ---
            GroupRead mainRead = ComposeGroup(batch, main, mainIds, people, candidates, roleIds, reach, visible);
            GroupRead? throughRead2 = through is null ? null : ComposeGroup(batch, through, batch.Tvp(through.Rows), people, candidates, roleIds, reach, visible);

            // --- The members of the roles named, the audience size, and the first users by name when a grant may reach everyone ---
            SqlIdentifier twin = new(context.ContentLanguageIndex switch
            {
                2 => nameof(User.Name2),
                3 => nameof(User.Name3),
                _ => nameof(User.Name),
            });
            BatchResult<RawResult> common = batch.Sql(
                $"""
                SELECT DISTINCT [m].[RoleId], [m].[UserId] FROM [core].[RoleMemberships] AS [m]
                JOIN [core].[Roles] AS [o] ON [o].[Id] = [m].[RoleId] AND [o].[IsActive] = 1 AND [o].[IsPublic] = 0
                JOIN [core].[Users] AS [u] ON [u].[Id] = [m].[UserId] AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                WHERE [m].[RoleId] IN (SELECT [Id] FROM {roleIds})
                  AND (([m].[StartsAt] IS NULL OR [m].[StartsAt] <= SYSUTCDATETIME()) AND ([m].[EndsAt] IS NULL OR [m].[EndsAt] > SYSUTCDATETIME()))
                ORDER BY [m].[RoleId], [m].[UserId];
                INSERT INTO {candidates} ([Id])
                SELECT DISTINCT [m].[UserId] FROM [core].[RoleMemberships] AS [m]
                JOIN [core].[Roles] AS [o] ON [o].[Id] = [m].[RoleId] AND [o].[IsActive] = 1 AND [o].[IsPublic] = 0
                JOIN [core].[Users] AS [u] ON [u].[Id] = [m].[UserId] AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                WHERE [m].[RoleId] IN (SELECT [Id] FROM {roleIds})
                  AND (([m].[StartsAt] IS NULL OR [m].[StartsAt] <= SYSUTCDATETIME()) AND ([m].[EndsAt] IS NULL OR [m].[EndsAt] > SYSUTCDATETIME()))
                  AND NOT EXISTS (SELECT 1 FROM {candidates} AS [k] WHERE [k].[Id] = [m].[UserId]);
                SELECT COUNT(*) AS [Audience] FROM [core].[Users] AS [u] WHERE [u].[IsActive] = 1 AND [u].[Kind] <> 'System';
                INSERT INTO {listed} ([Id])
                SELECT TOP ({cap}) [u].[Id] FROM [core].[Users] AS [u]
                WHERE {visible} = 1 AND EXISTS (SELECT 1 FROM {reach}) AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                ORDER BY COALESCE(NULLIF([u].{twin}, N''), [u].[Name]), [u].[Id];
                SELECT [l].[Id] FROM {listed} AS [l] ORDER BY [l].[Id];
                INSERT INTO {candidates} ([Id])
                SELECT [l].[Id] FROM {listed} AS [l] WHERE NOT EXISTS (SELECT 1 FROM {candidates} AS [k] WHERE [k].[Id] = [l].[Id]);
                INSERT INTO {people} ([Id])
                SELECT [k].[Id] FROM {candidates} AS [k] WHERE {visible} = 1 AND NOT EXISTS (SELECT 1 FROM {people} AS [p] WHERE [p].[Id] = [k].[Id]);
                """,
                new SqlOptions(resultSets: 3));

            // --- Per data securable: its readers' count and the named users who read it ---
            List<BatchResult<RawResult>?> readers = [];
            foreach (DataTarget target in data)
            {
                if (target.Everyone)
                {
                    readers.Add(null);
                    continue;
                }

                SqlIdentifier readerRoles = batch.DeclareIdTable();
                List<StringList> names = target.Names;
                List<StringList> actions = target.Actions;
                readers.Add(batch.Sql(
                    $"""
                    INSERT INTO {readerRoles} ([Id])
                    SELECT DISTINCT [o].[Id] FROM [core].[Permissions] AS [p] JOIN [core].[Roles] AS [o] ON [o].[Id] = [p].[RoleId]
                    WHERE [o].[IsActive] = 1 AND [o].[IsPublic] = 0
                      AND ([p].[Resource] IN (SELECT [Id] FROM {names}) OR ([p].[Resource] = '*' AND [p].[Action] IN (SELECT [Id] FROM {actions})));
                    SELECT CAST(CASE WHEN EXISTS (
                            SELECT 1 FROM [core].[Permissions] AS [p] JOIN [core].[Roles] AS [o] ON [o].[Id] = [p].[RoleId]
                            WHERE [o].[IsActive] = 1 AND [o].[IsPublic] = 1 AND [p].[Resource] IN (SELECT [Id] FROM {names}))
                        THEN 1 ELSE 0 END AS bit) AS [Everyone],
                        (SELECT COUNT(DISTINCT [m].[UserId]) FROM [core].[RoleMemberships] AS [m]
                         JOIN [core].[Users] AS [u] ON [u].[Id] = [m].[UserId] AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                         WHERE [m].[RoleId] IN (SELECT [Id] FROM {readerRoles})
                           AND (([m].[StartsAt] IS NULL OR [m].[StartsAt] <= SYSUTCDATETIME()) AND ([m].[EndsAt] IS NULL OR [m].[EndsAt] > SYSUTCDATETIME()))) AS [Readers];
                    SELECT DISTINCT [m].[UserId] FROM [core].[RoleMemberships] AS [m] JOIN {candidates} AS [k] ON [k].[Id] = [m].[UserId]
                    WHERE [m].[RoleId] IN (SELECT [Id] FROM {readerRoles})
                      AND (([m].[StartsAt] IS NULL OR [m].[StartsAt] <= SYSUTCDATETIME()) AND ([m].[EndsAt] IS NULL OR [m].[EndsAt] > SYSUTCDATETIME()))
                    ORDER BY [m].[UserId];
                    """,
                    new SqlOptions(resultSets: 2)));
            }

            // --- The projections: the users (with their kind), the roles, and the rows seen through the caller may read ---
            QueryexSchema schema = map.Schema;
            EntityMetadata userEntity = metadata.Get(typeof(TUser));
            List<string> userColumns = [.. RelatedProjections.Of(stacks, userEntity, schema)];
            if (!userColumns.Contains(nameof(User.Kind), StringComparer.Ordinal))
            {
                userColumns.Add(nameof(User.Kind));
            }

            BatchResult<EntityQueryResult<TUser>> users = batch.Query(new EntityQuery<TUser>
            {
                Select = string.Join(", ", userColumns),
                Restrictions = [new KeySetRestriction(userEntity.Key.Name, people.Name)],
                OrderBy = userEntity.Key.Name,
            });
            EntityMetadata roleEntity = metadata.Get(typeof(TRole));
            List<string> roleColumns = [.. RelatedProjections.Of(stacks, roleEntity, schema)];
            BatchResult<EntityQueryResult<TRole>> roles = batch.Query(new EntityQuery<TRole>
            {
                Select = string.Join(", ", roleColumns),
                Restrictions = [new KeySetRestriction(roleEntity.Key.Name, roleIds.Name)],
                OrderBy = roleEntity.Key.Name,
            });
            Func<IReadOnlySet<int>?, RelatedEntities, int>? throughRows = null;
            if (through is not null && throughRead2 is not null && throughRead is { IsAllowed: true })
            {
                EntityMetadata entity = through.Shape.Entity;
                List<string> columns = [.. RelatedProjections.Of(stacks, entity, schema)];
                throughRows = (Func<IReadOnlySet<int>?, RelatedEntities, int>)RowsDefinition.MakeGenericMethod(entity.ClrType)
                    .Invoke(null, [batch, entity.Name, columns, throughRead.Filter, throughRead2.Ids.Name])!;
            }

            return new First(
                context,
                map,
                main,
                through,
                data,
                membersVisible,
                cap,
                admitted,
                mainRead,
                throughRead2,
                common,
                readers,
                users,
                userEntity.Name,
                userColumns,
                roles,
                roleEntity.Name,
                roleColumns,
                throughRows);
        }

        /// <summary>
        ///     Composes one group's statement: its rows' owners, their grants, the stored permissions
        ///     of active roles that may reach them, and the users and roles they name.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="group">The group.</param>
        /// <param name="ids">The group's ids, bound.</param>
        /// <param name="people">The users whose projections the read carries.</param>
        /// <param name="candidates">The audience users the grants name, whose data readings are marked.</param>
        /// <param name="roleIds">The roles the grants and permissions name.</param>
        /// <param name="reach">Holds a row when a grant may reach every audience user.</param>
        /// <param name="visible">1 when the caller sees the members, else 0.</param>
        /// <returns>The group's pending read.</returns>
        private GroupRead ComposeGroup(
            IDataBatch batch, Group group, SqlIdentifier ids, SqlIdentifier people, SqlIdentifier candidates, SqlIdentifier roleIds, SqlIdentifier reach, int visible)
        {
            // --- The tables and columns of the pair, and the names a permission reaching its rows may store ---
            ShareableShape shape = group.Shape;
            SqlIdentifier entity = new($"{shape.Entity.Table.Schema}.{shape.Entity.Table.Name}");
            SqlIdentifier shares = new($"{shape.Share.Table.Schema}.{shape.Share.Table.Name}");
            SqlIdentifier row = new(shape.Row.ForeignKey.Column);
            SqlIdentifier owner = new(shape.Entity.Properties.First(static property => property.Name == nameof(IShareable.OwnerId)).Column);
            SecurableDescriptor? read = registry.Find(shape.Entity.Name, AccessActions.Read);
            List<SecurableDescriptor> levels = read is null ? [] : Levels(read);
            List<StringList> names = read is null ? List([shape.Entity.Name]) : Names(levels);
            List<StringList> actions = List([
                AccessActions.Read,
                AccessActions.Wildcard,
                .. levels.SelectMany(level => registry.ForResource(level.Resource)).Select(static registered => registered.Action),
            ]);

            // --- One statement: the rows (set 0), their grants (set 1) and the permissions (set 2), then the names ---
            BatchResult<RawResult> found = batch.Sql(
                $"""
                SELECT [r].[Id], [r].{owner}, CAST(CASE WHEN [u].[Id] IS NULL THEN 0 ELSE 1 END AS bit) AS [OwnerIsAudience]
                FROM {entity} AS [r]
                LEFT JOIN [core].[Users] AS [u] ON [u].[Id] = [r].{owner} AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                WHERE [r].[Id] IN (SELECT [Id] FROM {ids})
                ORDER BY [r].[Id];
                SELECT [s].{row}, [s].[Id], [s].[UserId], [s].[RoleId], [s].[Access], [s].[SharedById], [s].[SharedAt],
                    CAST(CASE WHEN [u].[Id] IS NULL THEN 0 ELSE 1 END AS bit) AS [UserIsAudience],
                    CAST(ISNULL([o].[IsActive], 0) AS bit) AS [RoleIsActive],
                    CAST(ISNULL([o].[IsPublic], 0) AS bit) AS [RoleIsPublic]
                FROM {shares} AS [s]
                LEFT JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[UserId] AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                LEFT JOIN [core].[Roles] AS [o] ON [o].[Id] = [s].[RoleId]
                WHERE [s].{row} IN (SELECT [Id] FROM {ids})
                ORDER BY [s].{row}, [s].[Id];
                SELECT [p].[Id], [p].[RoleId], [o].[Name], [o].[IsPublic], [p].[Resource], [p].[Action], [p].[Filter], [p].[FilterQueryexVersion]
                FROM [core].[Permissions] AS [p] JOIN [core].[Roles] AS [o] ON [o].[Id] = [p].[RoleId]
                WHERE [o].[IsActive] = 1
                  AND ([p].[Resource] IN (SELECT [Id] FROM {names}) OR ([p].[Resource] = '*' AND [p].[Action] IN (SELECT [Id] FROM {actions})))
                ORDER BY [p].[Id];
                INSERT INTO {people} ([Id])
                SELECT DISTINCT [x].[Id] FROM (
                    SELECT [r].{owner} AS [Id] FROM {entity} AS [r] WHERE [r].[Id] IN (SELECT [Id] FROM {ids})
                    UNION SELECT [s].[UserId] FROM {shares} AS [s] WHERE [s].{row} IN (SELECT [Id] FROM {ids})
                    UNION SELECT [s].[SharedById] FROM {shares} AS [s] WHERE {visible} = 1 AND [s].{row} IN (SELECT [Id] FROM {ids})) AS [x]
                WHERE [x].[Id] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {people} AS [k] WHERE [k].[Id] = [x].[Id]);
                INSERT INTO {candidates} ([Id])
                SELECT DISTINCT [x].[Id] FROM (
                    SELECT [r].{owner} AS [Id] FROM {entity} AS [r] WHERE [r].[Id] IN (SELECT [Id] FROM {ids})
                    UNION SELECT [s].[UserId] FROM {shares} AS [s] WHERE [s].{row} IN (SELECT [Id] FROM {ids})) AS [x]
                JOIN [core].[Users] AS [u] ON [u].[Id] = [x].[Id] AND [u].[IsActive] = 1 AND [u].[Kind] <> 'System'
                WHERE NOT EXISTS (SELECT 1 FROM {candidates} AS [k] WHERE [k].[Id] = [x].[Id]);
                INSERT INTO {roleIds} ([Id])
                SELECT DISTINCT [x].[Id] FROM (
                    SELECT [s].[RoleId] AS [Id] FROM {shares} AS [s] WHERE [s].{row} IN (SELECT [Id] FROM {ids})
                    UNION SELECT [p].[RoleId] FROM [core].[Permissions] AS [p] JOIN [core].[Roles] AS [o] ON [o].[Id] = [p].[RoleId]
                    WHERE [o].[IsActive] = 1
                      AND ([p].[Resource] IN (SELECT [Id] FROM {names}) OR ([p].[Resource] = '*' AND [p].[Action] IN (SELECT [Id] FROM {actions})))) AS [x]
                WHERE [x].[Id] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {roleIds} AS [k] WHERE [k].[Id] = [x].[Id]);
                INSERT INTO {reach} ([Id])
                SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM {reach}) AND (
                    EXISTS (SELECT 1 FROM {shares} AS [s] LEFT JOIN [core].[Roles] AS [o] ON [o].[Id] = [s].[RoleId]
                            WHERE [s].{row} IN (SELECT [Id] FROM {ids}) AND [s].[UserId] IS NULL
                              AND ([s].[RoleId] IS NULL OR ([o].[IsActive] = 1 AND [o].[IsPublic] = 1)))
                    OR EXISTS (SELECT 1 FROM [core].[Permissions] AS [p] JOIN [core].[Roles] AS [o] ON [o].[Id] = [p].[RoleId]
                               WHERE [o].[IsActive] = 1 AND [o].[IsPublic] = 1 AND [p].[Resource] IN (SELECT [Id] FROM {names})));
                """,
                new SqlOptions(resultSets: 3));
            return new GroupRead(ids, found);
        }

        /// <summary>Decides one securable for the connected caller; an unregistered pair is denied.</summary>
        /// <param name="user">The connected caller.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="now">The instant decided.</param>
        /// <returns>The decision.</returns>
        private AccessDecision Decide(ConnectedUser user, string resource, string action, DateTimeOffset now)
        {
            return registry.Find(resource, action) is SecurableDescriptor securable
                ? evaluator.DecideFrom(user.Access, securable, now)
                : AccessDecision.Deny(resource, action);
        }

        /// <summary>Takes the first round trip's results: the row admitted, the groups' facts and the permissions still to evaluate.</summary>
        /// <param name="first">The composed plan.</param>
        /// <returns>What was read.</returns>
        /// <exception cref="NotFoundException">The caller's Share decision does not admit the row, or it does not exist.</exception>
        private Read Take(First first)
        {
            // --- The row, admitted by the caller's Share decision ---
            if (first.Admitted is null || first.Admitted.Value.Rows.RowCount == 0)
            {
                throw new NotFoundException(first.Main.Shape.Entity.Name, [first.Main.Ids[0].ToString(CultureInfo.InvariantCulture)]);
            }

            // --- Each group's rows, grants and the permissions that reach them, or still to evaluate ---
            QueryexSchema schema = first.Map.Schema;
            List<Pending> pending = [];
            GroupFacts main = Facts(first.Main, first.MainRead, schema, pending);
            GroupFacts? through = first.Through is null || first.ThroughRead is null ? null : Facts(first.Through, first.ThroughRead, schema, pending);

            // --- The members of the roles named, the audience size and the users listed when a grant reaches everyone ---
            IReadOnlyList<QueryRowSet> common = first.Common.Value.ResultSets;
            Dictionary<int, List<int>> membersByRole = [];
            int[] memberRoles = (int[])common[0].GetBuffer(0);
            int[] memberUsers = (int[])common[0].GetBuffer(1);
            for (int index = 0; index < common[0].RowCount; index++)
            {
                if (!membersByRole.TryGetValue(memberRoles[index], out List<int>? members))
                {
                    membersByRole[memberRoles[index]] = members = [];
                }

                members.Add(memberUsers[index]);
            }

            int audience = common[1].RowCount == 0 ? 0 : ((int[])common[1].GetBuffer(0))[0];
            int[] listed = [.. ((int[])common[2].GetBuffer(0)).Take(common[2].RowCount)];

            // --- Who reads each data securable ---
            List<DataReaders> readers = [];
            foreach (BatchResult<RawResult>? result in first.Readers)
            {
                if (result is null)
                {
                    readers.Add(new DataReaders(true, audience, new HashSet<int>()));
                    continue;
                }

                QueryRowSet counts = result.Value.ResultSets[0];
                QueryRowSet named = result.Value.ResultSets[1];
                bool everyone = counts.RowCount > 0 && ((bool[])counts.GetBuffer(0))[0];
                int count = counts.RowCount > 0 ? ((int[])counts.GetBuffer(1))[0] : 0;
                readers.Add(new DataReaders(everyone, count, ((int[])named.GetBuffer(0)).Take(named.RowCount).ToHashSet()));
            }

            return new Read(
                first,
                main,
                through,
                pending,
                membersByRole.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<int>)pair.Value),
                audience,
                listed,
                readers);
        }

        /// <summary>
        ///     One group's facts: its rows that exist, each with its grants; the stored permissions that
        ///     reach every row, unfiltered; and the filtered ones, queued for the second round trip.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="read">The group's results.</param>
        /// <param name="schema">The tenant's query schema, which each filter is validated under.</param>
        /// <param name="pending">Receives the filtered permissions to evaluate.</param>
        /// <returns>The facts.</returns>
        private GroupFacts Facts(Group group, GroupRead read, QueryexSchema schema, List<Pending> pending)
        {
            IReadOnlyList<QueryRowSet> sets = read.Found.Value.ResultSets;

            // --- The rows that exist, with their owners ---
            QueryRowSet rows = sets[0];
            int[] ids = (int[])rows.GetBuffer(0);
            int[] owners = (int[])rows.GetBuffer(1);
            bool[] ownersAudience = (bool[])rows.GetBuffer(2);
            Dictionary<int, (int? Owner, bool Audience)> existing = [];
            for (int index = 0; index < rows.RowCount; index++)
            {
                existing[ids[index]] = (rows.IsNull(1, index) ? null : owners[index], ownersAudience[index]);
            }

            // --- Their grants ---
            QueryRowSet grants = sets[1];
            int[] rowIds = (int[])grants.GetBuffer(0);
            int[] grantIds = (int[])grants.GetBuffer(1);
            int[] userIds = (int[])grants.GetBuffer(2);
            int[] roleIds = (int[])grants.GetBuffer(3);
            string?[] accesses = (string?[])grants.GetBuffer(4);
            int[] sharers = (int[])grants.GetBuffer(5);
            var sharedAt = (DateTimeOffset[])grants.GetBuffer(6);
            bool[] userAudience = (bool[])grants.GetBuffer(7);
            bool[] roleActive = (bool[])grants.GetBuffer(8);
            bool[] rolePublic = (bool[])grants.GetBuffer(9);
            Dictionary<int, List<AudienceShare>> sharesByRow = [];
            for (int index = 0; index < grants.RowCount; index++)
            {
                if (!sharesByRow.TryGetValue(rowIds[index], out List<AudienceShare>? held))
                {
                    sharesByRow[rowIds[index]] = held = [];
                }

                held.Add(new AudienceShare(
                    grantIds[index],
                    grants.IsNull(2, index) ? null : userIds[index],
                    grants.IsNull(3, index) ? null : roleIds[index],
                    Enum.Parse<ShareAccess>(accesses[index] ?? nameof(ShareAccess.View)),
                    sharers[index],
                    sharedAt[index],
                    userAudience[index],
                    roleActive[index],
                    rolePublic[index]));
            }

            // --- The stored permissions, classified as the permission set build classifies them ---
            List<AudiencePermission> unfiltered = [];
            SecurableDescriptor? readSecurable = registry.Find(group.Shape.Entity.Name, AccessActions.Read);
            QueryRowSet permissions = sets[2];
            if (readSecurable is not null && existing.Count > 0)
            {
                int[] permissionIds = (int[])permissions.GetBuffer(0);
                int[] permissionRoles = (int[])permissions.GetBuffer(1);
                bool[] permissionPublic = (bool[])permissions.GetBuffer(3);
                string?[] resources = (string?[])permissions.GetBuffer(4);
                string?[] actions = (string?[])permissions.GetBuffer(5);
                string?[] filters = (string?[])permissions.GetBuffer(6);
                int[] versions = (int[])permissions.GetBuffer(7);
                Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];
                for (int index = 0; index < permissions.RowCount; index++)
                {
                    PermissionClassification classification = classifier.Classify(
                        resources[index],
                        actions[index],
                        permissions.IsNull(6, index) ? null : filters[index],
                        permissions.IsNull(7, index) ? null : versions[index],
                        permissionPublic[index],
                        schema,
                        validations);

                    // A permission that grants nothing, or that grants no action reading the rows implies,
                    // reaches no row; a filter never grants an action whose securable takes none.
                    if (classification.Problem is not null
                        || !AccessComposition.Matches(classification.Resource, classification.Action, readSecurable, classifier.ActionResources)
                        || (classification.Filter is not null && readSecurable.FilterRoot is null))
                    {
                        continue;
                    }

                    // Saving, or doing anything, edits the row; every other action only reads it.
                    bool edits = classification.Action == AccessActions.Wildcard
                        || string.Equals(classification.Action, AccessActions.Save, StringComparison.OrdinalIgnoreCase);
                    // A filter that reads the current user is listed for every row and evaluated for none:
                    // one evaluation, as the reader, would answer for the reader alone.
                    bool dependsOnViewer = classification.Filter is string text && ViewerFilters.ReadsCurrentUser(text);
                    AudiencePermission permission = new(
                        permissionIds[index],
                        permissionRoles[index],
                        permissionPublic[index],
                        edits ? AudienceAccess.Edit : AudienceAccess.View,
                        classification.Filter is not null,
                        dependsOnViewer);
                    if (!dependsOnViewer && classification.Filter is string filter && classification.FilterQueryexVersion is int version)
                    {
                        pending.Add(new Pending(group, read.Ids, filter, version, permission));
                    }
                    else
                    {
                        unfiltered.Add(permission);
                    }
                }
            }

            // --- The rows that exist, in the group's order ---
            List<GroupRow> found = [];
            foreach (int id in group.Ids)
            {
                if (existing.TryGetValue(id, out (int? Owner, bool Audience) row))
                {
                    found.Add(new GroupRow(id, row.Owner, row.Audience, sharesByRow.GetValueOrDefault(id) ?? []));
                }
            }

            return new GroupFacts(found, unfiltered);
        }

        /// <summary>
        ///     The second round trip: each distinct filter of the stored permissions that may reach the
        ///     rows, compiled under its own version and evaluated once over its group's rows.
        /// </summary>
        /// <param name="read">What the first round trip read.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The rows that satisfy each pending permission's filter.</returns>
        private async Task<IReadOnlyDictionary<Pending, IReadOnlySet<int>>> EvaluateAsync(Read read, CancellationToken cancellationToken)
        {
            Dictionary<(Group Group, string Text, int Version), BatchResult<RowPage>?>? statements = null;
            return await runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    // --- One statement per group and distinct text under its version, over the group's rows ---
                    batch.Limits = QueryexProfiles.PipelineLimits;
                    statements = [];
                    Dictionary<Group, SqlIdentifier> ids = [];
                    foreach (Pending pending in read.Pending)
                    {
                        (Group Group, string Text, int Version) key = (pending.Group, pending.Filter, pending.Version);
                        if (statements.ContainsKey(key))
                        {
                            continue;
                        }

                        if (!ids.TryGetValue(pending.Group, out SqlIdentifier? table))
                        {
                            ids[pending.Group] = table = batch.Tvp(pending.Group.Rows);
                        }

                        try
                        {
                            statements[key] = batch.Rows(
                                new QuerySpec
                                {
                                    Root = read.First.Map.Descriptor(pending.Group.Shape.Entity),
                                    Select = nameof(Entity<>.Id),
                                    Filter = FilterTree.Leaf(pending.Filter, pending.Version),
                                    Restrictions = [new KeySetRestriction(nameof(Entity<>.Id), table.Name)],
                                },
                                null,
                                null);
                        }
                        catch (ArgumentException)
                        {
                            // A validated filter beyond the compile ceilings grants nothing it cannot be
                            // shown to grant: the row is taken not to satisfy it.
                            statements[key] = null;
                        }
                    }

                    return ValueTask.CompletedTask;
                },
                (_, _) =>
                {
                    // --- The rows each pending permission's filter admits ---
                    Dictionary<Pending, IReadOnlySet<int>> satisfied = [];
                    foreach (Pending pending in read.Pending)
                    {
                        BatchResult<RowPage>? result = statements![(pending.Group, pending.Filter, pending.Version)];
                        QueryRowSet? rows = result?.Value.Rows;
                        HashSet<int> admitted = rows is null ? [] : [.. ((int[])rows.GetBuffer(0)).Take(rows.RowCount)];
                        satisfied[pending] = admitted;
                    }

                    return (IReadOnlyDictionary<Pending, IReadOnlySet<int>>)satisfied;
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Composes the audience from what the round trips read, and adds its projections.</summary>
        /// <param name="read">What the first round trip read.</param>
        /// <param name="satisfied">The rows that satisfy each pending permission's filter.</param>
        /// <returns>The audience.</returns>
        private static ShareAudience Finish(Read read, IReadOnlyDictionary<Pending, IReadOnlySet<int>> satisfied)
        {
            // --- Each row's subject: its owner, its grants, and the permissions that reach it ---
            List<AudienceSubject> subjects = [];
            foreach ((GroupFacts? facts, bool isThrough) in new[] { (read.Main, false), (read.Through, true) })
            {
                if (facts is null)
                {
                    continue;
                }

                foreach (GroupRow row in facts.Rows)
                {
                    List<AudiencePermission> reaching = [.. facts.Unfiltered];
                    reaching.AddRange(read.Pending
                        .Where(pending => pending.Group == (isThrough ? read.First.Through : read.First.Main) && satisfied.TryGetValue(pending, out IReadOnlySet<int>? rows) && rows.Contains(row.Id))
                        .Select(static pending => pending.Permission));
                    subjects.Add(new AudienceSubject(isThrough ? row.Id : null, row.OwnerId, row.OwnerIsAudience, row.Shares, reaching));
                }
            }

            // --- The composition, members by display name in the reader's language ---
            First first = read.First;
            var users = first.Users.Value.Entities.ToDictionary(static user => user.Id);
            CompareInfo compare = first.Context.CultureInfo.CompareInfo;
            int slot = first.Context.ContentLanguageIndex;
            var byName = Comparer<int>.Create((left, right) =>
            {
                string leftName = users.TryGetValue(left, out TUser? leftUser) ? NameOf(leftUser, slot) : left.ToString(CultureInfo.InvariantCulture);
                string rightName = users.TryGetValue(right, out TUser? rightUser) ? NameOf(rightUser, slot) : right.ToString(CultureInfo.InvariantCulture);
                int compared = compare.Compare(leftName, rightName, CompareOptions.IgnoreCase);
                return compared != 0 ? compared : left.CompareTo(right);
            });
            AudienceFacts audienceFacts = new(read.Audience, subjects, read.MembersByRole, read.Listed, read.Readers);
            AudienceComposition composition = AudienceComposer.Compose(audienceFacts, first.MembersVisible, first.Cap, byName);

            // --- The projections of the users and roles the audience names, and of the rows seen through ---
            RelatedEntities related = new();
            List<TUser> named = [.. composition.Users.Where(users.ContainsKey).Order().Select(id => users[id])];
            if (named.Count > 0)
            {
                related.Add(first.UserName, named, first.UserColumns.ToHashSet(StringComparer.Ordinal));
            }

            List<TRole> roles = [.. first.Roles.Value.Entities.Where(role => composition.Roles.Contains(role.Id))];
            if (roles.Count > 0)
            {
                related.Add(first.RoleName, roles, first.RoleColumns.ToHashSet(StringComparer.Ordinal));
            }

            _ = first.ThroughRows?.Invoke(read.Through?.Rows.Select(static row => row.Id).ToHashSet(), related);
            return new ShareAudience(composition.Total, composition.Capped, composition.CannotRead, composition.Members, composition.Grants, related);
        }

        /// <summary>One group of rows of one shareable entity: the row itself, or the rows seen through.</summary>
        /// <param name="Shape">The entity's shape.</param>
        /// <param name="Ids">The rows, in the query's order.</param>
        /// <param name="Rows">The rows as a table-valued parameter's rows.</param>
        private sealed record Group(ShareableShape Shape, IReadOnlyList<int> Ids, IReadOnlyList<IdList> Rows)
        {
            /// <summary>A group of rows.</summary>
            /// <param name="shape">The entity's shape.</param>
            /// <param name="ids">The rows.</param>
            /// <returns>The group.</returns>
            public static Group Of(ShareableShape shape, IReadOnlyList<int> ids)
            {
                return new Group(shape, ids, [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })]);
            }
        }

        /// <summary>A data securable's readers' terms.</summary>
        /// <param name="Index">Its position in the query.</param>
        /// <param name="Names">The names a stored permission on it, or on an owner up its chain, may store.</param>
        /// <param name="Actions">The actions a stored permission on the wildcard resource may store to read it.</param>
        /// <param name="Everyone">Whether a bespoke criterion lets every user read it.</param>
        private sealed record DataTarget(int Index, List<StringList> Names, List<StringList> Actions, bool Everyone);

        /// <summary>One group's pending results.</summary>
        /// <param name="Ids">The group's ids, bound.</param>
        /// <param name="Found">Its statement's three result sets.</param>
        private sealed record GroupRead(SqlIdentifier Ids, BatchResult<RawResult> Found);

        /// <summary>One row of a group that exists.</summary>
        /// <param name="Id">The row.</param>
        /// <param name="OwnerId">Its owner; null for a standard row.</param>
        /// <param name="OwnerIsAudience">Whether the owner is an audience user.</param>
        /// <param name="Shares">Its grants.</param>
        private sealed record GroupRow(int Id, int? OwnerId, bool OwnerIsAudience, IReadOnlyList<AudienceShare> Shares);

        /// <summary>A group's facts after the first round trip.</summary>
        /// <param name="Rows">The rows that exist, in the group's order.</param>
        /// <param name="Unfiltered">
        ///     The stored permissions listed for every row of the group: the unfiltered ones, which reach
        ///     it, and those whose filter reads the current user, which reach no one.
        /// </param>
        private sealed record GroupFacts(IReadOnlyList<GroupRow> Rows, IReadOnlyList<AudiencePermission> Unfiltered);

        /// <summary>A filtered stored permission that reaches the rows of its group that satisfy its filter.</summary>
        /// <param name="Group">The group.</param>
        /// <param name="Ids">The group's ids, bound in the first round trip.</param>
        /// <param name="Filter">The validated filter.</param>
        /// <param name="Version">Its Queryex version.</param>
        /// <param name="Permission">The permission, as it reaches a row.</param>
        private sealed record Pending(Group Group, SqlIdentifier Ids, string Filter, int Version, AudiencePermission Permission);

        /// <summary>What one composition of the first round trip hands its read.</summary>
        /// <param name="Context">The context it composed under.</param>
        /// <param name="Map">The tenant's query schema map.</param>
        /// <param name="Main">The row itself.</param>
        /// <param name="Through">The rows seen through; null for none.</param>
        /// <param name="Data">The data securables.</param>
        /// <param name="MembersVisible">Whether the caller sees the members.</param>
        /// <param name="Cap">The most members listed.</param>
        /// <param name="Admitted">The row under the caller's Share decision; null when the decision is denied.</param>
        /// <param name="MainRead">The row's group statement.</param>
        /// <param name="ThroughRead">The rows seen through's group statement; null for none.</param>
        /// <param name="Common">The members, the audience size and the listed users.</param>
        /// <param name="Readers">Per data securable, its readers' statement; null when a criterion lets everyone read it.</param>
        /// <param name="Users">The users' projections.</param>
        /// <param name="UserName">The user entity's name.</param>
        /// <param name="UserColumns">The users' projection.</param>
        /// <param name="Roles">The roles' projections.</param>
        /// <param name="RoleName">The role entity's name.</param>
        /// <param name="RoleColumns">The roles' projection.</param>
        /// <param name="ThroughRows">Adds the rows seen through the caller may read to the related entities; null when it may read none.</param>
        private sealed record First(
            RequestContext Context,
            QueryexSchemaMap Map,
            Group Main,
            Group? Through,
            IReadOnlyList<DataTarget> Data,
            bool MembersVisible,
            int Cap,
            BatchResult<RowPage>? Admitted,
            GroupRead MainRead,
            GroupRead? ThroughRead,
            BatchResult<RawResult> Common,
            IReadOnlyList<BatchResult<RawResult>?> Readers,
            BatchResult<EntityQueryResult<TUser>> Users,
            string UserName,
            IReadOnlyList<string> UserColumns,
            BatchResult<EntityQueryResult<TRole>> Roles,
            string RoleName,
            IReadOnlyList<string> RoleColumns,
            Func<IReadOnlySet<int>?, RelatedEntities, int>? ThroughRows);

        /// <summary>What the first round trip read.</summary>
        /// <param name="First">The composed plan.</param>
        /// <param name="Main">The row's facts.</param>
        /// <param name="Through">The rows seen through's facts; null for none.</param>
        /// <param name="Pending">The filtered permissions to evaluate.</param>
        /// <param name="MembersByRole">The current audience members of each active role that is not public and that a grant names.</param>
        /// <param name="Audience">The audience size.</param>
        /// <param name="Listed">The first audience users by name, when a grant may reach everyone and the caller sees the members.</param>
        /// <param name="Readers">Who reads each data securable.</param>
        private sealed record Read(
            First First,
            GroupFacts Main,
            GroupFacts? Through,
            IReadOnlyList<Pending> Pending,
            IReadOnlyDictionary<int, IReadOnlyList<int>> MembersByRole,
            int Audience,
            IReadOnlyList<int> Listed,
            IReadOnlyList<DataReaders> Readers);
    }
}
