// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Measures;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>
    ///     Looks at how the evaluator expects each partial column of a query's measures to arrive.
    /// </summary>
    public static partial class QueryexProbe
    {
        /// <summary>The backend type each whole-number partial is planned as.</summary>
        /// <param name="measures">A compiled query's measures.</param>
        /// <returns>
        ///     Parallel to the partials: <c>tinyint</c>, <c>smallint</c>, <c>int</c> or <c>bigint</c>
        ///     for a count and for a least or greatest whole number, which the evaluator reads from a
        ///     buffer of exactly that width; null for every other partial, whose type the reader
        ///     reports.
        /// </returns>
        /// <remarks>
        ///     A partial planned at another width than the backend's column is one the evaluator cannot
        ///     load from a reader, so a differential compares the two for every partial it runs.
        /// </remarks>
        public static IReadOnlyList<string?> PartialWholeTypes(CompiledMeasures measures)
        {
            ArgumentNullException.ThrowIfNull(measures);
            return [.. measures.Plans.Select(static plan => plan.Storage switch
            {
                // A count is always the wide count.
                PartialStorage.Count => "bigint",
                PartialStorage.Whole => plan.Width switch
                {
                    WholeWidth.TinyInt => "tinyint",
                    WholeWidth.SmallInt => "smallint",
                    WholeWidth.Int => "int",
                    WholeWidth.BigInt => "bigint",
                    _ => throw new InvalidOperationException("The width is not known."),
                },
                PartialStorage.Sum or PartialStorage.Decimal or PartialStorage.Bool or PartialStorage.Date
                    or PartialStorage.DateTime or PartialStorage.DateTimeOffset or PartialStorage.Text
                    or PartialStorage.Path => null,
                _ => throw new InvalidOperationException("The storage is not known."),
            })];
        }
    }
}
