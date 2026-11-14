// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     An entity saved only with its owner and never addressed by a service of its own. It has no
    ///     audit columns: a change to a child stamps its parent.
    /// </summary>
    /// <remarks>
    ///     A child declares exactly one owning foreign key, marked <see cref="ParentKeyAttribute" />, and
    ///     its owner carries it as a <c>[NotMapped]</c> list property. Any second foreign key to the
    ///     same parent type is an ordinary reference and never pairs a collection. A child may in turn
    ///     own grandchildren the same way.
    /// </remarks>
    /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
    public abstract class ChildEntity<TKey> : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>;

    /// <summary>A child entity with an <see cref="int" /> key, the default.</summary>
    public abstract class ChildEntity : ChildEntity<int>;
}
