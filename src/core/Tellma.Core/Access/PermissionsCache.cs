// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Caching;

namespace Tellma.Core.Access
{
    // The store is spec 0014 §6.2: the permissions kind of the versioned cache, fed only from
    // connect prologue results, entries stored under no tag and read with both of the set's tags.
    /// <summary>
    ///     The process's permission sets: one per tenant and user, stored under
    ///     <see cref="VersionTag.None" /> and current while both of the set's tags match what a
    ///     prologue read. The user connector stores every set it builds; the store never loads.
    /// </summary>
    /// <param name="options">The cache limits: <see cref="CacheOptions.PermissionsEntries" /> entries at most.</param>
    /// <param name="meterFactory">The meter factory.</param>
    internal sealed class PermissionsCache(IOptions<CacheOptions> options, IMeterFactory meterFactory)
        : VersionedCache<TenantUserCacheKey, UserAccess>(CacheKinds.Permissions, (options ?? throw new ArgumentNullException(nameof(options))).Value.PermissionsEntries, meterFactory);
}
