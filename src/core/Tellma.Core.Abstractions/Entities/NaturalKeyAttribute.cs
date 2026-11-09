// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     Declares a single-column natural key: a scalar that identifies a row by value, used as the
    ///     row key of imports and exports and in key restrictions. Implies <see cref="UniqueAttribute" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The property's type must have a key-list table type: <see cref="string" />,
    ///         <see cref="int" />, <see cref="long" />, <see cref="Guid" /> or <see cref="DateOnly" />.
    ///         The column is backed by a unique index, <c>UX_&lt;Table&gt;_&lt;Column&gt;</c>, filtered to
    ///         non-null values when nullable; on a child the index is scoped to the parent.
    ///     </para>
    ///     <para>
    ///         Without any declaration an entity's natural key is inferred over its unique string
    ///         columns. <see cref="Order" /> orders several declared keys; composite keys are reserved
    ///         behind it.
    ///     </para>
    /// </remarks>
    /// <param name="order">The key's position among the entity's declared natural keys.</param>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class NaturalKeyAttribute(int order = 0) : Attribute
    {
        /// <summary>The key's position among the entity's declared natural keys.</summary>
        public int Order { get; set; } = order;
    }
}
