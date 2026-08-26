// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>What sits directly under one plan node.</summary>
    /// <remarks>
    ///     Written once and shared, because two different answers to "what does this node contain"
    ///     would mean the pruning and the checks disagree about what a statement actually reads.
    /// </remarks>
    internal static class PlanWalk
    {
        /// <summary>The nodes one node contains.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its children, in the order they are written.</returns>
        internal static ImmutableArray<PlanNode> Children(PlanNode node)
        {
            return node switch
            {
                PlanTemplateValue template => template.Operands,
                PlanTemplatePredicate template => template.Operands,
                PlanArithmetic arithmetic => [arithmetic.Left, arithmetic.Right],
                PlanNegate negate => [negate.Operand],
                PlanAggregate aggregate => Present(aggregate.Argument, aggregate.Condition),
                PlanValueOfPredicate truth => [truth.Inner],
                PlanPredicateOfValue truth => [truth.Inner],
                PlanJunction junction => [.. junction.Operands.Cast<PlanNode>()],
                PlanNegation negation => [negation.Operand],
                PlanCompare compare => [compare.Left, compare.Right],
                PlanIsNull test => [test.Value],
                PlanIn membership => [membership.Value, .. membership.Elements.Cast<PlanNode>()],
                PlanSetComparison set => [set.Left, set.Right],
                PlanSetMembership set => [set.Value, .. set.Elements.Cast<PlanNode>()],
                PlanHierarchyTest hierarchy => [hierarchy.Node, hierarchy.Key],
                _ => [],
            };
        }

        /// <summary>Whether anything under a node reads a column.</summary>
        /// <param name="node">The node.</param>
        /// <returns>True when something does.</returns>
        /// <remarks>
        ///     Looks through a binding to what the binding computes, because a value that merely
        ///     names a binding still reads whatever that binding reads.
        /// </remarks>
        internal static bool ReadsAColumn(PlanNode node)
        {
            return node switch
            {
                PlanColumn => true,
                PlanBindingRef binding => ReadsAColumn(binding.Binding.Value),
                _ => Children(node).Any(ReadsAColumn),
            };
        }

        /// <summary>Whether anything under a node aggregates.</summary>
        /// <param name="node">The node.</param>
        /// <returns>True when something does.</returns>
        internal static bool Aggregates(PlanNode node)
        {
            return node switch
            {
                PlanAggregate => true,
                PlanBindingRef binding => Aggregates(binding.Binding.Value),
                _ => Children(node).Any(Aggregates),
            };
        }

        /// <summary>The children an aggregation has, skipping the ones it does not.</summary>
        /// <param name="argument">The value aggregated, when there is one.</param>
        /// <param name="condition">The per-row condition, when there is one.</param>
        /// <returns>The children.</returns>
        private static ImmutableArray<PlanNode> Present(PlanValue? argument, PlanPredicate? condition)
        {
            ImmutableArray<PlanNode>.Builder children = ImmutableArray.CreateBuilder<PlanNode>(2);
            if (argument is not null)
            {
                children.Add(argument);
            }

            if (condition is not null)
            {
                children.Add(condition);
            }

            return children.ToImmutable();
        }
    }
}
