// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Caching
{
    // The probe is spec 0013 §4.4's: every scalar property, no filter, key order, MaxRows + 1 rows.
    /// <summary>
    ///     One cacheable entity of the composition, as the entities cache loads it: its leaf metadata,
    ///     its tag, its resolved row limit, and the capped probe that reads its list.
    /// </summary>
    internal abstract class CacheableEntityType
    {
        /// <summary>Creates the type.</summary>
        /// <param name="metadata">The entity's leaf metadata.</param>
        /// <param name="maxRows">The resolved row limit.</param>
        protected CacheableEntityType(EntityMetadata metadata, int maxRows)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
            Metadata = metadata;
            MaxRows = maxRows;
            TagName = VersionTagNames.ForEntity(metadata.Name);
        }

        /// <summary>The entity's leaf metadata.</summary>
        public EntityMetadata Metadata { get; }

        /// <summary>The entity's name, the second half of its cache key.</summary>
        public string Name => Metadata.Name;

        /// <summary>The tenant-level tag the list is cached under (<c>entity:&lt;Name&gt;</c>).</summary>
        public string TagName { get; }

        /// <summary>The most rows the list holds; one more is an overflow.</summary>
        public int MaxRows { get; }

        /// <summary>Creates the type of a qualifying entity.</summary>
        /// <param name="metadata">The entity's leaf metadata; its class is a top-level entity with an int key.</param>
        /// <param name="maxRows">The resolved row limit.</param>
        /// <returns>The type.</returns>
        public static CacheableEntityType Create(EntityMetadata metadata, int maxRows)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return typeof(TopLevelEntity<int>).IsAssignableFrom(metadata.ClrType)
                ? (CacheableEntityType)Activator.CreateInstance(typeof(CacheableEntityType<>).MakeGenericType(metadata.ClrType), metadata, maxRows)!
                : throw new ArgumentException($"The entity {metadata.Name} is not a top-level entity with an int key, so it is not cacheable.", nameof(metadata));
        }

        /// <summary>The cache key of the type's list for a tenant under its multilingual shape.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="shape">The tenant's multilingual shape, which decides the list's columns.</param>
        /// <returns>The key.</returns>
        public CacheableEntityKey Key(int tenantId, MultilingualShape shape)
        {
            return new CacheableEntityKey(tenantId, Name, shape);
        }

        /// <summary>
        ///     The probe's select: every scalar property of the entity that the tenant's query schema
        ///     declares, in the entity's order. The schema admits a multilingual twin only for a tenant
        ///     whose shape has the language, so each tenant's list holds exactly its own columns.
        /// </summary>
        /// <param name="schema">The tenant's query schema.</param>
        /// <returns>The comma-separated paths.</returns>
        /// <exception cref="InvalidOperationException">The schema does not declare the entity.</exception>
        public string Select(QueryexSchema schema)
        {
            ArgumentNullException.ThrowIfNull(schema);
            EntityDescriptor descriptor = schema.FindEntity(Name)
                ?? throw new InvalidOperationException($"The query schema declares no entity {Name}, so its cached list cannot be read.");
            HashSet<string> declared = new(descriptor.Properties.Select(static property => property.Name), StringComparer.Ordinal) { descriptor.Key.Name };
            return string.Join(", ", Metadata.Properties.Select(static property => property.Name).Where(declared.Contains));
        }

        /// <summary>Composes the probe on a batch, through its public channel.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="select">The probe's select.</param>
        /// <returns>Reads the probe's rows once the batch executed.</returns>
        public abstract Func<IReadOnlyList<object>> Probe(IDataBatch batch, string select);

        /// <summary>Composes the probe on a batch, handing its rows over while the batch's result sets are read.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="select">The probe's select.</param>
        /// <param name="read">Takes the rows.</param>
        public abstract void Probe(DataBatch batch, string select, Action<ResultSetCursor, IReadOnlyList<object>> read);

        /// <summary>Builds the cached list from the probe's rows.</summary>
        /// <param name="tag">The tag the rows are valid for.</param>
        /// <param name="loadedAt">When they were read.</param>
        /// <param name="rows">The rows, in key order, no more than <see cref="MaxRows" />.</param>
        /// <returns>The list.</returns>
        public abstract CachedEntityList Build(VersionTag tag, DateTimeOffset loadedAt, IReadOnlyList<object> rows);

        /// <summary>The probe's query.</summary>
        /// <typeparam name="TEntity">The leaf class.</typeparam>
        /// <param name="select">The probe's select.</param>
        /// <returns>The query.</returns>
        protected EntityQuery<TEntity> Query<TEntity>(string select)
            where TEntity : class
        {
            return new EntityQuery<TEntity>
            {
                Select = select,
                OrderBy = Metadata.Key.Name,
                Take = MaxRows + 1,
                AllProperties = true,
            };
        }
    }

    /// <summary>A cacheable entity of a known leaf class.</summary>
    /// <typeparam name="TEntity">The leaf class.</typeparam>
    /// <param name="metadata">The entity's leaf metadata.</param>
    /// <param name="maxRows">The resolved row limit.</param>
    internal sealed class CacheableEntityType<TEntity>(EntityMetadata metadata, int maxRows) : CacheableEntityType(metadata, maxRows)
        where TEntity : TopLevelEntity<int>
    {
        /// <inheritdoc />
        public override Func<IReadOnlyList<object>> Probe(IDataBatch batch, string select)
        {
            ArgumentNullException.ThrowIfNull(batch);
            BatchResult<EntityQueryResult<TEntity>> rows = batch.Query(Query<TEntity>(select));
            return () => rows.Value.Entities;
        }

        /// <inheritdoc />
        public override void Probe(DataBatch batch, string select, Action<ResultSetCursor, IReadOnlyList<object>> read)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(read);
            _ = batch.PlanEntityQuery(Query<TEntity>(select), (cursor, result) => read(cursor, result.Entities));
        }

        /// <inheritdoc />
        public override CachedEntityList Build(VersionTag tag, DateTimeOffset loadedAt, IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(tag);
            ArgumentNullException.ThrowIfNull(rows);

            // --- Read-only collections over copies: the list is shared by every caller of every scope ---
            ReadOnlyCollection<TEntity> items = new([.. rows.Cast<TEntity>()]);
            CachedEntitySet<TEntity> set = new(tag, loadedAt, items, items.ToFrozenDictionary(static item => item.Id));
            return new CachedEntityList(this, tag, loadedAt, items, set);
        }
    }
}
