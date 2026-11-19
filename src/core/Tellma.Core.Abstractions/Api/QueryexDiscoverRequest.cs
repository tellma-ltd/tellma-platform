// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of <c>queryex/discover</c>: the clauses of a query, or one permission filter, being
    ///     written in an editor, checked as the query or the role save would compile them, with no
    ///     SQL run.
    /// </summary>
    /// <param name="Root">The entity the clauses are read from, such as <c>gl.Center</c>.</param>
    /// <param name="Select">The Queryex select; null for none.</param>
    /// <param name="Filters">Queryex predicates, each its own leaf, conjoined as a query conjoins them; null or empty for none.</param>
    /// <param name="Having">The group-level predicate of an aggregate query; null for none.</param>
    /// <param name="OrderBy">The Queryex ordering; null for none.</param>
    /// <param name="Measures">The measures of a pivot or a summary, which need an aggregate; not supported yet, so any is refused.</param>
    /// <param name="Aggregate">Whether the select is an aggregation.</param>
    /// <param name="Parameters">The parameter types the author confirmed; every other parameter's type is inferred.</param>
    /// <param name="Profile">Whether the text is checked as a query or as a permission filter.</param>
    /// <param name="Conjuncts">Whether the answer also carries each filter's top-level comparisons of a path or an expression with a literal.</param>
    public sealed record QueryexDiscoverRequest(
        string Root,
        string? Select = null,
        IReadOnlyList<string>? Filters = null,
        string? Having = null,
        string? OrderBy = null,
        string? Measures = null,
        bool Aggregate = false,
        IReadOnlyList<QueryexParameterInput>? Parameters = null,
        QueryexProfile Profile = QueryexProfile.Query,
        bool Conjuncts = false);
}
