// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of a stack's or a child query surface's <c>query</c>: Queryex clauses, paging and
    ///     the options of the one round trip that answers it with a page of columnar rows.
    /// </summary>
    /// <remarks>
    ///     Row-level security is conjoined on the server; the filter tree never travels. Paging
    ///     ceilings are refused, never clamped: a page size above the stack's maximum, and a skip plus
    ///     page size above its skip window, are a limit-exceeded error before any round trip.
    /// </remarks>
    public sealed class QueryRequest
    {
        /// <summary>The Queryex select; null for the stack's default select.</summary>
        public string? Select { get; set; }

        /// <summary>
        ///     Queryex predicates, each compiled as its own leaf so a diagnostic points at the one that
        ///     failed, conjoined; null or empty for none.
        /// </summary>
        public IReadOnlyList<string>? Filters { get; set; }

        /// <summary>
        ///     The stack's named filters to apply, by name, conjoined; an unknown name is a
        ///     query-invalid error. Null or empty for none.
        /// </summary>
        public IReadOnlyList<string>? NamedFilters { get; set; }

        /// <summary>The Queryex having clause; with <see cref="Aggregate" /> only.</summary>
        public string? Having { get; set; }

        /// <summary>The Queryex ordering; null for the stack's default ordering, else <c>Id desc</c>.</summary>
        public string? OrderBy { get; set; }

        /// <summary>The rows to skip; 0 by default.</summary>
        public int Skip { get; set; }

        /// <summary>The page size; null for 50.</summary>
        public int? Take { get; set; }

        /// <summary>The search text, carried unchanged; at most the stack's search length.</summary>
        public string? Search { get; set; }

        /// <summary>
        ///     The values of the clauses' parameters, as JSON scalars keyed by parameter name. Their
        ///     types are inferred from the clauses; a value that does not convert is a bad-request
        ///     error naming the parameter, a parameter without a value binds null, and a value no
        ///     clause mentions is ignored.
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement>? Arguments { get; set; }

        /// <summary>Whether the response carries the capped grand-total count, computed in the same round trip.</summary>
        public bool IncludeCount { get; set; }

        /// <summary>On a tree stack: whether the response carries the ancestors of the page's matches that are not in the page.</summary>
        public bool IncludeAncestors { get; set; }

        /// <summary>On an activatable stack: whether inactive rows are included, lifting the active-only conjunct.</summary>
        public bool IncludeInactive { get; set; }

        /// <summary>Whether the select is an aggregation, grouped by its non-aggregated items.</summary>
        public bool Aggregate { get; set; }
    }
}
