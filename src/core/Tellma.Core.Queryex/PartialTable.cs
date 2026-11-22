// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Measures;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A query's partials across a set of cells: one typed array per partial and one slot per
    ///     cell, which rows are loaded into and cells merged across.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Serves one cube, one totals row or one export, for the run whose evaluator created
    ///         it. Nothing it holds boxes.
    ///     </para>
    ///     <para>Not safe for use from several threads at once.</para>
    /// </remarks>
    public sealed class PartialTable
    {
        /// <summary>The cells the table holds before it first grows, when none were expected.</summary>
        private const int MinimumCapacity = 4;

        /// <summary>The cells there is room for.</summary>
        private int _capacity;

        /// <summary>Initializes an empty table.</summary>
        /// <param name="owner">The evaluator that created it.</param>
        /// <param name="capacity">The cells expected.</param>
        internal PartialTable(MeasureEvaluator owner, int capacity)
        {
            Owner = owner;
            _capacity = Math.Max(capacity, MinimumCapacity);
            Columns = owner.Run.CreateColumns(_capacity);
        }

        /// <summary>How many cells the table holds.</summary>
        public int CellCount { get; private set; }

        /// <summary>The evaluator that created the table.</summary>
        internal MeasureEvaluator Owner { get; }

        /// <summary>The partials' columns.</summary>
        internal PartialColumn[] Columns { get; }

        /// <summary>Adds an empty cell: its sums and least and greatest values absent, its counts zero.</summary>
        /// <returns>The new cell's position.</returns>
        public int AddCell()
        {
            if (CellCount == _capacity)
            {
                // Doubled, so a table filled one cell at a time grows in amortized constant time.
                _capacity = checked(_capacity * 2);
                foreach (PartialColumn column in Columns)
                {
                    column.Grow(_capacity);
                }
            }

            return CellCount++;
        }

        /// <summary>Combines one row's value of one partial into a cell.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="partial">The partial's position in <see cref="CompiledMeasures.Partials" />.</param>
        /// <param name="buffer">
        ///     The reader's typed array for the partial's column: <c>decimal[]</c> for a sum and a
        ///     decimal, <c>long[]</c> for a count, and for a least or greatest value the array of its
        ///     own type — <c>byte[]</c>, <c>short[]</c>, <c>int[]</c>, <c>long[]</c>, <c>bool[]</c>,
        ///     <c>DateOnly[]</c>, <c>DateTime[]</c>, <c>DateTimeOffset[]</c>, or <c>string?[]</c> for
        ///     text and for a hierarchy position's path.
        /// </param>
        /// <param name="row">The row's position in <paramref name="buffer" />; its value must be present.</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cell" />, <paramref name="partial" /> or <paramref name="row" /> is out of range.</exception>
        /// <exception cref="ArgumentException">
        ///     <paramref name="buffer" /> is not the array the partial is read from, or the row's value
        ///     does not fit the partial's type or is absent.
        /// </exception>
        /// <exception cref="QueryexEvaluationException">A sum passes 38 digits.</exception>
        public void Add(int cell, int partial, Array buffer, int row)
        {
            CheckCell(cell);
            ArgumentOutOfRangeException.ThrowIfNegative(partial);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(partial, Columns.Length);
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, buffer.Length);
            Columns[partial].Add(cell, buffer, row);
        }

        /// <summary>Combines one cell into another, partial by partial.</summary>
        /// <param name="target">The cell combined into.</param>
        /// <param name="source">The cell combined from, which is left as it was.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="target" /> or <paramref name="source" /> is out of range.</exception>
        /// <exception cref="ArgumentException"><paramref name="target" /> and <paramref name="source" /> are one cell.</exception>
        /// <exception cref="QueryexEvaluationException">A sum passes 38 digits.</exception>
        public void Merge(int target, int source)
        {
            CheckCell(target);
            CheckCell(source);
            if (target == source)
            {
                throw new ArgumentException("A cell cannot be merged into itself.", nameof(source));
            }

            foreach (PartialColumn column in Columns)
            {
                column.Merge(target, source);
            }
        }

        /// <summary>Checks that a cell exists.</summary>
        /// <param name="cell">The cell.</param>
        private void CheckCell(int cell)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(cell);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, CellCount);
        }
    }
}
