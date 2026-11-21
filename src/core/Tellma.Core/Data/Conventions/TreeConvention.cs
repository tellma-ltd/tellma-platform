// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Conventions
{
    /// <summary>
    ///     Shapes every <see cref="TreeEntity{TKey}" /> table: <c>ParentId</c> a self-referencing foreign
    ///     key with no navigation and no cascade, indexed by <c>IX_&lt;Table&gt;_ParentId</c>; the shadow
    ///     <c>Node hierarchyid NOT NULL</c> column, unique by <c>UX_&lt;Table&gt;_Node</c> and left out of
    ///     the table type; the subtree counts defaulting to 1; and the tree's maximum depth recorded on
    ///     the entity type.
    /// </summary>
    internal sealed class TreeConvention : IModelFinalizingConvention
    {
        /// <summary>The shadow node column of every tree table.</summary>
        public const string NodeColumn = "Node";

        /// <inheritdoc />
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (IConventionEntityType entityType in ConventionModel.Tables(modelBuilder.Metadata))
            {
                if (!EntityShapes.IsTree(entityType.ClrType))
                {
                    continue;
                }

                string table = ConventionModel.Table(entityType);

                // --- ParentId: a self reference with no navigation ---
                IConventionProperty parentId = ConventionModel.RequireProperty(entityType, nameof(TreeEntity.ParentId), "the tree of");
                _ = ConventionModel.AddForeignKey(entityType, parentId, entityType, DeleteBehavior.NoAction, unique: false, $"The tree column {entityType.DisplayName()}.ParentId");
                _ = ConventionModel.AddIndex(entityType, [parentId], ConstraintNames.Index(table, [ConventionModel.Column(parentId)]), unique: false, filter: null);

                // --- Node: the shadow hierarchyid path, unique and never bound ---
                IConventionPropertyBuilder node = entityType.Builder.Property(typeof(HierarchyId), NodeColumn, fromDataAnnotation: true)
                    ?? throw new InvalidOperationException(
                        $"The tree entity {entityType.DisplayName()} cannot take the shadow column {NodeColumn}: a model configuration or a member of the entity already uses the name.");
                _ = node.IsRequired(true, fromDataAnnotation: true);
                TableTypeConvention.Exclude(node.Metadata);
                _ = ConventionModel.AddIndex(entityType, [node.Metadata], ConstraintNames.UniqueIndex(table, [NodeColumn]), unique: true, filter: null);

                // --- The subtree counts: a new row is a leaf, so 1 ---
                ConventionModel.SetDefault(entityType, nameof(TreeEntity.SubtreeCount), 1);
                if (EntityShapes.IsActivatableTree(entityType.ClrType))
                {
                    ConventionModel.SetDefault(entityType, nameof(ActivatableTreeEntity.ActiveSubtreeCount), 1);
                }

                // --- The depth the tree statements cap their recursion at ---
                int maxDepth = EntityShapes.GetAttribute<TreeAttribute>(entityType.ClrType)?.MaxDepth ?? TreeAttribute.DefaultMaxDepth;
                _ = entityType.Builder.HasAnnotation(TellmaAnnotationNames.TreeMaxDepth, maxDepth, fromDataAnnotation: true);
            }
        }
    }
}
