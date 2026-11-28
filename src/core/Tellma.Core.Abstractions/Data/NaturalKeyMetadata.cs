// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A single-column natural key of an entity: a unique scalar that identifies a row by value, the
    ///     row key of spreadsheet imports and exports and of key restrictions.
    /// </summary>
    /// <param name="Property">The key's property.</param>
    /// <param name="IsDeclared">
    ///     True for a key declared with <see cref="NaturalKeyAttribute" />; false for one inferred from a
    ///     unique string column.
    /// </param>
    /// <param name="Order">
    ///     The key's rank among the entity's natural keys, from 0: the declared keys first, by their
    ///     declared order and then their declaration order, followed by the inferred ones.
    /// </param>
    public sealed record NaturalKeyMetadata(PropertyMetadata Property, bool IsDeclared, int Order);
}
