// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Trees
{
    // The C# tree check of spec 0012 §10.4: the whole table's (Id, ParentId) with the payload's
    // parents laid over it, so a cycle or a depth beyond the limit is a field error with a path in
    // the common case. Under write skew the save's cycle fence and recursion limit (§10.2) catch
    // what this check missed. The save pipeline runs it as spec 0015 §6.6 step 6, its load riding
    // round trip 1 and its report made once the ids are assigned; as a validator it runs alone.
    /// <summary>
    ///     Refuses a tree save whose parents would close a cycle (<see cref="ValidationCodes.TreeCycle" />,
    ///     a row's own id included) or nest a row deeper than the tree's depth limit
    ///     (<see cref="ValidationCodes.TreeTooDeep" />), each on the saved row's <c>ParentId</c>.
    /// </summary>
    /// <typeparam name="TEntity">The tree entity, a <see cref="TreeEntity{TKey}" /> of an <see cref="int" /> or a <see cref="long" /> key.</typeparam>
    /// <param name="metadata">The entity metadata, which names the tree's columns and depth limit.</param>
    /// <param name="schemas">The query schemas, whose descriptor the table's rows are loaded through.</param>
    internal sealed class TreeCycleValidator<TEntity>(IEntityMetadataProvider metadata, QueryexSchemaProvider schemas) : IEntityValidator<TEntity>
        where TEntity : class
    {
        /// <summary>The depth of a row on a cycle, or under one: it reaches no root.</summary>
        private const int Unrooted = -1;

        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (Request(context) is TreeCheck check)
            {
                await context.Loader.LoadAsync().ConfigureAwait(false);
                Report(check, context);
            }
        }

        /// <summary>
        ///     Requests the table's ids and parents from the context's loader, unless no payload row
        ///     names a parent: such a payload can close no cycle and nest no row deeper.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <returns>The pending check, which <see cref="Report" /> completes once the load ran; null when there is nothing to check.</returns>
        /// <exception cref="InvalidOperationException">The entity is no tree, or the tenant's settings are not loaded.</exception>
        internal TreeCheck? Request(SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            EntityMetadata entity = metadata.Get(typeof(TEntity));
            TreeMetadata tree = entity.Tree
                ?? throw new InvalidOperationException($"The entity {entity.Name} is not a tree, so it takes no tree check.");

            // --- The payload's parents; none that names a parent can close a cycle or nest deeper ---
            if (context.Entities.Select(static (row, index) => Payload(row, index)).All(static row => row.ParentId is null))
            {
                return null;
            }

            // --- The table's ids and parents, loaded with the round ---
            MultilingualShape shape = context.Context.TenantSettings?.Shape
                ?? throw new InvalidOperationException("The tenant's settings are not loaded in this scope, so the tree check has no shape to load the table under.");
            EntityDescriptor root = schemas.Map(shape).Descriptor(entity);
            ContextRef<QueryRowSet> loaded = context.Loader.Rows(new QuerySpec
            {
                Root = root,
                Select = $"{root.Key.Name},{tree.ParentKey.Name}",
            });
            return new TreeCheck(tree, loaded);
        }

        /// <summary>
        ///     Reports the cycles and the depths beyond the limit, from the loaded table with the
        ///     payload's parents laid over it. The payload's ids are read now, so a save reports after
        ///     its new rows received their ids.
        /// </summary>
        /// <param name="check">The check <see cref="Request" /> returned, its load run.</param>
        /// <param name="context">The save's context, whose errors take the report.</param>
        internal static void Report(TreeCheck check, SaveContext<TEntity> context)
        {
            ArgumentNullException.ThrowIfNull(check);
            ArgumentNullException.ThrowIfNull(context);
            TreeMetadata tree = check.Tree;

            // --- The table's parents, with the payload's laid over them ---
            List<(int Index, long Id, long? ParentId)> saved = [.. context.Entities.Select(static (row, index) => Payload(row, index))];
            Dictionary<long, long?> parents = Parents(check.Loaded.Value);
            foreach ((int _, long id, long? parentId) in saved)
            {
                parents[id] = parentId;
            }

            // --- Each row's depth, a root at 1, and each row's height, a leaf at 1 ---
            Dictionary<long, int> depths = Depths(parents);
            Dictionary<long, int> heights = Heights(parents, depths);

            // --- The errors, on the saved rows' parents ---
            ValidationPath rows = ValidationPath.Root;
            string parentProperty = tree.ParentKey.Name;
            foreach ((int index, long id, long? parentId) in saved)
            {
                if (parentId is null)
                {
                    continue;
                }

                // A row on a cycle is refused; a row only under one is not, since the rows on it are.
                if (depths[id] == Unrooted)
                {
                    if (OnCycle(parents, id))
                    {
                        context.Errors.Add(rows.Index(index).Property(parentProperty), ValidationCodes.TreeCycle);
                    }

                    continue;
                }

                // The deepest row the saved row's subtree reaches under its new parent.
                if (depths[id] + heights[id] - 1 > tree.MaxDepth)
                {
                    context.Errors.Add(rows.Index(index).Property(parentProperty), ValidationCodes.TreeTooDeep);
                }
            }
        }

        /// <summary>A pending tree check: the tree, and the table's ids and parents once the round ran.</summary>
        /// <param name="Tree">The tree's metadata.</param>
        /// <param name="Loaded">The table's ids and parents.</param>
        internal sealed record TreeCheck(TreeMetadata Tree, ContextRef<QueryRowSet> Loaded);

        /// <summary>A saved row's id and parent, read off the tree entity of its key type.</summary>
        /// <param name="row">The row.</param>
        /// <param name="index">Its position in the payload.</param>
        /// <returns>The position, the id and the parent.</returns>
        /// <exception cref="InvalidOperationException">The row is not a tree entity of an int or a long key.</exception>
        private static (int Index, long Id, long? ParentId) Payload(TEntity row, int index)
        {
            return row switch
            {
                TreeEntity<int> narrow => (index, narrow.Id, narrow.ParentId),
                TreeEntity<long> wide => (index, wide.Id, wide.ParentId),
                _ => throw new InvalidOperationException($"The tree check reads a tree entity of an int or a long key; {typeof(TEntity).Name} is neither."),
            };
        }

        /// <summary>The parent of every row the table holds, from the loaded id and parent columns.</summary>
        /// <param name="rows">The loaded rows: the id, then the parent.</param>
        /// <returns>Each row's parent, null for a root.</returns>
        private static Dictionary<long, long?> Parents(QueryRowSet rows)
        {
            Array ids = rows.GetBuffer(0);
            Array parentIds = rows.GetBuffer(1);
            Dictionary<long, long?> parents = new(rows.RowCount);
            for (int row = 0; row < rows.RowCount; row++)
            {
                parents[Key(ids, row)] = rows.IsNull(1, row) ? null : Key(parentIds, row);
            }

            return parents;
        }

        /// <summary>One key of a loaded key column, of either key type.</summary>
        /// <param name="buffer">The column's buffer.</param>
        /// <param name="row">The row.</param>
        /// <returns>The key.</returns>
        private static long Key(Array buffer, int row)
        {
            return buffer switch
            {
                int[] narrow => narrow[row],
                long[] wide => wide[row],
                _ => throw new InvalidOperationException($"A tree key column loads as int or long values, not {buffer.GetType().Name}."),
            };
        }

        /// <summary>
        ///     The depth of every row, a root at 1, or <see cref="Unrooted" /> for a row on or under a
        ///     cycle. A parent the table does not hold counts as a root; its own error is reported
        ///     where the parent is resolved.
        /// </summary>
        /// <param name="parents">Each row's parent.</param>
        /// <returns>Each row's depth.</returns>
        private static Dictionary<long, int> Depths(Dictionary<long, long?> parents)
        {
            Dictionary<long, int> depths = new(parents.Count);
            List<long> path = [];
            HashSet<long> walking = [];
            foreach (long start in parents.Keys)
            {
                // --- Up from the row until a row of known depth, a root, or a row already on the walk ---
                path.Clear();
                walking.Clear();
                int above = 0;
                long current = start;
                while (true)
                {
                    if (depths.TryGetValue(current, out int known))
                    {
                        above = known;
                        break;
                    }

                    if (!walking.Add(current))
                    {
                        above = Unrooted;
                        break;
                    }

                    // Every row on the walk is one the table holds: the start is, and so is each parent climbed to.
                    path.Add(current);
                    if (parents[current] is not long next)
                    {
                        // A root: nothing above it.
                        above = 0;
                        break;
                    }

                    if (!parents.ContainsKey(next))
                    {
                        // An unknown parent counts as a root above the row.
                        above = 1;
                        break;
                    }

                    current = next;
                }

                // --- Down the walk: each row one deeper than the one above it, or unrooted with it ---
                for (int step = path.Count - 1; step >= 0; step--)
                {
                    above = above == Unrooted ? Unrooted : above + 1;
                    depths[path[step]] = above;
                }
            }

            return depths;
        }

        /// <summary>The height of every rooted row, a leaf at 1: the levels its subtree spans.</summary>
        /// <param name="parents">Each row's parent.</param>
        /// <param name="depths">Each row's depth.</param>
        /// <returns>Each rooted row's height.</returns>
        private static Dictionary<long, int> Heights(Dictionary<long, long?> parents, Dictionary<long, int> depths)
        {
            // Deepest rows first, so each row's height is final before its parent reads it.
            Dictionary<long, int> heights = new(parents.Count);
            foreach (long id in depths.Where(static pair => pair.Value != Unrooted).OrderByDescending(static pair => pair.Value).Select(static pair => pair.Key))
            {
                int height = heights.TryGetValue(id, out int below) ? below : 1;
                heights[id] = height;
                if (parents[id] is long parent && heights.GetValueOrDefault(parent) < height + 1)
                {
                    heights[parent] = height + 1;
                }
            }

            return heights;
        }

        /// <summary>Whether a row lies on a cycle: the walk up from it comes back to it.</summary>
        /// <param name="parents">Each row's parent.</param>
        /// <param name="id">The row.</param>
        /// <returns>True when the row is on a cycle.</returns>
        private static bool OnCycle(Dictionary<long, long?> parents, long id)
        {
            HashSet<long> seen = [];
            long current = id;
            while (parents.TryGetValue(current, out long? parent) && parent is long next)
            {
                if (next == id)
                {
                    return true;
                }

                if (!seen.Add(next))
                {
                    return false;
                }

                current = next;
            }

            return false;
        }
    }
}
