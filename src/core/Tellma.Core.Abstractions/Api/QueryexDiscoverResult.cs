// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     What <c>queryex/discover</c> found in the text it was sent: every problem, every parameter
    ///     with its type, every path with whether the caller may read through it, and on request the
    ///     filters' top-level comparisons. A problem never fails the call.
    /// </summary>
    /// <param name="Diagnostics">The problems a query, or a role save, would refuse the text for; empty when it would compile.</param>
    /// <param name="Parameters">Each parameter the clauses mention.</param>
    /// <param name="Paths">Each path the clauses read.</param>
    /// <param name="UsesAggregation">Whether any clause aggregates.</param>
    /// <param name="Conjuncts">The filters' top-level comparisons with a literal; null unless asked for.</param>
    public sealed record QueryexDiscoverResult(
        IReadOnlyList<QueryDiagnostic> Diagnostics,
        IReadOnlyList<QueryexParameterInfo> Parameters,
        IReadOnlyList<QueryexPathInfo> Paths,
        bool UsesAggregation,
        IReadOnlyList<QueryexConjunct>? Conjuncts);
}
