// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     The key of a cached list of a cacheable entity: the tenant, the entity's name and the
    ///     tenant's multilingual shape the list was read under.
    /// </summary>
    /// <remarks>
    ///     The shape decides which multilingual twins a list's rows carry, and a language change moves
    ///     no entity tag, so a list read under one shape is never served to a caller under another:
    ///     the old shape's lists simply age out.
    /// </remarks>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="EntityName">The entity's name, as its metadata names it (<c>gl.Center</c>).</param>
    /// <param name="Shape">The tenant's multilingual shape the list's columns follow.</param>
    public readonly record struct CacheableEntityKey(int TenantId, string EntityName, MultilingualShape Shape) : ITenantCacheKey;
}
