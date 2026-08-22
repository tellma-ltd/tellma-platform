// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One entity: a queryable root, or the target of a navigation.
    /// </summary>
    public sealed class EntityDescriptor
    {
        /// <summary>Lookup by logical property name, case-insensitively.</summary>
        private FrozenDictionary<string, PropertyDescriptor> _propertiesByName =
            FrozenDictionary<string, PropertyDescriptor>.Empty;

        /// <summary>Lookup by logical navigation name, case-insensitively.</summary>
        private FrozenDictionary<string, NavigationDescriptor> _navigationsByName =
            FrozenDictionary<string, NavigationDescriptor>.Empty;

        /// <summary>Initializes the descriptor. Built through <see cref="QueryexSchemaBuilder" />.</summary>
        /// <param name="name">The logical entity name.</param>
        /// <param name="source">The physical source.</param>
        internal EntityDescriptor(string name, string source)
        {
            Name = name;
            Source = source;
        }

        /// <summary>The logical entity name, as expression authors write it.</summary>
        public string Name { get; }

        /// <summary>
        ///     The physical source: a bracket-quoted, schema-qualified table or view name, such as
        ///     <c>[gl].[Invoices]</c>. Host-authored and emitted verbatim, which is why the builder
        ///     validates its shape rather than trusting it.
        /// </summary>
        public string Source { get; }

        /// <summary>
        ///     The single-column surrogate key. Every entity has one: it anchors join conditions and
        ///     the deterministic paging tiebreaker.
        /// </summary>
        public PropertyDescriptor Key { get; internal set; } = null!;

        /// <summary>The scalar properties, in declaration order, including <see cref="Key" />.</summary>
        public IReadOnlyList<PropertyDescriptor> Properties { get; internal set; } = [];

        /// <summary>The navigations, in declaration order.</summary>
        public IReadOnlyList<NavigationDescriptor> Navigations { get; internal set; } = [];

        /// <summary>
        ///     The hierarchy-node property when the entity is hierarchical, which is what enables the
        ///     hierarchy predicates on it. Null otherwise.
        /// </summary>
        public PropertyDescriptor? TreeNode { get; internal set; }

        /// <summary>Finds a scalar property by logical name, case-insensitively.</summary>
        /// <param name="name">The logical property name.</param>
        /// <returns>The property, or null when the entity declares none by that name.</returns>
        internal PropertyDescriptor? FindProperty(string name)
        {
            return _propertiesByName.GetValueOrDefault(name);
        }

        /// <summary>Finds a navigation by logical name, case-insensitively.</summary>
        /// <param name="name">The logical navigation name.</param>
        /// <returns>The navigation, or null when the entity declares none by that name.</returns>
        internal NavigationDescriptor? FindNavigation(string name)
        {
            return _navigationsByName.GetValueOrDefault(name);
        }

        /// <summary>
        ///     Freezes the name lookups once every member has been declared and linked.
        /// </summary>
        internal void BuildLookups()
        {
            // Ordinal-ignore-case rather than culture-aware: resolution has to mean the same thing
            // on every machine, and a culture-sensitive fold would make it depend on the host.
            _propertiesByName = Properties.ToFrozenDictionary(
                static property => property.Name,
                StringComparer.OrdinalIgnoreCase);
            _navigationsByName = Navigations.ToFrozenDictionary(
                static navigation => navigation.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Returns the logical name, for diagnostics and debugging.</summary>
        /// <returns>The logical entity name.</returns>
        public override string ToString()
        {
            return Name;
        }
    }
}
