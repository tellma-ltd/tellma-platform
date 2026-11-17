// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data;

namespace Tellma.Core.Crud.Save
{
    // The per-entity reading of spec 0015 §6.2 (ownership), §6.3 (preprocessing), §6.6 (the ordered
    // checks) and §7.4 (foreign keys), derived once from the entity's metadata and its class.
    /// <summary>
    ///     What the save pipeline does to each row of one entity: which properties it trims, resets,
    ///     guards, canonicalises, checks for uniqueness, and which references it validates.
    /// </summary>
    internal sealed class RowRules
    {
        /// <summary>The rules of each entity, built on first use and dropped with the entity's metadata.</summary>
        private static readonly ConditionalWeakTable<EntityMetadata, RowRules> Built = [];

        /// <summary>The key types a key-set table holds, so a uniqueness load can restrict on them.</summary>
        private static readonly HashSet<Type> KeyableTypes = [typeof(int), typeof(long), typeof(short), typeof(byte), typeof(Guid), typeof(string), typeof(DateOnly)];

        /// <summary>The setters of child collections, by entity class and collection property, built on first use.</summary>
        private static readonly ConcurrentDictionary<(Type Type, string Property), Action<object, object?>> CollectionSetters = new();

        /// <summary>Builds the rules of an entity.</summary>
        /// <param name="entity">The entity's metadata.</param>
        private RowRules(EntityMetadata entity)
        {
            Entity = entity;
            static bool Written(PropertyMetadata property)
            {
                return property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce;
            }

            // --- Strings the client writes are trimmed, unless the property preserves its whitespace ---
            Trimmed = [.. entity.Properties.Where(property => property.ClrType == typeof(string) && Written(property) && !PreservesWhitespace(entity, property))];

            // --- Ownership: the server-owned columns the before image restores, the write-once ones it guards, the defaulted ones a hook fills ---
            ServerOwned = [.. entity.Properties.Where(property => property.Ownership == PropertyOwnership.ServerOwned && !ReferenceEquals(property, entity.Key))];
            WriteOnce = [.. entity.Properties.Where(static property => property.Ownership == PropertyOwnership.WriteOnce)];
            Defaulted = [.. entity.Properties.Where(property => property.IsDefaulted && Written(property))];
            ValueSets = [.. entity.Properties.Where(property => property.ValueSet is not null && property.ClrType == typeof(string) && Written(property))];
            Stamp = entity.IsTopLevel ? entity.Properties.FirstOrDefault(static property => property.Name == nameof(TopLevelEntity.ModifiedAt)) : null;

            // --- The twins of each multilingual group, by the language position each needs ---
            List<(PropertyMetadata Twin, int Position)> twins = [];
            foreach (MultilingualGroup group in entity.MultilingualGroups)
            {
                if (group.Secondary is PropertyMetadata secondary)
                {
                    twins.Add((secondary, 2));
                }

                if (group.Tertiary is PropertyMetadata tertiary)
                {
                    twins.Add((tertiary, 3));
                }
            }

            Twins = [.. twins];

            // --- The display-name prefix, when it is text ---
            Prefix = entity.DisplayName.Prefix is { } prefix && prefix.ClrType == typeof(string) ? prefix : null;

            // --- The references a client sets: never a server-owned audit column, a blob column or a derived one ---
            HashSet<PropertyMetadata> blobs = new(entity.BlobReferences.Select(static blob => blob.Property), ReferenceEqualityComparer.Instance);
            References = [.. entity.References.Where(reference => Written(reference.ForeignKey) && !blobs.Contains(reference.ForeignKey))];

            // --- Uniqueness: every unique property but the key, of a type a key-set table holds ---
            Unique = [.. entity.Properties.Where(property =>
                property.IsUnique
                && !ReferenceEquals(property, entity.Key)
                && property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce or PropertyOwnership.Derived
                && KeyableTypes.Contains(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType))];
        }

        /// <summary>The entity.</summary>
        public EntityMetadata Entity { get; }

        /// <summary>The string properties the client writes and the pipeline trims.</summary>
        public PropertyMetadata[] Trimmed { get; }

        /// <summary>The server-owned properties but the key, which an update restores from its before image.</summary>
        public PropertyMetadata[] ServerOwned { get; }

        /// <summary>The write-once properties.</summary>
        public PropertyMetadata[] WriteOnce { get; }

        /// <summary>The client-written properties that may arrive empty for a hook to fill.</summary>
        public PropertyMetadata[] Defaulted { get; }

        /// <summary>The client-written string properties that draw from a value set.</summary>
        public PropertyMetadata[] ValueSets { get; }

        /// <summary>The concurrency stamp of a top-level entity; null otherwise.</summary>
        public PropertyMetadata? Stamp { get; }

        /// <summary>The multilingual twins, each with the 1-based language position the tenant must configure for it.</summary>
        public (PropertyMetadata Twin, int Position)[] Twins { get; }

        /// <summary>The display-name prefix, when it is a string; null otherwise.</summary>
        public PropertyMetadata? Prefix { get; }

        /// <summary>The references whose foreign key the client sets: no audit, derived, database-owned or blob column.</summary>
        public ReferenceMetadata[] References { get; }

        /// <summary>The unique properties but the key whose values a key-set table holds.</summary>
        public PropertyMetadata[] Unique { get; }

        /// <summary>The rules of an entity.</summary>
        /// <param name="entity">The entity's metadata.</param>
        /// <returns>The rules, built once per metadata instance.</returns>
        public static RowRules Of(EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return Built.GetValue(entity, static metadata => new RowRules(metadata));
        }

        /// <summary>Whether a unique property is unique on its own, rather than within its parent.</summary>
        /// <param name="property">The property, one of <see cref="Unique" />.</param>
        /// <returns>True when a unique index holds the property alone.</returns>
        public bool IsGloballyUnique(PropertyMetadata property)
        {
            return Entity.UniqueIndexes.Any(index => index.Properties.Count == 1 && ReferenceEquals(index.Properties[0], property));
        }

        /// <summary>Sets a child collection of a row, nulling it when the collection is absent from the payload.</summary>
        /// <param name="row">The row.</param>
        /// <param name="collection">The collection.</param>
        /// <param name="value">The collection's new value, or null.</param>
        public static void SetCollection(object row, ChildCollectionMetadata collection, object? value)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(collection);
            Action<object, object?> setter = CollectionSetters.GetOrAdd((row.GetType(), collection.Property), static key =>
            {
                PropertyInfo property = key.Type.GetProperty(key.Property, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"The class {key.Type.Name} has no public collection property {key.Property}.");
                return property.CanWrite
                    ? property.SetValue
                    : throw new InvalidOperationException($"The collection property {key.Type.Name}.{key.Property} has no setter, so the pipeline cannot shape its before image.");
            });
            setter(row, value);
        }

        /// <summary>Whether a string property keeps its whitespace: its CLR property carries <see cref="PreserveWhitespaceAttribute" />.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property.</param>
        /// <returns>True when it does.</returns>
        private static bool PreservesWhitespace(EntityMetadata entity, PropertyMetadata property)
        {
            PropertyInfo? clr = entity.ClrType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);
            return clr is not null && EntityShapes.GetAttribute<PreserveWhitespaceAttribute>(clr) is not null;
        }
    }
}
