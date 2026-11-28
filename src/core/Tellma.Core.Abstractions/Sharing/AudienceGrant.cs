// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     One grant that can reach a shareable row, with the audience users it reaches: the owner, a
    ///     user, role or Everyone grant, a stored permission, or a row seen through. Only the ids that
    ///     fit the kind are set.
    /// </summary>
    /// <param name="Kind">What the grant is.</param>
    /// <param name="UserId">The owner, or the user of a user grant.</param>
    /// <param name="RoleId">The role of a role grant or of a stored permission.</param>
    /// <param name="PermissionId">The stored permission.</param>
    /// <param name="ThroughId">The row seen through.</param>
    /// <param name="Access">What the grant lets its users do.</param>
    /// <param name="Members">
    ///     The audience users the grant reaches: none for a grant of a deactivated user or an inactive
    ///     role, every audience user for the Everyone grant and a public role.
    /// </param>
    /// <param name="CannotRead">Parallel to the query's data securables: how many of those users lack each one.</param>
    public sealed record AudienceGrant(
        AudienceReasonKind Kind,
        int? UserId,
        int? RoleId,
        int? PermissionId,
        int? ThroughId,
        AudienceAccess Access,
        int Members,
        IReadOnlyList<int> CannotRead);
}
