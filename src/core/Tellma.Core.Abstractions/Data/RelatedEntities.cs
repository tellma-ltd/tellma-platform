// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The related entities a read carries beside its main entities, one typed set per entity name, each
    ///     restricted to that entity's related projection. Serialised as entity name to array.
    /// </summary>
    /// <remarks>
    ///     Adding to a set that exists appends its instances, and narrows its projection to the columns
    ///     every instance then carries. The reader that fills it deduplicates instances by key before
    ///     adding them. Not thread-safe while it is filled.
    /// </remarks>
    public sealed class RelatedEntities
    {
        private readonly Dictionary<string, RelatedEntitySet> _sets = new(StringComparer.Ordinal);

        /// <summary>The sets, by entity name.</summary>
        public IReadOnlyDictionary<string, RelatedEntitySet> Sets => _sets;

        /// <summary>Adds instances of one entity, with the columns they carry.</summary>
        /// <typeparam name="T">The entity's leaf.</typeparam>
        /// <param name="entityName">The entity's name, such as <c>core.User</c>.</param>
        /// <param name="entities">The instances.</param>
        /// <param name="projection">The property names the instances carry.</param>
        /// <exception cref="ArgumentNullException">An argument is null, or <paramref name="entities" /> holds a null entry.</exception>
        /// <exception cref="ArgumentException"><paramref name="entityName" /> is empty or whitespace.</exception>
        /// <exception cref="InvalidOperationException">The set of <paramref name="entityName" /> holds instances of another type.</exception>
        public void Add<T>(string entityName, IReadOnlyList<T> entities, IReadOnlySet<string> projection)
            where T : class
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(projection);

            object[] added = new object[entities.Count];
            for (int i = 0; i < added.Length; i++)
            {
                added[i] = entities[i] ?? throw new ArgumentNullException(nameof(entities), "The list must not hold a null entry.");
            }

            // --- A new set ---
            if (!_sets.TryGetValue(entityName, out RelatedEntitySet? existing))
            {
                _sets[entityName] = new RelatedEntitySet(typeof(T), Array.AsReadOnly(added), projection.ToFrozenSet(StringComparer.Ordinal));
                return;
            }

            // --- An existing set: one type per entity, and only the columns every instance carries ---
            if (existing.EntityType != typeof(T))
            {
                throw new InvalidOperationException(
                    $"The related set '{entityName}' holds {existing.EntityType.Name} instances, so {typeof(T).Name} instances cannot join it.");
            }

            var narrowed = existing.Projection.Where(projection.Contains).ToFrozenSet(StringComparer.Ordinal);
            _sets[entityName] = new RelatedEntitySet(typeof(T), Array.AsReadOnly([.. existing.Entities, .. added]), narrowed);
        }
    }
}
