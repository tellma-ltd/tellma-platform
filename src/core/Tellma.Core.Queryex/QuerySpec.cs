// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One query over one root entity, clause by clause.
    /// </summary>
    /// <remarks>
    ///     Two shapes fall out of this without needing a keyword of their own. A grand total is an
    ///     aggregate query whose select list derives no grouping keys, such as
    ///     <c>count()</c>. Distinct combinations are an aggregate query whose select list contains
    ///     no aggregation at all: its items become the grouping keys by construction.
    /// </remarks>
    public sealed record QuerySpec
    {
        /// <summary>The root entity. Must belong to the compilation's schema.</summary>
        public required EntityDescriptor Root { get; init; }

        /// <summary>
        ///     The select list: comma-separated value expressions. Binds row by row, or over groups
        ///     when <see cref="Aggregate" /> is set.
        /// </summary>
        public required string Select { get; init; }

        /// <summary>
        ///     True for a grouped query: the select and ordering lists see groups, aggregation-free
        ///     select items that read at least one path become the grouping keys, and
        ///     <see cref="Having" /> is permitted.
        /// </summary>
        /// <remarks>
        ///     There is no grouping clause in the surface language. The keys are derived from the
        ///     select list, which is what makes it impossible for a select list and its grouping to
        ///     disagree.
        /// </remarks>
        public bool Aggregate { get; init; }

        /// <summary>
        ///     The row-level predicate. Row-level always: in an aggregate query it filters the rows
        ///     that enter the groups, which is why access-control criteria belong here and never in
        ///     <see cref="Having" />.
        /// </summary>
        public FilterTree? Filter { get; init; }

        /// <summary>
        ///     The group-level predicate. A caller error unless <see cref="Aggregate" /> is set.
        /// </summary>
        public FilterTree? Having { get; init; }

        /// <summary>
        ///     The ordering list, with direction suffixes enabled. Sees rows or groups to match
        ///     <see cref="Aggregate" />.
        /// </summary>
        public string? OrderBy { get; init; }

        /// <summary>Rows to skip. Paging requires an explicit ordering.</summary>
        /// <remarks>
        ///     Caller-supplied rather than expression text, because it is never authored by a user
        ///     writing a query. The value still reaches SQL as a parameter, so paging does not
        ///     fragment the backend's plan cache. A negative value is a caller error.
        /// </remarks>
        public int? Skip { get; init; }

        /// <summary>Maximum rows to return. Paging requires an explicit ordering.</summary>
        /// <remarks>Zero is a caller error: the backend's paging clause rejects it.</remarks>
        public int? Take { get; init; }
    }

    /// <summary>
    ///     The clauses of a query under authoring: a lax mirror of <see cref="QuerySpec" /> for
    ///     discovery.
    /// </summary>
    /// <remarks>
    ///     Every clause is optional, so a half-written definition still discovers. There is no root
    ///     entity here either — discovery reports what an expression refers to, and it does that
    ///     whether or not a schema was supplied.
    /// </remarks>
    public sealed record QueryDiscoverySpec
    {
        /// <summary>The select list, when present.</summary>
        public string? Select { get; init; }

        /// <summary>True when the query under authoring is aggregate, which fixes each clause's mode.</summary>
        public bool Aggregate { get; init; }

        /// <summary>The row-level predicate, when present.</summary>
        public FilterTree? Filter { get; init; }

        /// <summary>
        ///     The group-level predicate, when present. A caller error unless
        ///     <see cref="Aggregate" /> is set.
        /// </summary>
        public FilterTree? Having { get; init; }

        /// <summary>The ordering list, when present.</summary>
        public string? OrderBy { get; init; }
    }
}
