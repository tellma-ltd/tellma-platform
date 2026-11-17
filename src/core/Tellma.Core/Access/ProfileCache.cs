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
    // The store is spec 0014 §6.3: spec 0013's preferences kind, keyed by tenant and user, valid
    // while the user's PreferencesTag matches.
    /// <summary>
    ///     The process's user profiles: one per tenant and user, stored under the user's
    ///     <c>PreferencesTag</c> the prologue that read it returned. The user connector stores every
    ///     profile a prologue returns; the store never loads.
    /// </summary>
    /// <param name="options">The cache limits: <see cref="CacheOptions.PreferencesEntries" /> entries at most.</param>
    /// <param name="meterFactory">The meter factory.</param>
    internal sealed class ProfileCache(IOptions<CacheOptions> options, IMeterFactory meterFactory)
        : VersionedCache<TenantUserCacheKey, UserProfile>(CacheKinds.Preferences, (options ?? throw new ArgumentNullException(nameof(options))).Value.PreferencesEntries, meterFactory);
}
