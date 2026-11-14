// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Caching;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Data.Batches
{
    // FromCache is spec 0013 §4.4: the cache first, the capped probe on a miss, the dependency of §2.6.
    /// <content>The cached lists of the cacheable entities, read through the batch.</content>
    internal sealed partial class DataBatch
    {
        /// <summary>The tags of the lists the batch probed on a persist batch, which its guard locks without comparing.</summary>
        private readonly HashSet<string> _lockedTags = new(StringComparer.Ordinal);

        /// <inheritdoc />
        /// <remarks>
        ///     <para>
        ///         On a hit the result is complete at once and the batch declares the list's tag as a
        ///         dependency: a re-run on a validate or persist batch, a refresh on a read batch. On a
        ///         miss the probe rides the batch, after its prelude, and its rows are stamped with
        ///         the tag that prelude read; the list is stored when the batch's results complete,
        ///         before the handle does, and no dependency is declared, since the rows are the
        ///         batch's own read. On a writing persist batch the guard still locks the list's tag
        ///         to the commit, without comparing it, so a concurrent write of the table waits for
        ///         the batch rather than committing under it.
        ///     </para>
        ///     <para>
        ///         A list is keyed by the batch's multilingual shape too: a list read under another
        ///         shape is a miss, since its rows carry another set of multilingual columns.
        ///     </para>
        ///     <para>
        ///         Refused after a statement of the batch declared a write to the entity's table: the
        ///         probe would read the batch's own uncommitted rows, and a hit would not show them.
        ///     </para>
        /// </remarks>
        /// <exception cref="NotSupportedException">The cacheable entities' cache is not composed.</exception>
        /// <exception cref="ArgumentException">The class is no cacheable entity of the composition.</exception>
        /// <exception cref="InvalidOperationException">The batch already writes the entity's table.</exception>
        public BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>()
            where TEntity : class
        {
            EnsureComposing();
            CacheableEntityStore store = _executor.CacheableEntities
                ?? throw new NotSupportedException($"{nameof(IDataBatch)}.{nameof(FromCache)} is not available: the cacheable entities' cache is not composed.");
            CacheableEntityType type = store.TypeOf(typeof(TEntity));
            EnsurePrologue();

            // --- One read per entity and write epoch: a second call answers from the first ---
            QueryStatementKey key = QueryStatementKey.For("from-cache").Add(typeof(TEntity).AssemblyQualifiedName).Build(Limits);
            if (_sharedQueries.TryGetValue(key, out object? shared))
            {
                return (BatchResult<CacheResult<CachedEntitySet<TEntity>>>)shared;
            }

            if (_written.Contains(type.Metadata.Table))
            {
                throw new InvalidOperationException(
                    $"The batch already writes {type.Metadata.Table}, so it cannot read the cached list of {type.Name}: read the list before the write, or in a batch of its own.");
            }

            // --- The cache first, under the tag the process's snapshot holds ---
            // The list's columns follow the shape the batch compiles under, so the shape is part of its key.
            int tenantId = Connection.TenantId;
            MultilingualShape shape = Connection.Shape;
            VersionTag current = _executor.Snapshots?.Current(tenantId)[type.TagName] ?? VersionTag.None;
            CacheOutcome lookup = store.Find(type, tenantId, shape, current, out CachedEntityList? cached);
            BatchResult<CacheResult<CachedEntitySet<TEntity>>> result;
            if (lookup == CacheOutcome.Hit)
            {
                // The list stands for rows the batch does not read, so a tag that moved under it is caught.
                result = new BatchResult<CacheResult<CachedEntitySet<TEntity>>>(
                    new CacheResult<CachedEntitySet<TEntity>>(cached!.As<TEntity>(), cached.Tag, CacheOutcome.Hit));
                DependsOn([new VersionTagDependency(type.TagName, current, VersionTagPolicies.For(type.TagName, Purpose))]);
            }
            else
            {
                // --- Otherwise the probe, its rows settled into the cache as the results complete ---
                BatchResult<CacheResult<CachedEntitySet<TEntity>>> pending = new();
                type.Probe(this, type.Select(Map.Schema), (cursor, rows) => cursor.Settle(
                    pending,
                    () => store.Settle<TEntity>(type, tenantId, shape, lookup, ReadTag(tenantId, type.TagName), rows)));
                result = pending;

                // A writing persist batch's guard locks the list's tag row to the commit without
                // comparing it: no cached list stood in for rows, but a concurrent write of the table
                // must not commit between this probe and the batch's commit.
                if (Purpose == BatchPurpose.Persist)
                {
                    _ = _lockedTags.Add(type.TagName);
                }
            }

            _sharedQueries[key] = result;
            return result;
        }

        /// <summary>The tags the guard of a writing persist batch locks to the commit without comparing them.</summary>
        internal IReadOnlySet<string> LockedTags => _lockedTags;

        /// <summary>The tag the batch's prelude read for a name; <see cref="VersionTag.None" /> when it read none.</summary>
        /// <param name="tenantId">The batch's tenant.</param>
        /// <param name="name">The tag's name.</param>
        /// <returns>The tag.</returns>
        private VersionTag ReadTag(int tenantId, string name)
        {
            return ReadVersionTags is VersionTagSnapshot read && read.TenantId == tenantId ? read[name] : VersionTag.None;
        }
    }
}
