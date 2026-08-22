// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     An immutable description of the entities a query root can reach.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Built once per model version through <see cref="QueryexSchemaBuilder" />, which
    ///         validates the host's input and resolves cross-references. A malformed schema is a
    ///         host bug, so it throws at build time and never becomes a user-facing diagnostic.
    ///     </para>
    ///     <para>
    ///         <b>The contract assumes enforced referential integrity.</b> A non-null foreign key is
    ///         taken to reference an existing row: that is what turns it into an inner join, and
    ///         what lets a join be dropped when nothing reads its target. Both silently lose or
    ///         restore rows wherever a dangling reference exists. Like the nullability and
    ///         uniqueness flags, this is a truthfulness obligation on the host, not something the
    ///         engine can check.
    ///     </para>
    ///     <para>
    ///         Deliberately absent: collection navigations, computed properties, and any column
    ///         whose store type the language has no type for. A host that needs one of those
    ///         exposes it as a column of a view.
    ///     </para>
    /// </remarks>
    public sealed class QueryexSchema
    {
        /// <summary>Lookup by logical entity name, case-insensitively.</summary>
        private readonly FrozenDictionary<string, EntityDescriptor> _entitiesByName;

        /// <summary>Initializes the schema. Built through <see cref="QueryexSchemaBuilder" />.</summary>
        /// <param name="version">The model version discriminator.</param>
        /// <param name="entities">The entities, in registration order.</param>
        internal QueryexSchema(string version, IReadOnlyList<EntityDescriptor> entities)
        {
            Version = version;
            Entities = entities;
            _entitiesByName = entities.ToFrozenDictionary(
                static entity => entity.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     An opaque version discriminator that participates in every cache key.
        /// </summary>
        /// <remarks>
        ///     Must change whenever any logical name, physical name, type, nullability, uniqueness,
        ///     or relationship changes — a content hash of the model is the natural choice. It is
        ///     the invalidation lever the caches depend on: bound expressions hold descriptors
        ///     rather than names, so a host that renamed a column without moving the version would
        ///     keep emitting the old one.
        /// </remarks>
        public string Version { get; }

        /// <summary>The entities, in registration order.</summary>
        public IReadOnlyList<EntityDescriptor> Entities { get; }

        /// <summary>Finds an entity by logical name, case-insensitively.</summary>
        /// <param name="name">The logical entity name.</param>
        /// <returns>The entity, or null when the schema declares none by that name.</returns>
        public EntityDescriptor? FindEntity(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            return _entitiesByName.GetValueOrDefault(name);
        }

        /// <summary>Whether the given entity belongs to this schema.</summary>
        /// <param name="entity">The entity to test.</param>
        /// <returns>True when this schema declared it.</returns>
        internal bool Contains(EntityDescriptor entity)
        {
            // Reference equality, not name equality: two schema versions can both declare "Invoice"
            // while describing different columns, and binding against the wrong one would emit SQL
            // for a table shape that is no longer there.
            return _entitiesByName.TryGetValue(entity.Name, out EntityDescriptor? found)
                && ReferenceEquals(found, entity);
        }
    }
}
