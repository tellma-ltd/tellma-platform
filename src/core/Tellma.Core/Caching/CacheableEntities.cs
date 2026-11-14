// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;

namespace Tellma.Core.Caching
{
    // The façade is spec 0013 §4.3's ICacheableEntities; the scoped-over-singleton split is §3.2's.
    /// <summary>
    ///     The scope's view of the cacheable entities' lists: the scope's tenant and its multilingual
    ///     shape, the current tag from the process's snapshot of it, the scope's database handle and
    ///     the scope's data cancellation, passed to the process-wide store.
    /// </summary>
    /// <param name="store">The process-wide store.</param>
    /// <param name="snapshots">The process's tag snapshots.</param>
    /// <param name="accessor">The scope's request context, which names the tenant and holds its settings.</param>
    /// <param name="database">The scope's database handle, on which a miss loads.</param>
    /// <param name="cancellation">The scope's data cancellation, which ends the scope's wait and a load it started.</param>
    internal sealed class CacheableEntities(
        CacheableEntityStore store,
        IVersionTagSnapshots snapshots,
        IRequestContextAccessor accessor,
        ITenantDatabase database,
        DataCancellation cancellation) : ICacheableEntities
    {
        /// <inheritdoc />
        public Task<CacheResult<CachedEntitySet<TEntity>>> GetAsync<TEntity>()
            where TEntity : class
        {
            // The scope's token, read now since the scope's opener sets it after the façade is created:
            // it ends this scope's wait on a load another scope started, and a load this scope starts.
            int tenantId = TenantId;
            return store.GetAsync<TEntity>(tenantId, Shape, CurrentTag<TEntity>(tenantId), database, cancellation.Token);
        }

        /// <inheritdoc />
        public CachedEntitySet<TEntity>? Peek<TEntity>()
            where TEntity : class
        {
            int tenantId = TenantId;
            return store.Peek<TEntity>(tenantId, Shape, CurrentTag<TEntity>(tenantId));
        }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> GetWireTagsAsync()
        {
            // --- Every cacheable entity, from the snapshot alone: an absent name is VersionTag.None, which no client holds ---
            VersionTagSnapshot snapshot = snapshots.Current(TenantId);
            IReadOnlyDictionary<string, string> tags = store.Types.ToFrozenDictionary(
                static type => type.Name,
                type => snapshot[type.TagName].ToWire(CachedEntitySet<object>.FormatVersion),
                StringComparer.Ordinal);
            return Task.FromResult(tags);
        }

        /// <summary>The scope's tenant.</summary>
        private int TenantId => accessor.Current.RequireTenant().Id;

        /// <summary>The scope's tenant's multilingual shape, which decides a list's columns.</summary>
        /// <exception cref="InvalidOperationException">The scope's tenant settings are not loaded yet.</exception>
        private MultilingualShape Shape => accessor.Current.TenantSettings?.Shape ?? throw new InvalidOperationException(
            "The tenant's settings are not loaded in this scope yet, so the cacheable entities' lists have no shape to be read under. Read them after the scope's settings load.");

        /// <summary>The tag the process's snapshot holds for an entity's list.</summary>
        /// <typeparam name="TEntity">The class the caller asks by.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The tag; <see cref="VersionTag.None" /> when the snapshot has none.</returns>
        private VersionTag CurrentTag<TEntity>(int tenantId)
            where TEntity : class
        {
            return snapshots.Current(tenantId)[store.TypeOf(typeof(TEntity)).TagName];
        }
    }
}
