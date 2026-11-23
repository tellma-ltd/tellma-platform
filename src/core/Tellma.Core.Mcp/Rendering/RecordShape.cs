// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>The child collections of a record's type, at every depth: what a get windows before it writes any JSON.</summary>
    internal sealed class RecordShape
    {
        /// <summary>The shapes built from metadata, by entity type.</summary>
        private static readonly ConcurrentDictionary<Type, RecordShape> Shapes = new();

        /// <summary>Creates a shape.</summary>
        /// <param name="collections">The type's child collections, in declaration order.</param>
        public RecordShape(IReadOnlyList<CollectionShape> collections)
        {
            ArgumentNullException.ThrowIfNull(collections);
            Collections = collections;
        }

        /// <summary>The shape of a type without child collections.</summary>
        public static RecordShape Empty { get; } = new([]);

        /// <summary>The type's child collections, in declaration order.</summary>
        public IReadOnlyList<CollectionShape> Collections { get; }

        /// <summary>The shape of an entity, from its metadata, built once per type.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The shape.</returns>
        public static RecordShape Of(EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            return Shapes.GetOrAdd(metadata.ClrType, static (_, entity) => Build(entity), metadata);
        }

        /// <summary>Builds an entity's shape; the aggregate rules keep the children's chain finite.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The shape.</returns>
        private static RecordShape Build(EntityMetadata metadata)
        {
            JsonNamingPolicy? naming = TellmaJson.Options.PropertyNamingPolicy;
            return new RecordShape([.. metadata.Children.Select(child => new CollectionShape(
                naming?.ConvertName(child.Property) ?? child.Property,
                metadata.ClrType.GetProperty(child.Property, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"The entity {metadata.Name} declares the child collection {child.Property}, which its type does not have."),
                Of(child.Child)))]);
        }
    }

    /// <summary>One child collection of a record's type: its property, its JSON name, and the shape of its rows.</summary>
    internal sealed class CollectionShape
    {
        /// <summary>The list type the property holds, which a cut collection is written back as.</summary>
        private readonly Type _listType;

        /// <summary>Creates the shape of a collection.</summary>
        /// <param name="name">The collection's JSON name, such as <c>lines</c>.</param>
        /// <param name="property">The property that holds it: a readable and writable list.</param>
        /// <param name="rows">The shape of its rows.</param>
        /// <exception cref="ArgumentException">The property cannot hold a <see cref="List{T}" />, or is not readable and writable.</exception>
        public CollectionShape(string name, PropertyInfo property, RecordShape rows)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(rows);
            Type? element = property.PropertyType.IsGenericType && property.PropertyType.GetGenericArguments() is [Type argument] ? argument : null;
            Type? list = element is null ? null : typeof(List<>).MakeGenericType(element);
            if (list is null || !property.PropertyType.IsAssignableFrom(list) || !property.CanRead || !property.CanWrite)
            {
                throw new ArgumentException($"The collection {property.DeclaringType?.Name}.{property.Name} is not a readable and writable list.", nameof(property));
            }

            Name = name;
            Property = property;
            Rows = rows;
            _listType = list;
        }

        /// <summary>The collection's JSON name.</summary>
        public string Name { get; }

        /// <summary>The property that holds the collection.</summary>
        public PropertyInfo Property { get; }

        /// <summary>The shape of the collection's rows.</summary>
        public RecordShape Rows { get; }

        /// <summary>Reads a record's collection.</summary>
        /// <param name="record">The record.</param>
        /// <returns>The rows, or null when the record carries none.</returns>
        public IList? Read(object record)
        {
            ArgumentNullException.ThrowIfNull(record);
            return (IList?)Property.GetValue(record);
        }

        /// <summary>Replaces a record's collection with the given rows.</summary>
        /// <param name="record">The record.</param>
        /// <param name="rows">The rows it now carries.</param>
        public void Write(object record, IEnumerable<object?> rows)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(rows);
            var list = (IList)Activator.CreateInstance(_listType)!;
            foreach (object? row in rows)
            {
                _ = list.Add(row);
            }

            Property.SetValue(record, list);
        }
    }
}
