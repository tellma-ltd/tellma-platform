// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     How one resolved call becomes SQL.
    /// </summary>
    /// <remarks>
    ///     Pure data. Lowering and the emitter read it; nothing here reaches back into them, which
    ///     is what keeps the function library free of any knowledge of how a plan is built.
    /// </remarks>
    internal abstract class EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        private protected EmitStrategy()
        {
        }

        /// <summary>
        ///     How many times each argument appears in the produced SQL. Lowering binds any argument
        ///     used more than once that is not already atomic.
        /// </summary>
        internal abstract ImmutableArray<int> UseCounts { get; }

        /// <summary>How each argument is consumed.</summary>
        internal abstract ImmutableArray<EmitShape> ArgumentShapes { get; }

        /// <summary>Whether the call itself produces a truth value or a scalar.</summary>
        internal abstract EmitShape ResultShape { get; }

        /// <summary>How many times a given argument appears, or zero past the declared arity.</summary>
        /// <param name="index">The argument position.</param>
        /// <returns>The count.</returns>
        internal int UseCountOf(int index)
        {
            return index < UseCounts.Length ? UseCounts[index] : 0;
        }

        /// <summary>The pattern this strategy produces, when it produces one.</summary>
        /// <param name="selectors">The resolved value of each consumed selector, by parameter.</param>
        /// <returns>The pattern, or null when the SQL is not a fixed pattern at all.</returns>
        /// <remarks>
        ///     Asked of the strategy rather than decided by whoever holds it, so that adding a
        ///     strategy whose SQL is chosen some new way needs no change anywhere else.
        /// </remarks>
        internal virtual EmitTemplate? TemplateFor(ImmutableArray<string?> selectors)
        {
            return null;
        }

        /// <summary>How a given argument is consumed, defaulting to scalar.</summary>
        /// <param name="index">The argument position.</param>
        /// <returns>The shape.</returns>
        internal EmitShape ShapeOf(int index)
        {
            return index < ArgumentShapes.Length ? ArgumentShapes[index] : EmitShape.Value;
        }
    }

    /// <summary>One fixed template. Covers most of the library.</summary>
    internal sealed class TemplateStrategy : EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        /// <param name="template">The template.</param>
        /// <param name="resultShape">Whether the call produces a truth value or a scalar.</param>
        internal TemplateStrategy(EmitTemplate template, EmitShape resultShape)
        {
            Template = template;
            ResultShape = resultShape;
        }

        /// <summary>The template.</summary>
        internal EmitTemplate Template { get; }

        /// <inheritdoc />
        internal override EmitTemplate? TemplateFor(ImmutableArray<string?> selectors)
        {
            return Template;
        }

        /// <inheritdoc />
        internal override ImmutableArray<int> UseCounts => Template.UseCounts;

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes => Template.ArgumentShapes;

        /// <inheritdoc />
        internal override EmitShape ResultShape { get; }
    }

    /// <summary>
    ///     One of several templates, chosen by the value of a literal-only selector argument.
    /// </summary>
    /// <remarks>
    ///     How a calendar code picks its arithmetic. The selector's text is consumed here and never
    ///     reaches the backend.
    /// </remarks>
    internal sealed class SelectedStrategy : EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        /// <param name="selectorIndex">Which argument chooses the template.</param>
        /// <param name="cases">The templates, by the selector value that chooses each.</param>
        /// <param name="resultShape">Whether the call produces a truth value or a scalar.</param>
        internal SelectedStrategy(
            int selectorIndex,
            FrozenDictionary<string, EmitTemplate> cases,
            EmitShape resultShape)
        {
            SelectorIndex = selectorIndex;
            Cases = cases;
            ResultShape = resultShape;

            // Every case has to agree on how each argument is used, or lowering could not decide
            // what to bind until after the selector was read.
            EmitTemplate first = cases.Values[0];
            UseCounts = first.UseCounts;
            ArgumentShapes = first.ArgumentShapes;
            foreach (EmitTemplate other in cases.Values)
            {
                if (!other.UseCounts.SequenceEqual(first.UseCounts))
                {
                    throw new ArgumentException(
                        "Every case of a selected emit strategy must use its arguments the same number of times.",
                        nameof(cases));
                }

                if (!other.ArgumentShapes.SequenceEqual(first.ArgumentShapes))
                {
                    throw new ArgumentException(
                        "Every case of a selected emit strategy must consume its arguments the same way.",
                        nameof(cases));
                }
            }
        }

        /// <summary>Which argument chooses the template.</summary>
        internal int SelectorIndex { get; }

        /// <summary>The templates, by the selector value that chooses each.</summary>
        internal FrozenDictionary<string, EmitTemplate> Cases { get; }

        /// <inheritdoc />
        internal override EmitTemplate? TemplateFor(ImmutableArray<string?> selectors)
        {
            string? selector = SelectorIndex < selectors.Length ? selectors[SelectorIndex] : null;
            return selector is not null ? Cases.GetValueOrDefault(selector) : null;
        }

        /// <inheritdoc />
        internal override ImmutableArray<int> UseCounts { get; }

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes { get; }

        /// <inheritdoc />
        internal override EmitShape ResultShape { get; }
    }

    /// <summary>
    ///     A conversion, whose SQL depends on both the source and the target type.
    /// </summary>
    internal sealed class CastStrategy : EmitStrategy
    {
        /// <summary>The one shared instance.</summary>
        internal static CastStrategy Instance { get; } = new CastStrategy();

        /// <inheritdoc />
        internal override ImmutableArray<int> UseCounts { get; } = [1, 0];

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes { get; } = [EmitShape.Value, EmitShape.Value];

        /// <inheritdoc />
        internal override EmitShape ResultShape => EmitShape.Value;
    }

    /// <summary>
    ///     A value the host supplies at execution: one parameter slot, shared by every use of it in
    ///     a statement so that they all see the same value.
    /// </summary>
    internal sealed class ContextStrategy : EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        /// <param name="origin">Where the value comes from.</param>
        internal ContextStrategy(QueryexParameterOrigin origin)
        {
            Origin = origin;
        }

        /// <summary>Where the value comes from.</summary>
        internal QueryexParameterOrigin Origin { get; }

        /// <inheritdoc />
        internal override ImmutableArray<int> UseCounts { get; } = [];

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes { get; } = [];

        /// <inheritdoc />
        internal override EmitShape ResultShape => EmitShape.Value;
    }

    /// <summary>Which way a hierarchy predicate looks.</summary>
    internal enum HierarchyDirection
    {
        /// <summary>True when the row is at or below one of the keyed rows.</summary>
        Descendant,

        /// <summary>True when the row is at or above one of the keyed rows.</summary>
        Ancestor,
    }

    /// <summary>
    ///     A hierarchy predicate, which hoists one node lookup per key and disjoins the guarded
    ///     per-key tests.
    /// </summary>
    internal sealed class HierarchyStrategy : EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        /// <param name="direction">Which way the predicate looks.</param>
        private HierarchyStrategy(HierarchyDirection direction)
        {
            Direction = direction;
        }

        /// <summary>The at-or-below predicate.</summary>
        internal static HierarchyStrategy Descendant { get; } = new HierarchyStrategy(HierarchyDirection.Descendant);

        /// <summary>The at-or-above predicate.</summary>
        internal static HierarchyStrategy Ancestor { get; } = new HierarchyStrategy(HierarchyDirection.Ancestor);

        /// <summary>Which way the predicate looks.</summary>
        internal HierarchyDirection Direction { get; }

        /// <inheritdoc />
        /// <remarks>
        ///     The key path is consumed rather than emitted: it names the table to look a node up in
        ///     and the column to match on. Each key becomes a hoisted variable, which is atomic, so
        ///     the guard that repeats it needs no binding.
        /// </remarks>
        internal override ImmutableArray<int> UseCounts { get; } = [0, 1];

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes { get; } = [EmitShape.Value, EmitShape.Value];

        /// <inheritdoc />
        internal override EmitShape ResultShape => EmitShape.Predicate;
    }

    /// <summary>Which aggregate a call computes.</summary>
    internal enum AggregateKind
    {
        /// <summary>Counts rows.</summary>
        Count,

        /// <summary>Adds values.</summary>
        Sum,

        /// <summary>Averages values.</summary>
        Average,

        /// <summary>The least value.</summary>
        Minimum,

        /// <summary>The greatest value.</summary>
        Maximum,
    }

    /// <summary>
    ///     An aggregation.
    /// </summary>
    /// <remarks>
    ///     Not a fixed template, because the SQL depends on how the argument is stored rather than
    ///     on its type in the language: a sum over an integer column has to widen or it overflows, an
    ///     average over one has to widen or it truncates, and the backend refuses a least-or-greatest
    ///     over its own boolean type outright. Those are emission facts, so the emitter decides them.
    /// </remarks>
    internal sealed class AggregateStrategy : EmitStrategy
    {
        /// <summary>Initializes the strategy.</summary>
        /// <param name="kind">Which aggregate is computed.</param>
        /// <param name="hasCondition">Whether a per-row condition was supplied.</param>
        /// <param name="hasArgument">Whether a value argument was supplied.</param>
        private AggregateStrategy(AggregateKind kind, bool hasCondition, bool hasArgument)
        {
            Kind = kind;
            HasCondition = hasCondition;
            HasArgument = hasArgument;

            UseCounts = (hasArgument, hasCondition) switch
            {
                (false, false) => [],
                (true, false) => [1],
                _ => [1, 1],
            };

            ArgumentShapes = (hasArgument, hasCondition) switch
            {
                (false, false) => [],
                (true, false) => [EmitShape.Value],
                _ => [EmitShape.Value, EmitShape.Predicate],
            };
        }

        /// <summary>Which aggregate is computed.</summary>
        internal AggregateKind Kind { get; }

        /// <summary>Whether a per-row condition was supplied.</summary>
        internal bool HasCondition { get; }

        /// <summary>Whether a value argument was supplied.</summary>
        internal bool HasArgument { get; }

        /// <inheritdoc />
        internal override ImmutableArray<int> UseCounts { get; }

        /// <inheritdoc />
        internal override ImmutableArray<EmitShape> ArgumentShapes { get; }

        /// <inheritdoc />
        internal override EmitShape ResultShape => EmitShape.Value;

        /// <summary>An aggregation of the given shape.</summary>
        /// <param name="kind">Which aggregate is computed.</param>
        /// <param name="hasArgument">Whether a value argument was supplied.</param>
        /// <param name="hasCondition">Whether a per-row condition was supplied.</param>
        /// <returns>The strategy.</returns>
        internal static AggregateStrategy Of(AggregateKind kind, bool hasArgument, bool hasCondition)
        {
            return new AggregateStrategy(kind, hasCondition, hasArgument);
        }
    }
}
