// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lowering;
using Tellma.Core.Queryex.Nullity;
using Tellma.Core.Queryex.Pipeline;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>
    ///     Looks at the lowered form of a query, which is otherwise not visible from outside.
    /// </summary>
    /// <remarks>
    ///     Reports findings rather than the plan itself. The plan is an internal shape that will
    ///     change; what a test has any business asserting is a property of it, and that is what
    ///     comes out here.
    /// </remarks>
    public static class ProbePlan
    {
        /// <summary>
        ///     Every value the writer would put into one fragment more than once, and could not.
        /// </summary>
        /// <param name="spec">The query.</param>
        /// <param name="schema">The schema to compile against.</param>
        /// <param name="parameters">The declared parameters.</param>
        /// <returns>One line per offending value, empty when there are none.</returns>
        /// <remarks>
        ///     <para>
        ///         Read off the plan rather than off the SQL, because the question is whether one
        ///         subexpression is written twice — and two identical pieces of text may perfectly
        ///         well be two different subexpressions that happen to look alike.
        ///     </para>
        ///     <para>
        ///         Counted per fragment. A grouping key appears in both the select list and the
        ///         grouping clause, and that is what the backend requires rather than a duplication
        ///         anyone chose. Aggregations are excused too: at group grain there is no lateral
        ///         binding to refer to, and the backend computes identical aggregations once however
        ///         many times they are written.
        ///     </para>
        /// </remarks>
        public static IReadOnlyList<string> Duplicated(
            QuerySpec spec,
            QueryexSchema schema,
            IReadOnlyList<QueryexParameterDeclaration>? parameters = null)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(schema);

            DiagnosticSink sink = new();
            CompilationBudget budget = new(QueryexLimits.Default);
            QueryClauses clauses = new(
                new ExpressionCompiler(new QueryexEngineOptions()),
                spec,
                new QueryCompilationOptions { Schema = schema, Parameters = parameters ?? [] },
                sink,
                budget,
                new NullityMap());

            if (!clauses.TryBind())
            {
                // Not an empty finding list: "nothing was written twice" and "nothing was examined"
                // are the same answer to a caller that only asserts the list is empty, and the
                // second one means the property this method exists to prove went unchecked.
                throw new InvalidOperationException(
                    "The query did not bind, so nothing could be inspected for duplication: "
                    + string.Join(
                        ", ",
                        sink.Drain().Select(diagnostic => diagnostic.Code + "@" + diagnostic.Location)));
            }

            RelationalPlan plan = clauses.Lower();
            List<string> findings = [];

            foreach (ValueBinding binding in plan.Bindings)
            {
                Inspect(binding.Value, "binding " + binding.Index.ToString(Culture), findings);
            }

            foreach (PlanSelectItem item in plan.Select)
            {
                Inspect(item.Value, "select " + item.Alias, findings);
            }

            if (plan.Where is not null)
            {
                Inspect(plan.Where, "filter", findings);
            }

            if (plan.Having is not null)
            {
                Inspect(plan.Having, "having", findings);
            }

            foreach (PlanOrderItem term in plan.OrderBy)
            {
                if (term.Value is not null)
                {
                    Inspect(term.Value, "ordering", findings);
                }
            }

            return findings;
        }

        /// <summary>How numbers are written into a finding.</summary>
        private static System.Globalization.CultureInfo Culture =>
            System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Counts what one fragment writes more than once.</summary>
        /// <param name="fragment">The fragment.</param>
        /// <param name="where">Which fragment it is, for the finding.</param>
        /// <param name="findings">The findings so far.</param>
        private static void Inspect(PlanNode fragment, string where, List<string> findings)
        {
            Dictionary<PlanValue, int> counts = new(ReferenceEqualityComparer.Instance);
            Count(fragment, counts);

            foreach ((PlanValue value, int occurrences) in counts)
            {
                if (occurrences > 1 && !value.IsAtomic && !Aggregates(value))
                {
                    findings.Add(where + ": a " + value.GetType().Name
                        + " is written " + occurrences.ToString(Culture) + " times");
                }
            }
        }

        /// <summary>Counts how often each value appears under a node.</summary>
        /// <param name="node">The node.</param>
        /// <param name="counts">The counts so far.</param>
        private static void Count(PlanNode node, Dictionary<PlanValue, int> counts)
        {
            if (node is PlanValue value)
            {
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }

            foreach (PlanNode child in Children(node))
            {
                Count(child, counts);
            }
        }

        /// <summary>Whether a value contains an aggregation anywhere.</summary>
        /// <param name="node">The value.</param>
        /// <returns>True when it does.</returns>
        private static bool Aggregates(PlanNode node)
        {
            return node is PlanAggregate || Children(node).Any(Aggregates);
        }

        /// <summary>What sits directly under one plan node.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its children.</returns>
        private static ImmutableArray<PlanNode> Children(PlanNode node)
        {
            return node switch
            {
                PlanTemplateValue template => template.Operands,
                PlanTemplatePredicate template => template.Operands,
                PlanArithmetic arithmetic => [arithmetic.Left, arithmetic.Right],
                PlanNegate negate => [negate.Operand],
                PlanAggregate aggregate => Both(aggregate.Argument, aggregate.Condition),
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

        /// <summary>The children an aggregation has, skipping the ones it does not.</summary>
        /// <param name="argument">The value aggregated, when there is one.</param>
        /// <param name="condition">The per-row condition, when there is one.</param>
        /// <returns>The children.</returns>
        private static ImmutableArray<PlanNode> Both(PlanValue? argument, PlanPredicate? condition)
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
