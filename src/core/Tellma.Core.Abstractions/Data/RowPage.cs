// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>One page of a rows query: the rows, and the count and ancestors when the query asked for them.</summary>
    /// <remarks>
    ///     On the wire the page is <c>{ "rows", "count", "countCapped", "ancestors" }</c>: <c>count</c>
    ///     is the count held to the cap and <c>countCapped</c> says whether more rows matched, both
    ///     absent when no count was asked for; <c>ancestors</c> is absent when they were not asked
    ///     for. A bare row set travels as a page with neither.
    /// </remarks>
    /// <param name="Rows">The page's rows.</param>
    /// <param name="Count">
    ///     The capped count of every row the query matches, when a cap was given: a value of the cap plus
    ///     one means more rows than the cap. Null when no count was asked for.
    /// </param>
    /// <param name="Ancestors">
    ///     The ancestors of a tree page's rows that are not on the page, in the same columns, when they
    ///     were asked for; null otherwise.
    /// </param>
    /// <param name="CountCap">
    ///     The cap <paramref name="Count" /> was counted to; null when no count was asked for. A count
    ///     is only ever taken under a cap, so a page with a count and no cap cannot be written.
    /// </param>
    [JsonConverter(typeof(RowPageJsonConverter))]
    public sealed record RowPage(QueryRowSet Rows, int? Count, QueryRowSet? Ancestors, int? CountCap);
}
