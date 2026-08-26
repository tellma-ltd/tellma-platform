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
        public required string Sql { get; init; }

        /// <summary>The parameter slots, in the order they were assigned.</summary>
        public required IReadOnlyList<QueryexParameterSlot> Parameters { get; init; }

        /// <summary>The result-set columns, positional to the select list.</summary>
        public required IReadOnlyList<QueryexColumn> Columns { get; init; }
    }
}
