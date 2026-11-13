// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Caching
{
    /// <summary>The key of a cached list of a cacheable entity: the tenant and the entity's name.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="EntityName">The entity's name, as its metadata names it (<c>gl.Center</c>).</param>
    public readonly record struct CacheableEntityKey(int TenantId, string EntityName) : ITenantCacheKey;
}
