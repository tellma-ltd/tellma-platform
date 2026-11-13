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
    /// <param name="sequence">The sequence the ranges come from.</param>
    internal sealed class SequenceBuffer(SequenceInfo sequence)
    {
        /// <summary>Guards the ranges.</summary>
        private readonly Lock _gate = new();

        /// <summary>The ranges, in the order they are handed out.</summary>
        private readonly LinkedList<IdRange> _ranges = new();

        /// <summary>How many ids the ranges hold.</summary>
        private long _count;

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

        /// <summary>Adds a fresh range at the back.</summary>
        /// <param name="range">The range.</param>
        public void Append(IdRange range)
        {
            lock (_gate)
            {
                _ = _ranges.AddLast(range);
                _count += range.Count;
            }
        }

        /// <summary>Puts ranges that were taken back at the front, in their order.</summary>
        /// <param name="ranges">The ranges.</param>
        public void Return(IReadOnlyList<IdRange> ranges)
        {
            ArgumentNullException.ThrowIfNull(ranges);
            lock (_gate)
            {
                for (int index = ranges.Count - 1; index >= 0; index--)
                {
                    _ = _ranges.AddFirst(ranges[index]);
                    _count += ranges[index].Count;
                }
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
