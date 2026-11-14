// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.Reflection;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Pairs every child collection with its child's <see cref="ParentKeyAttribute" /> property: the
    ///     parent key becomes a foreign key to the parent's table, <c>FK_&lt;Table&gt;_&lt;Column&gt;</c>
    ///     with no cascade, indexed by <c>IX_&lt;Table&gt;_&lt;Column&gt; (…) INCLUDE (Id)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A child collection is a <c>[NotMapped]</c> list property of a child type on a mapped
    ///         parent; the model never sees it. A child type whose parent key is missing or declared
    ///         twice is left unpaired for the metadata validation to report.
    ///     </para>
    ///     <para>
    ///         When a model configuration declares an unfiltered index that starts with the parent key,
    ///         that index stands in for the convention's, which is then not added. A filtered index
    ///         never stands in, since it does not hold every child of a parent.
    ///     </para>
    /// </remarks>
    internal sealed class ChildConvention : IModelFinalizingConvention
    {
        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach ((IConventionEntityType child, ChildPairing pairing) in Pairs(modelBuilder.Metadata))
            {
                if (ParentKey(child) is not IConventionProperty parentKey)
                {
                    continue;
                }

                string declaration = ConventionModel.Declaration("ParentKey", child, parentKey.Name);
                _ = ConventionModel.AddForeignKey(child, parentKey, pairing.Parent, DeleteBehavior.NoAction, unique: false, declaration);

                // An unfiltered index a model configuration declares with the parent key first serves
                // the same seeks (the clustered key rides along in every nonclustered index), so a
                // second one would only cost writes: the configuration's index stands in for the
                // convention's. A filtered one holds only some of a parent's children, so it never does.
                if (child.GetIndexes().Any(index => index.GetConfigurationSource() == ConfigurationSource.Explicit
                    && index.Properties[0] == parentKey
                    && index.GetFilter() is null))
                {
                    continue;
                }

                // The parent key's index carries the child's key, so the children of a set of parents
                // are found without a lookup into the clustered index.
                string table = ConventionModel.Table(child);
                IConventionIndexBuilder index = ConventionModel.AddIndex(
                    child, [parentKey], ConstraintNames.Index(table, [ConventionModel.Column(parentKey)]), unique: false, filter: null);
                // The provider reads the included properties back as an array, so it is given one.
                string[] included = [child.FindPrimaryKey()!.Properties[0].Name];
                _ = index.IncludeProperties(included, fromDataAnnotation: true);
            }
        }

        /// <summary>
        ///     Every child entity type of the model with the parent whose collection carries it. A child
        ///     carried by the collections of two different parents fails, since its one parent key can
        ///     reference only one of them.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns>The pairings, by child entity type.</returns>
        /// <exception cref="InvalidOperationException">A child is carried by two parent types, or a collection's child is absent from the model.</exception>
        public static Dictionary<IConventionEntityType, ChildPairing> Pairs(IConventionModel model)
        {
            Dictionary<IConventionEntityType, ChildPairing> pairs = [];
            foreach (IConventionEntityType parent in ConventionModel.Tables(model))
            {
                foreach ((PropertyInfo collection, Type childType) in EntityShapes.ChildCollections(parent.ClrType))
                {
                    string declaration = $"The child collection {TypeDisplay.Qualified(parent.ClrType)}.{collection.Name}";
                    IConventionEntityType child = ConventionModel.Leaf(model, childType, declaration);

                    if (pairs.TryGetValue(child, out ChildPairing? existing) && existing.Parent != parent)
                    {
                        throw new InvalidOperationException(
                            $"{declaration} and the child collection {TypeDisplay.Qualified(existing.Parent.ClrType)}.{existing.Collection.Name} both carry {TypeDisplay.Qualified(child.ClrType)}, whose one parent key can reference only one parent. Give each parent a child type of its own.");
                    }

                    pairs[child] = new ChildPairing(parent, collection);
                }
            }

            return pairs;
        }

        /// <summary>The child's one <see cref="ParentKeyAttribute" /> property, or <see langword="null" /> when it declares none or more than one.</summary>
        /// <param name="child">The child entity type.</param>
        /// <returns>The parent-key property, or <see langword="null" />.</returns>
        public static IConventionProperty? ParentKey(IConventionEntityType child)
        {
            List<PropertyInfo> declared = [.. EntityShapes.DeclaredProperties(child.ClrType)
                .Where(static property => EntityShapes.GetAttribute<ParentKeyAttribute>(property) is not null)];

            return declared is [PropertyInfo single] ? child.FindProperty(single.Name) : null;
        }
    }

    /// <summary>The parent of a child entity type and the collection property that carries it.</summary>
    /// <param name="Parent">The parent entity type.</param>
    /// <param name="Collection">The parent's collection property.</param>
    internal sealed record ChildPairing(IConventionEntityType Parent, PropertyInfo Collection);
}
