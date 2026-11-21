// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud.Query;

namespace Tellma.Core.Crud.ExcelRows
{
    // The statement shapes are spec 0015 §15: a query on the row source's own root takes the
    // source's clauses and its ordering, a dependent is restricted to the children of its owner's
    // rows through the child's parent key, and every other root is a lookup target.
    /// <summary>
    ///     One query the Excel codec asks a row source for, admitted against the tenant model before
    ///     any round trip: its root, the surface it plans on, its select and ordering, the column its
    ///     keys are read from when its dependents need them, and its dependents, each admitted the
    ///     same way.
    /// </summary>
    internal sealed class RowsNode
    {
        /// <summary>The ordering of a query that names none: by key.</summary>
        public const string KeyOrder = "Id";

        /// <summary>Creates a node; through <see cref="Admit" /> only.</summary>
        /// <param name="query">The query.</param>
        /// <param name="kind">What the query's root is to the row source.</param>
        /// <param name="entity">The root's metadata.</param>
        /// <param name="surface">The surface the root plans on.</param>
        /// <param name="select">The select, the columns joined, the key appended when it is read but not asked for.</param>
        /// <param name="orderBy">The ordering.</param>
        /// <param name="keyColumn">The select's column the keys are read from; null when nothing needs them.</param>
        /// <param name="parentKey">A dependent's parent key, which restricts it to its owner's rows; null otherwise.</param>
        /// <param name="ownerKeyType">The key type of a dependent's owner, whose keys a follow-up binds; null otherwise.</param>
        private RowsNode(
            ExcelQuery query,
            RowsRoot kind,
            EntityMetadata entity,
            QuerySurface surface,
            string select,
            string orderBy,
            int? keyColumn,
            string? parentKey,
            Type? ownerKeyType)
        {
            Query = query;
            Kind = kind;
            Entity = entity;
            Surface = surface;
            Select = select;
            OrderBy = orderBy;
            KeyColumn = keyColumn;
            ParentKey = parentKey;
            OwnerKeyType = ownerKeyType;
        }

        /// <summary>The query as the codec sent it.</summary>
        public ExcelQuery Query { get; }

        /// <summary>What the query's root is to the row source.</summary>
        public RowsRoot Kind { get; }

        /// <summary>The root's metadata.</summary>
        public EntityMetadata Entity { get; }

        /// <summary>The surface the root plans on.</summary>
        public QuerySurface Surface { get; }

        /// <summary>The select: the query's columns joined, then the key when it is read but not asked for.</summary>
        public string Select { get; }

        /// <summary>The columns each row answers: the query's, without a key appended for the row source's own use.</summary>
        public int Width => Query.SelectPaths.Count;

        /// <summary>The ordering, ended by the key unless it already ends with it, so it is total.</summary>
        public string OrderBy { get; }

        /// <summary>The select's column the keys are read from, for the dependents' follow-ups; null when the node has none.</summary>
        public int? KeyColumn { get; }

        /// <summary>A dependent's parent key, by which it is restricted to its owner's rows; null for a root.</summary>
        public string? ParentKey { get; }

        /// <summary>The key type of a dependent's owner, <see cref="int" /> or <see cref="long" />; null for a root.</summary>
        public Type? OwnerKeyType { get; }

        /// <summary>The dependents, parallel to the query's.</summary>
        public IReadOnlyList<RowsNode> Dependents { get; private init; } = [];

        /// <summary>
        ///     Admits a root query and, for a page stream, its dependents at every depth: each root an
        ///     entity of the tenant model, each select a list of columns, each dependent a child
        ///     collection of its owner, with no restriction, take or count of its own.
        /// </summary>
        /// <param name="query">The query.</param>
        /// <param name="surfaces">The query surfaces.</param>
        /// <param name="own">The row source's own surface, whose root takes the source's clauses.</param>
        /// <param name="sourceOrderBy">The source's ordering; null for none.</param>
        /// <param name="paged">Whether the query is a page stream's root, which may carry dependents; false for a flat read.</param>
        /// <returns>The node.</returns>
        /// <exception cref="ArgumentException">The query is not one the row source reads.</exception>
        public static RowsNode Admit(ExcelQuery query, QuerySurfaces surfaces, QuerySurface own, string? sourceOrderBy, bool paged)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(surfaces);
            ArgumentNullException.ThrowIfNull(own);

            // --- The root: the row source's own, which a page stream always reads, or a lookup target ---
            EntityMetadata entity = surfaces.Entity(query.RootEntity)
                ?? throw new ArgumentException($"The tenant model maps no entity named '{query.RootEntity}'.", nameof(query));
            RowsRoot kind = string.Equals(entity.Name, own.Resource, StringComparison.OrdinalIgnoreCase) ? RowsRoot.Own : RowsRoot.Lookup;
            if (paged && kind != RowsRoot.Own)
            {
                throw new ArgumentException($"A page stream reads its row source's own root {own.Resource}, not {entity.Name}.", nameof(query));
            }

            // --- The paging and the restriction: a positive take, and values only beside a restriction ---
            if (query.Take is < 1)
            {
                throw new ArgumentException("A query's take, when it names one, is at least one row.", nameof(query));
            }

            if (query.CountCap is < 0)
            {
                throw new ArgumentException("A query's count cap, when it names one, is zero or more.", nameof(query));
            }

            if ((query.Restriction is null) != (query.RestrictionValues is null))
            {
                throw new ArgumentException("A query's restriction and its values come together.", nameof(query));
            }

            if (!paged && query.Dependents is { Count: > 0 })
            {
                throw new ArgumentException("A flat read carries no dependents; a page stream carries them.", nameof(query));
            }

            // --- The ordering: the query's, else on the own root the source's, else by key ---
            string orderBy = Text(query.OrderBy) ?? (kind == RowsRoot.Own ? Text(sourceOrderBy) : null) ?? KeyOrder;
            RowsNode node = Shaped(query, kind, entity, kind == RowsRoot.Own ? own : surfaces.Root(entity.Name)!, orderBy, parentKey: null, ownerKeyType: null);
            return node.With(AdmitDependents(query, entity, surfaces));
        }

        /// <summary>Admits a node's dependents, each a child collection of the node's entity, with their own.</summary>
        /// <param name="owner">The owner's query.</param>
        /// <param name="entity">The owner's metadata.</param>
        /// <param name="surfaces">The query surfaces.</param>
        /// <returns>The dependents, parallel to the query's.</returns>
        /// <exception cref="ArgumentException">A dependent is not one the row source reads.</exception>
        private static List<RowsNode> AdmitDependents(ExcelQuery owner, EntityMetadata entity, QuerySurfaces surfaces)
        {
            List<RowsNode> dependents = [];
            foreach (ExcelQuery dependent in owner.Dependents ?? [])
            {
                ArgumentNullException.ThrowIfNull(dependent, nameof(owner));

                // --- A child collection of the owner, restricted by the row source alone ---
                ChildCollectionMetadata collection = entity.Children.FirstOrDefault(child => string.Equals(child.Child.Name, dependent.RootEntity, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"A dependent of {entity.Name} is one of its child collections; {dependent.RootEntity} is none.", nameof(owner));
                if (dependent.Restriction is not null || dependent.RestrictionValues is not null || dependent.Take is not null || dependent.CountCap is not null)
                {
                    throw new ArgumentException(
                        $"The dependent {collection.Child.Name} names a restriction, a take or a count cap; the row source restricts a dependent to its owner's rows.",
                        nameof(owner));
                }

                // --- Its ordering, the surface it plans on, and its own dependents ---
                Type ownerKeyType = Nullable.GetUnderlyingType(entity.Key.ClrType) ?? entity.Key.ClrType;
                RowsNode node = Shaped(
                    dependent,
                    RowsRoot.Dependent,
                    collection.Child,
                    surfaces.Root(collection.Child.Name)!,
                    Text(dependent.OrderBy) ?? KeyOrder,
                    collection.ParentKey.Name,
                    ownerKeyType);
                dependents.Add(node.With(AdmitDependents(dependent, collection.Child, surfaces)));
            }

            return dependents;
        }

        /// <summary>A node's select and key column: the key is appended when its dependents need it and the query does not ask for it.</summary>
        /// <param name="query">The query.</param>
        /// <param name="kind">What the root is to the row source.</param>
        /// <param name="entity">The root's metadata.</param>
        /// <param name="surface">The surface the root plans on.</param>
        /// <param name="orderBy">The ordering.</param>
        /// <param name="parentKey">A dependent's parent key; null otherwise.</param>
        /// <param name="ownerKeyType">A dependent owner's key type; null otherwise.</param>
        /// <returns>The node, without dependents.</returns>
        /// <exception cref="ArgumentException">The select is empty or holds a blank column.</exception>
        private static RowsNode Shaped(ExcelQuery query, RowsRoot kind, EntityMetadata entity, QuerySurface surface, string orderBy, string? parentKey, Type? ownerKeyType)
        {
            if (query.SelectPaths.Count == 0 || query.SelectPaths.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException($"A query of {entity.Name} selects at least one column, and no blank one.", nameof(query));
            }

            // --- The key column, which a follow-up binds as the owner ids of the node's dependents ---
            List<string> columns = [.. query.SelectPaths.Select(static path => path.Trim())];
            int? keyColumn = null;
            if (query.Dependents is { Count: > 0 })
            {
                int index = columns.FindIndex(path => string.Equals(path, entity.Key.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    columns.Add(entity.Key.Name);
                    index = columns.Count - 1;
                }

                keyColumn = index;
            }

            return new RowsNode(query, kind, entity, surface, string.Join(", ", columns), TieBroken(orderBy, entity.Key.Name), keyColumn, parentKey, ownerKeyType);
        }

        // Every page and follow-up is cut by skip and take over the ordering, and the store orders
        // rows that tie on it as it pleases, so two reads may each place a tied row on either side
        // of the cut. The key ends every ordering that does not already end with it, so the rows'
        // order is total and the reads neither repeat nor drop a row.
        /// <summary>The ordering with the key appended as its last term, unless that term is already the key.</summary>
        /// <param name="orderBy">The ordering, as query ordering text.</param>
        /// <param name="key">The entity's key column.</param>
        /// <returns>The total ordering.</returns>
        internal static string TieBroken(string orderBy, string key)
        {
            // --- The last top-level term: after the last comma outside parentheses and quotes ---
            int depth = 0;
            bool quoted = false;
            int start = 0;
            for (int index = 0; index < orderBy.Length; index++)
            {
                char current = orderBy[index];
                if (current == '\'')
                {
                    quoted = !quoted;
                }
                else if (!quoted && current == '(')
                {
                    depth++;
                }
                else if (!quoted && current == ')')
                {
                    depth--;
                }
                else if (!quoted && depth == 0 && current == ',')
                {
                    start = index + 1;
                }
            }

            // --- The term's path, its direction dropped ---
            string[] words = orderBy[start..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool directed = words.Length == 2
                && (string.Equals(words[1], "asc", StringComparison.OrdinalIgnoreCase) || string.Equals(words[1], "desc", StringComparison.OrdinalIgnoreCase));
            bool endsWithKey = (words.Length == 1 || directed) && string.Equals(words[0], key, StringComparison.OrdinalIgnoreCase);
            return endsWithKey ? orderBy : $"{orderBy}, {key}";
        }

        /// <summary>A clause's text, or null when it is absent or blank.</summary>
        /// <param name="text">The text as sent.</param>
        /// <returns>The text, or null.</returns>
        private static string? Text(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>The node with its dependents.</summary>
        /// <param name="dependents">The dependents.</param>
        /// <returns>The node.</returns>
        private RowsNode With(IReadOnlyList<RowsNode> dependents)
        {
            return new RowsNode(Query, Kind, Entity, Surface, Select, OrderBy, KeyColumn, ParentKey, OwnerKeyType) { Dependents = dependents };
        }
    }

    /// <summary>What a query's root is to the row source that reads it.</summary>
    internal enum RowsRoot
    {
        /// <summary>The row source's own root, which takes the source's clauses and the caller's read filter.</summary>
        Own,

        /// <summary>A child collection of its owner query's root, restricted to the children of the owner's rows.</summary>
        Dependent,

        /// <summary>Another entity, read under the caller's read decision on it: a lookup target.</summary>
        Lookup,
    }
}
