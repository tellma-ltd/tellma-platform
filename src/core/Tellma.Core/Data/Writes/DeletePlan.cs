// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Data.Writes
{
    /// <summary>
    ///     What a delete of one entity removes, fixed by its metadata: the entity's table, every child
    ///     collection's table under it (recursively), the blob columns and user-id columns of each,
    ///     and the sibling tables whose rows the database removes with their owner's, a sibling holding
    ///     a blob reference or a user-level version tag rule removed explicitly so its blobs and users
    ///     are captured. Built once per entity.
    /// </summary>
    internal sealed class DeletePlan
    {
        /// <summary>The plans, by entity; the metadata provider is a singleton, so each entity's metadata is one instance.</summary>
        private static readonly ConditionalWeakTable<EntityMetadata, DeletePlan> Plans = [];

        /// <summary>Builds the plan.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata, which names the siblings.</param>
        /// <param name="versionTags">The version tag registry, whose user-level rules name the user-id columns; null when none is composed.</param>
        private DeletePlan(EntityMetadata root, IEntityMetadataProvider metadata, IVersionTagRegistry? versionTags)
        {
            // --- The siblings of the running model, each with the entity it belongs to ---
            List<(Type Owner, EntityMetadata Entity)> siblings = [];
            foreach (EntityMetadata candidate in metadata.All)
            {
                if (EntityShapes.GetAttribute<SiblingAttribute>(candidate.ClrType) is { } sibling)
                {
                    siblings.Add((sibling.Owner, candidate));
                }
            }

            // --- The tables, in pre-order: the root, then each collection and each explicitly deleted sibling under its owner ---
            Builder builder = new(siblings, table => UserIdCapture.Columns(versionTags, table));
            Root = builder.Build(root, parentKey: null, isSiblings: false);
            Nodes = builder.Nodes;
            Aggregate = builder.Aggregate.ToFrozenSet();
            Writes = builder.Writes.ToFrozenSet();
            Captures = [.. Nodes.SelectMany(static node => node.BlobColumns.Select(column => new DeleteCapture(node, column)))];
            UserIdCaptures = [.. Nodes.Where(static node => node.UserIdColumns.Count > 0)];
        }

        /// <summary>The deleted entity's node.</summary>
        public DeleteNode Root { get; }

        /// <summary>Every node, the root first, in pre-order.</summary>
        public IReadOnlyList<DeleteNode> Nodes { get; }

        /// <summary>
        ///     The aggregate's own tables: the ones the delete's caller needs the authority to write. A
        ///     sibling's rows leave with their owner's under the cascade its owning feature declared,
        ///     even when a delete removes them explicitly to capture their blobs, so a sibling's stack
        ///     never has to authorize its owner's deletes.
        /// </summary>
        public IReadOnlySet<TableName> Aggregate { get; }

        /// <summary>The tables a delete writes, for its tags and change tracking: the aggregate's and its siblings'.</summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>The blob captures, one per blob column of every node, the explicitly deleted siblings' included, in node order.</summary>
        public IReadOnlyList<DeleteCapture> Captures { get; }

        /// <summary>The nodes whose table has a user-level version tag rule, each capturing its rows' users, in node order.</summary>
        public IReadOnlyList<DeleteNode> UserIdCaptures { get; }

        /// <summary>The plan of an entity, built on first use.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata.</param>
        /// <param name="versionTags">The running model's version tag registry; null when none is composed.</param>
        /// <returns>The plan.</returns>
        public static DeletePlan For(EntityMetadata root, IEntityMetadataProvider metadata, IVersionTagRegistry? versionTags)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(metadata);
            return Plans.GetValue(root, entity => new DeletePlan(entity, metadata, versionTags));
        }

        /// <summary>
        ///     Builds a plan's nodes. A sibling's rows leave with their owner's by its cascading key,
        ///     so a sibling is only declared among the writes; but a cascade reports no rows, so a
        ///     sibling that holds a blob reference or a user-level version tag rule (itself, in a
        ///     collection or in a sibling of its own) is deleted explicitly before its owner, its blobs
        ///     and users captured like the aggregate's. Its tables stay out of the aggregate's: the
        ///     sibling's feature declared the cascade, so the caller needs no authority over them.
        /// </summary>
        /// <param name="siblings">The siblings of the running model, each with the entity it belongs to.</param>
        /// <param name="userIdColumns">The user-id columns of a table's user-level version tag rules.</param>
        private sealed class Builder(List<(Type Owner, EntityMetadata Entity)> siblings, Func<TableName, IReadOnlyList<string>> userIdColumns)
        {
            /// <summary>Every node built, in pre-order.</summary>
            public List<DeleteNode> Nodes { get; } = [];

            /// <summary>The aggregate's own tables.</summary>
            public HashSet<TableName> Aggregate { get; } = [];

            /// <summary>Every table the delete writes.</summary>
            public HashSet<TableName> Writes { get; } = [];

            /// <summary>Builds a node and its descendants.</summary>
            /// <param name="entity">The node's entity.</param>
            /// <param name="parentKey">The column that names the owning row; null for the root, the key for a sibling.</param>
            /// <param name="isSiblings">Whether the node is a sibling's, or under one.</param>
            /// <returns>The node.</returns>
            public DeleteNode Build(EntityMetadata entity, string? parentKey, bool isSiblings)
            {
                DeleteNode node = new(entity, parentKey, userIdColumns(entity.Table));
                Nodes.Add(node);
                _ = Writes.Add(entity.Table);
                if (!isSiblings)
                {
                    _ = Aggregate.Add(entity.Table);
                }

                // --- The collections, then the siblings, each under its owner ---
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    node.Children.Add(Build(collection.Child, collection.ParentKey.Column, isSiblings));
                }

                foreach ((Type _, EntityMetadata sibling) in siblings.Where(candidate => candidate.Owner.IsAssignableFrom(entity.ClrType)))
                {
                    _ = Writes.Add(sibling.Table);
                    if (NeedsCapture(sibling))
                    {
                        node.Children.Add(Build(sibling, sibling.Key.Column, isSiblings: true));
                    }
                }

                return node;
            }

            /// <summary>
            ///     Whether an entity, one of its collections or one of its siblings holds a blob
            ///     reference or a user-level version tag rule.
            /// </summary>
            /// <param name="entity">The entity.</param>
            /// <returns>Whether a delete of its rows has blobs or users to capture.</returns>
            private bool NeedsCapture(EntityMetadata entity)
            {
                return entity.BlobReferences.Count > 0
                    || userIdColumns(entity.Table).Count > 0
                    || entity.Children.Any(collection => NeedsCapture(collection.Child))
                    || siblings.Exists(candidate => candidate.Owner.IsAssignableFrom(entity.ClrType) && NeedsCapture(candidate.Entity));
            }
        }
    }

    /// <summary>One table a delete removes rows from: the deleted entity's, a collection's or an explicitly deleted sibling's, under its owner.</summary>
    /// <param name="entity">The table's entity.</param>
    /// <param name="parentKey">The column that names the owning row; null for the root, and the key for a sibling.</param>
    /// <param name="userIdColumns">The user-id columns of the table's user-level version tag rules.</param>
    internal sealed class DeleteNode(EntityMetadata entity, string? parentKey, IReadOnlyList<string> userIdColumns)
    {
        /// <summary>The table's entity.</summary>
        public EntityMetadata Entity { get; } = entity;

        /// <summary>The column that names the owning row; null for the root, and the key for a sibling, whose id is its owner's.</summary>
        public string? ParentKey { get; } = parentKey;

        /// <summary>The collections' nodes, in metadata order, then the explicitly deleted siblings'.</summary>
        public List<DeleteNode> Children { get; } = [];

        /// <summary>The columns that hold a blob reference.</summary>
        public IReadOnlyList<string> BlobColumns { get; } = [.. entity.BlobReferences.Select(static blob => blob.Property.Column)];

        /// <summary>The user-id columns of the table's user-level version tag rules, whose values each delete captures.</summary>
        public IReadOnlyList<string> UserIdColumns { get; } = userIdColumns;

        /// <summary>The statement-local word of the table's user-id capture.</summary>
        public string UserIdWord => UserIdCapture.Word(Entity.Table);

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
