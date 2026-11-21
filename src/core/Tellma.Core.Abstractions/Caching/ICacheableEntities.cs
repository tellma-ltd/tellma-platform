// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The cached whole-table lists of the composition's cacheable entities, for the tenant of the
    ///     current scope. A scoped view over a process-wide store, validated by each entity's version
    ///     tag.
    /// </summary>
    /// <remarks>
    ///     A list read here in service code is not declared to the batch it feeds; a service whose
    ///     decision rests on one declares the dependency itself.
    /// </remarks>
    public interface ICacheableEntities
    {
        /// <summary>
        ///     The entity's list: the cached one when it carries the entity's current tag, else loaded
        ///     in one read batch and stored.
        /// </summary>
        /// <typeparam name="TEntity">The cacheable entity.</typeparam>
        /// <returns>
        ///     The list and how it was served; <see cref="CacheOutcome.Oversized" /> with no value when
        ///     the table holds more rows than the entity's limit.
        /// </returns>
        Task<CacheResult<CachedEntitySet<TEntity>>> GetAsync<TEntity>()
            where TEntity : class;

        /// <summary>The entity's cached list when present and current; never loads.</summary>
        /// <typeparam name="TEntity">The cacheable entity.</typeparam>
        /// <returns>The list, or null.</returns>
        CachedEntitySet<TEntity>? Peek<TEntity>()
            where TEntity : class;

        /// <summary>
        ///     The wire tag of every cacheable entity of the composition, by entity name: the entity's
        ///     tag in <see cref="VersionTag.ToWire" /> form under <see cref="CachedEntitySet{TEntity}.FormatVersion" />.
        ///     An entity whose tag row is missing gets the wire form of <see cref="VersionTag.None" />,
        ///     which never matches a value a client stored.
        /// </summary>
        /// <returns>The wire tags.</returns>
        Task<IReadOnlyDictionary<string, string>> GetWireTagsAsync();
    }
}
