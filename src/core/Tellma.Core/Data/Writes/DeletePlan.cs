// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Data.Writes
{
    /// <summary>
    ///     What a delete of one entity removes, fixed by its metadata: the entity's table, every child
    ///     collection's table under it (recursively), the blob columns of each, and the sibling tables
    ///     whose rows the database removes with their owner's. Built once per entity.
    /// </summary>
    internal sealed class DeletePlan
    {
        /// <summary>The plans, by entity; the metadata provider is a singleton, so each entity's metadata is one instance.</summary>
        private static readonly ConditionalWeakTable<EntityMetadata, DeletePlan> Plans = [];

        /// <summary>Builds the plan.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata, which names the siblings.</param>
        private DeletePlan(EntityMetadata root, IEntityMetadataProvider metadata)
        {
            // --- The tables, in pre-order: the root, then each collection under its owner ---
            List<DeleteNode> nodes = [];
            Root = Build(root, parentKey: null, nodes);
            Nodes = nodes;

            // --- The siblings: a sibling's rows leave with their owner's by its cascading key ---
            HashSet<TableName> writes = [.. nodes.Select(static node => node.Entity.Table)];
            foreach (EntityMetadata candidate in metadata.All)
            {
                if (EntityShapes.GetAttribute<SiblingAttribute>(candidate.ClrType) is { } sibling
                    && nodes.Exists(node => sibling.Owner.IsAssignableFrom(node.Entity.ClrType)))
                {
                    _ = writes.Add(candidate.Table);
                }
            }

            Writes = writes.ToFrozenSet();
            Captures = [.. nodes.SelectMany(static node => node.BlobColumns.Select(column => new DeleteCapture(node, column)))];
        }

        /// <summary>The deleted entity's node.</summary>
        public DeleteNode Root { get; }

        /// <summary>Every node, the root first, in pre-order.</summary>
        public IReadOnlyList<DeleteNode> Nodes { get; }

        /// <summary>The tables a delete writes: the aggregate's and its siblings'.</summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>The blob captures, one per blob column of every table of the aggregate, in node order.</summary>
        public IReadOnlyList<DeleteCapture> Captures { get; }

        /// <summary>The plan of an entity, built on first use.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata.</param>
        /// <returns>The plan.</returns>
        public static DeletePlan For(EntityMetadata root, IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(metadata);
            return Plans.GetValue(root, entity => new DeletePlan(entity, metadata));
        }

        /// <summary>Builds a node and its descendants.</summary>
        /// <param name="entity">The node's entity.</param>
        /// <param name="parentKey">The column that names the owning row; null for the root.</param>
        /// <param name="nodes">The nodes built so far, in pre-order.</param>
        /// <returns>The node.</returns>
        private static DeleteNode Build(EntityMetadata entity, string? parentKey, List<DeleteNode> nodes)
        {
            DeleteNode node = new(entity, parentKey);
            nodes.Add(node);
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                node.Children.Add(Build(collection.Child, collection.ParentKey.Column, nodes));
            }

            return node;
        }
    }

    /// <summary>One table a delete removes rows from: the deleted entity's, or a collection's under its owner.</summary>
    /// <param name="entity">The table's entity.</param>
    /// <param name="parentKey">The column that names the owning row; null for the root.</param>
    internal sealed class DeleteNode(EntityMetadata entity, string? parentKey)
    {
        /// <summary>The table's entity.</summary>
        public EntityMetadata Entity { get; } = entity;

        /// <summary>The column that names the owning row; null for the root.</summary>
        public string? ParentKey { get; } = parentKey;

        /// <summary>The collections' nodes, in metadata order.</summary>
        public List<DeleteNode> Children { get; } = [];

        /// <summary>The columns that hold a blob reference.</summary>
        public IReadOnlyList<string> BlobColumns { get; } = [.. entity.BlobReferences.Select(static blob => blob.Property.Column)];

        /// <summary>The table, delimited.</summary>
        public string Table => Entity.Table.Delimited;

        /// <summary>The key's SQL type.</summary>
        public string KeyType => Entity.Key.ClrType == typeof(long) ? "bigint" : "int";
    }

    /// <summary>A blob column a delete captures.</summary>
    /// <param name="Node">The column's table.</param>
    /// <param name="Column">The column.</param>
    internal sealed record DeleteCapture(DeleteNode Node, string Column)
    {
        /// <summary>The capture table's statement-local word, <c>blob_&lt;Schema&gt;_&lt;Table&gt;_&lt;Column&gt;</c>.</summary>
        public string Word => ReservedSqlNames.StatementNames.BlobCapture(Node.Entity.Table.Schema, Node.Entity.Table.Name, Column);
    }
}
