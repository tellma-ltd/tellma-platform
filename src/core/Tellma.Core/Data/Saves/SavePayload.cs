// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Data.Saves
{
    /// <summary>
    ///     A save's payload sorted by the plan's tables: each table's new and existing rows, and the
    ///     parents whose collection the payload supplied. A row is new when a reservation assigned its
    ///     id and no batch inserting it committed since; every other row is existing.
    /// </summary>
    internal sealed class SavePayload
    {
        /// <summary>The rows of each table, by node.</summary>
        private readonly Dictionary<SaveNode, SaveRows> _parts;

        /// <summary>The allocator, which tells new rows from existing ones.</summary>
        private readonly IdAllocator _allocator;

        /// <summary>Creates an empty payload.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="allocator">The allocator.</param>
        private SavePayload(SavePlan plan, IdAllocator allocator)
        {
            _parts = plan.Nodes.ToDictionary(static node => node, static _ => new SaveRows());
            _allocator = allocator;
        }

        /// <summary>The reservations that assigned the new rows' ids.</summary>
        public HashSet<ReservationHold> Holds { get; } = [];

        /// <summary>The new rows of every table.</summary>
        public int Inserted => _parts.Values.Sum(static part => part.New.Count);

        /// <summary>The rows of the largest table-valued parameter the save binds.</summary>
        public int LargestTable => _parts.Values.Max(static part => Math.Max(part.Parents.Count, Math.Max(part.New.Count, part.Existing.Count)));

        /// <summary>The new rows of every table.</summary>
        public IEnumerable<object> NewRows => _parts.Values.SelectMany(static part => part.New);

        /// <summary>A table's rows.</summary>
        /// <param name="node">The table's node.</param>
        /// <returns>The rows.</returns>
        public SaveRows this[SaveNode node] => _parts[node];

        /// <summary>Sorts a payload by the plan's tables.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The roots, with their collections populated or null.</param>
        /// <param name="allocator">The allocator.</param>
        /// <returns>The sorted payload.</returns>
        /// <exception cref="ArgumentException">
        ///     A row is null, of another type or without an id; two rows of one table share an id; or a
        ///     child's parent key is not its parent's id.
        /// </exception>
        /// <exception cref="InvalidOperationException">A new row's reservation gave its ids back.</exception>
        public static SavePayload Collect(SavePlan plan, IReadOnlyList<object> rows, IdAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(allocator);

            SavePayload payload = new(plan, allocator);
            payload.Classify(plan.Root, rows);
            return payload;
        }

        /// <summary>Sorts one table's rows, then each of its collections' rows under them, recursively.</summary>
        /// <param name="node">The table's node.</param>
        /// <param name="rows">The table's rows.</param>
        private void Classify(SaveNode node, IReadOnlyList<object> rows)
        {
            SaveRows part = _parts[node];
            HashSet<long> seen = [];
            foreach (object? row in rows)
            {
                // --- A row of the table, with its id assigned, once ---
                if (row is null || !node.Entity.ClrType.IsInstanceOfType(row))
                {
                    throw new ArgumentException(
                        $"A save of {node.Entity.Name} rows holds {(row is null ? "a null row" : $"a {row.GetType().Name}")} where a {node.Entity.ClrType.Name} row belongs.",
                        nameof(rows));
                }

                long id = PayloadRows.KeyOf(node.Entity, row);
                if (id <= 0)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"A {node.Entity.Name} row of the save has no id yet ({id}). Reserve ids and assign them before the save."),
                        nameof(rows));
                }

                if (!seen.Add(id))
                {
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The save holds two {node.Entity.Name} rows with the id {id}."), nameof(rows));
                }

                // --- New when a live reservation assigned the id ---
                if (_allocator.HoldOf(row) is ReservationHold hold)
                {
                    if (hold.IsReturned)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The {node.Entity.Name} row {id} carries an id its reservation gave back when it was disposed. Reserve and assign ids again."));
                    }

                    _ = Holds.Add(hold);
                    part.New.Add(row);
                }
                else
                {
                    part.Existing.Add(row);
                }
            }

            // --- Each collection: its rows under the parents that supplied it ---
            foreach (SaveNode child in node.Children)
            {
                SaveRows childPart = _parts[child];
                List<object> children = [];
                foreach (object row in rows)
                {
                    if (child.Collection!.Getter(row) is not IReadOnlyList<object> list)
                    {
                        continue;
                    }

                    long parentId = PayloadRows.KeyOf(node.Entity, row);
                    childPart.Parents.Add(parentId);
                    foreach (object? item in list)
                    {
                        if (item is not null && child.Entity.ClrType.IsInstanceOfType(item) && PayloadRows.ValueOf(child.Collection.ParentKey, item) != parentId)
                        {
                            throw new ArgumentException(
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"A {child.Entity.Name} row in the {child.Collection.Property} of the {node.Entity.Name} row {parentId} holds another parent key; a child's parent key is its parent's id."),
                                nameof(rows));
                        }

                        children.Add(item!);
                    }
                }

                Classify(child, children);
            }
        }
    }

    /// <summary>One table's part of a save payload.</summary>
    internal sealed class SaveRows
    {
        /// <summary>The rows to insert.</summary>
        public List<object> New { get; } = [];

        /// <summary>The rows to update when they changed.</summary>
        public List<object> Existing { get; } = [];

        /// <summary>The ids of the parents whose collection the payload supplied; empty for the root.</summary>
        public List<long> Parents { get; } = [];
    }
}
