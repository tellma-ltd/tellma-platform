// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.TableTypes;
using Tellma.Core.Data.Writes;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data.Saves
{
    /// <summary>
    ///     What a save of one root entity writes, fixed by its metadata: the root and every child
    ///     collection, recursively, each with its table-valued parameters, its columns, its blob
    ///     captures and the user-id columns its user-level version tag rules name; and the tables the
    ///     database writes when the save deletes child rows, by the keys referencing a child table
    ///     with an <c>ON DELETE</c> action. The emitter's text is cached on the plan.
    /// </summary>
    internal sealed class SavePlan
    {
        /// <summary>The cached texts, without and with the recompile hint.</summary>
        private readonly string?[] _texts = new string?[2];

        /// <summary>Builds the plan of a root entity.</summary>
        /// <param name="root">The root entity, top-level.</param>
        /// <param name="binder">The table-type binder, whose definitions give each table's columns.</param>
        /// <param name="foreignKeys">The model's foreign keys whose delete actions write their tables.</param>
        /// <param name="userIdColumns">
        ///     The user-id columns of a table's user-level version tag rules, whose values the save
        ///     captures for the bump; none when null.
        /// </param>
        public SavePlan(EntityMetadata root, TableTypeBinder binder, ForeignKeyActions foreignKeys, Func<TableName, IReadOnlyList<string>>? userIdColumns = null)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentNullException.ThrowIfNull(foreignKeys);
            userIdColumns ??= static _ => [];

            // --- The nodes, in pre-order, numbering the table-valued parameters as they are bound ---
            List<SaveNode> nodes = [];
            int tables = 0;
            Root = Build(root, parent: null, collection: null, ordinal: 0, rootCollection: 0, binder, userIdColumns, nodes, ref tables);
            Nodes = nodes;
            TableCount = tables;

            // --- What the save declares: every table of the aggregate, what deleting child rows makes the
            // database write, and every blob column's capture ---
            // A save deletes the child rows a supplied collection no longer carries, never a root, so
            // only the keys referencing a child table act; the caller needs no authority over the
            // tables they write, as for a delete's.
            Aggregate = nodes.Select(static node => node.Entity.Table).ToFrozenSet();
            Writes = Aggregate.Concat(foreignKeys.WrittenByDeleting(nodes.Skip(1).Select(static node => node.Entity.Table))).ToFrozenSet();

            // A key's action reports no rows, so a save cannot tell the users a consequence's rows name;
            // a delete captures them by selection, but a save's removed child rows are known only as it
            // runs. The startup check of the version tags refuses such a model; this is its backstop.
            if (foreignKeys.SaveConsequenceNamingUsers(root, userIdColumns) is TableName named)
            {
                throw new InvalidOperationException(ForeignKeyActions.SaveMessage(root, named));
            }

            Captures = [.. nodes.SelectMany(static node => node.BlobColumns.Select(column => new SaveCapture(node, column)))];
            UserIdCaptures = [.. nodes.Where(static node => node.UserIdColumns.Count > 0)];
        }

        /// <summary>The root's node.</summary>
        public SaveNode Root { get; }

        /// <summary>Every node, the root first, in pre-order.</summary>
        public IReadOnlyList<SaveNode> Nodes { get; }

        /// <summary>
        ///     The aggregate's tables, whichever collections a payload supplies: the ones the save's
        ///     caller needs the authority to write.
        /// </summary>
        public IReadOnlySet<TableName> Aggregate { get; }

        /// <summary>
        ///     The tables the save writes, for its tags, its guards and change tracking: the aggregate's,
        ///     and every table a key referencing a child table writes when the save deletes child rows.
        /// </summary>
        public IReadOnlySet<TableName> Writes { get; }

        /// <summary>The blob captures, one per blob column of every table, in node order.</summary>
        public IReadOnlyList<SaveCapture> Captures { get; }

        /// <summary>The nodes whose table has a user-level version tag rule, each capturing its rows' users, in node order.</summary>
        public IReadOnlyList<SaveNode> UserIdCaptures { get; }

        /// <summary>How many table-valued parameters the statement binds.</summary>
        public int TableCount { get; }

        /// <summary>The cached text with a placeholder ordinal, or null before it was built.</summary>
        /// <param name="recompile">Whether the text carries the recompile hint.</param>
        /// <returns>The text.</returns>
        public string? CachedText(bool recompile)
        {
            return Volatile.Read(ref _texts[recompile ? 1 : 0]);
        }

        /// <summary>Caches a built text.</summary>
        /// <param name="recompile">Whether the text carries the recompile hint.</param>
        /// <param name="text">The text.</param>
        public void Cache(bool recompile, string text)
        {
            Volatile.Write(ref _texts[recompile ? 1 : 0], text);
        }

        /// <summary>Builds a node and its descendants.</summary>
        /// <param name="entity">The node's entity.</param>
        /// <param name="parent">The parent node; null for the root.</param>
        /// <param name="collection">The collection the node synchronises; null for the root.</param>
        /// <param name="ordinal">The collection's position among its owner's collections.</param>
        /// <param name="rootCollection">The position of the root's collection the node descends through.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <param name="userIdColumns">The user-id columns of a table's user-level version tag rules.</param>
        /// <param name="nodes">The nodes built so far, in pre-order.</param>
        /// <param name="tables">The table-valued parameters numbered so far.</param>
        /// <returns>The node.</returns>
        private static SaveNode Build(
            EntityMetadata entity,
            SaveNode? parent,
            ChildCollectionMetadata? collection,
            int ordinal,
            int rootCollection,
            TableTypeBinder binder,
            Func<TableName, IReadOnlyList<string>> userIdColumns,
            List<SaveNode> nodes,
            ref int tables)
        {
            TableTypeDefinition definition = binder.Binding(entity.ClrType).Definition;

            // A child binds its parents first; every table its new rows, then its existing rows.
            int parents = parent is null ? -1 : tables++;
            int newRows = tables++;
            int existingRows = tables++;
            SaveNode node = new(entity, parent, collection, ordinal, rootCollection, parents, newRows, existingRows, definition.Columns, userIdColumns(entity.Table));
            nodes.Add(node);

            for (int index = 0; index < entity.Children.Count; index++)
            {
                node.Children.Add(Build(entity.Children[index].Child, node, entity.Children[index], index, parent is null ? index : rootCollection, binder, userIdColumns, nodes, ref tables));
            }

            return node;
        }
    }

    /// <summary>
    ///     One table of a save: the root, or a child collection synchronised under its parents. Its
    ///     columns come from the table's row-image type, its column roles from the entity's metadata.
    /// </summary>
    internal sealed class SaveNode
    {
        /// <summary>The names of the audit columns' properties, which the statement stamps.</summary>
        private static readonly FrozenSet<string> AuditProperties = new[]
        {
            nameof(TopLevelEntity.CreatedAt),
            nameof(TopLevelEntity.CreatedById),
            nameof(TopLevelEntity.ModifiedAt),
            nameof(TopLevelEntity.ModifiedById),
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>Builds the node.</summary>
        /// <param name="entity">The table's entity.</param>
        /// <param name="parent">The parent node; null for the root.</param>
        /// <param name="collection">The collection; null for the root.</param>
        /// <param name="ordinal">The collection's position among its owner's collections.</param>
        /// <param name="rootCollection">The position of the root's collection the node descends through.</param>
        /// <param name="parentsTable">The parents parameter's index; -1 for the root.</param>
        /// <param name="newTable">The new rows parameter's index.</param>
        /// <param name="existingTable">The existing rows parameter's index.</param>
        /// <param name="columns">The row-image type's columns, in order.</param>
        /// <param name="userIdColumns">The user-id columns of the table's user-level version tag rules.</param>
        public SaveNode(
            EntityMetadata entity,
            SaveNode? parent,
            ChildCollectionMetadata? collection,
            int ordinal,
            int rootCollection,
            int parentsTable,
            int newTable,
            int existingTable,
            IReadOnlyList<TableTypeColumnDefinition> columns,
            IReadOnlyList<string> userIdColumns)
        {
            Entity = entity;
            Parent = parent;
            Collection = collection;
            CollectionOrdinal = ordinal;
            RootCollection = rootCollection;
            ParentsTable = parentsTable;
            NewTable = newTable;
            ExistingTable = existingTable;
            Level = parent is null ? 0 : parent.Level + 1;

            // --- The column roles, by each column's property ---
            var byColumn = entity.Properties.ToDictionary(static property => property.Column, StringComparer.OrdinalIgnoreCase);
            string? parentKey = collection?.ParentKey.Column;
            List<SaveColumn> saveColumns = [];
            foreach (TableTypeColumnDefinition column in columns)
            {
                PropertyMetadata? property = byColumn.GetValueOrDefault(column.Name);
                bool isAudit = parent is null && property is not null && AuditProperties.Contains(property.Name);

                // Editable and derived columns are set and compared; never the key, nor a child's
                // parent key, since a child is never re-parented.
                bool isSet = property is { Ownership: PropertyOwnership.Editable or PropertyOwnership.Derived }
                    && !ReferenceEquals(property, entity.Key)
                    && !string.Equals(column.Name, parentKey, StringComparison.OrdinalIgnoreCase);
                saveColumns.Add(new SaveColumn(column.Name, IsText(column.StoreType), isAudit, isSet));
            }

            Columns = saveColumns;
            BlobColumns = [.. entity.BlobReferences.Select(static blob => blob.Property.Column)];
            UserIdColumns = userIdColumns;
        }

        /// <summary>The table's entity.</summary>
        public EntityMetadata Entity { get; }

        /// <summary>The parent node; null for the root.</summary>
        public SaveNode? Parent { get; }

        /// <summary>The collection the node synchronises; null for the root.</summary>
        public ChildCollectionMetadata? Collection { get; }

        /// <summary>The collection's position among its owner's collections.</summary>
        public int CollectionOrdinal { get; }

        /// <summary>The position of the root's collection the node descends through: the touched set's <c>[Collection]</c>.</summary>
        public int RootCollection { get; }

        /// <summary>The depth: 0 for the root, 1 for a child collection, and so on.</summary>
        public int Level { get; }

        /// <summary>The parents parameter's index; -1 for the root.</summary>
        public int ParentsTable { get; }

        /// <summary>The new rows parameter's index.</summary>
        public int NewTable { get; }

        /// <summary>The existing rows parameter's index.</summary>
        public int ExistingTable { get; }

        /// <summary>The row-image columns, in order, with their roles.</summary>
        public IReadOnlyList<SaveColumn> Columns { get; }

        /// <summary>The columns that hold a blob reference.</summary>
        public IReadOnlyList<string> BlobColumns { get; }

        /// <summary>The user-id columns of the table's user-level version tag rules, whose values each write captures.</summary>
        public IReadOnlyList<string> UserIdColumns { get; }

        /// <summary>The statement-local word of the table's user-id capture.</summary>
        public string UserIdWord => UserIdCapture.Word(Entity.Table);

        /// <summary>The child collections' nodes, in metadata order.</summary>
        public List<SaveNode> Children { get; } = [];

        /// <summary>The table, delimited.</summary>
        public string Table => Entity.Table.Delimited;

        /// <summary>The key's SQL type.</summary>
        public string KeyType => SqlKeyType(Entity.Key.ClrType);

        /// <summary>Whether the key is a <see cref="long" />.</summary>
        public bool IsLong => Entity.Key.ClrType == typeof(long);

        /// <summary>The parent key's column; null for the root.</summary>
        public string? ParentKey => Collection?.ParentKey.Column;

        /// <summary>The SQL type of an <see cref="int" /> or <see cref="long" /> key.</summary>
        /// <param name="type">The key's CLR type.</param>
        /// <returns><c>int</c> or <c>bigint</c>.</returns>
        public static string SqlKeyType(Type type)
        {
            return type == typeof(long) ? "bigint" : "int";
        }

        /// <summary>Whether a store type is character data, which the unchanged-row comparison compares as binary.</summary>
        /// <param name="storeType">The store type.</param>
        /// <returns>True for <c>char</c>, <c>varchar</c>, <c>nchar</c> and <c>nvarchar</c>.</returns>
        private static bool IsText(string storeType)
        {
            string type = storeType.TrimStart();
            return type.StartsWith("char", StringComparison.OrdinalIgnoreCase)
                || type.StartsWith("varchar", StringComparison.OrdinalIgnoreCase)
                || type.StartsWith("nchar", StringComparison.OrdinalIgnoreCase)
                || type.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A row-image column and its role in the save's statements.</summary>
    /// <param name="Name">The column.</param>
    /// <param name="IsText">Whether it is character data, compared as binary.</param>
    /// <param name="IsAudit">Whether it is a root's audit column, stamped by the statement.</param>
    /// <param name="IsSet">Whether it is set on update and compared to tell an unchanged row.</param>
    internal sealed record SaveColumn(string Name, bool IsText, bool IsAudit, bool IsSet);

    /// <summary>A blob column a save captures.</summary>
    /// <param name="Node">The column's table.</param>
    /// <param name="Column">The column.</param>
    internal sealed record SaveCapture(SaveNode Node, string Column)
    {
        /// <summary>The capture table's statement-local word, <c>blob_&lt;Schema&gt;_&lt;Table&gt;_&lt;Column&gt;</c>.</summary>
        public string Word => ReservedSqlNames.StatementNames.BlobCapture(Node.Entity.Table.Schema, Node.Entity.Table.Name, Column);
    }
}
