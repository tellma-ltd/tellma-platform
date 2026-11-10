// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>One page of a rows query: the rows, and the count and ancestors when the query asked for them.</summary>
    /// <param name="Rows">The page's rows.</param>
    /// <param name="Count">
    ///     The capped count of every row the query matches, when a cap was given: a value of the cap plus
    ///     one means more rows than the cap. Null when no count was asked for.
    /// </param>
    /// <param name="Ancestors">
    ///     The ancestors of a tree page's rows that are not on the page, in the same columns, when they
    ///     were asked for; null otherwise.
    /// </param>
    public sealed record RowPage(QueryRowSet Rows, int? Count, QueryRowSet? Ancestors);
}
