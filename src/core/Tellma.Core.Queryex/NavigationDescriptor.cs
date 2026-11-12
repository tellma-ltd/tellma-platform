// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One navigation: a foreign key on the declaring entity referencing the target's key.
    /// </summary>
    /// <remarks>
    ///     Every navigation is many-to-one. There is no parent-to-child collection concept, which is
    ///     also what makes aggregation sound with no cardinality analysis: the join tree cannot fan
    ///     out, so aggregates over the root grain never double-count.
    /// </remarks>
    public sealed class NavigationDescriptor
    {
        /// <summary>Initializes the descriptor. Built through <see cref="QueryexSchemaBuilder" />.</summary>
        /// <param name="name">The logical navigation name.</param>
        /// <param name="isOptional">Whether the target row may be missing although the foreign key is present.</param>
        internal NavigationDescriptor(string name, bool isOptional)
        {
            Name = name;
            IsOptional = isOptional;
        }

        /// <summary>The logical navigation name, as expression authors write it.</summary>
        public string Name { get; }

        /// <summary>
        ///     The target entity. A join condition equates <see cref="ForeignKey" /> with the
        ///     target's key.
        /// </summary>
        /// <remarks>
        ///     Resolved when the schema is built, because entities reference each other freely and
        ///     self-referencing hierarchies make cycles ordinary rather than exceptional.
        /// </remarks>
        public EntityDescriptor Target { get; internal set; } = null!;

        /// <summary>
        ///     The foreign-key property on the declaring entity. Whether it can hold an absent value
        ///     drives both the kind of join emitted and the nullity of any path through it.
        /// </summary>
        public PropertyDescriptor ForeignKey { get; internal set; } = null!;

        /// <summary>
        ///     Whether the target row may be missing even where <see cref="ForeignKey" /> holds a
        ///     value, such as a one-to-one extension table keyed by the declaring entity's own key.
        /// </summary>
        /// <remarks>
        ///     Such a step is joined as an optional one and makes every path through it nullable,
        ///     exactly as a nullable foreign key does: an inner join would silently drop the rows
        ///     that have no target.
        /// </remarks>
        public bool IsOptional { get; }

        /// <summary>Whether the step may find no target row: its foreign key may be absent, or it is optional.</summary>
        internal bool MayBeMissing => IsOptional || !ForeignKey.IsNotNull;

        /// <summary>Returns the logical name, for diagnostics and debugging.</summary>
        /// <returns>The logical navigation name.</returns>
        public override string ToString()
        {
            return Name;
        }
    }
}
