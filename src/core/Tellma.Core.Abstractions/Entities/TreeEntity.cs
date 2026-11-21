// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     A top-level entity arranged as a tree through a self-referencing parent key. A leaf extends
    ///     it by plain inheritance and is registered in place of the pack's default like any other
    ///     entity.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ParentId" /> is a foreign key to the same table, configured without a CLR
    ///         navigation; the query navigation <c>Parent</c> derives from the column name like every
    ///         other navigation-less foreign key.
    ///     </para>
    ///     <para>
    ///         The table also carries a <c>Node hierarchyid NOT NULL</c> column with a unique index,
    ///         added by the platform's conventions as a shadow column and never a member: the data layer
    ///         maintains it after every save. There is no level column (the query function
    ///         <c>level(Node)</c> computes it) and no leaf flag (<see cref="SubtreeCount" /> is 1 for a
    ///         leaf). The tree's maximum depth comes from <see cref="TreeAttribute" />.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
    public abstract class TreeEntity<TKey> : TopLevelEntity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The parent row, or <see langword="null" /> for a root; a foreign key to the same table.</summary>
        public TKey? ParentId { get; set; }

        /// <summary>
        ///     The number of rows in the subtree rooted at this row, the row itself included, so 1 for a
        ///     leaf; server-owned, recounted by the data layer after every write.
        /// </summary>
        public int SubtreeCount { get; set; }
    }

    /// <summary>A tree entity with an <see cref="int" /> key, the default.</summary>
    public abstract class TreeEntity : TreeEntity<int>;
}
