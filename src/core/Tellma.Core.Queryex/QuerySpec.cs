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
        ///     <see cref="Aggregate" /> is set.
        /// </summary>
        public FilterTree? Having { get; init; }

        /// <summary>The ordering list, when present.</summary>
        public string? OrderBy { get; init; }
    }
}
