// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Notifications;
using Tellma.Core.Queryex;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §5.4 and §7. Each command runs as a task of its action's validation rounds: it queues
    // the targets' grants, the grantees and the sharer beside the targets in the first round, judges
    // the arguments against them, and composes its writes onto the action's persist batch. The grants
    // merge, never replace, so two people sharing at once never overwrite each other: a removal is
    // one DELETE, and the grants set are an UPDATE of the access that changed and an INSERT of the
    // grants that are new, each over one table-valued parameter (TELLMA0003 forbids MERGE, so the
    // spec's MERGE is these two statements), followed by an assertion of the grant caps inside the
    // transaction. Share and publish never stamp the row; transfer writes the owner through the
    // action's update, which stamps. A new user grant notifies its grantee, and removing one
    // withdraws the notice.
    /// <summary>The sharing commands of a shareable entity: its <c>share</c>, <c>publish</c> and <c>transfer</c> actions' work.</summary>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    /// <typeparam name="TShare">The share entity.</typeparam>
    internal sealed class ShareCommands<TEntity, TShare> : IShareCommands<TEntity>
        where TEntity : TopLevelEntity, IShareable
        where TShare : Entity<int>, IShareRow
    {
        /// <summary>The path of an action's arguments, under which the arguments' errors sit.</summary>
        private const string ArgumentsProperty = "Arguments";

        /// <summary>The most ids one filter of the loads lists, under the query language's list ceiling.</summary>
        private const int IdsPerFilter = 500;

        /// <summary>The select of a grant the commands judge against.</summary>
        private static readonly string GrantSelect = string.Join(
            ",", nameof(Entity<>.Id), nameof(IShareRow.UserId), nameof(IShareRow.RoleId), nameof(IShareRow.Access));

        /// <summary>The select of a grantee user, an owner, and the sharer, in a tenant of one content language.</summary>
        private static readonly string UserSelect = string.Join(
            ",", nameof(User.Id), nameof(User.Kind), nameof(User.IsActive), nameof(User.Name));

        /// <summary>That select in a tenant of two content languages, whose query schema declares the secondary twin.</summary>
        private static readonly string UserSelect2 = string.Join(",", UserSelect, nameof(User.Name2));

        /// <summary>That select in a tenant of three content languages, whose query schema declares both twins.</summary>
        private static readonly string UserSelect3 = string.Join(",", UserSelect2, nameof(User.Name3));

        /// <summary>The select of a grantee role.</summary>
        private static readonly string RoleSelect = string.Join(",", nameof(Role.Id), nameof(Role.IsActive));

        /// <summary>The shareable's shape in the model.</summary>
        private readonly ShareableShape _shape;

        /// <summary>The share table, the one table the commands' statements write.</summary>
        private readonly FrozenSet<TableName> _writes;

        /// <summary>The share table, as a statement's identifier.</summary>
        private readonly SqlIdentifier _table;

        /// <summary>The share table's sequence, as a statement's identifier.</summary>
        private readonly SqlIdentifier _sequence;

        /// <summary>The share table's column of the shared row, as a statement's identifier.</summary>
        private readonly SqlIdentifier _row;

        /// <summary>The notification type a new user grant raises.</summary>
        private readonly string _noticeType;

        /// <summary>The most recipients one notification request names.</summary>
        private readonly int _recipientsPerRequest;

        /// <summary>Creates the commands.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="notifications">The notifications' options, whose recipient cap the notices respect.</param>
        /// <param name="notice">The notification type a new user grant raises.</param>
        public ShareCommands(IEntityMetadataProvider metadata, IOptions<NotificationsOptions> notifications, ShareableNotice notice)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(notifications);
            ArgumentNullException.ThrowIfNull(notice);
            _shape = ShareableShape.Of(metadata, typeof(TEntity), typeof(TShare));
            TableName table = _shape.Share.Table;
            _writes = FrozenSet.Create(table);
            _table = new SqlIdentifier($"{table.Schema}.{table.Name}");
            _sequence = new SqlIdentifier($"{table.Schema}.sq_{table.Name}");
            _row = new SqlIdentifier(_shape.Row.ForeignKey.Column);
            _noticeType = notice.Type;
            _recipientsPerRequest = notifications.Value.MaxRecipientsPerRequest;
        }

        /// <inheritdoc />
        public async Task ShareAsync(ActionContext<TEntity, int> context, ShareArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            IReadOnlyList<UserGrant?> set = arguments.Set ?? [];
            IReadOnlyList<int> remove = arguments.Remove ?? [];
            ValidationPath root = ValidationPath.Root.Property(ArgumentsProperty);

            // --- The arguments' own shape: no hole, no grantee twice ---
            bool valid = CheckGrantees(context.Errors, root, nameof(ShareArguments.Set), nameof(UserGrant.UserId), [.. set.Select(static grant => grant?.UserId)], nameof(ShareArguments.Remove), remove);

            // --- Beside the targets: their grants, the grantees and the sharer ---
            int sharer = Sharer(context);
            int[] granted = [.. set.OfType<UserGrant>().Select(static grant => grant.UserId).Distinct()];
            List<ContextRef<IReadOnlyList<TShare>>> existing = Grants(context);
            ContextRef<IReadOnlyDictionary<int, User>> users = context.Loader.ByIds<User, int>([.. granted.Append(sharer).Distinct().Order()], UsersOf(context));
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- Each grantee an active individual, and no target's owner ---
            for (int index = 0; index < set.Count; index++)
            {
                if (set[index] is not UserGrant grant)
                {
                    continue;
                }

                ValidationPath path = root.Property(nameof(ShareArguments.Set)).Index(index).Property(nameof(UserGrant.UserId));
                if (!users.Value.TryGetValue(grant.UserId, out User? user) || user is not { Kind: UserKind.Individual, IsActive: true })
                {
                    context.Errors.Add(path, SharingValidationCodes.UserInvalid, Arguments(("userId", grant.UserId)));
                    valid = false;
                }
                else if (context.Entities.Any(target => target.OwnerId == grant.UserId))
                {
                    context.Errors.Add(path, SharingValidationCodes.OwnerIsImplicit, Arguments(("userId", grant.UserId)));
                    valid = false;
                }
            }

            // --- Each target within the user grants' cap once the grants merge ---
            Dictionary<int, TShare[]> before = ByRow(existing);
            HashSet<int> removed = [.. remove];
            foreach (TEntity target in context.Entities)
            {
                IEnumerable<int> after = Held(before, target.Id, static grant => grant.UserId).Where(user => !removed.Contains(user)).Union(granted);
                if (after.Count() > ShareableNames.MaxUserGrants)
                {
                    context.Errors.Add(root.Property(nameof(ShareArguments.Set)), SharingValidationCodes.TooManyGrants, Arguments(("kind", "user"), ("max", ShareableNames.MaxUserGrants)));
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                return;
            }

            // --- The writes: the removals, then the grants set ---
            int[] targets = Targets(context);
            Remove(context, targets, remove, users: true);
            Merge(context, targets, [.. set.OfType<UserGrant>().Select(static grant => (grant.UserId, (int?)null, grant.Access))], sharer);

            // --- The notices: each new grantee of each target is told, each removed grantee's notice withdrawn ---
            User? actor = users.Value.GetValueOrDefault(sharer);
            foreach (TEntity target in context.Entities)
            {
                HashSet<int> held = [.. Held(before, target.Id, static grant => grant.UserId)];
                int[] added = [.. granted.Where(user => !held.Contains(user)).Order()];
                Notify(context, target, added, sharer, actor);

                int[] withdrawn = [.. remove.Where(held.Contains).Distinct().Order()];
                foreach (int[] recipients in withdrawn.Chunk(_recipientsPerRequest))
                {
                    context.Withdraw(new WithdrawRequest(_shape.DedupKey(target.Id), recipients));
                }
            }
        }

        /// <inheritdoc />
        public async Task PublishAsync(ActionContext<TEntity, int> context, PublishArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            IReadOnlyList<RoleGrant?> set = arguments.Set ?? [];
            IReadOnlyList<int> remove = arguments.Remove ?? [];
            ValidationPath root = ValidationPath.Root.Property(ArgumentsProperty);

            // --- The arguments' own shape: no hole, no grantee twice ---
            bool valid = CheckGrantees(context.Errors, root, nameof(PublishArguments.Set), nameof(RoleGrant.RoleId), [.. set.Select(static grant => grant?.RoleId)], nameof(PublishArguments.Remove), remove);

            // --- Beside the targets: their grants and the grantee roles ---
            int sharer = Sharer(context);
            int[] granted = [.. set.OfType<RoleGrant>().Select(static grant => grant.RoleId).Distinct()];
            List<ContextRef<IReadOnlyList<TShare>>> existing = Grants(context);
            ContextRef<IReadOnlyDictionary<int, Role>> roles = context.Loader.ByIds<Role, int>([.. granted.Order()], RoleSelect);
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- Each grantee an active role ---
            for (int index = 0; index < set.Count; index++)
            {
                if (set[index] is RoleGrant grant && (!roles.Value.TryGetValue(grant.RoleId, out Role? role) || !role.IsActive))
                {
                    context.Errors.Add(root.Property(nameof(PublishArguments.Set)).Index(index).Property(nameof(RoleGrant.RoleId)), SharingValidationCodes.RoleInvalid, Arguments(("roleId", grant.RoleId)));
                    valid = false;
                }
            }

            // --- Each target within the role grants' cap once the grants merge ---
            Dictionary<int, TShare[]> before = ByRow(existing);
            HashSet<int> removed = [.. remove];
            foreach (TEntity target in context.Entities)
            {
                IEnumerable<int> after = Held(before, target.Id, static grant => grant.RoleId).Where(role => !removed.Contains(role)).Union(granted);
                if (after.Count() > ShareableNames.MaxRoleGrants)
                {
                    context.Errors.Add(root.Property(nameof(PublishArguments.Set)), SharingValidationCodes.TooManyGrants, Arguments(("kind", "role"), ("max", ShareableNames.MaxRoleGrants)));
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                return;
            }

            // --- The writes: the removals, the Everyone grant's removal, then the grants set ---
            int[] targets = Targets(context);
            Remove(context, targets, remove, users: false);
            if (arguments.Everyone == false)
            {
                RemoveEveryone(context, targets);
            }

            List<(int? UserId, int? RoleId, ShareAccess Access)> grants = [.. set.OfType<RoleGrant>().Select(static grant => ((int?)null, (int?)grant.RoleId, grant.Access))];
            if (arguments.Everyone == true)
            {
                grants.Add((null, null, ShareAccess.View));
            }

            Merge(context, targets, grants, sharer);
        }

        /// <inheritdoc />
        public async Task TransferAsync(ActionContext<TEntity, int> context, TransferArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            int owner = arguments.OwnerId;

            // --- Beside the targets: the new owner, and the targets' grants ---
            List<ContextRef<IReadOnlyList<TShare>>> existing = Grants(context);
            ContextRef<IReadOnlyDictionary<int, User>> users = context.Loader.ByIds<User, int>([owner], UsersOf(context));
            await context.Loader.LoadAsync().ConfigureAwait(false);

            // --- The new owner, an active individual ---
            if (!users.Value.TryGetValue(owner, out User? user) || user is not { Kind: UserKind.Individual, IsActive: true })
            {
                context.Errors.Add(ValidationPath.Root.Property(ArgumentsProperty).Property(nameof(TransferArguments.OwnerId)), SharingValidationCodes.OwnerInvalid, Arguments(("userId", owner)));
                return;
            }

            // --- The owner changes, stamping each target; the new owner's own grant goes, with its notice ---
            context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(IShareable.OwnerId)] = owner });
            int[] targets = Targets(context);
            Remove(context, targets, [owner], users: true);
            Dictionary<int, TShare[]> before = ByRow(existing);
            foreach (TEntity target in context.Entities)
            {
                if (Held(before, target.Id, static grant => grant.UserId).Contains(owner))
                {
                    context.Withdraw(new WithdrawRequest(_shape.DedupKey(target.Id), [owner]));
                }
            }
        }

        /// <inheritdoc />
        public void WithdrawAll(DeletePersistContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // A delete's rows exist only as the batch's table of deleted ids, so one statement spells
            // each row's dedup key from them. A withdrawal needs no grant row: the cascade that
            // removes the grants may run before or after it in the batch.
            Notifier notifier = context.Notifier as Notifier
                ?? throw new NotSupportedException($"A delete withdraws its rows' share notices through the platform's notifier; the host registers {context.Notifier?.GetType().Name ?? "none"}.");
            notifier.WithdrawEach(context.Batch, _shape.DedupKeyPrefix, context.AffectedIds);
        }

        /// <summary>Refuses a hole among the grants set, a grantee set twice, and a grantee both set and removed.</summary>
        /// <param name="errors">The action's errors.</param>
        /// <param name="root">The arguments' path.</param>
        /// <param name="setProperty">The name of the grants set.</param>
        /// <param name="granteeProperty">The name of a grant's grantee.</param>
        /// <param name="set">The grantees set, in order; null for a hole.</param>
        /// <param name="removeProperty">The name of the grantees removed.</param>
        /// <param name="remove">The grantees removed, in order.</param>
        /// <returns>True when the arguments are sound.</returns>
        private static bool CheckGrantees(
            ValidationErrors errors, ValidationPath root, string setProperty, string granteeProperty, IReadOnlyList<int?> set, string removeProperty, IReadOnlyList<int> remove)
        {
            bool valid = true;
            HashSet<int> seen = [];
            for (int index = 0; index < set.Count; index++)
            {
                ValidationPath item = root.Property(setProperty).Index(index);
                if (set[index] is not int grantee)
                {
                    errors.Add(item, ValidationCodes.Required);
                    valid = false;
                }
                else if (!seen.Add(grantee))
                {
                    errors.Add(item.Property(granteeProperty), ValidationCodes.Unique);
                    valid = false;
                }
            }

            HashSet<int> removed = [];
            for (int index = 0; index < remove.Count; index++)
            {
                if (seen.Contains(remove[index]) || !removed.Add(remove[index]))
                {
                    errors.Add(root.Property(removeProperty).Index(index), ValidationCodes.Unique);
                    valid = false;
                }
            }

            return valid;
        }

        /// <summary>Queues the load of the targets' grants, in chunks under the query language's list ceiling.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The pending loads.</returns>
        private List<ContextRef<IReadOnlyList<TShare>>> Grants(ActionContext<TEntity, int> context)
        {
            string row = _shape.Row.ForeignKey.Name;
            return [.. context.Ids.Distinct().Order().Chunk(IdsPerFilter)
                .Select(chunk => context.Loader.Query<TShare>(In(row, chunk), select: $"{GrantSelect},{row}"))];
        }

        /// <summary>The loaded grants, by the row they belong to.</summary>
        /// <param name="loads">The loads.</param>
        /// <returns>The grants by row.</returns>
        private Dictionary<int, TShare[]> ByRow(List<ContextRef<IReadOnlyList<TShare>>> loads)
        {
            return loads.SelectMany(static load => load.Value)
                .GroupBy(grant => (int)_shape.Row.ForeignKey.Getter(grant)!)
                .ToDictionary(static group => group.Key, static group => group.ToArray());
        }

        /// <summary>The grantees of one kind a row's grants hold.</summary>
        /// <param name="grants">The grants by row.</param>
        /// <param name="row">The row.</param>
        /// <param name="grantee">Reads a grant's grantee of the kind; null for a grant of another kind.</param>
        /// <returns>The grantees.</returns>
        private static IEnumerable<int> Held(Dictionary<int, TShare[]> grants, int row, Func<TShare, int?> grantee)
        {
            return grants.TryGetValue(row, out TShare[]? held) ? held.Select(grantee).OfType<int>() : [];
        }

        /// <summary>Composes the removal of the targets' grants to some users or some roles.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="targets">The targets.</param>
        /// <param name="grantees">The users or roles whose grants go; nothing is composed for none.</param>
        /// <param name="users">True for user grants, false for role grants.</param>
        private void Remove(ActionContext<TEntity, int> context, int[] targets, IReadOnlyList<int> grantees, bool users)
        {
            if (grantees.Count == 0 || targets.Length == 0)
            {
                return;
            }

            List<IdList> rows = Ids(targets);
            List<IdList> removed = Ids(grantees);
            SqlIdentifier grantee = new(users ? nameof(IShareRow.UserId) : nameof(IShareRow.RoleId));
            _ = context.Batch.Sql(
                $"DELETE [s] FROM {_table} AS [s] WHERE [s].{_row} IN (SELECT [Id] FROM {rows}) AND [s].{grantee} IN (SELECT [Id] FROM {removed});",
                new SqlOptions(writes: _writes));
        }

        /// <summary>Composes the removal of the targets' Everyone grants.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="targets">The targets.</param>
        private void RemoveEveryone(ActionContext<TEntity, int> context, int[] targets)
        {
            if (targets.Length == 0)
            {
                return;
            }

            List<IdList> rows = Ids(targets);
            _ = context.Batch.Sql(
                $"DELETE [s] FROM {_table} AS [s] WHERE [s].{_row} IN (SELECT [Id] FROM {rows}) AND [s].[UserId] IS NULL AND [s].[RoleId] IS NULL;",
                new SqlOptions(writes: _writes));
        }

        /// <summary>
        ///     Composes the merge of grants into every target: the access of a grant that exists
        ///     changes, a grant that does not is inserted by the sharer at the request's time, and the
        ///     caps are asserted once the grants merged.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <param name="targets">The targets.</param>
        /// <param name="grants">The grants, each set on every target; nothing is composed for none.</param>
        /// <param name="sharer">The user who makes the new grants.</param>
        private void Merge(ActionContext<TEntity, int> context, int[] targets, List<(int? UserId, int? RoleId, ShareAccess Access)> grants, int sharer)
        {
            if (grants.Count == 0 || targets.Length == 0)
            {
                return;
            }

            // --- One row per target and grant ---
            List<ShareGrantList> rows = new(targets.Length * grants.Count);
            foreach (int target in targets)
            {
                foreach ((int? userId, int? roleId, ShareAccess access) in grants)
                {
                    rows.Add(new ShareGrantList { Index = rows.Count, RowId = target, UserId = userId, RoleId = roleId, Access = access.ToString() });
                }
            }

            // --- The existing grants' access, then the new grants, then the caps, in one statement ---
            // The grantee columns are compared null-safe: the Everyone grant names neither. The insert
            // takes range locks on the grants it looks for, so two merges of one grantee serialise.
            DateTimeOffset now = context.Context.Now;
            int maxUsers = ShareableNames.MaxUserGrants;
            int maxRoles = ShareableNames.MaxRoleGrants;
            _ = context.Batch.Sql(
                $"""
                UPDATE [s] SET [s].[Access] = [g].[Access]
                FROM {_table} AS [s] INNER JOIN {rows} AS [g] ON [g].[RowId] = [s].{_row}
                    AND ([s].[UserId] = [g].[UserId] OR ([s].[UserId] IS NULL AND [g].[UserId] IS NULL))
                    AND ([s].[RoleId] = [g].[RoleId] OR ([s].[RoleId] IS NULL AND [g].[RoleId] IS NULL))
                WHERE [s].[Access] <> [g].[Access];
                INSERT INTO {_table} ([Id], {_row}, [UserId], [RoleId], [Access], [SharedById], [SharedAt])
                SELECT NEXT VALUE FOR {_sequence}, [g].[RowId], [g].[UserId], [g].[RoleId], [g].[Access], {sharer}, {now}
                FROM {rows} AS [g]
                WHERE NOT EXISTS (
                    SELECT 1 FROM {_table} AS [s] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [s].{_row} = [g].[RowId]
                        AND ([s].[UserId] = [g].[UserId] OR ([s].[UserId] IS NULL AND [g].[UserId] IS NULL))
                        AND ([s].[RoleId] = [g].[RoleId] OR ([s].[RoleId] IS NULL AND [g].[RoleId] IS NULL)));
                IF EXISTS (
                    SELECT 1 FROM {_table} AS [s]
                    WHERE [s].{_row} IN (SELECT [g].[RowId] FROM {rows} AS [g])
                    GROUP BY [s].{_row}
                    HAVING COUNT([s].[UserId]) > {maxUsers} OR COUNT([s].[RoleId]) > {maxRoles})
                    THROW 50422, N'Sharing.TooManyGrants', 1;
                """,
                new SqlOptions(writes: _writes));
        }

        /// <summary>Appends the notice of one target to its new grantees, as many requests as the recipient cap needs.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="target">The target.</param>
        /// <param name="added">The users newly granted the target.</param>
        /// <param name="sharer">The user who shared it.</param>
        /// <param name="actor">The sharer's row, whose names the notice shows; null when it did not load.</param>
        private void Notify(ActionContext<TEntity, int> context, TEntity target, int[] added, int sharer, User? actor)
        {
            if (added.Length == 0)
            {
                return;
            }

            LocalizedText actorName = actor is null ? new LocalizedText(sharer.ToString(CultureInfo.InvariantCulture)) : LocalizedText.Names(actor);
            ShareNoticeArguments arguments = new(Title(target), actorName);
            foreach (int[] recipients in added.Chunk(_recipientsPerRequest))
            {
                context.Notify(new NotificationRequest(
                    _noticeType,
                    recipients,
                    arguments,
                    _shape.Entity.Name,
                    target.Id,
                    sharer,
                    _shape.DedupKey(target.Id),
                    DuplicatePolicy.Replace));
            }
        }

        /// <summary>A row's display name in each content language: its multilingual group, else the one text of its display property.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The texts.</returns>
        private LocalizedText Title(TEntity row)
        {
            DisplayNameMetadata display = _shape.Entity.DisplayName;
            string? text = Convert.ToString(display.Source.Getter(row), CultureInfo.InvariantCulture);
            return display.Group is not null && !string.IsNullOrEmpty(text)
                ? LocalizedText.Names(row, display.Source.Name)
                : new LocalizedText(string.IsNullOrEmpty(text) ? row.Id.ToString(CultureInfo.InvariantCulture) : text);
        }

        /// <summary>
        ///     The select of the users an action loads: the name twins the tenant's query schema
        ///     declares, since a twin of an unconfigured content language does not compile.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The select.</returns>
        private static string UsersOf(ActionContext<TEntity, int> context)
        {
            return (context.Context.TenantSettings?.Shape ?? MultilingualShape.Primary) switch
            {
                MultilingualShape.All => UserSelect3,
                MultilingualShape.PrimaryAndSecondary => UserSelect2,
                MultilingualShape.Primary => UserSelect,
                _ => UserSelect,
            };
        }

        /// <summary>The user the grants are made by: the system user in a system scope, else the caller.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The user.</returns>
        /// <exception cref="InvalidOperationException">The scope has no user.</exception>
        private static int Sharer(ActionContext<TEntity, int> context)
        {
            return context.Context.IsSystem ? WellKnownIds.SystemUserId : context.Context.UserId
                ?? throw new InvalidOperationException("A grant names the user who made it, and the scope has no user.");
        }

        /// <summary>The ids of the targets the first round loaded, in load order.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The ids.</returns>
        private static int[] Targets(ActionContext<TEntity, int> context)
        {
            return [.. context.Entities.Select(static target => target.Id)];
        }

        /// <summary>The rows of an id list.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The rows, each id once.</returns>
        private static List<IdList> Ids(IEnumerable<int> ids)
        {
            return [.. ids.Distinct().Order().Select(static id => new IdList { Id = id })];
        }

        /// <summary>The filter of the rows whose property is among some ids, spelled as constants.</summary>
        /// <param name="property">The property.</param>
        /// <param name="ids">The ids, at most the query language's list ceiling.</param>
        /// <returns>The filter.</returns>
        private static FilterTree In(string property, int[] ids)
        {
            StringBuilder text = new StringBuilder(property).Append(" in (");
            for (int index = 0; index < ids.Length; index++)
            {
                _ = text.Append(index == 0 ? string.Empty : ", ").Append(ids[index].ToString(CultureInfo.InvariantCulture));
            }

            return FilterTree.Leaf(text.Append(')').ToString());
        }

        /// <summary>Error arguments.</summary>
        /// <param name="pairs">The arguments' names and values.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] pairs)
        {
            return pairs.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);
        }
    }

    /// <summary>The notification type a shareable's new user grant raises, handed to its commands by the composition.</summary>
    /// <param name="Type">The type's key.</param>
    internal sealed record ShareableNotice(string Type);

    /// <summary>The arguments of a share notice, stored as <c>{ title, actorName }</c>.</summary>
    /// <param name="Title">The shared row's title in each content language.</param>
    /// <param name="ActorName">The name of the user who shared it, in each content language.</param>
    internal sealed record ShareNoticeArguments(LocalizedText Title, LocalizedText ActorName);
}
