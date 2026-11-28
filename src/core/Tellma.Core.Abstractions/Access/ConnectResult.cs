// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Access
{
    // The result is spec 0014 §7.1; its fields are the prologue's result sets 0 to 3 (§7.3).
    /// <summary>
    ///     What one connect prologue read: the user it resolved, the tags it compared, which parts
    ///     were stale, whether its guard passed, and the fresh permission rows and profile when the
    ///     cached ones were stale.
    /// </summary>
    /// <param name="UserId">The resolved user's id; null when no user matched.</param>
    /// <param name="Kind">The user's kind; null when no user matched.</param>
    /// <param name="IsActive">Whether the user is active; false when no user matched.</param>
    /// <param name="State">The user's state after the first-request join; null when no user matched, and for the system.</param>
    /// <param name="PreferencesTag">The user's preferences tag; null when no user matched, and for the system.</param>
    /// <param name="UserPermissionsTag">The user's own permissions tag; null when no user matched, and for the system.</param>
    /// <param name="TenantTags">Every tenant-level tag, by name.</param>
    /// <param name="PermissionsStale">Whether the cached permission set's tags differ from the database's.</param>
    /// <param name="ProfileStale">Whether the cached profile's tag differs from the database's.</param>
    /// <param name="SettingsStale">Whether the settings the batch composes under are not the tenant's current ones.</param>
    /// <param name="GuardPassed">Whether the guard passed, so the batch's body ran.</param>
    /// <param name="PermissionRows">The user's permission rows, when they were returned; otherwise null.</param>
    /// <param name="Profile">The user's profile, when it was returned; otherwise null.</param>
    public sealed record ConnectResult(
        int? UserId,
        UserKind? Kind,
        bool IsActive,
        UserState? State,
        Guid? PreferencesTag,
        Guid? UserPermissionsTag,
        IReadOnlyDictionary<string, Guid> TenantTags,
        bool PermissionsStale,
        bool ProfileStale,
        bool SettingsStale,
        bool GuardPassed,
        IReadOnlyList<PermissionRow>? PermissionRows,
        UserProfile? Profile)
    {
        /// <inheritdoc cref="ConnectResult(int?, UserKind?, bool, UserState?, Guid?, Guid?, IReadOnlyDictionary{string, Guid}, bool, bool, bool, bool, IReadOnlyList{PermissionRow}?, UserProfile?)" />
        public IReadOnlyDictionary<string, Guid> TenantTags { get; init => field = Freeze(value); } = Freeze(TenantTags);

        /// <inheritdoc cref="ConnectResult(int?, UserKind?, bool, UserState?, Guid?, Guid?, IReadOnlyDictionary{string, Guid}, bool, bool, bool, bool, IReadOnlyList{PermissionRow}?, UserProfile?)" />
        public IReadOnlyList<PermissionRow>? PermissionRows { get; init => field = Copy(value); } = Copy(PermissionRows);

        /// <summary>An immutable copy of a tag map, keyed ordinally.</summary>
        /// <param name="tags">The tags.</param>
        /// <returns>The copy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tags" /> is null.</exception>
        private static FrozenDictionary<string, Guid> Freeze(IReadOnlyDictionary<string, Guid> tags)
        {
            ArgumentNullException.ThrowIfNull(tags, nameof(TenantTags));
            return tags is FrozenDictionary<string, Guid> frozen && ReferenceEquals(frozen.Comparer, StringComparer.Ordinal)
                ? frozen
                : tags.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>A read-only copy of the rows, refusing a null row.</summary>
        /// <param name="rows">The rows, or null.</param>
        /// <returns>The copy, or null.</returns>
        /// <exception cref="ArgumentException">A row is null.</exception>
        private static ReadOnlyCollection<PermissionRow>? Copy(IReadOnlyList<PermissionRow>? rows)
        {
            return rows is null ? null : AccessLists.Copy(rows, nameof(PermissionRows));
        }
    }
}
