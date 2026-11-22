// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>A role grant the <c>publish</c> action adds, or whose access it changes.</summary>
    /// <param name="RoleId">The grantee: an active role.</param>
    /// <param name="Access">What the grant lets the role's members do.</param>
    public sealed record RoleGrant([property: EntityRef("core.Role")] int RoleId, ShareAccess Access);
}
