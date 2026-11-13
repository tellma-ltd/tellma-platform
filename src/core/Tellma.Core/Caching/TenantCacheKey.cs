// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Caching
{
    /// <summary>The key of a kind with one entry per tenant, such as the tenant's settings.</summary>
    /// <param name="TenantId">The tenant.</param>
    public readonly record struct TenantCacheKey(int TenantId) : ITenantCacheKey;
}
