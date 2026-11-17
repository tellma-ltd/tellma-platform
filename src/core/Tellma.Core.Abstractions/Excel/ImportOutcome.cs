// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     What an import committed: the parent and child counts, the runs of workbook rows it
    ///     committed, and its warnings. A background import's counts cover every attempt.
    /// </summary>
    /// <param name="Inserted">The parent rows inserted.</param>
    /// <param name="Updated">The parent rows updated.</param>
    /// <param name="ChildrenInserted">The child rows inserted, at every depth.</param>
    /// <param name="ChildrenUpdated">The child rows updated, at every depth.</param>
    /// <param name="ChildrenDeleted">The existing child rows the import dropped, never counting their own descendants.</param>
    /// <param name="CommittedRowRanges">
    ///     The workbook rows of every parent row committed, as maximal runs of consecutive rows in
    ///     ascending order; a fully blank row never breaks a run.
    /// </param>
    /// <param name="Warnings">What the import warned about; never an error.</param>
    /// <param name="Stopped">Whether a requested stop ended a background import between chunks, the counts covering the chunks that committed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="CommittedRowRanges" /> or <paramref name="Warnings" /> is null.</exception>
    /// <exception cref="ArgumentException">A list holds a null entry.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    public sealed record ImportOutcome(
        int Inserted,
        int Updated,
        int ChildrenInserted,
        int ChildrenUpdated,
        int ChildrenDeleted,
        IReadOnlyList<RowRange> CommittedRowRanges,
        IReadOnlyList<ImportError> Warnings,
        bool Stopped = false)
    {
        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public int Inserted { get; init => field = NotNegative(value, nameof(Inserted)); } = NotNegative(Inserted, nameof(Inserted));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public int Updated { get; init => field = NotNegative(value, nameof(Updated)); } = NotNegative(Updated, nameof(Updated));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public int ChildrenInserted { get; init => field = NotNegative(value, nameof(ChildrenInserted)); } =
            NotNegative(ChildrenInserted, nameof(ChildrenInserted));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public int ChildrenUpdated { get; init => field = NotNegative(value, nameof(ChildrenUpdated)); } =
            NotNegative(ChildrenUpdated, nameof(ChildrenUpdated));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public int ChildrenDeleted { get; init => field = NotNegative(value, nameof(ChildrenDeleted)); } =
            NotNegative(ChildrenDeleted, nameof(ChildrenDeleted));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public IReadOnlyList<RowRange> CommittedRowRanges { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(CommittedRowRanges)); } =
            ReadOnlyCopies.OfEntries(CommittedRowRanges, requireOne: false, nameof(CommittedRowRanges));

        /// <inheritdoc cref="ImportOutcome(int, int, int, int, int, IReadOnlyList{RowRange}, IReadOnlyList{ImportError}, bool)" />
        public IReadOnlyList<ImportError> Warnings { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Warnings)); } =
            ReadOnlyCopies.OfEntries(Warnings, requireOne: false, nameof(Warnings));

        /// <summary>Refuses a negative count.</summary>
        /// <param name="value">The count.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The count.</returns>
        private static int NotNegative(int value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, name);
            return value;
        }
    }
}
