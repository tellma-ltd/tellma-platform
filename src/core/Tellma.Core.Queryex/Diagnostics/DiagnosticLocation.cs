// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Queryex.Diagnostics
{
    /// <summary>A clause of a query, in the order the compiler binds them.</summary>
    internal enum QueryexClause
    {
        /// <summary>The row-level predicate.</summary>
        Filter = 0,

        /// <summary>The select list. Binds after the filter so that grouping keys are fixed early.</summary>
        Select = 1,

        /// <summary>The group-level predicate.</summary>
        Having = 2,

        /// <summary>The ordering list.</summary>
        OrderBy = 3,
    }

    /// <summary>A logical connective inside a filter tree.</summary>
    internal enum FilterConnective
    {
        /// <summary>A conjunction.</summary>
        And,

        /// <summary>A disjunction.</summary>
        Or,

        /// <summary>A negation, which has exactly one child and therefore carries no index.</summary>
        Not,
    }

    /// <summary>
    ///     Which of a compilation's input texts a span indexes into.
    /// </summary>
    /// <remarks>
    ///     Built structurally rather than by concatenating strings at each call site, so the grammar
    ///     of a location — which hosts will parse — lives in exactly one place.
    /// </remarks>
    internal readonly record struct DiagnosticLocation
    {
        /// <summary>Initializes a location.</summary>
        /// <param name="text">The rendered path, or null when there is only one input.</param>
        /// <param name="order">The clause's binding order, for deterministic diagnostic ordering.</param>
        private DiagnosticLocation(string? text, int order)
        {
            Text = text;
            Order = order;
        }

        /// <summary>No location: a single expression list, validated on its own.</summary>
        internal static DiagnosticLocation None { get; } = new DiagnosticLocation(null, int.MaxValue);

        /// <summary>The rendered path, or null for <see cref="None" />.</summary>
        internal string? Text { get; }

        /// <summary>The clause's binding order. Diagnostics are grouped by it.</summary>
        internal int Order { get; }

        /// <summary>A whole clause.</summary>
        /// <param name="clause">The clause.</param>
        /// <returns>The location.</returns>
        internal static DiagnosticLocation Clause(QueryexClause clause)
        {
            return new DiagnosticLocation(clause.ToString(), (int)clause);
        }

        /// <summary>One item of a value list within this clause.</summary>
        /// <param name="index">The zero-based item index.</param>
        /// <returns>The location.</returns>
        internal DiagnosticLocation Item(int index)
        {
            return new DiagnosticLocation(
                string.Create(CultureInfo.InvariantCulture, $"{Text}[{index}]"),
                Order);
        }

        /// <summary>One child of a connective within this clause's filter tree.</summary>
        /// <param name="connective">The connective.</param>
        /// <param name="childIndex">The zero-based child index. Ignored for a negation.</param>
        /// <returns>The location.</returns>
        internal DiagnosticLocation Child(FilterConnective connective, int childIndex)
        {
            // A negation has exactly one child, so an index there would be noise a host has to
            // parse and then ignore.
            string suffix = connective == FilterConnective.Not
                ? ".Not"
                : string.Create(CultureInfo.InvariantCulture, $".{connective}[{childIndex}]");

            return new DiagnosticLocation(Text + suffix, Order);
        }
    }
}
