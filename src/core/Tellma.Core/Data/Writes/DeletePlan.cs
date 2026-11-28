// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Data.Writes
{
    /// <summary>
    ///     What a delete of one entity removes, fixed by its metadata and the model's foreign keys: the
    ///     entity's table, every child collection's table under it (recursively), the blob columns and
    ///     user-id columns of each, a sibling holding a blob reference or a user-level version tag rule
    ///     removed explicitly so its blobs and users are captured, and every table the database writes
    ///     by a referencing key's <c>ON DELETE</c> action: the siblings' tables, and any other table
    ///     whose key cascades or sets a value, transitively. Built once per entity.
    /// </summary>
    internal sealed class DeletePlan
    {
        /// <summary>The plans, by entity; the metadata provider is a singleton, so each entity's metadata is one instance.</summary>
        private static readonly ConditionalWeakTable<EntityMetadata, DeletePlan> Plans = [];

        /// <summary>Builds the plan.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata, which names the siblings.</param>
        /// <param name="versionTags">The version tag registry, whose user-level rules name the user-id columns; null when none is composed.</param>
        /// <param name="foreignKeys">The running model's foreign keys whose delete actions write their tables.</param>
        private DeletePlan(EntityMetadata root, IEntityMetadataProvider metadata, IVersionTagRegistry? versionTags, ForeignKeyActions foreignKeys)
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

            // --- What the delete writes: the tables its statements name, and what their rows' referencing keys write ---
            // A sibling's key cascades, so its table is among the consequences whether or not it is
            // deleted explicitly; so is every other table whose key cascades or sets a value.
            HashSet<TableName> deleted = [.. Nodes.Select(static node => node.Entity.Table)];
            Consequences = foreignKeys.WrittenByDeleting(deleted).ToFrozenSet();
            Writes = deleted.Concat(Consequences).ToFrozenSet();
            Captures = [.. Nodes.SelectMany(static node => node.BlobColumns.Select(column => new DeleteCapture(node, column)))];
            UserIdCaptures = [.. Nodes.Where(static node => node.UserIdColumns.Count > 0)];

            // --- The users a consequence's rows name, captured by selection before the deletes ---
            // Spec 0013 §2.5: every written table with a user-level rule tells the epilogue its users.
            // A key's action reports no rows, so a consequence table with a rule has the rows the
            // actions will reach read first, by the keys from the tables that lose rows.
            IReadOnlySet<TableName> removed = foreignKeys.RemovedByDeleting(deleted);
            List<DeleteConsequence> consequences = [];
            foreach (TableName table in Consequences.OrderBy(static table => table.Schema, StringComparer.Ordinal).ThenBy(static table => table.Name, StringComparer.Ordinal))
            {
                IReadOnlyList<string> columns = UserIdCapture.Columns(versionTags, table);
                if (columns.Count > 0)
                {
                    consequences.Add(new DeleteConsequence(table, columns, Reaches(table, columns, removed, foreignKeys, [table])));
                }
            }

            ConsequenceUserIdCaptures = consequences;
        }

        /// <summary>The deleted entity's node.</summary>
        public DeleteNode Root { get; }

        /// <summary>Every node, the root first, in pre-order.</summary>
        public IReadOnlyList<DeleteNode> Nodes { get; }

        /// <summary>
        ///     The aggregate's own tables: the ones the delete's caller needs the authority to write. A
        ///     sibling's rows leave with their owner's under the cascade its owning feature declared,
        ///     even when a delete removes them explicitly to capture their blobs, and every other
        ///     consequence is a referencing key's own action on an authorised delete, so neither a
        ///     sibling's stack nor a referencing table's stack has to authorize the owner's deletes.
        /// </summary>
        public IReadOnlySet<TableName> Aggregate { get; }

        /// <summary>
        ///     The tables the database writes by the <c>ON DELETE</c> action of a key referencing a
        ///     table the delete removes rows from: each sibling's, and each other table whose key
        ///     cascades or sets a value, with the tables a cascaded table's own keys write in turn.
        ///     The delete declares them but needs no authority over them.
        /// </summary>
        public IReadOnlySet<TableName> Consequences { get; }

        /// <summary>
        ///     The tables a delete writes, for its tags, its guards and change tracking: the aggregate's,
        ///     the explicitly deleted siblings' and the consequences.
        /// </summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>The blob captures, one per blob column of every node, the explicitly deleted siblings' included, in node order.</summary>
        public IReadOnlyList<DeleteCapture> Captures { get; }

        /// <summary>The nodes whose table has a user-level version tag rule, each capturing its rows' users, in node order.</summary>
        public IReadOnlyList<DeleteNode> UserIdCaptures { get; }

        /// <summary>
        ///     The consequence tables with a user-level version tag rule, in table order: each captures,
        ///     before the deletes, the users its rows that the keys' actions reach name.
        /// </summary>
        public IReadOnlyList<DeleteConsequence> ConsequenceUserIdCaptures { get; }

        /// <summary>The plan of an entity, built on first use.</summary>
        /// <param name="root">The deleted entity.</param>
        /// <param name="metadata">The running model's metadata.</param>
        /// <param name="versionTags">The running model's version tag registry; null when none is composed.</param>
        /// <param name="foreignKeys">The running model's foreign keys whose delete actions write their tables.</param>
        /// <returns>The plan.</returns>
        public static DeletePlan For(EntityMetadata root, IEntityMetadataProvider metadata, IVersionTagRegistry? versionTags, ForeignKeyActions foreignKeys)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(foreignKeys);
            return Plans.GetValue(root, entity => new DeletePlan(entity, metadata, versionTags, foreignKeys));
        }

        /// <summary>
        ///     The ways the keys' actions reach a table's rows: one per key the table holds to a table
        ///     that loses rows, each with the ways the referenced rows are removed in turn.
        /// </summary>
        /// <param name="table">The reached table.</param>
        /// <param name="userIdColumns">The reached table's user-id columns, which a <c>SET DEFAULT</c> must not write.</param>
        /// <param name="removed">The tables that lose rows.</param>
        /// <param name="foreignKeys">The model's keys with a writing delete action.</param>
        /// <param name="path">The tables on the way here, which a cascade cannot reach again.</param>
        /// <returns>The reaches, in key order.</returns>
        private List<DeleteReach> Reaches(TableName table, IReadOnlyList<string> userIdColumns, IReadOnlySet<TableName> removed, ForeignKeyActions foreignKeys, HashSet<TableName> path)
        {
            List<DeleteReach> reaches = [];
            foreach (ForeignKeyAction key in foreignKeys.HeldBy(table).Where(key => removed.Contains(key.Principal)))
            {
                // A SET DEFAULT names a user the capture cannot know, so a rule's column never takes one.
                // The startup check of the version tags refuses such a model; this is its backstop.
                if (key.OnDelete == ReferentialAction.SetDefault && key.Columns.Intersect(userIdColumns, StringComparer.Ordinal).Any())
                {
                    throw new InvalidOperationException(ForeignKeyActions.DefaultUserMessage(key));
                }

                reaches.Add(new DeleteReach(key, [.. Nodes.Where(node => node.Entity.Table == key.Principal)], Removals(key.Principal, removed, foreignKeys, path)));
            }

            return reaches;
        }

        /// <summary>The ways a table that loses rows loses them by cascade: one per key it holds that cascades from a table that loses rows.</summary>
        /// <param name="table">The table.</param>
        /// <param name="removed">The tables that lose rows.</param>
        /// <param name="foreignKeys">The model's keys with a writing delete action.</param>
        /// <param name="path">The tables on the way here.</param>
        /// <returns>The reaches, in key order.</returns>
        private List<DeleteReach> Removals(TableName table, IReadOnlySet<TableName> removed, ForeignKeyActions foreignKeys, HashSet<TableName> path)
        {
            // The database refuses a cycle of cascades, so its migration never applies; a model that
            // declares one anyway is refused here, as a backstop.
            if (!path.Add(table))
            {
                throw new InvalidOperationException($"The cascading keys of {table} form a cycle, which the database refuses.");
            }

            List<DeleteReach> reaches = [];
            foreach (ForeignKeyAction key in foreignKeys.HeldBy(table).Where(key => key.OnDelete == ReferentialAction.Cascade && removed.Contains(key.Principal)))
            {
                reaches.Add(new DeleteReach(key, [.. Nodes.Where(node => node.Entity.Table == key.Principal)], Removals(key.Principal, removed, foreignKeys, path)));
            }

            _ = path.Remove(table);
            return reaches;
        }

        /// <summary>
        ///     Builds a plan's nodes. A sibling's rows leave with their owner's by its cascading key,
        ///     which the plan's consequences declare; but a cascade reports no rows, so a sibling that
        ///     holds a blob reference or a user-level version tag rule (itself, in a collection or in a
        ///     sibling of its own) is deleted explicitly before its owner, its blobs and users captured
        ///     like the aggregate's. Its tables stay out of the aggregate's: the sibling's feature
        ///     declared the cascade, so the caller needs no authority over them.
        /// </summary>
        /// <param name="siblings">The siblings of the running model, each with the entity it belongs to.</param>
        /// <param name="userIdColumns">The user-id columns of a table's user-level version tag rules.</param>
        private sealed class Builder(List<(Type Owner, EntityMetadata Entity)> siblings, Func<TableName, IReadOnlyList<string>> userIdColumns)
        {
            /// <summary>Every node built, in pre-order.</summary>
            public List<DeleteNode> Nodes { get; } = [];

            /// <summary>The aggregate's own tables.</summary>
            public HashSet<TableName> Aggregate { get; } = [];

            /// <summary>Builds a node and its descendants.</summary>
            /// <param name="entity">The node's entity.</param>
            /// <param name="parentKey">The column that names the owning row; null for the root, the key for a sibling.</param>
            /// <param name="isSiblings">Whether the node is a sibling's, or under one.</param>
            /// <returns>The node.</returns>
            public DeleteNode Build(EntityMetadata entity, string? parentKey, bool isSiblings)
            {
                DeleteNode node = new(entity, parentKey, userIdColumns(entity.Table));
                Nodes.Add(node);
                if (!isSiblings)
                {
                    _ = Aggregate.Add(entity.Table);
                }

                // --- The collections, then the siblings with something to capture, each under its owner ---
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    node.Children.Add(Build(collection.Child, collection.ParentKey.Column, isSiblings));
                }

                foreach ((Type _, EntityMetadata sibling) in siblings.Where(candidate => candidate.Owner.IsAssignableFrom(entity.ClrType) && NeedsCapture(candidate.Entity)))
                {
                    node.Children.Add(Build(sibling, sibling.Key.Column, isSiblings: true));
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

    /// <summary>A consequence table with a user-level version tag rule, whose reached rows' users a delete captures before it runs.</summary>
    /// <param name="Table">The table.</param>
    /// <param name="UserIdColumns">The user-id columns of the table's user-level rules.</param>
    /// <param name="Reaches">The keys by which the delete reaches the table's rows.</param>
    internal sealed record DeleteConsequence(TableName Table, IReadOnlyList<string> UserIdColumns, IReadOnlyList<DeleteReach> Reaches)
    {
        /// <summary>The capture's statement-local word, <c>uidk_&lt;Schema&gt;_&lt;Table&gt;</c>.</summary>
        public string Word => UserIdCapture.ConsequenceWord(Table);
    }

    /// <summary>
    ///     A key by which a delete reaches rows: the rows of <see cref="ForeignKeyAction.Dependent" />
    ///     that reference a removed row of <see cref="ForeignKeyAction.Principal" />, which is removed
    ///     by the delete's own statements (its nodes) or by a cascade of its own (its removals).
    /// </summary>
    /// <param name="Key">The key.</param>
    /// <param name="Nodes">The plan's nodes over the referenced table, whose rows the delete removes explicitly.</param>
    /// <param name="Removals">The cascading keys by which the referenced table loses rows.</param>
    internal sealed record DeleteReach(ForeignKeyAction Key, IReadOnlyList<DeleteNode> Nodes, IReadOnlyList<DeleteReach> Removals);

    /// <summary>A blob column a delete captures.</summary>
    /// <param name="Node">The column's table.</param>
    /// <param name="Column">The column.</param>
    internal sealed record DeleteCapture(DeleteNode Node, string Column)
    {
        /// <summary>The capture table's statement-local word, <c>blob_&lt;Schema&gt;_&lt;Table&gt;_&lt;Column&gt;</c>.</summary>
        public string Word => ReservedSqlNames.StatementNames.BlobCapture(Node.Entity.Table.Schema, Node.Entity.Table.Name, Column);
    }
}
