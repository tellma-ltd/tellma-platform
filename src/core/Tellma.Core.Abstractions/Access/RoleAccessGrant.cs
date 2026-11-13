// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One stored permission of an active role that reaches the caller: through a membership, with
    ///     its window, or through a public role, which reaches every user.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A permission reached through two memberships of one role is two grants, one per window.
    ///         The window is half-open: the grant holds from <see cref="StartsAt" />, inclusive, to
    ///         <see cref="EndsAt" />, exclusive, and a null bound is unbounded.
    ///     </para>
    ///     <para>
    ///         The filter and its stamp travel together: a grant without a filter carries no version,
    ///         and a filtered grant carries the version its filter was validated under, one this engine
    ///         compiles. Construction refuses anything else, so a grant can always become a leaf.
    ///     </para>
    /// </remarks>
    /// <param name="Resource">The resource as registered, or the wildcard.</param>
    /// <param name="Action">The action as registered, or the wildcard.</param>
    /// <param name="PermissionId">The id of the stored permission.</param>
    /// <param name="RoleId">The id of the role that holds it.</param>
    /// <param name="RoleName">The role's name, for the "why" answer.</param>
    /// <param name="IsPublic">Whether the role is public, so the grant reaches every user without a membership.</param>
    /// <param name="Filter">The row-level filter, Queryex over the securable's filter root; null for an unrestricted grant.</param>
    /// <param name="FilterQueryexVersion">The Queryex version the filter was validated under; null exactly when the filter is.</param>
    /// <param name="StartsAt">When the membership that brings the grant starts; null since always, and for a public role.</param>
    /// <param name="EndsAt">When that membership ends, exclusive; null for no end, and for a public role.</param>
    public sealed record RoleAccessGrant(
        string Resource,
        string Action,
        int PermissionId,
        int RoleId,
        string RoleName,
        bool IsPublic,
        string? Filter,
        int? FilterQueryexVersion,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt) : AccessGrant(Resource, Action)
    {
        /// <summary>The role's name.</summary>
        public string RoleName { get; } = RoleName ?? throw new ArgumentNullException(nameof(RoleName));

        /// <summary>The row-level filter; null for an unrestricted grant.</summary>
        /// <remarks>An empty filter must never read as "every row": construction refuses it instead.</remarks>
        public string? Filter { get; } = Filter is not null && string.IsNullOrWhiteSpace(Filter)
            ? throw new ArgumentException("A grant's filter must carry an expression; an unrestricted grant has none.", nameof(Filter))
            : Filter;

        /// <summary>The Queryex version the filter was validated under; null exactly when the filter is.</summary>
        public int? FilterQueryexVersion { get; } = Filter is null
            ? FilterQueryexVersion is null
                ? null
                : throw new ArgumentException("A grant without a filter carries no Queryex version.", nameof(FilterQueryexVersion))
            : FilterQueryexVersion is int stamp && QueryexLanguage.IsSupported(stamp)
                ? FilterQueryexVersion
                : throw new ArgumentException("A filtered grant carries the Queryex version its filter was validated under, and this engine must compile it.", nameof(FilterQueryexVersion));

        /// <summary>Whether the grant's window contains an instant: started at or before it, and not ended at it.</summary>
        /// <param name="at">The instant.</param>
        /// <returns>True when the grant holds at the instant.</returns>
        public bool IsCurrentAt(DateTimeOffset at)
        {
            return (StartsAt is not DateTimeOffset start || start <= at) && (EndsAt is not DateTimeOffset end || end > at);
        }
    }
}
