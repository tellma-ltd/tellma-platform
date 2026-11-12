// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Prints texts with their arguments written in as literals, for a host that has to hand a
    ///     self-contained text to something that runs it.
    /// </summary>
    /// <remarks>
    ///     Syntactic throughout: the text is parsed, folded, given its literals and printed, and no
    ///     schema is consulted. That is what makes the answer safe to build a filter from — it is the
    ///     engine's own reading of the text, where a host replacing names in the text would trip
    ///     over a parameter marker inside a string.
    /// </remarks>
    internal static class ParameterBinder
    {
        /// <summary>The position of an aggregation's condition among its arguments.</summary>
        private const int ConditionIndex = 1;

        /// <summary>The spelling of a disjunction between two printed conditions.</summary>
        private const string Disjunction = " or ";

        /// <summary>Prints a text with its arguments as literals and its omitted parameters folded.</summary>
        /// <param name="compiler">The stage runner, which remembers what it has already parsed.</param>
        /// <param name="text">The text.</param>
        /// <param name="options">The binding options.</param>
        /// <returns>The canonical text, or the diagnostics that stopped it parsing.</returns>
        internal static QueryexResult<string> Bind(
            ExpressionCompiler compiler,
            string text,
            ParameterBindingOptions options)
        {
            // The caller's part is checked before the text is read, so a host mistake is reported
            // as one whatever the text holds.
            var arguments = ParameterArguments.Resolve(options, nameof(options));

            DiagnosticSink sink = new();
            if (!compiler.TryParse(text, options.Limits, sink, DiagnosticLocation.None, out ExpressionListSyntax? syntax)
                || syntax is null)
            {
                return new QueryexResult<string>(sink.Drain());
            }

            FoldedPredicateList bound = arguments.Apply(syntax, options.Mode);
            return new QueryexResult<string>(bound.Constant is bool constant
                ? Truth(constant)
                : SyntaxPrinter.Print(bound.List!));
        }

        /// <summary>
        ///     Works out the row condition a measure aggregates over, as one self-contained predicate.
        /// </summary>
        /// <param name="compiler">The stage runner, which remembers what it has already parsed.</param>
        /// <param name="measure">The measure.</param>
        /// <param name="options">The binding options; their mode is not consulted.</param>
        /// <returns>The rows the measure aggregates, or the diagnostics that stopped it parsing.</returns>
        /// <remarks>
        ///     The rows that feed a measure are the union of the rows that feed each of its
        ///     aggregations, so the answer is the disjunction of their conditions. One aggregation
        ///     with no condition, or with one that folds to true, reads every row, and so does the
        ///     measure; a condition that folds to false reads none and adds nothing to the union.
        /// </remarks>
        internal static QueryexResult<MeasureRows> MeasureCondition(
            ExpressionCompiler compiler,
            string measure,
            ParameterBindingOptions options)
        {
            var arguments = ParameterArguments.Resolve(options, nameof(options));

            DiagnosticSink sink = new();
            if (!compiler.TryParse(measure, options.Limits, sink, DiagnosticLocation.None, out ExpressionListSyntax? syntax)
                || syntax is null)
            {
                return new QueryexResult<MeasureRows>(sink.Drain());
            }

            List<CallSyntax> aggregations = [];
            CollectAggregations(syntax, aggregations);
            if (aggregations.Count == 0)
            {
                // Not a measure at all, which binding refuses wherever one is required; there is no
                // condition to report.
                return new QueryexResult<MeasureRows>(new MeasureRows(null));
            }

            // Distinct conditions in the order they are first met, compared as the canonical text
            // they print as, so two spellings of one condition count once.
            List<string> conditions = [];
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (CallSyntax aggregation in aggregations)
            {
                if (aggregation.Arguments.Count <= ConditionIndex)
                {
                    return new QueryexResult<MeasureRows>(new MeasureRows(null));
                }

                FoldedPredicate folded = arguments.ApplyToCondition(aggregation.Arguments[ConditionIndex]);

                // A truth value, whether folding produced it or the author wrote it, either reads
                // every row or none.
                bool? settled = folded.Constant
                    ?? (SyntaxPrinter.Unwrap(folded.Node!) is BooleanSyntax written ? written.Value : null);

                if (settled == true)
                {
                    return new QueryexResult<MeasureRows>(new MeasureRows(null));
                }

                if (settled == false)
                {
                    continue;
                }

                string printed = SyntaxPrinter.Print(folded.Node!);
                if (seen.Add(printed))
                {
                    conditions.Add(printed);
                }
            }

            string condition = conditions.Count == 0
                ? Truth(false)
                : string.Join(Disjunction, conditions.Select(static printed => "(" + printed + ")"));

            return new QueryexResult<MeasureRows>(new MeasureRows(condition));
        }

        /// <summary>Collects every aggregation in a tree, outermost and leftmost first.</summary>
        /// <param name="node">The tree.</param>
        /// <param name="aggregations">The aggregations found so far.</param>
        private static void CollectAggregations(SyntaxNode node, List<CallSyntax> aggregations)
        {
            if (node is CallSyntax call && FunctionRegistry.IsAggregate(call.Name))
            {
                aggregations.Add(call);
            }

            foreach (SyntaxNode child in node.Children)
            {
                CollectAggregations(child, aggregations);
            }
        }

        /// <summary>A truth value's spelling.</summary>
        /// <param name="value">The truth value.</param>
        /// <returns>The keyword.</returns>
        private static string Truth(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
