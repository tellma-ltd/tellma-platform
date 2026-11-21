// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     An entity query: the entities of one type, with their child collections and the related
    ///     entities their selected navigations reach, materialised after the batch executes.
    /// </summary>
    /// <typeparam name="TEntity">The entity type; a replaceable default resolves to its registered leaf.</typeparam>
    public sealed class EntityQuery<TEntity>
        where TEntity : class
    {
        /// <summary>The selected paths: bare paths only, comma separated.</summary>
        public required string Select { get; set; }

        /// <summary>The filter, or null for every row.</summary>
        public FilterTree? Filter { get; set; }

        /// <summary>The ordering, or null for the key's.</summary>
        public string? OrderBy { get; set; }

        /// <summary>How many rows to skip, or null for none.</summary>
        public int? Skip { get; set; }

        /// <summary>How many rows to return, or null for all.</summary>
        public int? Take { get; set; }

        /// <summary>The query's declared parameters, or null for none.</summary>
        public QueryArguments? Arguments { get; set; }

        /// <summary>Key-set restrictions: the roots must have keys in each named table.</summary>
        public IReadOnlyList<KeySetRestriction> Restrictions { get; set; } = [];

        /// <summary>
        ///     The child collections to load, by their unmapped collection property names; dotted for
        ///     a grandchild collection.
        /// </summary>
        public IReadOnlyList<string> Children { get; set; } = [];

        /// <summary>
        ///     Select every mapped property of the root and of each loaded collection, the JSON columns
        ///     included.
        /// </summary>
        public bool AllProperties { get; set; }

        /// <summary>For a tree's roots only: also load the ancestors of the returned entities.</summary>
        public bool IncludeAncestors { get; set; }

        /// <summary>
        ///     The access filter alone, applied to the ancestors; null when the caller's access is
        ///     unrestricted.
        /// </summary>
        public FilterTree? AncestorsFilter { get; set; }
    }
}
