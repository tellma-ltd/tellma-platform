// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Access
{
    // The rules are spec 0014 §3.1, §3.6, §3.9 and §8.4 (UserAccessRules), with the escalation test
    // of §8.4 applied to the roles a membership grants. The guards of §8.3 back them inside the
    // transaction; these give the field-level message first.
    /// <summary>
    ///     The access rules of the users' stack: the system user is never written; a user's kind and
    ///     email agree; an invited user's email is locked; the memberships' windows are well formed,
    ///     do not overlap and do not exceed the cap; no caller removes, ends or defers their own
    ///     administrator membership, deactivates or deletes themselves, or grants a role holding a
    ///     permission they lack unrestricted; only a new user is deleted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rule loads the permissions of the roles the payload's memberships name, with the
    ///         save's first round, unless the caller holds every pair unrestricted. The caller's own
    ///         set is in memory.
    ///     </para>
    ///     <para>
    ///         A new user's email is where the invitation goes, so changing it hands the user's
    ///         memberships to whoever holds the new address: every membership that has not ended is
    ///         then tested as granted. When the payload omits the memberships, the stored ones stay,
    ///         and the pairs of their roles load in a second round.
    ///     </para>
    ///     <para>
    ///         The rule validates deletes and the built-in actions too, so they run the pipeline's
    ///         general path, with the targets loaded in the first round.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    /// <param name="evaluator">The caller's access evaluator, whose set the escalation test reads.</param>
    /// <param name="registry">The securable registry, which resolves a permission's resource as registered.</param>
    /// <param name="options">The access options, which cap the memberships.</param>
    /// <param name="time">The host's clock, which the windows are judged against.</param>
    /// <param name="metrics">The access instruments.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    internal sealed class UserAccessRules<TUser>(
        IAccessEvaluator evaluator,
        ISecurableRegistry registry,
        IOptions<AccessOptions> options,
        TimeProvider time,
        AccessMetrics metrics,
        ILoggerFactory loggerFactory) : IEntityValidator<TUser>
        where TUser : User
    {
        /// <summary>The built-in action that deactivates a user.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>What a membership's escalation test loads of each role: its pairs.</summary>
        private const string PermissionSelect = nameof(Permission.Resource) + "," + nameof(Permission.Action);

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            DateTimeOffset now = time.GetUtcNow();

            // --- The caller's set, in memory; the permissions of every role a membership names, with the first round ---
            UserAccess caller = await evaluator.GetAccessAsync(context.CancellationToken).ConfigureAwait(false);
            bool exempt = AccessDelegation.IsExempt(caller, now);
            int[] roleIds = exempt
                ? []
                : [.. context.Entities.SelectMany(static user => user.RoleMemberships ?? []).Select(static membership => membership.RoleId).Distinct().Order()];
            ContextRef<IReadOnlyDictionary<int, IReadOnlyList<Permission>>>? permissions = roleIds.Length > 0
                ? context.Loader.ByParentIds<Permission, int>(roleIds, PermissionSelect)
                : null;

            // --- The before images and the permissions ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            IReadOnlyDictionary<int, IReadOnlyList<Permission>> granted = permissions?.Value ?? new Dictionary<int, IReadOnlyList<Permission>>();

            // --- A new user's changed email, with the memberships left out: their stored pairs, in a second round ---
            // Which emails change is known only once the before images loaded.
            int[] kept = exempt
                ? []
                : [.. Enumerable.Range(0, context.Entities.Count)
                    .Where(index => context.Entities[index].RoleMemberships is null && Retargets(context, index))
                    .Select(index => context.Entities[index].Id)
                    .Distinct()
                    .Order()];
            ContextRef<RawResult>? standing = kept.Length > 0 ? StandingPairs(context.Loader, kept, now) : null;
            if (standing is not null)
            {
                await context.Loader.LoadAsync().ConfigureAwait(false);
            }

            // --- Every row's rules ---
            ILookup<int, (int Role, string Resource, string Action)> standingByUser = Pairs(standing?.Value);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                CheckRow(context, index, caller, exempt, granted, standingByUser, now);
            }
        }

        /// <inheritdoc />
        public async Task ValidateDeleteAsync(DeleteContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The targets load with the first round ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            IReadOnlyList<int>? ids = (context as DeleteContext<TUser, int>)?.Ids;
            int? caller = context.Context.UserId;
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TUser user = context.Entities[index];
                ValidationPath at = AccessRefusals.Target(ids, user.Id, index);
                if (user.Id == WellKnownIds.SystemUserId)
                {
                    context.Errors.Add(at, AccessCodes.SystemUserImmutable);
                }
                else if (user.Id == caller)
                {
                    context.Errors.Add(at, AccessCodes.CannotDeleteSelf);
                    AccessRefusals.Guard(metrics, _logger, context.Context, AccessTelemetryNames.GuardSelfDeletion, AccessCodes.CannotDeleteSelf);
                }
                else if (user.State != UserState.New || user.Kind == UserKind.System)
                {
                    // Every user that ever acted is referenced by the audit columns: it is deactivated instead.
                    context.Errors.Add(at, AccessCodes.OnlyNewUsersDeletable);
                }
            }
        }

        /// <inheritdoc />
        public async Task ValidateActionAsync(string action, ActionContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);

            // --- The targets load with the first round ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            IReadOnlyList<int>? ids = (context as ActionContext<TUser, int>)?.Ids;
            int? caller = context.Context.UserId;
            for (int index = 0; index < context.Entities.Count; index++)
            {
                TUser user = context.Entities[index];
                ValidationPath at = AccessRefusals.Target(ids, user.Id, index);
                if (user.Id == WellKnownIds.SystemUserId)
                {
                    // The system user accepts no action at all, built-in or custom.
                    context.Errors.Add(at, AccessCodes.SystemUserImmutable);
                }
                else if (user.Id == caller && string.Equals(action, Deactivate, StringComparison.Ordinal))
                {
                    context.Errors.Add(at, AccessCodes.CannotDeactivateSelf);
                    AccessRefusals.Guard(metrics, _logger, context.Context, AccessTelemetryNames.GuardSelfDeactivation, AccessCodes.CannotDeactivateSelf);
                }
            }
        }

        /// <summary>Whether one membership window grants for longer than another: an earlier start, or a later end.</summary>
        /// <param name="before">The stored window.</param>
        /// <param name="after">The saved window.</param>
        /// <returns>True when the saved window reaches outside the stored one.</returns>
        internal static bool Widens(RoleMembership before, RoleMembership after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);

            // A null start is since always and a null end is never, so each reaches past any instant.
            bool earlier = after.StartsAt is DateTimeOffset start ? before.StartsAt is DateTimeOffset was && start < was : before.StartsAt is not null;
            bool later = after.EndsAt is DateTimeOffset end ? before.EndsAt is DateTimeOffset ended && end > ended : before.EndsAt is not null;
            return earlier || later;
        }

        /// <summary>Whether two membership windows share an instant; a null bound is unbounded, and the end is exclusive.</summary>
        /// <param name="left">One membership.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they overlap.</returns>
        internal static bool Overlap(RoleMembership left, RoleMembership right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            bool leftStartsFirst = left.StartsAt is not DateTimeOffset leftStart || right.EndsAt is not DateTimeOffset rightEnd || leftStart < rightEnd;
            bool rightStartsFirst = right.StartsAt is not DateTimeOffset rightStart || left.EndsAt is not DateTimeOffset leftEnd || rightStart < leftEnd;
            return leftStartsFirst && rightStartsFirst;
        }

        /// <summary>
        ///     Whether a payload row changes the email of a stored user who is still new, so the
        ///     invitation, and the user's memberships with it, would go to another address.
        /// </summary>
        /// <param name="context">The save's context, after its first round.</param>
        /// <param name="index">The row.</param>
        /// <returns>True when the stored user is new and the email differs.</returns>
        private static bool Retargets(SaveContext<TUser> context, int index)
        {
            return context.Before(index) is { State: UserState.New } before
                && !string.Equals(context.Entities[index].Email, before.Email, StringComparison.Ordinal);
        }

        /// <summary>The pairs of every role a user holds a membership of that has not ended, for each of the users.</summary>
        /// <param name="loader">The save's loader.</param>
        /// <param name="userIds">The users, distinct and ordered.</param>
        /// <param name="now">The save's instant, which a membership must not have ended by.</param>
        /// <returns>The pending load: one row per user, role and pair.</returns>
        private static ContextRef<RawResult> StandingPairs(IContextLoader loader, int[] userIds, DateTimeOffset now)
        {
            List<IdList> keys = [.. userIds.Select(static id => new IdList { Id = id })];
            return loader.Sql(
                $"""
                SELECT M.[UserId], M.[RoleId], P.[Resource], P.[Action]
                  FROM [core].[RoleMemberships] AS M JOIN [core].[Permissions] AS P ON P.[RoleId] = M.[RoleId]
                 WHERE M.[UserId] IN (SELECT K.[Id] FROM {keys} AS K) AND (M.[EndsAt] IS NULL OR M.[EndsAt] > {now});
                """,
                new SqlOptions(resultSets: 1));
        }

        /// <summary>The pairs a load of <see cref="StandingPairs" /> returned, by user.</summary>
        /// <param name="result">The load's result; null when nothing was loaded.</param>
        /// <returns>The roles' pairs, by user.</returns>
        private static ILookup<int, (int Role, string Resource, string Action)> Pairs(RawResult? result)
        {
            if (result is null)
            {
                return Array.Empty<int>().ToLookup(static user => user, static _ => (0, string.Empty, string.Empty));
            }

            QueryRowSet set = result.ResultSets[0];
            int[] users = (int[])set.GetBuffer(0);
            int[] roles = (int[])set.GetBuffer(1);
            string?[] resources = (string?[])set.GetBuffer(2);
            string?[] actions = (string?[])set.GetBuffer(3);
            return Enumerable.Range(0, set.RowCount).ToLookup(row => users[row], row => (roles[row], resources[row] ?? string.Empty, actions[row] ?? string.Empty));
        }

        /// <summary>The rules of one payload row, its before image and its memberships.</summary>
        /// <param name="context">The save's context, after its first round.</param>
        /// <param name="index">The row.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="exempt">Whether the caller may grant every role.</param>
        /// <param name="permissions">The loaded permissions of the roles the memberships name, by role.</param>
        /// <param name="standing">The roles' pairs of the stored memberships of each new user whose email changes and whose payload omits them.</param>
        /// <param name="now">The save's instant.</param>
        private void CheckRow(
            SaveContext<TUser> context,
            int index,
            UserAccess caller,
            bool exempt,
            IReadOnlyDictionary<int, IReadOnlyList<Permission>> permissions,
            ILookup<int, (int Role, string Resource, string Action)> standing,
            DateTimeOffset now)
        {
            TUser user = context.Entities[index];
            ValidationPath row = ValidationPath.Root.Index(index);

            // --- The system user accepts no edit, so nothing more is said of it ---
            if (user.Id == WellKnownIds.SystemUserId)
            {
                context.Errors.Add(row, AccessCodes.SystemUserImmutable);
                return;
            }

            // --- The kind and the email agree (CK_Users_KindEmail): an individual's is required, any other's refused ---
            if (user.Kind == UserKind.System)
            {
                context.Errors.Add(index, static row => row.Kind, AccessCodes.KindNotAllowed);
            }
            else if (user.Kind == UserKind.Individual && string.IsNullOrWhiteSpace(user.Email))
            {
                context.Errors.Add(index, static row => row.Email, ValidationCodes.Required, AccessRefusals.Arguments(("property", nameof(User.Email))));
            }
            else if (user.Kind != UserKind.Individual && user.Email is not null)
            {
                context.Errors.Add(index, static row => row.Email, AccessCodes.EmailNotAllowed);
            }

            // --- The email is the identity server's key once the user was invited ---
            TUser? before = context.Before(index);
            if (before is not null && before.State != UserState.New && !string.Equals(user.Email, before.Email, StringComparison.Ordinal))
            {
                context.Errors.Add(index, static row => row.Email, AccessCodes.EmailLockedAfterInvite);
            }

            // --- The memberships, when the payload carries them: the whole list, synchronised under the user ---
            bool retargets = Retargets(context, index);
            if (user.RoleMemberships is IReadOnlyList<RoleMembership> memberships)
            {
                CheckMemberships(context, index, user, before?.RoleMemberships ?? [], memberships, caller, exempt, permissions, retargets, now);
            }
            else if (retargets && !exempt)
            {
                // --- The stored memberships, which the changed email hands over as they stand ---
                foreach (IGrouping<int, (int Role, string Resource, string Action)> role in standing[user.Id].GroupBy(static pair => pair.Role))
                {
                    if (FirstUncovered(role.Select(static pair => (pair.Resource, pair.Action)), caller, now) is (string resource, string action))
                    {
                        context.Errors.Add(index, static row => row.Email, AccessCodes.MembershipEscalation, AccessRefusals.Arguments(("resource", resource), ("action", action)));
                        AccessRefusals.Escalation(metrics, _logger, context.Context, role.Key, resource, action, AccessCodes.MembershipEscalation);
                        break;
                    }
                }
            }
        }

        /// <summary>The rules of one user's memberships, against their stored ones.</summary>
        /// <param name="context">The save's context.</param>
        /// <param name="index">The row.</param>
        /// <param name="user">The payload row.</param>
        /// <param name="stored">The stored memberships; empty for a new user.</param>
        /// <param name="memberships">The saved memberships.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="exempt">Whether the caller may grant every role.</param>
        /// <param name="permissions">The loaded permissions of the roles the memberships name, by role.</param>
        /// <param name="retargets">Whether the save changes a new user's email, so every membership that has not ended is a grant.</param>
        /// <param name="now">The save's instant.</param>
        private void CheckMemberships(
            SaveContext<TUser> context,
            int index,
            TUser user,
            IReadOnlyList<RoleMembership> stored,
            IReadOnlyList<RoleMembership> memberships,
            UserAccess caller,
            bool exempt,
            IReadOnlyDictionary<int, IReadOnlyList<Permission>> permissions,
            bool retargets,
            DateTimeOffset now)
        {
            ValidationPath list = ValidationPath.Root.Index(index).Property(nameof(User.RoleMemberships));
            var storedById = stored.Where(static membership => membership.Id > 0).ToDictionary(static membership => membership.Id);
            bool[] malformed = new bool[memberships.Count];

            // --- Each window: well formed, and not ended already unless saved as it was ---
            for (int position = 0; position < memberships.Count; position++)
            {
                RoleMembership membership = memberships[position];
                ValidationPath at = list.Index(position);
                RoleMembership? was = Stored(storedById, membership);
                bool windowChanged = was is null || was.RoleId != membership.RoleId || was.StartsAt != membership.StartsAt || was.EndsAt != membership.EndsAt;
                if (membership.StartsAt is DateTimeOffset start && membership.EndsAt is DateTimeOffset end && end <= start)
                {
                    // Reported before CK_RoleMemberships_Window would refuse it.
                    context.Errors.Add(at.Property(nameof(RoleMembership.EndsAt)), AccessCodes.MembershipEndsBeforeStart);
                    malformed[position] = true;
                }
                else if (windowChanged && membership.EndsAt is DateTimeOffset ended && ended <= now)
                {
                    // An ended row saved as it was passes, so its notes stay editable.
                    context.Errors.Add(at.Property(nameof(RoleMembership.EndsAt)), AccessCodes.MembershipEndsInPast);
                }
            }

            // --- One role's windows never overlap; a permanent one overlaps every other ---
            for (int later = 1; later < memberships.Count; later++)
            {
                for (int earlier = 0; earlier < later; earlier++)
                {
                    if (!malformed[later] && !malformed[earlier]
                        && memberships[earlier].RoleId == memberships[later].RoleId
                        && Overlap(memberships[earlier], memberships[later]))
                    {
                        context.Errors.Add(list.Index(later).Property(nameof(RoleMembership.StartsAt)), AccessCodes.MembershipOverlap);
                        break;
                    }
                }
            }

            // --- At most so many memberships that have not ended ---
            int max = options.Value.MaxRolesPerUser;
            if (memberships.Count(membership => membership.EndsAt is not DateTimeOffset end || end > now) > max)
            {
                context.Errors.Add(list, AccessCodes.TooManyRoles, AccessRefusals.Arguments(("max", max)));
            }

            // --- The caller's own administrator membership stays as it is ---
            if (user.Id == context.Context.UserId)
            {
                CheckOwnAdministratorMembership(context, list, stored, memberships, now);
            }

            // --- A granted or widened membership hands out at most what the caller holds ---
            if (!exempt)
            {
                CheckEscalation(context, list, storedById, memberships, caller, permissions, retargets, now);
            }
        }

        /// <summary>
        ///     Refuses the caller's removal of their own administrator membership: deleting it, moving
        ///     it to another role, giving it an end or moving its end, or moving its start past now.
        ///     Clearing its end passes. A membership that ended already grants nothing, so it is free.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <param name="list">The path of the memberships.</param>
        /// <param name="stored">The caller's stored memberships.</param>
        /// <param name="memberships">The saved memberships.</param>
        /// <param name="now">The save's instant.</param>
        private void CheckOwnAdministratorMembership(
            SaveContext<TUser> context,
            ValidationPath list,
            IReadOnlyList<RoleMembership> stored,
            IReadOnlyList<RoleMembership> memberships,
            DateTimeOffset now)
        {
            foreach (RoleMembership was in stored.Where(membership => membership.RoleId == WellKnownIds.AdministratorRoleId && (membership.EndsAt is not DateTimeOffset end || end > now)))
            {
                int position = IndexOf(memberships, was.Id);
                ValidationPath? refused = position < 0 ? list : Removal(list.Index(position), was, memberships[position], now);
                if (refused is not null)
                {
                    context.Errors.Add(refused, AccessCodes.CannotRemoveOwnAdministratorMembership);
                    AccessRefusals.Guard(metrics, _logger, context.Context, AccessTelemetryNames.GuardOwnAdministratorMembership, AccessCodes.CannotRemoveOwnAdministratorMembership);
                }
            }
        }

        /// <summary>
        ///     Where a saved administrator membership takes the administrator role away from its
        ///     holder: moved to another role, given an end or its end moved, or its start moved past now.
        /// </summary>
        /// <param name="at">The saved membership's path.</param>
        /// <param name="was">The stored membership.</param>
        /// <param name="saved">The saved membership.</param>
        /// <param name="now">The save's instant.</param>
        /// <returns>The path of the property that takes it away; null when the membership keeps it.</returns>
        private static ValidationPath? Removal(ValidationPath at, RoleMembership was, RoleMembership saved, DateTimeOffset now)
        {
            return saved.RoleId != WellKnownIds.AdministratorRoleId ? at.Property(nameof(RoleMembership.RoleId))
                : saved.EndsAt is not null && saved.EndsAt != was.EndsAt ? at.Property(nameof(RoleMembership.EndsAt))
                : saved.StartsAt != was.StartsAt && saved.StartsAt is DateTimeOffset start && start > now ? at.Property(nameof(RoleMembership.StartsAt))
                : null;
        }

        /// <summary>
        ///     Refuses a membership granted to a role, or widened, when the role holds a permission the
        ///     caller lacks unrestricted. When the save hands the user to another address, every
        ///     membership that has not ended counts as granted.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <param name="list">The path of the memberships.</param>
        /// <param name="storedById">The stored memberships, by id.</param>
        /// <param name="memberships">The saved memberships.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="permissions">The loaded permissions of the roles the memberships name, by role.</param>
        /// <param name="retargets">Whether the save changes a new user's email.</param>
        /// <param name="now">The save's instant.</param>
        /// <exception cref="InvalidOperationException">The permissions of a role a membership grants were not loaded.</exception>
        private void CheckEscalation(
            SaveContext<TUser> context,
            ValidationPath list,
            Dictionary<int, RoleMembership> storedById,
            IReadOnlyList<RoleMembership> memberships,
            UserAccess caller,
            IReadOnlyDictionary<int, IReadOnlyList<Permission>> permissions,
            bool retargets,
            DateTimeOffset now)
        {
            for (int position = 0; position < memberships.Count; position++)
            {
                RoleMembership membership = memberships[position];
                RoleMembership? was = Stored(storedById, membership);
                bool grants = retargets
                    ? membership.EndsAt is not DateTimeOffset end || end > now
                    : was is null || was.RoleId != membership.RoleId || Widens(was, membership);

                if (!grants)
                {
                    continue;
                }

                // Every role a membership names was loaded, and the loader answers every requested
                // role (one that does not exist under an empty list, the reference check's to
                // report), so an absent role is a defect, refused rather than passed.
                if (!permissions.TryGetValue(membership.RoleId, out IReadOnlyList<Permission>? held))
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"The permissions of role {membership.RoleId} were not loaded, so the membership granting it cannot be tested for escalation."));
                }

                if (FirstUncovered(held.Select(static permission => (permission.Resource, permission.Action)), caller, now) is (string resource, string action))
                {
                    context.Errors.Add(
                        list.Index(position).Property(nameof(RoleMembership.RoleId)),
                        AccessCodes.MembershipEscalation,
                        AccessRefusals.Arguments(("resource", resource), ("action", action)));
                    AccessRefusals.Escalation(metrics, _logger, context.Context, membership.RoleId, resource, action, AccessCodes.MembershipEscalation);
                }
            }
        }

        /// <summary>The first of a role's pairs the caller lacks unrestricted, with the resource resolved as registered.</summary>
        /// <param name="pairs">The pairs, as stored.</param>
        /// <param name="caller">The caller's set.</param>
        /// <param name="now">The save's instant.</param>
        /// <returns>The pair as stored; null when the caller holds every one.</returns>
        private (string Resource, string Action)? FirstUncovered(IEnumerable<(string Resource, string Action)> pairs, UserAccess caller, DateTimeOffset now)
        {
            foreach ((string stored, string action) in pairs)
            {
                string resource = stored == CoreResources.Wildcard ? CoreResources.Wildcard : registry.FindResource(stored) ?? stored;
                if (!AccessDelegation.Covers(caller, resource, action, now))
                {
                    return (stored, action);
                }
            }

            return null;
        }

        /// <summary>The stored membership a saved one updates; null for a new one.</summary>
        /// <param name="storedById">The stored memberships, by id.</param>
        /// <param name="membership">The saved membership.</param>
        /// <returns>The stored membership.</returns>
        private static RoleMembership? Stored(Dictionary<int, RoleMembership> storedById, RoleMembership membership)
        {
            return membership.Id > 0 && storedById.TryGetValue(membership.Id, out RoleMembership? was) ? was : null;
        }

        /// <summary>The position of a membership id among the saved memberships.</summary>
        /// <param name="memberships">The saved memberships.</param>
        /// <param name="id">The id.</param>
        /// <returns>The position, or -1.</returns>
        private static int IndexOf(IReadOnlyList<RoleMembership> memberships, int id)
        {
            for (int position = 0; position < memberships.Count; position++)
            {
                if (memberships[position].Id == id)
                {
                    return position;
                }
            }

            return -1;
        }
    }
}
