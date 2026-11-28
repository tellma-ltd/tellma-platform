// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>One reason an audience user can see a shareable row. Only the ids that fit the kind are set.</summary>
    /// <param name="Kind">What brings the user.</param>
    /// <param name="Access">What this reason lets the user do.</param>
    /// <param name="RoleId">The role of a role grant or of a stored permission.</param>
    /// <param name="SharedById">The user who made a user, role or Everyone grant.</param>
    /// <param name="SharedAt">When a user, role or Everyone grant was made.</param>
    /// <param name="PermissionId">The stored permission of a permission reason.</param>
    /// <param name="Filtered">Whether the stored permission carries a filter, which the row satisfies.</param>
    /// <param name="ThroughId">The row seen through, of the query's through resource.</param>
    public sealed record AudienceReason(
        AudienceReasonKind Kind,
        AudienceAccess Access,
        int? RoleId,
        int? SharedById,
        DateTimeOffset? SharedAt,
        int? PermissionId,
        bool Filtered,
        int? ThroughId);
}
