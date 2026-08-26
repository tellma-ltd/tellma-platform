// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>What the result of an expression must be.</summary>
    public enum QueryexShape
    {
        /// <summary>
        ///     A truth value: the expression must have type <see cref="QueryexType.QxBool" /> and is
        ///     emitted in predicate position.
        /// </summary>
        Predicate,

        /// <summary>A scalar of any type, emitted in value position.</summary>
        Value,
    }

    /// <summary>Whether an expression sees rows or groups.</summary>
    public enum QueryexGrouping
    {
        /// <summary>Row by row. Aggregations are forbidden.</summary>
        Row,

        /// <summary>
        ///     Over groups. Aggregations are permitted, and aggregation-free items that read at
        ///     least one path become grouping keys.
        /// </summary>
        Group,
    }

    /// <summary>
    ///     The position an expression is compiled for: a pair of independent axes.
    /// </summary>
    /// <remarks>
    ///     Every rule the compiler applies follows from one axis alone; none depends on the
    ///     combination. The named accessors exist for call-site readability and add no information.
    /// </remarks>
    /// <param name="Shape">What the result must be.</param>
    /// <param name="Grouping">Whether the expression sees rows or groups.</param>
    public readonly record struct QueryexMode(QueryexShape Shape, QueryexGrouping Grouping)
    {
        /// <summary>Row-level predicate, as in a WHERE clause.</summary>
        public static QueryexMode Filter => new(QueryexShape.Predicate, QueryexGrouping.Row);

        /// <summary>Row-level value, as in a select or ordering item.</summary>
        public static QueryexMode Value => new(QueryexShape.Value, QueryexGrouping.Row);

        /// <summary>Group-level value, as in a select or ordering item of a grouped query.</summary>
        public static QueryexMode Aggregate => new(QueryexShape.Value, QueryexGrouping.Group);

        /// <summary>Group-level predicate, as in a HAVING clause.</summary>
        public static QueryexMode AggregateFilter => new(QueryexShape.Predicate, QueryexGrouping.Group);
    }
}
