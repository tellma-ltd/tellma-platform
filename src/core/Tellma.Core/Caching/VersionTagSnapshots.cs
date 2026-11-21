// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Caching;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     The process's latest tenant-level tags of each tenant, in a private memory cache bounded by
    ///     <see cref="CacheOptions.TagSnapshots" /> entries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An absent tenant (never read, or evicted) yields an empty snapshot, which has no name,
    ///         so every cache of that tenant loads until the next batch's prelude replaces it.
    ///     </para>
    ///     <para>
    ///         When the cache is full, a new tenant's snapshot is refused while the cache compacts in
    ///         the background, least recently used first; the tenant then reads as absent, which costs
    ///         loads, never correctness. A replaced tenant's entry is removed before the new one is
    ///         set, so a full cache can never keep the older snapshot in place of a refused newer one.
    ///     </para>
    /// </remarks>
    internal sealed class VersionTagSnapshots : IVersionTagSnapshots, IDisposable
    {
        /// <summary>The snapshots, each of size one.</summary>
        private readonly MemoryCache _cache;

        /// <summary>Creates the store.</summary>
        /// <param name="options">The cache settings, whose <see cref="CacheOptions.TagSnapshots" /> bounds the store.</param>
        public VersionTagSnapshots(IOptions<CacheOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.TagSnapshots });
        }

        /// <inheritdoc />
        public VersionTagSnapshot Current(int tenantId)
        {
            return _cache.TryGetValue(tenantId, out VersionTagSnapshot? snapshot) && snapshot is not null
                ? snapshot
                : Empty(tenantId);
        }

        /// <inheritdoc />
        public void Replace(VersionTagSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            // Removed first: a set the full cache refuses must not leave the older snapshot behind.
            _cache.Remove(snapshot.TenantId);
            _ = _cache.Set(snapshot.TenantId, snapshot, new MemoryCacheEntryOptions { Size = 1 });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }

        /// <summary>The snapshot of a tenant this instance holds no tags of: no name, so every kind loads.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The snapshot, read at the earliest time.</returns>
        internal static VersionTagSnapshot Empty(int tenantId)
        {
            return new VersionTagSnapshot(tenantId, new Dictionary<string, VersionTag>(StringComparer.Ordinal), DateTimeOffset.MinValue);
        }
    }

    /// <summary>
    ///     The process's latest user-level tags of each user, keyed by tenant and user, in a private
    ///     memory cache bounded by <see cref="CacheOptions.PreferencesEntries" /> entries.
    /// </summary>
    /// <remarks>
    ///     Filled only by the executor, after a batch that carried the caller's connect statements. A
    ///     full cache refuses a new user's snapshot while it compacts in the background, least recently
    ///     used first, and that user then reads as absent until the next batch replaces it.
    /// </remarks>
    internal sealed class UserVersionTagSnapshots : IUserVersionTagSnapshots, IDisposable
    {
        /// <summary>The snapshots, each of size one.</summary>
        private readonly MemoryCache _cache;

        /// <summary>Creates the store.</summary>
        /// <param name="options">The cache settings, whose <see cref="CacheOptions.PreferencesEntries" /> bounds the store.</param>
        public UserVersionTagSnapshots(IOptions<CacheOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.PreferencesEntries });
        }

        /// <inheritdoc />
        public UserVersionTagSnapshot? Current(int tenantId, int userId)
        {
            return _cache.TryGetValue((tenantId, userId), out UserVersionTagSnapshot? snapshot) ? snapshot : null;
        }

        /// <inheritdoc />
        public void Replace(UserVersionTagSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            // Removed first, as the tenant store does: a refused set never leaves an older snapshot.
            (int TenantId, int UserId) key = (snapshot.TenantId, snapshot.UserId);
            _cache.Remove(key);
            _ = _cache.Set(key, snapshot, new MemoryCacheEntryOptions { Size = 1 });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cache.Dispose();
        }
    }
}
