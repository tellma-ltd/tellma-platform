// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The checkpoint of a background import, committed with each chunk: where the next chunk
    ///     starts, the running totals, the rows committed and the new ids later rows reference.
    /// </summary>
    /// <param name="NextRow">The position, in the import's row order, of the first parent row not yet committed.</param>
    /// <param name="OrderHash">The hash of the row order the checkpoint was written under; a resumed import must recompute the same.</param>
    /// <param name="Inserted">The parent rows inserted by every committed chunk.</param>
    /// <param name="Updated">The parent rows updated by every committed chunk.</param>
    /// <param name="ChildrenInserted">The child rows inserted, at every depth.</param>
    /// <param name="ChildrenUpdated">The child rows updated, at every depth.</param>
    /// <param name="ChildrenDeleted">The existing child rows dropped, never counting their own descendants.</param>
    /// <param name="CommittedRowRanges">The workbook rows of every committed parent row, as maximal runs in ascending order.</param>
    /// <param name="CommittedNewIds">
    ///     For each committed new row a later row references by its negative sheet id, that sheet id
    ///     mapped to the id the row was committed with.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="NextRow" /> or a count is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="OrderHash" />, <paramref name="CommittedRowRanges" /> or <paramref name="CommittedNewIds" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="CommittedRowRanges" /> holds a null entry.</exception>
    public sealed record ImportCheckpointState(
        int NextRow,
        string OrderHash,
        int Inserted,
        int Updated,
        int ChildrenInserted,
        int ChildrenUpdated,
        int ChildrenDeleted,
        IReadOnlyList<RowRange> CommittedRowRanges,
        IReadOnlyDictionary<long, long> CommittedNewIds)
    {
        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int NextRow { get; init => field = NotNegative(value, nameof(NextRow)); } = NotNegative(NextRow, nameof(NextRow));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public string OrderHash { get; init => field = value ?? throw new ArgumentNullException(nameof(OrderHash)); } =
            OrderHash ?? throw new ArgumentNullException(nameof(OrderHash));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int Inserted { get; init => field = NotNegative(value, nameof(Inserted)); } = NotNegative(Inserted, nameof(Inserted));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int Updated { get; init => field = NotNegative(value, nameof(Updated)); } = NotNegative(Updated, nameof(Updated));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int ChildrenInserted { get; init => field = NotNegative(value, nameof(ChildrenInserted)); } =
            NotNegative(ChildrenInserted, nameof(ChildrenInserted));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int ChildrenUpdated { get; init => field = NotNegative(value, nameof(ChildrenUpdated)); } =
            NotNegative(ChildrenUpdated, nameof(ChildrenUpdated));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public int ChildrenDeleted { get; init => field = NotNegative(value, nameof(ChildrenDeleted)); } =
            NotNegative(ChildrenDeleted, nameof(ChildrenDeleted));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public IReadOnlyList<RowRange> CommittedRowRanges { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(CommittedRowRanges)); } =
            ReadOnlyCopies.OfEntries(CommittedRowRanges, requireOne: false, nameof(CommittedRowRanges));

        /// <inheritdoc cref="ImportCheckpointState(int, string, int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyDictionary{long, long})" />
        public IReadOnlyDictionary<long, long> CommittedNewIds { get; init => field = Copy(value); } = Copy(CommittedNewIds);

        /// <summary>Refuses a negative count or position.</summary>
        /// <param name="value">The value.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The value.</returns>
        private static int NotNegative(int value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, name);
            return value;
        }

        /// <summary>Copies the id map.</summary>
        /// <param name="value">The map.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyDictionary<long, long> Copy(IReadOnlyDictionary<long, long> value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(CommittedNewIds));
            return new Dictionary<long, long>(value).AsReadOnly();
        }
    }
}
