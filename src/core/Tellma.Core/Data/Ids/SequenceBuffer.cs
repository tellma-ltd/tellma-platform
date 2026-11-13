// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Ids
{
    /// <summary>A contiguous range of ids, both ends included.</summary>
    /// <param name="First">The first id.</param>
    /// <param name="Last">The last id, at least <paramref name="First" />.</param>
    internal readonly record struct IdRange(long First, long Last)
    {
        /// <summary>How many ids the range holds.</summary>
        public long Count => Last - First + 1;

        /// <summary>How many ids of some ranges are at or below a floor.</summary>
        /// <param name="ranges">The ranges.</param>
        /// <param name="floor">The floor.</param>
        /// <returns>The ids at or below it.</returns>
        public static long CountAtOrBelow(IEnumerable<IdRange> ranges, long floor)
        {
            return ranges.Sum(range => range.First > floor ? 0 : Math.Min(range.Last, floor) - range.First + 1);
        }

        /// <summary>The parts of some ranges above a floor, in their order.</summary>
        /// <param name="ranges">The ranges.</param>
        /// <param name="floor">The floor.</param>
        /// <returns>The parts above it.</returns>
        public static List<IdRange> Above(IEnumerable<IdRange> ranges, long floor)
        {
            return [.. ranges.Where(range => range.Last > floor).Select(range => range with { First = Math.Max(range.First, floor + 1) })];
        }

        /// <summary>The ids of the range, ascending.</summary>
        /// <returns>The ids.</returns>
        public IEnumerable<long> Ids()
        {
            for (long id = First; id <= Last; id++)
            {
                yield return id;
            }
        }
    }

    /// <summary>
    ///     The ranges of one sequence of one tenant the process holds ahead of its saves. Takes come
    ///     from the front; a range that returns goes back to the front, so the ids it held are the next
    ///     handed out; a refill goes to the back. Thread-safe.
    /// </summary>
    /// <remarks>
    ///     The buffer keeps a floor that only rises: the largest id any read found its table holding
    ///     ahead of the sequence. No id at or below it is ever held again, whichever path brings it
    ///     back. A refill that finds the sequence behind also marks the buffer behind, which stops its
    ///     refills until a reservation reads a range again.
    /// </remarks>
    /// <param name="tenantId">The tenant whose database issued the ranges.</param>
    /// <param name="sequence">The sequence the ranges come from.</param>
    internal sealed class SequenceBuffer(int tenantId, SequenceInfo sequence)
    {
        /// <summary>Guards the ranges.</summary>
        private readonly Lock _gate = new();

        /// <summary>The ranges, in the order they are handed out.</summary>
        private readonly LinkedList<IdRange> _ranges = new();

        /// <summary>How many ids the ranges hold.</summary>
        private long _count;

        /// <summary>The largest id a read found the table holding ahead of the sequence; ids at or below it are never held.</summary>
        private long _floor = long.MinValue;

        /// <summary>Whether a refill found the sequence behind its table and no reservation read a range since.</summary>
        private bool _behind;

        /// <summary>The tenant whose database issued the ranges.</summary>
        public int TenantId { get; } = tenantId;

        /// <summary>The sequence the ranges come from.</summary>
        public SequenceInfo Sequence { get; } = sequence;

        /// <summary>How many ids the buffer holds.</summary>
        public long Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        /// <summary>Takes up to a number of ids from the front, splitting a range when it holds more.</summary>
        /// <param name="count">The ids wanted.</param>
        /// <returns>The ranges taken, holding <paramref name="count" /> ids or every id the buffer held.</returns>
        public List<IdRange> Take(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            List<IdRange> taken = [];
            lock (_gate)
            {
                while (count > 0 && _ranges.First is LinkedListNode<IdRange> node)
                {
                    IdRange range = node.Value;
                    if (range.Count <= count)
                    {
                        taken.Add(range);
                        _ranges.RemoveFirst();
                        count -= range.Count;
                        _count -= range.Count;
                    }
                    else
                    {
                        taken.Add(new IdRange(range.First, range.First + count - 1));
                        node.Value = new IdRange(range.First + count, range.Last);
                        _count -= count;
                        count = 0;
                    }
                }
            }

            return taken;
        }

        /// <summary>Whether a refill found the sequence behind its table and no reservation read a range since.</summary>
        public bool IsBehind
        {
            get
            {
                lock (_gate)
                {
                    return _behind;
                }
            }
        }

        /// <summary>Adds a fresh range at the back, less its ids at or below the floor.</summary>
        /// <param name="range">The range.</param>
        /// <returns>How many of its ids were discarded.</returns>
        public long Append(IdRange range)
        {
            lock (_gate)
            {
                long discarded = IdRange.CountAtOrBelow([range], _floor);
                if (range.Last > _floor)
                {
                    IdRange kept = range with { First = Math.Max(range.First, _floor + 1) };
                    _ = _ranges.AddLast(kept);
                    _count += kept.Count;
                }

                return discarded;
            }
        }

        /// <summary>Puts ranges that were taken back at the front, in their order, less their ids at or below the floor.</summary>
        /// <param name="ranges">The ranges.</param>
        /// <returns>How many of their ids were discarded.</returns>
        public long Return(IReadOnlyList<IdRange> ranges)
        {
            ArgumentNullException.ThrowIfNull(ranges);
            lock (_gate)
            {
                // Ids taken before a read raised the floor may come back stale: they never return.
                long discarded = IdRange.CountAtOrBelow(ranges, _floor);
                List<IdRange> kept = IdRange.Above(ranges, _floor);
                for (int index = kept.Count - 1; index >= 0; index--)
                {
                    _ = _ranges.AddFirst(kept[index]);
                    _count += kept[index].Count;
                }

                return discarded;
            }
        }

        /// <summary>
        ///     Raises the floor and drops every id at or below it: the table already holds an id that
        ///     large, so any of them may collide.
        /// </summary>
        /// <param name="floor">The table's largest id.</param>
        /// <returns>How many ids were dropped.</returns>
        public long DropAtOrBelow(long floor)
        {
            lock (_gate)
            {
                _floor = Math.Max(_floor, floor);
                floor = _floor;
                long dropped = IdRange.CountAtOrBelow(_ranges, floor);
                if (dropped == 0)
                {
                    return 0;
                }

                List<IdRange> kept = IdRange.Above(_ranges, floor);
                _ranges.Clear();
                foreach (IdRange range in kept)
                {
                    _ = _ranges.AddLast(range);
                }

                _count -= dropped;
                return dropped;
            }
        }

        /// <summary>Marks the buffer behind: its refills stop until a reservation reads a range.</summary>
        /// <returns>Whether it was not marked yet, so the condition is new.</returns>
        public bool MarkBehind()
        {
            lock (_gate)
            {
                bool fresh = !_behind;
                _behind = true;
                return fresh;
            }
        }

        /// <summary>Clears the behind mark: a reservation read a range, healing the sequence when it was behind.</summary>
        public void ClearBehind()
        {
            lock (_gate)
            {
                _behind = false;
            }
        }

        /// <summary>Drops every range.</summary>
        /// <returns>How many ids were dropped.</returns>
        public long Clear()
        {
            lock (_gate)
            {
                long dropped = _count;
                _ranges.Clear();
                _count = 0;
                return dropped;
            }
        }
    }

    /// <summary>A table's sequence, which serves the ids of its new rows.</summary>
    /// <param name="Entity">The table's entity.</param>
    /// <param name="Name">The sequence, <c>&lt;schema&gt;.sq_&lt;Table&gt;</c>.</param>
    /// <param name="PrimaryKey">The table's primary-key name, <c>PK_&lt;Table&gt;</c>.</param>
    internal sealed record SequenceInfo(EntityMetadata Entity, TableName Name, string PrimaryKey)
    {
        /// <summary>The table the sequence serves.</summary>
        public TableName Table => Entity.Table;

        /// <summary>Whether the key, and so the sequence, is a <c>bigint</c>.</summary>
        public bool IsLong => Entity.Key.ClrType == typeof(long);

        /// <summary>The value of the instruments' <c>sequence</c> tag: the schema-qualified name.</summary>
        public string Tag => Name.ToString();
    }
}
