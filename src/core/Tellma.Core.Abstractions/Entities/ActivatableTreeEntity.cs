// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     A tree whose rows can be deactivated, with the active-only subtree size an expander on the
    ///     active-only view reads without a subquery.
    /// </summary>
    /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
    public abstract class ActivatableTreeEntity<TKey> : TreeEntity<TKey>, IActivatable
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <inheritdoc />
        public bool IsActive { get; set; } = true;

        /// <summary>
        ///     The number of active rows in the subtree rooted at this row, the row itself included when
        ///     it is active; server-owned, recounted by the data layer after every write.
        /// </summary>
        public int ActiveSubtreeCount { get; set; }
    }

    /// <summary>An activatable tree entity with an <see cref="int" /> key, the default.</summary>
    public abstract class ActivatableTreeEntity : ActivatableTreeEntity<int>;
}
