// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §6.2 step 4: each user becomes one member with every reason and its CannotRead
    // indexes, the access is the highest of the reasons, the members are cut at the cap and every
    // count stays exact. A grant that reaches every audience user (the Everyone grant, a public
    // role) is never expanded into users: its counts come from the audience size and the readers'
    // count, and the members it brings are the first users by name, which the round trip read.
    /// <summary>
    ///     Composes an audience from the facts its round trips read: one grant per owner, user, role
    ///     and Everyone grant, per stored permission that reaches the row and per row seen through,
    ///     each with the audience users it reaches; the members with their reasons; and the counts of
    ///     those who cannot read each data securable. Pure: no I/O and no clock.
    /// </summary>
    internal static class AudienceComposer
    {
        /// <summary>Composes the audience.</summary>
        /// <param name="facts">What the round trips read, the permissions already narrowed to those that reach each row.</param>
        /// <param name="membersVisible">Whether the caller may read the roles, and so sees the members one by one.</param>
        /// <param name="cap">The most members listed.</param>
        /// <param name="byName">Orders users by display name in the reader's language, then by id.</param>
        /// <returns>The composition.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The cap is below one.</exception>
        public static AudienceComposition Compose(AudienceFacts facts, bool membersVisible, int cap, IComparer<int> byName)
        {
            ArgumentNullException.ThrowIfNull(facts);
            ArgumentNullException.ThrowIfNull(byName);
            ArgumentOutOfRangeException.ThrowIfLessThan(cap, 1);

            // --- Every grant of the row, then one per row seen through, each with what it reaches ---
            List<Entry> entries = [];
            foreach (AudienceSubject subject in facts.Subjects)
            {
                List<Entry> own = Entries(subject, facts.MembersByRole);
                if (subject.ThroughId is not int through)
                {
                    entries.AddRange(own);
                    continue;
                }

                var seen = Reach.Union(own.Select(static entry => entry.Reach));
                AudienceReason reason = new(AudienceReasonKind.Through, AudienceAccess.View, null, null, null, null, false, through);
                entries.Add(new Entry(AudienceReasonKind.Through, null, null, null, through, reason, seen));
            }

            // --- The audience: every grant's users, exact ---
            var audience = Reach.Union(entries.Select(static entry => entry.Reach));
            int total = audience.Count(facts.Audience);
            int[] cannotRead = [.. facts.Readers.Select(readers => Lacking(audience, readers, facts.Audience))];
            List<AudienceGrant> grants = [.. entries.Select(entry => new AudienceGrant(
                entry.Kind,
                entry.UserId,
                entry.RoleId,
                entry.PermissionId,
                entry.ThroughId,
                entry.Reason.Access,
                entry.Reach.Count(facts.Audience),
                [.. facts.Readers.Select(readers => Lacking(entry.Reach, readers, facts.Audience))],
                entry.DependsOnViewer))];

            // --- The users and roles the grants name, which the related projections carry ---
            HashSet<int> users = [.. entries.Select(static entry => entry.UserId).OfType<int>()];
            HashSet<int> roles = [.. entries.Select(static entry => entry.RoleId).OfType<int>()];
            if (!membersVisible)
            {
                return new AudienceComposition(total, false, cannotRead, null, grants, users, roles);
            }

            // --- The members: every user, or the first by name when a grant reaches everyone ---
            List<int> candidates = [.. audience.All ? facts.Listed.Distinct() : audience.Users];
            candidates.Sort(byName);
            List<AudienceMember> members = new(Math.Min(cap, candidates.Count));
            foreach (int userId in candidates.Take(cap))
            {
                AudienceReason[] reasons = [.. entries.Where(entry => entry.Reach.Contains(userId)).Select(static entry => entry.Reason)];
                AudienceAccess access = reasons.Length == 0 ? AudienceAccess.View : reasons.Max(static reason => reason.Access);
                int[] lacks = [.. Enumerable.Range(0, facts.Readers.Count).Where(index => !facts.Readers[index].CanRead(userId))];
                members.Add(new AudienceMember(userId, access, reasons, lacks));
                _ = users.Add(userId);
                users.UnionWith(reasons.Select(static reason => reason.SharedById).OfType<int>());
            }

            return new AudienceComposition(total, members.Count < total, cannotRead, members, grants, users, roles);
        }

        /// <summary>The grants of one row, in the share dialog's order, each with the audience users it reaches.</summary>
        /// <param name="subject">The row's facts.</param>
        /// <param name="membersByRole">The current audience members of each active role that is not public.</param>
        /// <returns>The grants: the owner, the user grants, the role grants, the Everyone grant, then the stored permissions.</returns>
        private static List<Entry> Entries(AudienceSubject subject, IReadOnlyDictionary<int, IReadOnlyList<int>> membersByRole)
        {
            List<Entry> entries = [];

            // --- The owner, when an audience user: a standard row has none ---
            if (subject.OwnerId is int owner)
            {
                AudienceReason reason = new(AudienceReasonKind.Owner, AudienceAccess.Owner, null, null, null, null, false, null);
                entries.Add(new Entry(AudienceReasonKind.Owner, owner, null, null, null, reason, subject.OwnerIsAudience ? Reach.Of([owner]) : Reach.None));
            }

            // --- The user grants, the role grants and the Everyone grant, each group by grant id ---
            IOrderedEnumerable<AudienceShare> shares = subject.Shares
                .OrderBy(static share => share.UserId is not null ? 0 : share.RoleId is not null ? 1 : 2)
                .ThenBy(static share => share.Id);
            foreach (AudienceShare share in shares)
            {
                AudienceAccess access = share.Access == ShareAccess.Edit ? AudienceAccess.Edit : AudienceAccess.View;
                if (share.UserId is int user)
                {
                    AudienceReason reason = new(AudienceReasonKind.UserShare, access, null, share.SharedById, share.SharedAt, null, false, null);
                    entries.Add(new Entry(AudienceReasonKind.UserShare, user, null, null, null, reason, share.UserIsAudience ? Reach.Of([user]) : Reach.None));
                }
                else if (share.RoleId is int role)
                {
                    // An inactive role reaches no one; a public one reaches every audience user.
                    Reach reach = !share.RoleIsActive ? Reach.None : share.RoleIsPublic ? Reach.Everyone : Reach.Of(membersByRole.GetValueOrDefault(role) ?? []);
                    AudienceReason reason = new(AudienceReasonKind.RoleShare, access, role, share.SharedById, share.SharedAt, null, false, null);
                    entries.Add(new Entry(AudienceReasonKind.RoleShare, null, role, null, null, reason, reach));
                }
                else
                {
                    AudienceReason reason = new(AudienceReasonKind.Everyone, access, null, share.SharedById, share.SharedAt, null, false, null);
                    entries.Add(new Entry(AudienceReasonKind.Everyone, null, null, null, null, reason, Reach.Everyone));
                }
            }

            // --- The stored permissions that reach the row, by role then permission ---
            foreach (AudiencePermission permission in subject.Permissions.OrderBy(static permission => permission.RoleId).ThenBy(static permission => permission.PermissionId))
            {
                // A filter that reads the current user reaches the row for some holders and not for
                // others; evaluating it once, as the reader, would answer for the reader alone, so the
                // grant is listed but reaches no one.
                Reach reach = permission.DependsOnViewer ? Reach.None
                    : permission.IsPublic ? Reach.Everyone
                    : Reach.Of(membersByRole.GetValueOrDefault(permission.RoleId) ?? []);
                AudienceReason reason = new(AudienceReasonKind.Permission, permission.Access, permission.RoleId, null, null, permission.PermissionId, permission.Filtered, null);
                entries.Add(new Entry(AudienceReasonKind.Permission, null, permission.RoleId, permission.PermissionId, null, reason, reach, permission.DependsOnViewer));
            }

            return entries;
        }

        /// <summary>How many of the users a grant reaches lack a data securable.</summary>
        /// <param name="reach">The users reached.</param>
        /// <param name="readers">Who can read the securable.</param>
        /// <param name="audience">The audience size, which every audience user counts in.</param>
        /// <returns>The count.</returns>
        private static int Lacking(Reach reach, DataReaders readers, int audience)
        {
            // Every user can read it, or the readers of the whole audience were counted in SQL.
            return readers.Everyone ? 0
                : reach.All ? Math.Max(0, audience - readers.Count)
                : reach.Users.Count(user => !readers.Users.Contains(user));
        }

        /// <summary>One grant of the audience, with the reason it gives each user it reaches.</summary>
        /// <param name="Kind">What the grant is.</param>
        /// <param name="UserId">The owner, or the user of a user grant.</param>
        /// <param name="RoleId">The role of a role grant or of a stored permission.</param>
        /// <param name="PermissionId">The stored permission.</param>
        /// <param name="ThroughId">The row seen through.</param>
        /// <param name="Reason">The reason it gives.</param>
        /// <param name="Reach">The audience users it reaches.</param>
        /// <param name="DependsOnViewer">Whether it is a stored permission whose filter reads the current user, so it reaches no one.</param>
        private sealed record Entry(AudienceReasonKind Kind, int? UserId, int? RoleId, int? PermissionId, int? ThroughId, AudienceReason Reason, Reach Reach, bool DependsOnViewer = false);

        /// <summary>The audience users a grant reaches: every one, or a set.</summary>
        /// <param name="All">Whether it reaches every audience user.</param>
        /// <param name="Users">The users it reaches, when not every one.</param>
        private sealed record Reach(bool All, IReadOnlySet<int> Users)
        {
            /// <summary>No user.</summary>
            public static readonly Reach None = new(false, new HashSet<int>());

            /// <summary>Every audience user.</summary>
            public static readonly Reach Everyone = new(true, new HashSet<int>());

            /// <summary>A set of users.</summary>
            /// <param name="users">The users.</param>
            /// <returns>The reach.</returns>
            public static Reach Of(IEnumerable<int> users)
            {
                return new Reach(false, users.ToHashSet());
            }

            /// <summary>Every user some reach reaches.</summary>
            /// <param name="reaches">The reaches.</param>
            /// <returns>The union.</returns>
            public static Reach Union(IEnumerable<Reach> reaches)
            {
                HashSet<int> users = [];
                foreach (Reach reach in reaches)
                {
                    if (reach.All)
                    {
                        return Everyone;
                    }

                    users.UnionWith(reach.Users);
                }

                return new Reach(false, users);
            }

            /// <summary>How many users it reaches.</summary>
            /// <param name="audience">The audience size.</param>
            /// <returns>The count.</returns>
            public int Count(int audience)
            {
                return All ? audience : Users.Count;
            }

            /// <summary>Whether it reaches a user, who is an audience user.</summary>
            /// <param name="user">The user.</param>
            /// <returns>True when it does.</returns>
            public bool Contains(int user)
            {
                return All || Users.Contains(user);
            }
        }
    }

    /// <summary>What an audience's round trips read, the permissions already narrowed to those that reach each row.</summary>
    /// <param name="Audience">How many audience users the tenant has: active users other than the system user.</param>
    /// <param name="Subjects">The row first, then each row seen through that exists, in the query's order.</param>
    /// <param name="MembersByRole">The current audience members of each active role that is not public and that a grant names.</param>
    /// <param name="Listed">
    ///     The first audience users by display name, at most the cap, when a grant may reach every
    ///     audience user and the caller sees the members; otherwise empty.
    /// </param>
    /// <param name="Readers">Parallel to the query's data securables: who can read each one.</param>
    internal sealed record AudienceFacts(
        int Audience,
        IReadOnlyList<AudienceSubject> Subjects,
        IReadOnlyDictionary<int, IReadOnlyList<int>> MembersByRole,
        IReadOnlyList<int> Listed,
        IReadOnlyList<DataReaders> Readers);

    /// <summary>One row whose grants reach the audience: the row itself, or a row seen through.</summary>
    /// <param name="ThroughId">The row's id when it is seen through; null for the row itself.</param>
    /// <param name="OwnerId">The row's owner; null for a standard row.</param>
    /// <param name="OwnerIsAudience">Whether the owner is an audience user.</param>
    /// <param name="Shares">The row's grants.</param>
    /// <param name="Permissions">The stored permissions that reach the row.</param>
    internal sealed record AudienceSubject(int? ThroughId, int? OwnerId, bool OwnerIsAudience, IReadOnlyList<AudienceShare> Shares, IReadOnlyList<AudiencePermission> Permissions);

    /// <summary>One grant of a row, with what its grantee is.</summary>
    /// <param name="Id">The grant's id.</param>
    /// <param name="UserId">The user granted; null for a role grant or the Everyone grant.</param>
    /// <param name="RoleId">The role granted; null for a user grant or the Everyone grant.</param>
    /// <param name="Access">What the grant lets its grantee do.</param>
    /// <param name="SharedById">The user who made the grant.</param>
    /// <param name="SharedAt">When the grant was made.</param>
    /// <param name="UserIsAudience">Whether the user granted is an audience user.</param>
    /// <param name="RoleIsActive">Whether the role granted is active.</param>
    /// <param name="RoleIsPublic">Whether the role granted is public.</param>
    internal sealed record AudienceShare(
        int Id,
        int? UserId,
        int? RoleId,
        ShareAccess Access,
        int SharedById,
        DateTimeOffset SharedAt,
        bool UserIsAudience,
        bool RoleIsActive,
        bool RoleIsPublic);

    /// <summary>One stored permission of an active role that reaches a row.</summary>
    /// <param name="PermissionId">The permission.</param>
    /// <param name="RoleId">Its role.</param>
    /// <param name="IsPublic">Whether the role is public, so the permission reaches every audience user.</param>
    /// <param name="Access">What it lets its holders do with the row.</param>
    /// <param name="Filtered">Whether it carries a filter, which the row satisfies.</param>
    /// <param name="DependsOnViewer">
    ///     Whether its filter reads the current user, so it was not evaluated and reaches no one; such
    ///     a permission is listed as a grant whatever its filter says.
    /// </param>
    internal sealed record AudiencePermission(int PermissionId, int RoleId, bool IsPublic, AudienceAccess Access, bool Filtered, bool DependsOnViewer = false);

    /// <summary>Who can read one data securable.</summary>
    /// <param name="Everyone">Whether every user can: a bespoke criterion or a public role grants it.</param>
    /// <param name="Count">How many audience users can, when not every one.</param>
    /// <param name="Users">The users among those an audience names who can.</param>
    internal sealed record DataReaders(bool Everyone, int Count, IReadOnlySet<int> Users)
    {
        /// <summary>Whether a user the audience names can read the securable.</summary>
        /// <param name="user">The user.</param>
        /// <returns>True when they can.</returns>
        public bool CanRead(int user)
        {
            return Everyone || Users.Contains(user);
        }
    }

    /// <summary>A composed audience, before its related projections.</summary>
    /// <param name="Total">The distinct audience users.</param>
    /// <param name="Capped">Whether the members were cut at the cap.</param>
    /// <param name="CannotRead">Parallel to the data securables: the audience users who lack each one.</param>
    /// <param name="Members">The members; null when the caller does not see them.</param>
    /// <param name="Grants">The grants.</param>
    /// <param name="Users">The users the grants and the listed members name, the members' sharers included.</param>
    /// <param name="Roles">The roles the grants name.</param>
    internal sealed record AudienceComposition(
        int Total,
        bool Capped,
        IReadOnlyList<int> CannotRead,
        IReadOnlyList<AudienceMember>? Members,
        IReadOnlyList<AudienceGrant> Grants,
        IReadOnlySet<int> Users,
        IReadOnlySet<int> Roles);
}
