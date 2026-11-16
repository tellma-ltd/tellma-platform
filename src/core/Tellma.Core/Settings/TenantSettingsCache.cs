// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Data;

namespace Tellma.Core.Settings
{
    // The façade is spec 0013 §5.6's ITenantSettingsCache; the scoped-over-singleton split is §3.2's.
    /// <summary>
    ///     The scope's view of the settings store: keyed on the scope's tenant, validated by the
    ///     process's snapshot of its <c>settings</c> tag, loaded on the scope's database handle.
    /// </summary>
    /// <param name="store">The process's settings store.</param>
    /// <param name="snapshots">The process's version-tag snapshots.</param>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="database">The scope's tenant database handle.</param>
    /// <param name="cancellation">The scope's cancellation.</param>
    internal sealed class TenantSettingsCache(
        TenantSettingsStore store,
        IVersionTagSnapshots snapshots,
        IRequestContextAccessor accessor,
        ITenantDatabase database,
        DataCancellation cancellation) : ITenantSettingsCache
    {
        /// <inheritdoc />
        public async Task<CacheResult<TenantSettings>> GetAsync()
        {
            (TenantSettingsEntry entry, CacheResult<TenantSettingsEntry> result) = await LoadAsync().ConfigureAwait(false);
            return new CacheResult<TenantSettings>(entry.Settings, result.Tag, result.Outcome);
        }

        /// <inheritdoc />
        public async Task<(byte[] Json, string WireTag)> GetForClientAsync()
        {
            (TenantSettingsEntry entry, CacheResult<TenantSettingsEntry> result) = await LoadAsync().ConfigureAwait(false);
            return (entry.ClientJson, result.Tag.ToWire(TenantSettingsForClient.FormatVersion));
        }

        /// <inheritdoc />
        public TenantSettings? Peek()
        {
            int tenantId = accessor.Current.RequireTenant().Id;
            TenantSettingsEntry? entry = store.Peek(new TenantCacheKey(tenantId), CurrentTag(tenantId));
            return entry is null ? null : Admit(entry, tenantId).Settings;
        }

        /// <summary>The scope's tenant's entry: the cached one when current, else loaded once and stored.</summary>
        /// <returns>The entry and how it was served.</returns>
        private async Task<(TenantSettingsEntry Entry, CacheResult<TenantSettingsEntry> Result)> LoadAsync()
        {
            // The scope's token, read now since the scope's opener sets it after the façade is created.
            int tenantId = accessor.Current.RequireTenant().Id;
            CacheResult<TenantSettingsEntry> result = await store
                .GetAsync(new TenantCacheKey(tenantId), CurrentTag(tenantId), database, cancellation.Token)
                .ConfigureAwait(false);
            TenantSettingsEntry entry = result.Value
                ?? throw new InvalidOperationException("The settings store served no settings, which it never sizes out.");
            return (Admit(entry, tenantId), result);
        }

        /// <summary>
        ///     Refuses the unprovisioned placeholder outside the platform's own work: a load another
        ///     scope started may have accepted it for that scope alone.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="tenantId">The scope's tenant.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="TenantDatabaseMismatchException">The entry is the placeholder and the scope is not the platform's own work.</exception>
        private TenantSettingsEntry Admit(TenantSettingsEntry entry, int tenantId)
        {
            return entry.IsPlaceholder && !accessor.Current.IsSystem
                ? throw store.Mismatch(tenantId, Abstractions.Settings.Settings.PlaceholderTenantId)
                : entry;
        }

        /// <summary>The tag the process's snapshot holds for the tenant's settings.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The tag; none before the tenant's first batch.</returns>
        private VersionTag CurrentTag(int tenantId)
        {
            return snapshots.Current(tenantId)[VersionTagNames.Settings];
        }
    }
}
