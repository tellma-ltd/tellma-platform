// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Ids
{
    /// <summary>
    ///     What one reservation holds: per table, the ranges taken from the buffer and those its
    ///     statements reserved. It assigns them, and gives them back when it is disposed before any
    ///     persist was attempted with the rows it assigned.
    /// </summary>
    /// <remarks>
    ///     A reservation statement's range may arrive after the reservation was disposed, when the
    ///     batch it rides executes later; it then goes straight back to the buffer. Thread-safe.
    /// </remarks>
    /// <param name="allocator">The allocator.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="root">The payload's root entity.</param>
    internal sealed class ReservationHold(IdAllocator allocator, int tenantId, EntityMetadata root) : IIdReservationHold
    {
        /// <summary>Guards the hold's state.</summary>
        private readonly Lock _gate = new();

        /// <summary>The needs, one per table with new rows, in payload order.</summary>
        private readonly List<Need> _needs = [];

        /// <summary>Whether the ids were assigned.</summary>
        private bool _assigned;

        /// <summary>Whether the hold was disposed.</summary>
        private bool _disposed;

        /// <summary>Whether a batch saving the assigned rows executed, so no id may return.</summary>
        private bool _persistAttempted;

        /// <inheritdoc />
        public bool IsImmediate { get; private set; }

        /// <summary>Whether the hold gave its ids back: it was disposed before any persist was attempted.</summary>
        public bool IsReturned
        {
            get
            {
                lock (_gate)
                {
                    return _disposed && !_persistAttempted;
                }
            }
        }

        /// <summary>Adds a table's need: the ranges taken from the buffer and how many ids a statement still reserves.</summary>
        /// <param name="sequence">The table's sequence.</param>
        /// <param name="taken">The ranges taken from the buffer.</param>
        /// <param name="pending">The ids a reservation statement reserves.</param>
        /// <returns>The need.</returns>
        public Need Add(SequenceInfo sequence, List<IdRange> taken, long pending)
        {
            Need need = new(sequence, taken, pending);
            lock (_gate)
            {
                _needs.Add(need);
            }

            return need;
        }

        /// <summary>Fixes whether the buffer covered every need, once every need was added.</summary>
        public void Seal()
        {
            lock (_gate)
            {
                IsImmediate = _needs.TrueForAll(static need => need.Pending == 0);
            }
        }

        /// <summary>Hands a need the ids its statement reserved; after a disposal without a persist they return to the buffer.</summary>
        /// <param name="need">The need.</param>
        /// <param name="ranges">The ranges, holding exactly the pending ids.</param>
        public void Fill(Need need, List<IdRange> ranges)
        {
            lock (_gate)
            {
                if (_disposed && !_persistAttempted)
                {
                    allocator.Return(tenantId, need.Sequence, ranges);
                    need.Pending = 0;
                    return;
                }

                need.Held.AddRange(ranges);
                need.Pending = 0;
            }
        }

        /// <summary>Records that a need's statement ran but its ids could not be settled.</summary>
        /// <param name="need">The need.</param>
        /// <param name="failure">The failure, which <see cref="Assign" /> reports.</param>
        public void Fail(Need need, Exception failure)
        {
            lock (_gate)
            {
                need.Failure = failure;
            }
        }

        /// <summary>Records that a batch saving the assigned rows executed: from now on no id returns.</summary>
        public void MarkPersistAttempted()
        {
            lock (_gate)
            {
                _persistAttempted = true;
            }
        }

        /// <inheritdoc />
        public void Assign(IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, typeof(IdReservation));
                if (_assigned)
                {
                    throw new InvalidOperationException("The reservation already assigned its ids. A reservation assigns once.");
                }

                // --- Every table's ids must be in hand ---
                foreach (Need need in _needs)
                {
                    if (need.Failure is Exception failure)
                    {
                        throw new InvalidOperationException($"The reservation of {need.Sequence.Entity.Name} ids failed after its statement ran, so it holds none.", failure);
                    }

                    if (need.Pending > 0)
                    {
                        throw new InvalidOperationException(
                            $"The {need.Sequence.Entity.Name} ids are not reserved yet. They are once the batch given to Reserve executed and its body ran; assign after it executes, or at once when IsImmediate.");
                    }
                }

                // --- The plan: the next id of each table to each new row, in topological order ---
                List<PayloadRow> payload = PayloadRows.Enumerate(root, rows);
                var next = _needs.ToDictionary(
                    static need => need.Sequence.Entity.ClrType,
                    static need => need.Held.SelectMany(static range => range.Ids()).GetEnumerator());
                List<(PayloadRow Row, long Id)> plan = [];
                Dictionary<(Type Type, long Temporary), long> temporaries = [];
                foreach (PayloadRow row in payload)
                {
                    long current = row.Id;
                    if (current > 0)
                    {
                        continue;
                    }

                    if (!next.TryGetValue(row.Entity.ClrType, out IEnumerator<long>? ids) || !ids.MoveNext())
                    {
                        throw Mismatch(row.Entity);
                    }

                    plan.Add((row, ids.Current));
                    if (current < 0)
                    {
                        temporaries[(row.Entity.ClrType, current)] = ids.Current;
                    }
                }

                foreach (Need need in _needs)
                {
                    if (next[need.Sequence.Entity.ClrType].MoveNext())
                    {
                        throw Mismatch(need.Sequence.Entity);
                    }
                }

                // --- The ids, then the parent keys from the nesting, then the temporary ids wherever referenced ---
                foreach ((PayloadRow row, long id) in plan)
                {
                    PayloadRows.SetId(row.Entity.Key, row.Row, id);
                    allocator.Register(row.Row, this);
                }

                foreach (PayloadRow row in payload)
                {
                    if (row is { Collection: ChildCollectionMetadata collection, Owner: object owner, OwnerEntity: EntityMetadata ownerEntity })
                    {
                        PayloadRows.SetId(collection.ParentKey, row.Row, PayloadRows.KeyOf(ownerEntity, owner));
                    }

                    foreach (ReferenceMetadata reference in row.Entity.References)
                    {
                        Rewrite(reference.ForeignKey, row.Row, reference.Target.ClrType, temporaries);
                    }

                    if (row.Entity.Tree is TreeMetadata tree)
                    {
                        Rewrite(tree.ParentKey, row.Row, row.Entity.ClrType, temporaries);
                    }
                }

                _assigned = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                // After a persist attempt the ids may be in the database: none returns.
                if (_persistAttempted)
                {
                    return;
                }

                foreach (Need need in _needs)
                {
                    allocator.Return(tenantId, need.Sequence, need.Held);
                    need.Held.Clear();
                }
            }
        }

        /// <summary>Rewrites a temporary id a foreign key holds to the id the payload's row that defines it received.</summary>
        /// <param name="property">The foreign key.</param>
        /// <param name="row">The row.</param>
        /// <param name="target">The referenced entity.</param>
        /// <param name="temporaries">The ids by entity and temporary id.</param>
        private static void Rewrite(PropertyMetadata property, object row, Type target, Dictionary<(Type Type, long Temporary), long> temporaries)
        {
            // A temporary id no row defines stays, for the pipeline to report at its path.
            if (PayloadRows.ValueOf(property, row) is long value && value < 0 && temporaries.TryGetValue((target, value), out long id))
            {
                PayloadRows.SetId(property, row, id);
            }
        }

        /// <summary>The refusal of a payload whose new rows differ in number from what was reserved.</summary>
        /// <param name="entity">The entity whose count differs.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException Mismatch(EntityMetadata entity)
        {
            return new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The payload holds another number of new {entity.Name} rows than was reserved. Assign the payload given to Reserve, unchanged."));
        }

        /// <summary>One table's part of the reservation.</summary>
        /// <param name="sequence">The table's sequence.</param>
        /// <param name="held">The ranges held so far.</param>
        /// <param name="pending">The ids a reservation statement still reserves.</param>
        public sealed class Need(SequenceInfo sequence, List<IdRange> held, long pending)
        {
            /// <summary>The table's sequence.</summary>
            public SequenceInfo Sequence { get; } = sequence;

            /// <summary>The ranges held.</summary>
            public List<IdRange> Held { get; } = held;

            /// <summary>The ids a reservation statement still reserves; zero once filled.</summary>
            public long Pending { get; set; } = pending;

            /// <summary>The failure that settled the statement's range, if one did.</summary>
            public Exception? Failure { get; set; }
        }
    }
}
