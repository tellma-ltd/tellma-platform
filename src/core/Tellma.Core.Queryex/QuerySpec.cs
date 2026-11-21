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
        /// <remarks>
        ///     It sees groups rather than rows, so neither a <see cref="FilterTree.ViaNode" /> nor a
        ///     <see cref="FilterTree.ExistsNode" /> may appear in it: both read rows, and are a caller
        ///     error here.
        /// </remarks>
        public FilterTree? Having { get; init; }

        /// <summary>
        ///     The ordering list, with direction suffixes enabled. Sees rows or groups to match
        ///     <see cref="Aggregate" />.
        /// </summary>
        public string? OrderBy { get; init; }

        /// <summary>
        ///     Measures a host evaluates itself over rolled-up groups, as a comma-separated list. A
        ///     caller error unless <see cref="Aggregate" /> is set; absent, empty or blank, the
        ///     statement is exactly what it would be without this member.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each item is a measure on the grouped rows, such as <c>sum(Amount) / count()</c>.
        ///         It is not computed in SQL: its aggregations are, each one a sum, a count, a least or
        ///         a greatest value appended to the select list as a partial column, an average
        ///         contributing a sum and a count. <see cref="CompiledQuery.Measures" /> describes the
        ///         partials and evaluates each measure over them in the backend's own arithmetic, so a
        ///         host can combine fine groups into coarser ones and evaluate a subtotal as the
        ///         measure of the combined rows rather than as a combination of the measures.
        ///     </para>
        ///     <para>
        ///         Outside its aggregations an item may use only what that evaluation computes
        ///         exactly: literals, parameters, the context values, arithmetic, comparisons over
        ///         numbers, truth values and dates, the logical connectives, <c>if</c>,
        ///         <c>coalesce</c>, <c>abs</c>, <c>round</c>, <c>floor</c>, <c>ceiling</c>, identity
        ///         conversions, and the calendar parts and differences of dates. Measures never
        ///         become grouping keys, and no other clause sees them.
        ///     </para>
        /// </remarks>
        public string? Measures { get; init; }

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

        /// <summary>
        ///     The key sets the root's rows are restricted to, each conjoined after
        ///     <see cref="Filter" />.
        /// </summary>
        /// <remarks>
        ///     Host structure rather than user text, so a restriction that does not fit the schema is
        ///     a caller error rather than a diagnostic: a path that does not resolve, that ends at a
        ///     navigation, or that ends at a property whose values cannot be compared for equality.
        ///     Empty, the statement is exactly what it would be without this member.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The list, or an item of it, is null.</exception>
        public IReadOnlyList<KeySetRestriction> Restrictions
        {
            get;
            init
            {
                ArgumentNullException.ThrowIfNull(value);

                // Copied rather than captured: the list is part of what a cached compilation is
                // keyed on, and a caller that kept a mutable one could change it afterwards.
                KeySetRestriction[] copy = [.. value];
                foreach (KeySetRestriction restriction in copy)
                {
                    ArgumentNullException.ThrowIfNull(restriction, nameof(value));
                }

                field = copy;
            }
        } = [];

        /// <summary>
        ///     Physical columns of the root's source that the schema leaves out on purpose, read as
        ///     text after the select list.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         For the columns a schema omits by design, such as opaque JSON text, which a host
        ///         still wants in the statement that reads the rest of the row. Each name is an
        ///         unquoted column of the root's source, written as a letter or underscore and then
        ///         letters, digits or underscores; it is read on the root row and reported as a
        ///         nullable text column after the compiled ones, in list order.
        ///     </para>
        ///     <para>
        ///         The engine cannot look a name up — the schema omits it — so that the column exists
        ///         and holds text is the host's obligation. Like the schema's own sources and
        ///         columns, the names are host-authored text that reaches SQL, and must never be
        ///         derived from anything a user supplied. No clause can mention one, so the joins,
        ///         parameters and ordering are those of the same query without the list.
        ///     </para>
        ///     <para>
        ///         Row grain only: a caller error in a grouped query, as is a name not of the
        ///         accepted form or one listed twice ignoring case.
        ///     </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">The list, or an item of it, is null.</exception>
        public IReadOnlyList<string> UndeclaredColumns
        {
            get;
            init
            {
                ArgumentNullException.ThrowIfNull(value);

                string[] copy = [.. value];
                foreach (string name in copy)
                {
                    ArgumentNullException.ThrowIfNull(name, nameof(value));
                }

                field = copy;
            }
        } = [];
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
        ///     <see cref="Aggregate" /> is set, or when it holds a rebase or a semi-join.
        /// </summary>
        public FilterTree? Having { get; init; }

        /// <summary>The ordering list, when present.</summary>
        public string? OrderBy { get; init; }

        /// <summary>
        ///     The measures, when present: checked as a compilation checks them, each item at a
        ///     location of its own. A caller error unless <see cref="Aggregate" /> is set; empty or
        ///     blank is absent.
        /// </summary>
        public string? Measures { get; init; }

        /// <summary>
        ///     True to have the row-level predicate's top-level restrictions reported, each as a
        ///     compared path or expression, an operator and a literal value.
        /// </summary>
        /// <remarks>
        ///     Off by default, and with it off discovery answers exactly as it would without it. With
        ///     it on, <see cref="DiscoveryResult.Conjuncts" /> holds what was found, which is how a
        ///     list opened from a bare link can describe the filter it runs from that filter's own
        ///     parse rather than from text carried beside it.
        /// </remarks>
        public bool Conjuncts { get; init; }
    }
}
