// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // Spec 0012 §11.1: the map beside each schema lets the materializer find the CLR type behind a
    // descriptor without an engine change.
    /// <summary>
    ///     One multilingual shape's query schema, with the maps between its entities and the model's
    ///     entity metadata, and the columns each entity has that the schema leaves out on purpose.
    /// </summary>
    internal sealed class QueryexSchemaMap
    {
        /// <summary>Each entity's descriptor, by its metadata.</summary>
        private readonly FrozenDictionary<EntityMetadata, EntityDescriptor> _descriptors;

        /// <summary>Each entity's metadata, by its descriptor.</summary>
        private readonly FrozenDictionary<EntityDescriptor, EntityMetadata> _metadata;

        /// <summary>Each entity's JSON columns, by its metadata.</summary>
        private readonly FrozenDictionary<EntityMetadata, IReadOnlyList<PropertyMetadata>> _jsonColumns;

        /// <summary>Each entity's properties the schema declares, by its metadata.</summary>
        private readonly FrozenDictionary<EntityMetadata, IReadOnlyList<PropertyMetadata>> _declared;

        /// <summary>Creates the map.</summary>
        /// <param name="shape">The multilingual shape.</param>
        /// <param name="schema">The shape's schema.</param>
        /// <param name="entities">Every entity: its metadata, its descriptor, its JSON columns and the properties the schema declares.</param>
        public QueryexSchemaMap(
            MultilingualShape shape,
            QueryexSchema schema,
            IReadOnlyList<(EntityMetadata Metadata, EntityDescriptor Descriptor, IReadOnlyList<PropertyMetadata> JsonColumns, IReadOnlyList<PropertyMetadata> Declared)> entities)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(entities);
            Shape = shape;
            Schema = schema;
            _descriptors = entities.ToFrozenDictionary(static entity => entity.Metadata, static entity => entity.Descriptor);
            _metadata = entities.ToFrozenDictionary(static entity => entity.Descriptor, static entity => entity.Metadata);
            _jsonColumns = entities.ToFrozenDictionary(static entity => entity.Metadata, static entity => entity.JsonColumns);
            _declared = entities.ToFrozenDictionary(static entity => entity.Metadata, static entity => entity.Declared);
        }

        /// <summary>The multilingual shape the schema serves.</summary>
        public MultilingualShape Shape { get; }

        /// <summary>The schema.</summary>
        public QueryexSchema Schema { get; }

        /// <summary>The descriptor of an entity.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The descriptor.</returns>
        /// <exception cref="ArgumentException">The entity is not one of the model's.</exception>
        public EntityDescriptor Descriptor(EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return _descriptors.TryGetValue(metadata, out EntityDescriptor? descriptor)
                ? descriptor
                : throw new ArgumentException($"The entity {metadata.Name} is not one the query schema declares.", nameof(metadata));
        }

        /// <summary>The metadata of an entity of the schema.</summary>
        /// <param name="descriptor">The entity's descriptor.</param>
        /// <returns>The metadata.</returns>
        /// <exception cref="ArgumentException">The descriptor is not one of this schema's.</exception>
        public EntityMetadata Metadata(EntityDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return _metadata.TryGetValue(descriptor, out EntityMetadata? metadata)
                ? metadata
                : throw new ArgumentException(
                    $"The entity {descriptor.Name} is not one of the batch's query schema; take the root from the schema of the tenant's shape.",
                    nameof(descriptor));
        }

        /// <summary>
        ///     The entity's JSON columns, which the schema omits and an every-property read asks for
        ///     as undeclared columns, in declaration order.
        /// </summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The JSON properties; empty when it has none.</returns>
        public IReadOnlyList<PropertyMetadata> JsonColumns(EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return _jsonColumns.GetValueOrDefault(metadata) ?? [];
        }

        /// <summary>The entity's properties the schema declares under this shape, in declaration order.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The properties.</returns>
        public IReadOnlyList<PropertyMetadata> Declared(EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return _declared.GetValueOrDefault(metadata) ?? [];
        }
    }
}
