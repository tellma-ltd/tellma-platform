// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     The entities cache's value: one entity's list, held as the set of its leaf class, with a
    ///     view of it for each base class a caller asks by, built once. Immutable once built.
    /// </summary>
    internal sealed class CachedEntityList
    {
        /// <summary>The set of the leaf class.</summary>
        private readonly object _leaf;

        /// <summary>The views of the set by a base class, built on first use.</summary>
        private readonly ConcurrentDictionary<Type, object> _views = new();

        /// <summary>Creates the list.</summary>
        /// <param name="type">The entity.</param>
        /// <param name="tag">The tag the rows are valid for.</param>
        /// <param name="loadedAt">When they were read.</param>
        /// <param name="items">The rows, in key order.</param>
        /// <param name="leaf">The <see cref="CachedEntitySet{TEntity}" /> of the leaf class over the same rows.</param>
        public CachedEntityList(CacheableEntityType type, VersionTag tag, DateTimeOffset loadedAt, IReadOnlyList<TopLevelEntity<int>> items, object leaf)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(tag);
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(leaf);
            Type = type;
            Tag = tag;
            LoadedAt = loadedAt;
            Items = items;
            _leaf = leaf;
        }

        /// <summary>The entity.</summary>
        public CacheableEntityType Type { get; }

        /// <summary>The tag the rows are valid for.</summary>
        public VersionTag Tag { get; }

        /// <summary>When the rows were read.</summary>
        public DateTimeOffset LoadedAt { get; }

        /// <summary>The rows, in key order.</summary>
        public IReadOnlyList<TopLevelEntity<int>> Items { get; }

        /// <summary>The list as a set of the class a caller asks by: the leaf class, or one of its bases.</summary>
        /// <typeparam name="TEntity">The class.</typeparam>
        /// <returns>The set; the same instance for every call with one class.</returns>
        /// <exception cref="ArgumentException">The class is not the leaf class or one of its bases.</exception>
        public CachedEntitySet<TEntity> As<TEntity>()
            where TEntity : class
        {
            // The leaf class is the set itself; a base class reads a view built once.
            return _leaf is CachedEntitySet<TEntity> leaf ? leaf
                : typeof(TEntity).IsAssignableFrom(Type.Metadata.ClrType)
                ? (CachedEntitySet<TEntity>)_views.GetOrAdd(typeof(TEntity), static (_, list) => list.View<TEntity>(), this)
                : throw new ArgumentException($"The cached list of {Type.Name} holds {Type.Metadata.ClrType.Name} rows, which are not {typeof(TEntity).Name}.", nameof(TEntity));
        }

        /// <summary>The rows as a base class: the same instances, in the same order.</summary>
        /// <typeparam name="TEntity">The base class.</typeparam>
        /// <returns>The view.</returns>
        private CachedEntitySet<TEntity> View<TEntity>()
            where TEntity : class
        {
            TEntity[] items = [.. Items.Cast<TEntity>()];
            return new CachedEntitySet<TEntity>(
                Tag,
                LoadedAt,
                new ReadOnlyCollection<TEntity>(items),
                Items.ToFrozenDictionary(static item => item.Id, static item => (TEntity)(object)item));
        }
    }
}
