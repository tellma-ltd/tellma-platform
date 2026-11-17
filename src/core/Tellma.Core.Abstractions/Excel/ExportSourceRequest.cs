// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     Which rows an export carries: the rows named by <see cref="Ids" />, or the rows the clauses
    ///     select exactly as a grid's <c>query</c> selects them. Never both.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Non-null <see cref="Ids" /> selects by id, with <see cref="OrderBy" /> beside it; the
    ///         ids name the rows, so no activatable conjunct applies. Otherwise the clauses select,
    ///         each optional: with none, the caller's read filter and the activatable conjunct alone
    ///         apply, in id order. <see cref="Ids" /> together with a filter, a named filter, a search,
    ///         arguments or <see cref="IncludeInactive" /> is a bad request.
    ///     </para>
    ///     <para>
    ///         The clauses mean what they mean in a grid's query: each filter its own leaf,
    ///         conjoined; the named filters by name; the search through the stack's search filter;
    ///         the caller's read filter conjoined.
    ///     </para>
    /// </remarks>
    public class ExportSourceRequest
    {
        /// <summary>The rows by id, or null to select by the clauses; at most the stack's id limit.</summary>
        public IReadOnlyList<long>? Ids { get; set; }

        /// <summary>Query predicates, each compiled as its own leaf, conjoined; null or empty for none.</summary>
        public IReadOnlyList<string>? Filters { get; set; }

        /// <summary>The stack's named filters to apply, by name, conjoined; null or empty for none.</summary>
        public IReadOnlyList<string>? NamedFilters { get; set; }

        /// <summary>The ordering, as query ordering text; null orders by id.</summary>
        public string? OrderBy { get; set; }

        /// <summary>The search text, carried unchanged; null for none.</summary>
        public string? Search { get; set; }

        /// <summary>The values of the clauses' parameters, as JSON scalars keyed by parameter name; null for none.</summary>
        public IReadOnlyDictionary<string, JsonElement>? Arguments { get; set; }

        /// <summary>
        ///     On an activatable stack: whether inactive rows are included. False by default, so an
        ///     export by the clauses carries the rows the grid shows.
        /// </summary>
        public bool IncludeInactive { get; set; }
    }
}
