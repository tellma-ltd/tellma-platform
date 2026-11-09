// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>Phases 4 and 5 of the build: the parts that name other entities, and the warnings.</summary>
    internal sealed partial class EntityMetadataBuilder
    {
        /// <summary>The suffix a foreign-key property's navigation name drops.</summary>
        private const string IdSuffix = "Id";

        /// <summary>
        ///     Pairs every child collection of a leaf with its child's parent key. Reports a collection
        ///     whose child maps no leaf, a parent key that is not a foreign key to the parent, a
        ///     non-positive <see cref="MaxChildrenAttribute" />, and a <c>[NotMapped]</c> list of a class
        ///     that is not a child entity.
        /// </summary>
        /// <param name="leaf">The parent leaf.</param>
        private void PairChildren(Leaf leaf)
        {
            foreach ((PropertyInfo collection, Type childType) in EntityShapes.ChildCollections(leaf.ClrType))
            {
                Type childClr = _definition.LeafOf(childType);
                Leaf? child = _leaves.FirstOrDefault(candidate => candidate.ClrType == childClr && candidate.Metadata is not null);
                if (child is null)
                {
                    Report(
                        $"The child collection {Display(leaf)}.{collection.Name} carries {TypeDisplay.Qualified(childClr)}, which the model maps no leaf for.",
                        $"Correct the child entity {TypeDisplay.Short(childClr)} so that the model maps it.");
                    continue;
                }

                // A child without one parent key was reported when it was described.
                if (child.ParentKey is null || !child.Properties.TryGetValue(child.ParentKey.Name, out PropertyMetadata? parentKey))
                {
                    continue;
                }

                child.Parent = leaf;
                bool referencesParent = child.EntityType.FindProperty(parentKey.Name)!
                    .GetContainingForeignKeys()
                    .Any(foreignKey => foreignKey.PrincipalEntityType == leaf.EntityType);
                if (!referencesParent)
                {
                    Report(
                        $"The parent key {Display(child)}.{parentKey.Name} is not a foreign key to {Display(leaf)}, whose collection {collection.Name} carries the child.",
                        $"Remove the model configuration that redirects the foreign key of {parentKey.Name}.");
                }

                int maxCount = EntityShapes.GetAttribute<MaxChildrenAttribute>(collection)?.Count ?? MaxChildrenAttribute.DefaultCount;
                if (maxCount < 1)
                {
                    Report(
                        $"The child collection {Display(leaf)}.{collection.Name} declares [MaxChildren({maxCount})]; a parent may carry at least one child.",
                        $"Declare a positive cap on {collection.Name}, or none for the default of {MaxChildrenAttribute.DefaultCount:N0}.");
                }

                leaf.Collections.Add(new ChildCollectionMetadata(
                    collection.Name, child.Metadata!, parentKey, maxCount, PropertyAccessors.CollectionGetter(leaf.ClrType, collection)));
            }

            // --- A [NotMapped] list is a child collection or nothing ---
            foreach (PropertyInfo property in EntityShapes.DeclaredProperties(leaf.ClrType))
            {
                if (EntityShapes.GetAttribute<NotMappedAttribute>(property) is not null
                    && ListElement(property.PropertyType) is Type element
                    && !EntityShapes.IsChild(element))
                {
                    Report(
                        $"The entity {Display(leaf)} declares the [NotMapped] list {property.Name} of {TypeDisplay.Short(element)}, which is not a child entity.",
                        $"Make {TypeDisplay.Short(element)} a ChildEntity with a [ParentKey] to {TypeDisplay.Short(leaf.DefaultType)}, or remove {property.Name} from the entity.");
                }
            }
        }

        /// <summary>The element type of a property type a <see cref="List{T}" /> can be stored in; null for any other type.</summary>
        /// <param name="propertyType">The property type.</param>
        /// <returns>The element type, or <see langword="null" />.</returns>
        private static Type? ListElement(Type propertyType)
        {
            return propertyType.IsGenericType
                && propertyType.GetGenericArguments() is [Type element]
                && propertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(element))
                    ? element
                    : null;
        }

        /// <summary>
        ///     The aggregate rules of a child: some parent's collection carries it, its chain of parents ends
        ///     at a top-level entity without a cycle, and it is temporal only under a temporal root.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        private void CheckAggregate(Leaf leaf)
        {
            if (!EntityShapes.IsChild(leaf.ClrType) || leaf.ParentKey is null)
            {
                return;
            }

            if (leaf.Parent is null)
            {
                Report(
                    $"The child entity {Display(leaf)} is in the model, but no entity's collection carries it.",
                    $"Add a [NotMapped] List<{TypeDisplay.Short(leaf.DefaultType)}> collection to its parent, or remove the child from the composition.");
                return;
            }

            // --- Up the chain of parents to the aggregate's root ---
            HashSet<Leaf> visited = [leaf];
            Leaf root = leaf;
            while (root.Parent is Leaf parent)
            {
                if (!visited.Add(parent))
                {
                    Report(
                        $"The child entity {Display(leaf)} is carried by a cycle of child collections, so no top-level entity owns it.",
                        "Break the cycle: a child's chain of parents ends at a top-level entity.");
                    return;
                }

                root = parent;
            }

            if (!EntityShapes.IsTopLevel(root.ClrType))
            {
                Report(
                    $"The child entity {Display(leaf)} belongs to {Display(root)}, which is not a top-level entity; a child is saved with a top-level owner, which a change to it stamps.",
                    $"Derive {TypeDisplay.Short(root.DefaultType)} from TopLevelEntity, or carry the child in a top-level entity's collection.");
            }

            if (leaf.Metadata!.IsTemporal && !root.Metadata!.IsTemporal)
            {
                Report(
                    $"The child entity {Display(leaf)} is temporal, but its aggregate's root {Display(root)} is not.",
                    $"Declare [Temporal] on {TypeDisplay.Short(root.DefaultType)}, or remove it from {TypeDisplay.Short(leaf.DefaultType)}.");
            }
        }

        /// <summary>
        ///     Every many-to-one foreign key of a leaf, in the order of its properties, named by the model's
        ///     navigation or the property's name without <c>Id</c>. Reports a composite or shadow foreign
        ///     key and one whose name gives no navigation.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        private void CollectReferences(Leaf leaf)
        {
            List<(int Position, ReferenceMetadata Reference)> references = [];
            List<PropertyMetadata> properties = [.. leaf.Metadata!.Properties];
            foreach (IForeignKey foreignKey in leaf.EntityType.GetForeignKeys())
            {
                if (foreignKey.Properties is not [IProperty dependent])
                {
                    Report(
                        $"The entity {Display(leaf)} has a composite foreign key over {string.Join(", ", foreignKey.Properties.Select(static property => property.Name))}; a reference is one int or long column.",
                        "Remove the model configuration that declares the composite foreign key.");
                    continue;
                }

                // A sibling's key is its owner's: that one-to-one link is the owner's navigation, not a reference.
                if (dependent.IsPrimaryKey())
                {
                    continue;
                }

                if (!leaf.Properties.TryGetValue(dependent.Name, out PropertyMetadata? column))
                {
                    Report(
                        $"The entity {Display(leaf)} has a foreign key over {dependent.Name}, which is not a property of the class.",
                        $"Declare {dependent.Name} on the class and reference the target with [References].");
                    continue;
                }

                if (!_byEntityType.TryGetValue(foreignKey.PrincipalEntityType, out Leaf? target) || target.Metadata is null)
                {
                    continue;
                }

                string? navigation = foreignKey.DependentToPrincipal?.Name
                    ?? (dependent.Name.Length > IdSuffix.Length && dependent.Name.EndsWith(IdSuffix, StringComparison.Ordinal)
                        ? dependent.Name[..^IdSuffix.Length]
                        : null);
                if (navigation is null)
                {
                    Report(
                        $"The foreign key {Display(leaf)}.{dependent.Name} does not end in Id, so no navigation name derives from it.",
                        $"Name the property {dependent.Name}Id, or after its target with an Id suffix.");
                    continue;
                }

                references.Add((properties.IndexOf(column), new ReferenceMetadata(navigation, column, target.Metadata)));

                // A blob or job column never travels in a spreadsheet, so its target's natural key serves nothing.
                bool keyed = EntityShapes.GetAttribute<BlobReferenceAttribute>(leaf.Members[dependent.Name]) is null
                    && leaf.JobIdMember?.Name != dependent.Name;
                if (keyed)
                {
                    leaf.KeyedReferences.Add((target, $"{leaf.Metadata.Name}.{navigation}"));
                }
            }

            leaf.References.AddRange(references.OrderBy(static item => item.Position).Select(static item => item.Reference));
        }

        /// <summary>
        ///     A leaf's navigation names (its references', and the ones its siblings declare on it) collide
        ///     neither with a property of the class nor with each other.
        /// </summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="described">Every leaf with metadata.</param>
        private void CheckNavigationNames(Leaf leaf, List<Leaf> described)
        {
            // --- Every name a navigation could collide with: the class's properties, model navigations aside ---
            HashSet<string> members = [.. EntityShapes.DeclaredProperties(leaf.ClrType)
                .Where(property => leaf.EntityType.FindNavigation(property.Name) is null)
                .Select(static property => property.Name)];

            List<(string Name, string Source)> navigations = [.. leaf.References
                .Select(static reference => (reference.Navigation, $"the foreign key {reference.ForeignKey.Name}"))];
            foreach (Leaf sibling in described)
            {
                if (EntityShapes.GetAttribute<SiblingAttribute>(sibling.ClrType) is SiblingAttribute declared
                    && _byType.TryGetValue(declared.Owner, out EntityMetadata? owner)
                    && owner == leaf.Metadata)
                {
                    navigations.Add((declared.Navigation, $"the sibling {TypeDisplay.Qualified(sibling.ClrType)}"));
                }
            }

            foreach (IGrouping<string, (string Name, string Source)> group in navigations.GroupBy(static navigation => navigation.Name, StringComparer.Ordinal))
            {
                string sources = string.Join(" and ", group.Select(static navigation => navigation.Source));
                if (members.Contains(group.Key))
                {
                    Report(
                        $"The navigation {group.Key} that {sources} gives {Display(leaf)} has the name of a property of the class.",
                        $"Rename the property {group.Key}, or the foreign key or sibling navigation it collides with.");
                }

                if (group.Count() > 1)
                {
                    Report(
                        $"The entity {Display(leaf)} has two navigations named {group.Key}, from {sources}.",
                        "Rename one of the foreign keys or sibling navigations.");
                }
            }
        }

        /// <summary>
        ///     Phase 5: warns for every entity a reference points at that has no natural key, since its
        ///     rows then travel between spreadsheets and tenants by surrogate id alone.
        /// </summary>
        /// <param name="described">Every leaf with metadata.</param>
        private void WarnUnkeyedTargets(List<Leaf> described)
        {
            Dictionary<Leaf, List<string>> referencedBy = [];
            foreach (Leaf leaf in described)
            {
                foreach ((Leaf target, string description) in leaf.KeyedReferences)
                {
                    if (!referencedBy.TryGetValue(target, out List<string>? descriptions))
                    {
                        referencedBy[target] = descriptions = [];
                    }

                    descriptions.Add(description);
                }
            }

            foreach (Leaf target in described)
            {
                if (target.Metadata!.NaturalKeys.Count == 0 && referencedBy.TryGetValue(target, out List<string>? descriptions))
                {
                    _warnings.Add(new EntityMetadataWarning(
                        target.Metadata.Name,
                        $"The entity {target.Metadata.Name} is referenced by {string.Join(", ", descriptions.Distinct(StringComparer.Ordinal))} but has no natural key, so its rows export and import by surrogate id, which differs between tenants; declare [NaturalKey] on a unique property."));
                }
            }
        }
    }
}
