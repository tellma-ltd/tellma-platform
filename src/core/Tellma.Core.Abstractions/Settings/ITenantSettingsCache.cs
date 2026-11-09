// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     The settings of the current scope's tenant: a scoped view over the process-wide settings
    ///     store, validated by the tenant's <see cref="VersionTagNames.Settings" /> tag.
    /// </summary>
    public interface ITenantSettingsCache
    {
        /// <summary>
        ///     The tenant's settings: the cached instance when it carries the current tag, with no round
        ///     trip; else loaded once, whatever the number of concurrent callers, and stored.
        /// </summary>
        /// <returns>The settings and how they were served.</returns>
        Task<CacheResult<TenantSettings>> GetAsync();

        /// <summary>The client view as UTF-8 JSON serialized when the settings were loaded, and its wire tag.</summary>
        /// <returns>The bytes and the wire tag (<see cref="VersionTag.ToWire" /> under <see cref="TenantSettingsForClient.FormatVersion" />).</returns>
        Task<(byte[] Json, string WireTag)> GetForClientAsync();

        /// <summary>The cached settings when present and current; never loads.</summary>
        /// <returns>The settings, or null.</returns>
        TenantSettings? Peek();
    }
}
