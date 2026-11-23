// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>One grant of a shareable row, as the <c>shares</c> details extra lists it.</summary>
    /// <param name="Id">The grant's id.</param>
    /// <param name="UserId">The user granted; null for a role grant or the Everyone grant.</param>
    /// <param name="RoleId">The role granted; null for a user grant or the Everyone grant.</param>
    /// <param name="Access">What the grant lets its grantee do.</param>
    /// <param name="SharedById">The user who made the grant.</param>
    /// <param name="SharedAt">When the grant was made.</param>
    public sealed record ShareGrantView(int Id, int? UserId, int? RoleId, ShareAccess Access, int SharedById, DateTimeOffset SharedAt);
}
