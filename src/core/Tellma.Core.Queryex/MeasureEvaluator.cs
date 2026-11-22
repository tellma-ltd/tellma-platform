// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Measures;

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A query's measures prepared for one run: the backend type of every number fixed from
    ///     what that run's reader reported, ready to evaluate over any cell of the run's tables.
    /// </summary>
    /// <remarks>
    ///     Immutable once prepared. Evaluating only reads it, so one evaluator can serve several
    ///     threads, each over a table of its own.
    /// </remarks>
    public sealed class MeasureEvaluator
    {

        /// <summary>Initializes the evaluator.</summary>
        /// <param name="run">The prepared run.</param>
        /// <param name="resultTypes">Each measure's backend type, for a number.</param>
        internal MeasureEvaluator(MeasureRun run, ImmutableArray<QueryexNumericType?> resultTypes)
        {
            Run = run;
            ResultTypes = resultTypes;
        }

        /// <summary>
        ///     Parallel to <see cref="CompiledMeasures.Items" />: for a numeric measure, the precision
        ///     and scale its value comes back at, which are the backend's for the same expression; for
        ///     any other measure, null.
        /// </summary>
        /// <remarks>
        ///     A measure the backend holds as a whole number reports the decimal of the same digit
        ///     count: <c>(3, 0)</c>, <c>(5, 0)</c>, <c>(10, 0)</c> or <c>(19, 0)</c>.
        /// </remarks>
        public IReadOnlyList<QueryexNumericType?> ResultTypes { get; }

        /// <summary>The prepared run, for the tables it creates.</summary>
        internal MeasureRun Run { get; }

        /// <summary>Creates an empty table of partials for this run.</summary>
        /// <param name="capacity">How many cells the table is expected to hold; it grows past that as needed.</param>
        /// <returns>The table.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is negative.</exception>
        public PartialTable CreateTable(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);
            return new PartialTable(this, capacity);
        }

        /// <summary>Evaluates one measure over one cell.</summary>
        /// <param name="table">A table this evaluator created.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="measure">The measure's position in <see cref="CompiledMeasures.Items" />.</param>
        /// <returns>
        ///     The value, typed as the measure is; a number comes back at the precision and scale
        ///     <see cref="ResultTypes" /> reports for it.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="table" /> is null.</exception>
        /// <exception cref="ArgumentException">Another evaluator created <paramref name="table" />.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="cell" /> or <paramref name="measure" /> is out of range.</exception>
        /// <exception cref="QueryexEvaluationException">
        ///     The measure divides by zero or overflows for the cell, where the backend would have
        ///     raised an error computing it over the same rows.
        /// </exception>
        public MeasureValue Evaluate(PartialTable table, int cell, int measure)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (!ReferenceEquals(table.Owner, this))
            {
                throw new ArgumentException(
                    "The table was created by another evaluator, whose partials may be typed differently.",
                    nameof(table));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(cell);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, table.CellCount);
            ArgumentOutOfRangeException.ThrowIfNegative(measure);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(measure, ResultTypes.Count);
            return Run.Evaluate(table.Columns, cell, measure);
        }
    }
}
