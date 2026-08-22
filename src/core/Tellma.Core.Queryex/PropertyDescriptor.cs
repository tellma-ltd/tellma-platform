// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One scalar property of an entity.
    /// </summary>
    /// <remarks>
    ///     Carries two names that are never interchangeable: a logical one that expression authors
    ///     write and the binder resolves, and a physical one that only the emitter reads. Keeping
    ///     them apart as separate fields is what makes leaking a logical name into SQL structurally
    ///     impossible rather than a discipline to maintain.
    /// </remarks>
    public sealed class PropertyDescriptor
    {
        /// <summary>Initializes the descriptor. Built through <see cref="QueryexSchemaBuilder" />.</summary>
        /// <param name="name">The logical property name.</param>
        /// <param name="column">The physical column name, unquoted.</param>
        /// <param name="type">The property's type.</param>
        /// <param name="isNotNull">Whether the column cannot hold an absent value.</param>
        /// <param name="isUnique">Whether the column is backed by a unique constraint or index.</param>
        /// <param name="storeType">The physical column type, or null for the default mapping.</param>
        internal PropertyDescriptor(
            string name,
            string column,
            QueryexType type,
            bool isNotNull,
            bool isUnique,
            QueryexStoreType? storeType)
        {
            Name = name;
            Column = column;
            Type = type;
            IsNotNull = isNotNull;
            IsUnique = isUnique;
            StoreType = storeType;
        }

        /// <summary>The logical property name, as expression authors write it.</summary>
        public string Name { get; }

        /// <summary>
        ///     The physical column name, unquoted. Host-authored; the emitter quotes it and it is
        ///     one of only two pieces of host text that ever reach emitted SQL.
        /// </summary>
        public string Column { get; }

        /// <summary>The property's type in the language.</summary>
        public QueryexType Type { get; }

        /// <summary>
        ///     True when the column cannot hold an absent value. The nullity analysis builds on it,
        ///     and the emitter omits guards on its strength, so it must be truthful.
        /// </summary>
        public bool IsNotNull { get; }

        /// <summary>
        ///     True when the column is backed by a unique constraint or index. Must be truthful:
        ///     the hierarchy predicates depend on it to identify at most one row per key.
        /// </summary>
        /// <remarks>An entity's key is treated as unique however the host declared it.</remarks>
        public bool IsUnique { get; internal set; }

        /// <summary>
        ///     The physical column type, structured rather than free text. Consulted only to type
        ///     the parameter slots whose values are compared against this column; the language's
        ///     semantics never read it. Null applies the default mapping for <see cref="Type" />.
        /// </summary>
        public QueryexStoreType? StoreType { get; }

        /// <summary>Returns the logical name, for diagnostics and debugging.</summary>
        /// <returns>The logical property name.</returns>
        public override string ToString()
        {
            return Name;
        }
    }
}
