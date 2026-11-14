// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Caching
{
    /// <summary>The key of a kind with one entry per user of a tenant, such as preferences and permissions.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="UserId">The user, an id of the tenant's database.</param>
    public readonly record struct TenantUserCacheKey(int TenantId, int UserId) : ITenantCacheKey;
}
