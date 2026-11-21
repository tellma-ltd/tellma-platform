// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Measures;

namespace Tellma.Core.Queryex
{
    /// <summary>Which aggregation a partial column holds.</summary>
    public enum MeasurePartialKind
    {
        /// <summary>The total of the values a group's rows contribute.</summary>
        Sum,

        /// <summary>How many of a group's rows contribute.</summary>
        Count,

        /// <summary>The least value a group's rows contribute.</summary>
        Min,

        /// <summary>The greatest value a group's rows contribute.</summary>
        Max,
    }

    /// <summary>
    ///     One aggregation a query's measures are computed from, held in one result column.
    /// </summary>
    /// <remarks>
    ///     A partial is what combines across groups without any knowledge of the measure: two groups'
    ///     sums add, their counts add, and the least of their least values is the least of their
    ///     combined rows. An average is never a partial; it is computed from a sum and a count.
    /// </remarks>
    /// <param name="Ordinal">
    ///     The result column that holds it: a select item that is the same aggregation, or a column
    ///     appended after the select items.
    /// </param>
    /// <param name="Kind">Which aggregation it is.</param>
    /// <param name="Type">
    ///     The aggregation's type: <see cref="QueryexType.QxNumeric" /> for a sum and a count, and the
    ///     aggregated value's type for a least or greatest value.
    /// </param>
    public sealed record MeasurePartial(int Ordinal, MeasurePartialKind Kind, QueryexType Type);

    /// <summary>One measure, as a host describes it.</summary>
    /// <param name="Text">The measure's source text, for display and diagnostics.</param>
    /// <param name="Type">The measure's type in the language.</param>
    /// <param name="Nullity">Whether the measure can be absent for a group.</param>
    public sealed record CompiledMeasure(string Text, QueryexType Type, QueryexNullity Nullity);

    /// <summary>
    ///     A query's measures: the partial columns they are computed from, and a way to compute each
    ///     measure over partials that a host has combined itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Immutable and safe to share across threads, which is what lets it be cached with the
    ///         compiled statement and served to every compilation of the same query.
    ///     </para>
    ///     <para>
    ///         A host reads the statement's rows, loads each row's partial columns into a cell of a
    ///         <see cref="PartialTable" />, merges cells into coarser ones, and evaluates any cell.
    ///         Evaluating a measure over a merged cell gives exactly what the backend gives for the
    ///         same measure over the combined rows, digit for digit and at the same scale, because
    ///         the evaluator reproduces the backend's own numeric types and rounding.
    ///     </para>
    /// </remarks>
    public sealed class CompiledMeasures
    {
        /// <summary>Initializes the measures of one compiled query.</summary>
        /// <param name="partials">The partial columns, deduplicated across every measure.</param>
        /// <param name="items">The measures, in the order the query lists them.</param>
        /// <param name="plans">How each partial is stored and typed, parallel to the partials.</param>
        /// <param name="roots">Each measure's evaluation tree, parallel to the items.</param>
        /// <param name="nodeCount">How many nodes the trees hold between them.</param>
        internal CompiledMeasures(
            ImmutableArray<MeasurePartial> partials,
            ImmutableArray<CompiledMeasure> items,
            ImmutableArray<PartialPlan> plans,
            ImmutableArray<MeasureNode> roots,
            int nodeCount)
        {
            Partials = partials;
            Items = items;
            Plans = plans;
            Roots = roots;
            NodeCount = nodeCount;
        }

        /// <summary>The partial columns, deduplicated across every measure, in first-use order.</summary>
        public IReadOnlyList<MeasurePartial> Partials { get; }

        /// <summary>The measures, in the order the query lists them.</summary>
        public IReadOnlyList<CompiledMeasure> Items { get; }

        /// <summary>How each partial is stored and typed, parallel to <see cref="Partials" />.</summary>
        internal ImmutableArray<PartialPlan> Plans { get; }

        /// <summary>Each measure's evaluation tree, parallel to <see cref="Items" />.</summary>
        internal ImmutableArray<MeasureNode> Roots { get; }

        /// <summary>How many nodes the evaluation trees hold between them.</summary>
        internal int NodeCount { get; }

        /// <summary>Fixes every partial's and every node's backend type for one run.</summary>
        /// <param name="context">
        ///     What the run read: the decimal types of its partial columns, and the values the
        ///     statement's parameters and context slots were bound with.
        /// </param>
        /// <returns>The prepared measures, for that run only.</returns>
        /// <remarks>
        ///     Once per run rather than once per compilation, because a decimal partial's precision
        ///     and scale are the backend's to derive from what it aggregates, and the same compiled
        ///     statement can meet two different ones.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="context" /> or one of its members is null.</exception>
        /// <exception cref="ArgumentException">
        ///     The decimal types are not one per partial, a decimal partial has none, another partial
        ///     has one, a type's precision is not between 1 and 38 or its scale exceeds its precision,
        ///     or a parameter a measure reads has no value, or a value not of its declared type.
        /// </exception>
        public MeasureEvaluator Prepare(MeasureContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return MeasureRun.Prepare(this, context);
        }
    }
}
