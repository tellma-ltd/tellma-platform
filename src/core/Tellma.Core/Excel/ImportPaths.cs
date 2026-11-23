// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Excel
{
    /// <summary>What a column path names on its sheet's entity.</summary>
    internal enum ImportPathKind
    {
        /// <summary>The row's identity: <c>Id</c>.</summary>
        Id,

        /// <summary>The parent row's concurrency stamp: <c>Stamp</c>.</summary>
        Stamp,

        /// <summary>A scalar, or one slot of a multilingual group: <c>Code</c>, <c>Name2</c>.</summary>
        Property,

        /// <summary>A foreign key in a key of its target: <c>Parent.Code</c>, <c>Center.Id</c>.</summary>
        Reference,

        /// <summary>A child sheet's link to its owner row, matched within the workbook: <c>User.Id</c>.</summary>
        ParentReference,

        /// <summary>A bare navigation, as a display export writes its target's display name: <c>Parent</c>. Never importable.</summary>
        Navigation,
    }

    /// <summary>
    ///     A column path resolved against its sheet's entity and the target tenant's languages: what
    ///     it names, the property it writes, and the path the plan reports for it.
    /// </summary>
    /// <param name="Kind">What the path names.</param>
    /// <param name="Text">
    ///     The resolved path: the target slot of a multilingual column (<c>Name2</c>), the key of a
    ///     reference (<c>Parent.Code</c>), <c>Center.Id</c> for a bare foreign key.
    /// </param>
    /// <param name="Property">
    ///     The property the column writes: the key for <c>Id</c>, the modification time for
    ///     <c>Stamp</c>, the slot for a multilingual column, the foreign key for a reference.
    /// </param>
    /// <param name="Reference">The reference a reference or navigation names; null for any other path, and for a parent link without a navigation.</param>
    /// <param name="Key">The target property a reference is expressed in, its slot for a multilingual key; null for any other path.</param>
    /// <param name="Language">The tenant language of the slot a multilingual column or key resolved to; null for a column of none.</param>
    internal sealed record ImportPath(
        ImportPathKind Kind,
        string Text,
        PropertyMetadata Property,
        ReferenceMetadata? Reference,
        PropertyMetadata? Key,
        string? Language);

    /// <summary>
    ///     A column path's resolution: the path, or the language the target tenant does not
    ///     configure, or neither for a path the entity does not carry.
    /// </summary>
    /// <param name="Path">The resolved path; null when it did not resolve.</param>
    /// <param name="UnconfiguredLanguage">The column's language when the tenant configures no slot for it; null otherwise.</param>
    internal readonly record struct ImportPathResolution(ImportPath? Path, string? UnconfiguredLanguage)
    {
        /// <summary>Whether the path names nothing the entity carries.</summary>
        public bool IsUnknown => Path is null && UnconfiguredLanguage is null;
    }

    /// <summary>One collection path of an entity, with the collection it ends at.</summary>
    /// <param name="Path">The dotted path from the stack's entity (<c>WidgetParts.WidgetPartNotes</c>).</param>
    /// <param name="Owner">The entity declaring the collection.</param>
    /// <param name="Collection">The collection.</param>
    /// <param name="OwnerPath">The owner's collection path; empty for the stack's entity.</param>
    /// <param name="Labels">The collections' properties along the path, from the stack's entity down.</param>
    internal sealed record ImportCollection(string Path, EntityMetadata Owner, ChildCollectionMetadata Collection, string OwnerPath, IReadOnlyList<(EntityMetadata Owner, string Collection)> Labels);

    // Spec 0019 §8.3: the one grammar that names columns in the manifest, the plan and the overrides.
    // A path is relative to its sheet's entity; a multilingual group's primary with a language is that
    // language's slot in the target tenant (§8.2), without one the primary slot, and a twin's name is
    // its slot whatever the language. A bare foreign key is accepted for <Navigation>.Id and never
    // written back; a child sheet's parent link is always <Navigation>.Id. A bare navigation, which a
    // display export writes as the target's display name, resolves so the column reads as server-owned
    // rather than unknown.
    /// <summary>The column and collection path grammar of an import, resolved against an entity's metadata.</summary>
    internal static class ImportPaths
    {
        /// <summary>The path of the identity column.</summary>
        public const string Id = ExportPlanner.IdColumn;

        /// <summary>The path of the stamp column.</summary>
        public const string Stamp = ExportPlanner.StampColumn;

        /// <summary>The deepest collection path an import follows.</summary>
        public const int MaxCollectionDepth = 8;

        /// <summary>Resolves a column path.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="path">The path.</param>
        /// <param name="language">The column's language, from the manifest or a header's symbol; null for none.</param>
        /// <param name="tenantLanguages">The target tenant's content languages, in slot order.</param>
        /// <returns>The resolution.</returns>
        public static ImportPathResolution Resolve(EntityMetadata entity, ChildCollectionMetadata? collection, string path, string? language, IReadOnlyList<string> tenantLanguages)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(tenantLanguages);

            // A path is one segment, or a navigation and a key of its target; anything deeper names nothing.
            string[] segments = path.Split('.');
            return segments.Any(static segment => segment.Length == 0) || segments.Length > 2
                ? default
                : segments.Length == 1
                    ? Single(entity, collection, segments[0], language, tenantLanguages)
                    : Pair(entity, collection, segments[0], segments[1], language, tenantLanguages);
        }

        /// <summary>
        ///     Whether a resolved path is importable from its sheet: an identity, a stamp, a parent link,
        ///     or a property or foreign key the import writes. Any other path is server-owned.
        /// </summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="path">The resolved path.</param>
        /// <returns>True when the import writes it.</returns>
        public static bool IsImportable(EntityMetadata entity, ChildCollectionMetadata? collection, ImportPath path)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(path);
            return path.Kind switch
            {
                ImportPathKind.Id or ImportPathKind.Stamp or ImportPathKind.ParentReference => true,
                ImportPathKind.Property or ImportPathKind.Reference => ExportPlanner.IsEditable(entity, path.Property, collection),
                ImportPathKind.Navigation => false,
                _ => false,
            };
        }

        /// <summary>Every collection path of an entity, depth first in declaration order, to the deepest an import follows.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <returns>The collection paths.</returns>
        public static IReadOnlyList<ImportCollection> Collections(EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            List<ImportCollection> collections = [];
            Add(entity, string.Empty, [], depth: 1);
            return collections;

            void Add(EntityMetadata owner, string ownerPath, IReadOnlyList<(EntityMetadata, string)> labels, int depth)
            {
                if (depth > MaxCollectionDepth)
                {
                    return;
                }

                foreach (ChildCollectionMetadata collection in owner.Children)
                {
                    string path = ownerPath.Length == 0 ? collection.Property : $"{ownerPath}.{collection.Property}";
                    List<(EntityMetadata, string)> chain = [.. labels, (owner, collection.Property)];
                    collections.Add(new ImportCollection(path, owner, collection, ownerPath, chain));
                    Add(collection.Child, path, chain, depth + 1);
                }
            }
        }

        /// <summary>The navigation a child sheet's parent link is named by: the reference over the parent key, else the key itself.</summary>
        /// <param name="collection">The collection.</param>
        /// <returns>The navigation's name.</returns>
        public static string ParentNavigation(ChildCollectionMetadata collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            return collection.Child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name)?.Navigation
                ?? collection.ParentKey.Name;
        }

        /// <summary>The group a property belongs to, with its one-based slot.</summary>
        /// <param name="entity">The entity declaring it.</param>
        /// <param name="property">The property's name.</param>
        /// <returns>The group and slot; null for a property outside every group.</returns>
        public static (MultilingualGroup Group, int Slot)? Slot(EntityMetadata entity, string property)
        {
            ArgumentNullException.ThrowIfNull(entity);
            foreach (MultilingualGroup group in entity.MultilingualGroups)
            {
                int slot = group.Primary.Name == property ? 1 : group.Secondary?.Name == property ? 2 : group.Tertiary?.Name == property ? 3 : 0;
                if (slot > 0)
                {
                    return (group, slot);
                }
            }

            return null;
        }

        /// <summary>Resolves a path of one segment: the identity, the stamp, a scalar, a slot, a bare foreign key or a navigation.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="name">The segment.</param>
        /// <param name="language">The column's language.</param>
        /// <param name="tenantLanguages">The tenant's languages.</param>
        /// <returns>The resolution.</returns>
        private static ImportPathResolution Single(EntityMetadata entity, ChildCollectionMetadata? collection, string name, string? language, IReadOnlyList<string> tenantLanguages)
        {
            // --- The identity, and the parent sheet's stamp ---
            if (name == Id || name == entity.Key.Name)
            {
                return Of(ImportPathKind.Id, Id, entity.Key);
            }

            if (name == Stamp && collection is null && entity.Properties.FirstOrDefault(static property => property.Name == ExportPlanner.StampSource) is PropertyMetadata modified)
            {
                return Of(ImportPathKind.Stamp, Stamp, modified);
            }

            PropertyMetadata? property = entity.Properties.FirstOrDefault(candidate => candidate.Name == name);
            if (property is null)
            {
                // --- A bare navigation: the display name a display export writes, never importable ---
                return entity.References.FirstOrDefault(reference => reference.Navigation == name) is ReferenceMetadata navigation
                    ? Of(ImportPathKind.Navigation, navigation.Navigation, navigation.ForeignKey, navigation)
                    : default;
            }

            // --- A bare foreign key: the parent link on a child sheet, else the reference by its surrogate ---
            if (collection is not null && property.Name == collection.ParentKey.Name)
            {
                return ParentLink(collection);
            }

            if (entity.References.FirstOrDefault(reference => reference.ForeignKey.Name == property.Name) is ReferenceMetadata foreign)
            {
                return Of(ImportPathKind.Reference, $"{foreign.Navigation}.{Id}", property, foreign, foreign.Target.Key);
            }

            // --- A slot of a multilingual group, or any other scalar ---
            return Slot(entity, property.Name) is (MultilingualGroup group, int slot)
                ? SlotOf(group, slot, language, tenantLanguages) switch
                {
                    (PropertyMetadata twin, var resolved) => Of(ImportPathKind.Property, twin.Name, twin, language: resolved),
                    _ => new ImportPathResolution(null, language),
                }
                : Of(ImportPathKind.Property, property.Name, property);
        }

        /// <summary>Resolves a path of two segments: a reference in a key of its target, or a child sheet's parent link.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="navigation">The first segment.</param>
        /// <param name="keyName">The second segment.</param>
        /// <param name="language">The column's language.</param>
        /// <param name="tenantLanguages">The tenant's languages.</param>
        /// <returns>The resolution.</returns>
        private static ImportPathResolution Pair(
            EntityMetadata entity,
            ChildCollectionMetadata? collection,
            string navigation,
            string keyName,
            string? language,
            IReadOnlyList<string> tenantLanguages)
        {
            // --- The parent link: always in the owner's surrogate, matched within the workbook ---
            if (collection is not null && navigation == ParentNavigation(collection))
            {
                return keyName == Id ? ParentLink(collection) : default;
            }

            if (entity.References.FirstOrDefault(reference => reference.Navigation == navigation) is not ReferenceMetadata reference)
            {
                return default;
            }

            // --- The target's surrogate, or a scalar of the target that fits a cell and a key-set lookup ---
            // Spec 0019 §5.4: a lookup restricts by a key set, so a key whose type cannot be one is no reference key.
            EntityMetadata target = reference.Target;
            if (keyName == Id || keyName == target.Key.Name)
            {
                return Of(ImportPathKind.Reference, $"{navigation}.{Id}", reference.ForeignKey, reference, target.Key);
            }

            if (target.Properties.FirstOrDefault(candidate => candidate.Name == keyName) is not PropertyMetadata key
                || !ExportPlanner.IsKeyable(target, key)
                || !ImportKeys.IsRestrictable(key.ClrType))
            {
                return default;
            }

            // A multilingual key resolves through the slot of the column's language.
            return Slot(target, key.Name) is (MultilingualGroup group, int slot)
                ? SlotOf(group, slot, language, tenantLanguages) switch
                {
                    (PropertyMetadata twin, var resolved) => Of(ImportPathKind.Reference, $"{navigation}.{twin.Name}", reference.ForeignKey, reference, twin, resolved),
                    _ => new ImportPathResolution(null, language),
                }
                : Of(ImportPathKind.Reference, $"{navigation}.{key.Name}", reference.ForeignKey, reference, key);
        }

        /// <summary>
        ///     The slot a multilingual column writes: the slot of its language when the group's primary
        ///     comes with one, the primary slot when it comes with none, and a twin's own slot whatever
        ///     its language.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="slot">The one-based slot the path names.</param>
        /// <param name="language">The column's language.</param>
        /// <param name="tenantLanguages">The tenant's languages.</param>
        /// <returns>The slot's property and its tenant language; a null property when the tenant has no slot of the language in the group.</returns>
        private static (PropertyMetadata? Twin, string? Language) SlotOf(MultilingualGroup group, int slot, string? language, IReadOnlyList<string> tenantLanguages)
        {
            PropertyMetadata?[] slots = [group.Primary, group.Secondary, group.Tertiary];
            if (slot == 1 && language is not null)
            {
                int index = -1;
                for (int i = 0; i < tenantLanguages.Count && i < slots.Length; i++)
                {
                    if (string.Equals(tenantLanguages[i], language, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }

                return index < 0 || slots[index] is not PropertyMetadata twin ? (null, null) : (twin, tenantLanguages[index]);
            }

            return (slots[slot - 1], slot <= tenantLanguages.Count ? tenantLanguages[slot - 1] : null);
        }

        /// <summary>A child sheet's parent link.</summary>
        /// <param name="collection">The collection.</param>
        /// <returns>The resolution.</returns>
        private static ImportPathResolution ParentLink(ChildCollectionMetadata collection)
        {
            ReferenceMetadata? reference = collection.Child.References.FirstOrDefault(candidate => candidate.ForeignKey.Name == collection.ParentKey.Name);
            return Of(ImportPathKind.ParentReference, $"{ParentNavigation(collection)}.{Id}", collection.ParentKey, reference, reference?.Target.Key);
        }

        /// <summary>A resolved path.</summary>
        /// <param name="kind">What it names.</param>
        /// <param name="text">The resolved path text.</param>
        /// <param name="property">The property it writes.</param>
        /// <param name="reference">The reference it names.</param>
        /// <param name="key">The target property a reference is expressed in.</param>
        /// <param name="language">The slot's tenant language.</param>
        /// <returns>The resolution.</returns>
        private static ImportPathResolution Of(
            ImportPathKind kind,
            string text,
            PropertyMetadata property,
            ReferenceMetadata? reference = null,
            PropertyMetadata? key = null,
            string? language = null)
        {
            return new ImportPathResolution(new ImportPath(kind, text, property, reference, key, language), null);
        }
    }
}
