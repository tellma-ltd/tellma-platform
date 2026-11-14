// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One stored permission as a caller's permission set is built from: the permission of an
    ///     active role, once per membership window that brings it, or once for an active public role.
    /// </summary>
    /// <remarks>
    ///     The load excludes inactive roles and ended memberships; the set build resolves each row
    ///     against the securable registry and the tenant's query schema, and a row that resolves to
    ///     nothing grants nothing.
    /// </remarks>
    /// <param name="PermissionId">The id of the stored permission.</param>
    /// <param name="RoleId">The id of its role.</param>
    /// <param name="RoleName">The role's name.</param>
    /// <param name="IsPublic">Whether the role is public, so the row reaches every user without a membership.</param>
    /// <param name="Resource">The resource as stored: a resource name, a former name, or the wildcard.</param>
    /// <param name="Action">The action as stored: an action name or the wildcard.</param>
    /// <param name="Filter">The stored filter; null for an unrestricted permission.</param>
    /// <param name="FilterQueryexVersion">The Queryex version the filter was validated under when saved; null without a filter.</param>
    /// <param name="StartsAt">When the membership starts; null since always, and for a public role.</param>
    /// <param name="EndsAt">When the membership ends, exclusive; null for no end, and for a public role.</param>
    public sealed record PermissionRow(
        int PermissionId,
        int RoleId,
        string RoleName,
        bool IsPublic,
        string Resource,
        string Action,
        string? Filter,
        int? FilterQueryexVersion,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt);
}
