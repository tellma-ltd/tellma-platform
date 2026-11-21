// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     The key of a <see cref="VersionedCache{TKey, TValue}" /> entry. Every key begins with the
    ///     tenant id: user ids and entity rows are per-tenant, and one subject belongs to several
    ///     tenants, so an entry of one tenant is never served to another.
    /// </summary>
    public interface ITenantCacheKey
    {
        /// <summary>The tenant the entry belongs to.</summary>
        int TenantId { get; }
    }
}
