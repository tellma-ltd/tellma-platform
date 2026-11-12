// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     The rows a measure aggregates, as a row-level predicate a list of those rows can run.
    /// </summary>
    /// <remarks>
    ///     A result of its own rather than a bare text, because "every row" is a successful answer
    ///     and a missing value already means that the measure did not parse.
    /// </remarks>
    /// <param name="Condition">
    ///     The predicate, self-contained and printed canonically: the distinct conditions of the
    ///     measure's aggregations, each parenthesized and joined with <c>or</c>. Null when every row
    ///     in scope feeds the measure.
    /// </param>
    public sealed record MeasureRows(string? Condition);
}
