// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>One subject's membership hint in a tenant, as the admin surface lists a tenant's members.</summary>
    /// <param name="Subject">The identity server's subject of the user.</param>
    /// <param name="IsActive">Whether the user is active in the tenant, as last recorded.</param>
    /// <param name="UpdatedAt">When the hint was last written.</param>
    internal sealed record TenantMemberHint(string Subject, bool IsActive, DateTimeOffset UpdatedAt);
}
