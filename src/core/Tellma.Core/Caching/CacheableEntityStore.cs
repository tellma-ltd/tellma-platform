// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Caching
{
    // The entities kind is spec 0013 §3.2's last row and §4: one list per (tenant, entity), sized in
    // rows, loaded by a capped probe whose overflow is reported rather than cached. The key also
    // carries the tenant's multilingual shape, since the list's columns follow it and a language
    // change moves no entity tag.
    /// <summary>
    ///     The process-wide store of the cacheable entities' lists, keyed by tenant, entity name and
    ///     the tenant's multilingual shape, and sized in rows. A list loads through one read batch carrying the capped probe, either
    ///     its own (<see cref="GetAsync{TEntity}" />) or the consumer's (the batch's
    ///     <c>FromCache</c>), and is stored under the tag that batch's prelude returned.
    /// </summary>
    /// <remarks>
    ///     An entity the composition marks cacheable but that does not qualify (not a top-level
    ///     entity with an int key, a child collection, a row limit out of range) has no list here; the
    ///     startup gate reports it.
    /// </remarks>
    internal sealed class CacheableEntityStore : VersionedCache<CacheableEntityKey, CachedEntityList>
    {
        /// <summary>The qualifying cacheable entities, by name.</summary>
        private readonly FrozenDictionary<string, CacheableEntityType> _types;

        /// <summary>The model's metadata, which resolves a class a caller asks by to its leaf.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The query schemas by multilingual shape, which pick a load's columns from its key.</summary>
        private readonly IQueryexSchemaProvider _schemas;

        /// <summary>The clock that stamps a list's load time.</summary>
        private readonly TimeProvider _time;

        /// <summary>The logger of the overflow warnings.</summary>
        private readonly ILogger _logger;

        /// <summary>The entities already warned about as overflowing, once per process.</summary>
        private readonly ConcurrentDictionary<string, bool> _warnedOverflow = new(StringComparer.Ordinal);

        /// <summary>Creates the store.</summary>
        /// <param name="metadata">The model's metadata.</param>
        /// <param name="schemas">The query schemas by multilingual shape.</param>
        /// <param name="options">The cache limits.</param>
        /// <param name="meterFactory">The host's meter factory.</param>
        /// <param name="time">The clock.</param>
        /// <param name="logger">The logger.</param>
        public CacheableEntityStore(
            IEntityMetadataProvider metadata,
            IQueryexSchemaProvider schemas,
            IOptions<CacheOptions> options,
            IMeterFactory meterFactory,
            TimeProvider time,
            ILogger<CacheableEntityStore> logger)
            : base(CacheKinds.Entities, Limits(options).EntityRows, meterFactory)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(schemas);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(logger);

            _metadata = metadata;
            _schemas = schemas;
            _time = time;
            _logger = logger;

            // --- The qualifying cacheable entities; the gate reports the others ---
            CacheOptions limits = options.Value;
            Dictionary<string, CacheableEntityType> types = new(StringComparer.Ordinal);
            foreach (EntityMetadata entity in metadata.All.Where(static entity => entity.IsCacheable))
            {
                int maxRows = ResolveMaxRows(entity, limits);
                if (Qualifies(entity) && maxRows >= 1 && maxRows <= limits.EntityMaxRowsCeiling)
                {
                    types[entity.Name] = CacheableEntityType.Create(entity, maxRows);
                }
            }

            _types = types.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>The qualifying cacheable entities.</summary>
        public IEnumerable<CacheableEntityType> Types => _types.Values;

        /// <summary>
        ///     The row limit an entity's <see cref="CacheableAttribute" /> resolves to: its own, or
        ///     <see cref="CacheOptions.EntityMaxRowsDefault" /> when it leaves it unspecified (0).
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="options">The cache limits.</param>
        /// <returns>The limit; 0 when the entity is not cacheable.</returns>
        public static int ResolveMaxRows(EntityMetadata entity, CacheOptions options)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(options);
            return EntityShapes.GetAttribute<CacheableAttribute>(entity.ClrType) is CacheableAttribute cacheable
                ? cacheable.MaxRows == 0 ? options.EntityMaxRowsDefault : cacheable.MaxRows
                : 0;
        }

        /// <summary>Whether an entity's shape allows a cached list: a top-level entity with an int key and no child collection.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>True when it qualifies.</returns>
        public static bool Qualifies(EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return typeof(TopLevelEntity<int>).IsAssignableFrom(entity.ClrType) && entity.Children.Count == 0;
        }

        /// <summary>The cacheable entity of a class a caller asks by: its leaf class, or one of its bases.</summary>
        /// <param name="requested">The class.</param>
        /// <returns>The entity.</returns>
        /// <exception cref="ArgumentException">The class is no cacheable entity of the composition.</exception>
        public CacheableEntityType TypeOf(Type requested)
        {
            ArgumentNullException.ThrowIfNull(requested);
            EntityMetadata metadata;
            try
            {
                metadata = _metadata.Get(requested);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"The class {requested.Name} is no entity of the composition, so it has no cached list.", nameof(requested), exception);
            }

            return _types.TryGetValue(metadata.Name, out CacheableEntityType? type) && requested.IsAssignableFrom(type.Metadata.ClrType)
                ? type
                : throw new ArgumentException(
                    $"The entity {metadata.Name} is not cacheable: mark a top-level entity with an int key and no child collection [Cacheable].",
                    nameof(requested));
        }

        /// <summary>The list of an entity for a tenant, loaded on a miss on the caller's handle.</summary>
        /// <typeparam name="TEntity">The class the caller asks by.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="shape">The tenant's multilingual shape, which decides the list's columns.</param>
        /// <param name="currentTag">The tag the caller's snapshot holds for the entity.</param>
        /// <param name="database">The caller's scoped handle, of the same tenant and shape.</param>
        /// <param name="cancellationToken">Cancels the wait, and the load the caller starts.</param>
        /// <returns>The list, the tag it is valid for and the outcome; no list when oversized.</returns>
        public async Task<CacheResult<CachedEntitySet<TEntity>>> GetAsync<TEntity>(
            int tenantId, MultilingualShape shape, VersionTag currentTag, ITenantDatabase database, CancellationToken cancellationToken = default)
            where TEntity : class
        {
            CacheableEntityType type = TypeOf(typeof(TEntity));
            CacheResult<CachedEntityList> result = await GetAsync(type.Key(tenantId, shape), currentTag, database, cancellationToken).ConfigureAwait(false);
            return new CacheResult<CachedEntitySet<TEntity>>(result.Value?.As<TEntity>(), result.Tag, result.Outcome);
        }

        /// <summary>The list of an entity for a tenant when it is current; never loads.</summary>
        /// <typeparam name="TEntity">The class the caller asks by.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="shape">The tenant's multilingual shape, which decides the list's columns.</param>
        /// <param name="currentTag">The tag the caller's snapshot holds for the entity.</param>
        /// <returns>The list, or null.</returns>
        public CachedEntitySet<TEntity>? Peek<TEntity>(int tenantId, MultilingualShape shape, VersionTag currentTag)
            where TEntity : class
        {
            CacheableEntityType type = TypeOf(typeof(TEntity));
            return Peek(type.Key(tenantId, shape), currentTag)?.As<TEntity>();
        }

        /// <summary>
        ///     The list a batch composing <c>FromCache</c> finds without loading, and the outcome of
        ///     the lookup, counted only when it hits: a miss is counted with the outcome its probe
        ///     settles to (<see cref="Settle{TEntity}" />).
        /// </summary>
        /// <param name="type">The entity.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="shape">The batch's multilingual shape, which decides the list's columns.</param>
        /// <param name="currentTag">The tag the batch's snapshot holds for the entity.</param>
        /// <param name="list">The current list on a hit; otherwise null.</param>
        /// <returns><see cref="CacheOutcome.Hit" />, <see cref="CacheOutcome.Miss" /> or <see cref="CacheOutcome.Stale" />.</returns>
        public CacheOutcome Find(CacheableEntityType type, int tenantId, MultilingualShape shape, VersionTag currentTag, out CachedEntityList? list)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(currentTag);
            CacheOutcome outcome = Lookup(type.Key(tenantId, shape), currentTag, out list);
            if (outcome == CacheOutcome.Hit)
            {
                RecordRequest(CacheOutcome.Hit);
            }

            return outcome;
        }

        /// <summary>
        ///     Settles the rows a batch's probe read: an overflow is reported and nothing is stored; rows
        ///     under a tag the batch's prelude did not find are served and not stored; otherwise the
        ///     list is stored under the prelude's tag. Counts the request with the settled outcome.
        /// </summary>
        /// <remarks>
        ///     Storing is best effort: a store already disposed (the host stopping) logs and skips the
        ///     list, and the rows are still returned, since a batch's completions run after its commit.
        /// </remarks>
        /// <typeparam name="TEntity">The class the caller asks by.</typeparam>
        /// <param name="type">The entity.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="shape">The batch's multilingual shape, which the probe's columns followed.</param>
        /// <param name="missed">How the lookup missed: <see cref="CacheOutcome.Miss" /> or <see cref="CacheOutcome.Stale" />.</param>
        /// <param name="tag">The tag the batch's prelude returned for the entity; <see cref="VersionTag.None" /> when it found none.</param>
        /// <param name="rows">The probe's rows.</param>
        /// <returns>The result.</returns>
        public CacheResult<CachedEntitySet<TEntity>> Settle<TEntity>(
            CacheableEntityType type, int tenantId, MultilingualShape shape, CacheOutcome missed, VersionTag tag, IReadOnlyList<object> rows)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(tag);
            ArgumentNullException.ThrowIfNull(rows);

            CacheLoad<CachedEntityList> load = Read(type, tag, rows);
            CacheOutcome outcome = load.IsOversized ? CacheOutcome.Oversized : tag == VersionTag.None ? CacheOutcome.Uncached : missed;
            if (outcome is not CacheOutcome.Oversized and not CacheOutcome.Uncached)
            {
                // Best effort: this runs among a batch's completions, after a persist batch committed,
                // so a store disposed at host stop skips the list rather than failing a committed write.
                try
                {
                    Set(type.Key(tenantId, shape), load.Value!, tag);
                }
                catch (ObjectDisposedException)
                {
                    CachingLog.CacheableEntityNotStored(_logger, type.Name);
                }
            }

            RecordRequest(outcome);
            return new CacheResult<CachedEntitySet<TEntity>>(load.Value?.As<TEntity>(), tag, outcome);
        }

        /// <inheritdoc />
        /// <remarks>
        ///     The probe selects the columns of the key's shape. A handle whose shape is known and
        ///     differs from the key's is refused, so a list is only ever read under its own shape.
        /// </remarks>
        protected override async Task<CacheLoad<CachedEntityList>> LoadAsync(CacheableEntityKey key, ITenantDatabase database, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(database);
            CacheableEntityType type = _types.TryGetValue(key.EntityName, out CacheableEntityType? found)
                ? found
                : throw new ArgumentException($"The entity {key.EntityName} is not cacheable.", nameof(key));
            if (database is IBatchConnection connection && connection.Shape != key.Shape)
            {
                throw new InvalidOperationException(
                    $"The list of {type.Name} under the {key.Shape} shape was asked of a handle under the {connection.Shape} shape; load a list on a handle of its own shape.");
            }

            // --- One read batch on the caller's handle: its prelude's tag, then the capped probe of the key's columns ---
            IDataBatch batch = database.CreateBatch(BatchPurpose.Read);
            Func<IReadOnlyList<object>> rows = type.Probe(batch, type.Select(_schemas.GetSchema(key.Shape)));
            BatchOutcome outcome = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            // A handle of another tenant would store that tenant's rows under this key.
            return outcome.VersionTags.TenantId == key.TenantId
                ? Read(type, outcome.VersionTags[type.TagName], rows())
                : throw new InvalidOperationException(
                    $"The list of {type.Name} for tenant {key.TenantId} was loaded on a handle of tenant {outcome.VersionTags.TenantId}; load a tenant's list on a handle of the same tenant.");
        }

        /// <inheritdoc />
        protected override long SizeOf(CachedEntityList value)
        {
            ArgumentNullException.ThrowIfNull(value);

            // Rows are the kind's unit; an empty list still takes one.
            return Math.Max(1, value.Items.Count);
        }

        /// <summary>The cache limits, read before the base is created.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The limits.</returns>
        private static CacheOptions Limits(IOptions<CacheOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return options.Value;
        }

        /// <summary>The list a probe's rows make, or the overflow they signal, logged once per entity.</summary>
        /// <param name="type">The entity.</param>
        /// <param name="tag">The tag the rows are valid for.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The load.</returns>
        private CacheLoad<CachedEntityList> Read(CacheableEntityType type, VersionTag tag, IReadOnlyList<object> rows)
        {
            // MaxRows + 1 rows are a signal, not a list: they are discarded.
            if (rows.Count > type.MaxRows)
            {
                if (_warnedOverflow.TryAdd(type.Name, true))
                {
                    CachingLog.CacheableEntityOverflow(_logger, type.Name, rows.Count, type.MaxRows);
                }

                return CacheLoad.Oversized<CachedEntityList>(tag);
            }

            return CacheLoad.Loaded(type.Build(tag, _time.GetUtcNow(), rows), tag);
        }
    }
}
