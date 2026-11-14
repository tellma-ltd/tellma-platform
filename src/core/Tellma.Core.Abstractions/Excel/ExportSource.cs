// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     Which rows of a stack an export or a lookup reads: the rows named by id, the rows a query's
    ///     clauses select, or every row the caller may read. One case per request; the row source
    ///     composes the caller's read filter with each.
    /// </summary>
    /// <remarks>
    ///     The set is closed by construction: a record's copy constructor is always reachable by a
    ///     derived type, but <see cref="Case" /> is an internal abstract member, which a type outside
    ///     this assembly cannot implement. A row source can therefore switch on the three cases.
    /// </remarks>
    public abstract record ExportSource
    {
        /// <summary>Initialises a case.</summary>
        private protected ExportSource()
        {
        }

        /// <summary>The case's name in diagnostics: <c>ids</c>, <c>query</c> or <c>all</c>.</summary>
        internal abstract string Case { get; }

        /// <summary>The rows named by id, inactive rows included.</summary>
        /// <param name="Ids">The rows' ids, as the wire carries them; narrowed to the stack's key type by the row source.</param>
        /// <param name="OrderBy">The ordering, as query ordering text; null orders by id.</param>
        /// <exception cref="ArgumentNullException"><paramref name="Ids" /> is null.</exception>
        public sealed record ByIds(IReadOnlyList<long> Ids, string? OrderBy) : ExportSource
        {
            /// <inheritdoc cref="ByIds(IReadOnlyList{long}, string?)" />
            public IReadOnlyList<long> Ids { get; init => field = value ?? throw new ArgumentNullException(nameof(Ids)); } =
                Ids ?? throw new ArgumentNullException(nameof(Ids));

            /// <inheritdoc />
            internal override string Case => "ids";
        }

        /// <summary>
        ///     The rows a query's clauses select, as a grid's query selects them: each filter its own
        ///     conjoined leaf, the named filters, the search, and the activatable conjunct unless
        ///     <paramref name="IncludeInactive" />.
        /// </summary>
        /// <param name="Filters">The filters, each its own leaf, conjoined; null for none.</param>
        /// <param name="NamedFilters">The stack's named filters by name, conjoined; null for none.</param>
        /// <param name="Search">The search term; null for none.</param>
        /// <param name="OrderBy">The ordering, as query ordering text; null for the stack's default.</param>
        /// <param name="Arguments">The values of the parameters the clauses mention, by name; null for none.</param>
        /// <param name="IncludeInactive">Whether inactive rows of an activatable stack are included.</param>
        public sealed record ByQuery(
            IReadOnlyList<string>? Filters,
            IReadOnlyList<string>? NamedFilters,
            string? Search,
            string? OrderBy,
            IReadOnlyDictionary<string, JsonElement>? Arguments,
            bool IncludeInactive)
            : ExportSource
        {
            /// <inheritdoc />
            internal override string Case => "query";
        }

        /// <summary>Every row the caller may read: no clause, no ordering and no activatable conjunct, so the read filter alone applies.</summary>
        public sealed record All : ExportSource
        {
            /// <inheritdoc />
            internal override string Case => "all";
        }
    }
}
