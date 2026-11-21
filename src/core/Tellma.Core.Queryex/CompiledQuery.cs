// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One result-set column.
    /// </summary>
    /// <param name="Ordinal">The zero-based column position.</param>
    /// <param name="Text">The select item's source text, for display and diagnostics.</param>
    /// <param name="Type">
    ///     The column's type in the language. A single language type can arrive as any of several
    ///     backend types — a count is a wide integer where a year is a narrow one, and a truth value
    ///     is a bit or an integer depending on the shape of the expression — so a host converts from
    ///     whatever it reads rather than assuming one storage type per language type. Forcing them
    ///     uniform would mean rounding or overflowing perfectly good values.
    /// </param>
    /// <param name="Nullity">Whether the column can carry absent values.</param>
    /// <param name="Path">
    ///     The resolved logical path segments when the item is a bare path — what an entity
    ///     materializer keys on. Null for a computed expression.
    /// </param>
    /// <param name="IsGroupingKey">
    ///     True when the query is aggregate and this column is one of its grouping keys.
    /// </param>
    public sealed record QueryexColumn(
        int Ordinal,
        string Text,
        QueryexType Type,
        QueryexNullity Nullity,
        IReadOnlyList<string>? Path,
        bool IsGroupingKey);

    /// <summary>
    ///     A compiled query: SQL text plus everything a host needs to execute it and to interpret
    ///     its result set.
    /// </summary>
    public sealed record CompiledQuery
    {
        /// <summary>
        ///     The batch to execute as command text, with the parameters of
        ///     <see cref="Parameters" /> bound. Never string-interpolated further.
        /// </summary>
        /// <remarks>
        ///     Always <see cref="Prologue" />, then <see cref="Body" />, then the clause
        ///     <c>OPTION (RECOMPILE)</c> and a line feed when <see cref="Recompile" /> is true, then a
        ///     semicolon and a line feed.
        /// </remarks>
        public required string Sql { get; init; }

        /// <summary>
        ///     The declarations the statement depends on, each on its own line, followed by one
        ///     empty line; empty when the statement needs none.
        /// </summary>
        /// <remarks>
        ///     What a host composing the statement into its own SQL writes first. The variables
        ///     declared here are read by <see cref="Body" />, so they belong to the same batch and
        ///     precede it; a host composing several compiled statements writes each one's prologue
        ///     before its body.
        /// </remarks>
        public string Prologue { get; init; } = string.Empty;

        /// <summary>
        ///     The statement itself: the select through its last clause and that clause's line
        ///     feed, with no terminating semicolon.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         What a host embeds in its own SQL — as the source of an insert, or as a derived
        ///         table it counts or filters further. The values the select reads are named by
        ///         <see cref="Parameters" /> exactly as for <see cref="Sql" />.
        ///     </para>
        ///     <para>
        ///         A body is a valid derived table or insert source when it has no ordering, or when
        ///         it pages: the backend refuses an ordering inside a derived table that neither
        ///         limits nor offsets. A host that needs a bare count of an ordered query compiles an
        ///         unordered twin of it.
        ///     </para>
        /// </remarks>
        public string Body { get; init; } = string.Empty;

        /// <summary>
        ///     Whether the statement should be planned afresh at each execution.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         True when a filter reads a value that <see cref="Prologue" /> computes, such as a
        ///         date in a calendar the host's month table defines. Planned once and reused, the
        ///         statement would be estimated without knowing the value, and a range over a large
        ///         table may then be read in full.
        ///     </para>
        ///     <para>
        ///         <see cref="Sql" /> already carries the hint. A host that embeds
        ///         <see cref="Body" /> in its own statement adds <c>RECOMPILE</c> to the one
        ///         <c>OPTION</c> clause that statement may carry, beside any hint of its own: the
        ///         backend takes no hint inside a derived table or after a condition, and refuses a
        ///         second <c>OPTION</c> clause.
        ///     </para>
        /// </remarks>
        public bool Recompile { get; init; }

        /// <summary>
        ///     The parameter slots, in the order they were assigned, with the paging slots last.
        /// </summary>
        public required IReadOnlyList<QueryexParameterSlot> Parameters { get; init; }

        /// <summary>
        ///     The result-set columns, positional to the select list, followed by the undeclared
        ///     columns the query asked for.
        /// </summary>
        public required IReadOnlyList<QueryexColumn> Columns { get; init; }
    }
}
