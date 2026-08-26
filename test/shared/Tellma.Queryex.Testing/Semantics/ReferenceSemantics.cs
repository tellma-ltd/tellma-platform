// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Pipeline;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>What one expression evaluated to for one row.</summary>
    /// <param name="Row">The row's key.</param>
    /// <param name="Values">The value of each item of the list.</param>
    public sealed record RowReading(int Row, IReadOnlyList<QxValue> Values);

    /// <summary>
    ///     The language as a second implementation reads it.
    /// </summary>
    /// <remarks>
    ///     The engine's own intermediate forms stay internal; what leaves here is values and the
    ///     places where the two readings disagree, which is all a test has any business seeing.
    /// </remarks>
    public static class ReferenceSemantics
    {
        /// <summary>The stages, shared because this reading only ever asks them to bind.</summary>
        private static readonly ExpressionCompiler Compiler = new(new QueryexEngineOptions());

        /// <summary>Evaluates one expression list against every row of the fixture.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="mode">The position the expression is written for.</param>
        /// <param name="context">What the evaluation depends on besides the row.</param>
        /// <returns>What it evaluated to, row by row.</returns>
        /// <exception cref="ArgumentException">The expression did not compile.</exception>
        public static IReadOnlyList<RowReading> Evaluate(
            string expression,
            EntityDescriptor root,
            QueryexMode mode,
            InterpreterContext context)
        {
            BoundExpression bound = Bind(expression, root, mode, hasGroupingKeys: false, hasUser: context.UserId is not null);
            ImmutableArray<BoundItem> items = bound.Items;
            List<RowReading> readings = [];

            foreach (LedgerRow row in LedgerData.Rows(root))
            {
                Interpreter interpreter = new(row, context);
                List<QxValue> values = [];
                foreach (BoundItem item in items)
                {
                    values.Add(interpreter.Evaluate(item.Expression));
                }

                readings.Add(new RowReading((int)row[root.Key.Name].AsNumber.Value, values));
            }

            return readings;
        }

        /// <summary>
        ///     Checks every claim the analysis made about absence against what actually happens.
        /// </summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="mode">The position the expression is written for.</param>
        /// <param name="context">What the evaluation depends on besides the row.</param>
        /// <param name="hasGroupingKeys">Whether the enclosing query groups by anything.</param>
        /// <param name="hasUser">Whether execution will have a signed-in user.</param>
        /// <param name="declarations">The declared parameters, when the expression names any.</param>
        /// <returns>One line per claim that did not hold, empty when they all did.</returns>
        /// <remarks>
        ///     Both directions. Saying a value is always present and finding a row where it is not
        ///     means a guard was left out and rows are being dropped; saying a value is never present
        ///     and finding one where it is means a whole subexpression was folded away wrongly.
        /// </remarks>
        public static IReadOnlyList<string> NullityViolations(
            string expression,
            EntityDescriptor root,
            QueryexMode mode,
            InterpreterContext context,
            bool hasGroupingKeys = false,
            bool hasUser = true,
            IReadOnlyList<QueryexParameterDeclaration>? declarations = null)
        {
            // Bound once, so the answers being checked belong to the very nodes being evaluated.
            // Binding a second time to fetch them would compare against a different set of nodes and
            // quietly find nothing wrong with anything.
            BoundExpression bound = Bind(expression, root, mode, hasGroupingKeys, hasUser, declarations);

            List<string> violations = [];
            foreach (LedgerRow row in LedgerData.Rows(root))
            {
                Interpreter interpreter = new(row, context) { Group = LedgerData.Rows(root) };
                interpreter.Sweep(bound.Items);

                foreach ((TypedExpr node, QxValue value) in interpreter.Readings)
                {
                    QueryexNullity claimed = bound.Nullity[node];
                    if (claimed == QueryexNullity.NotNull && value.IsAbsent)
                    {
                        violations.Add(Describe(expression, row, node, "claimed always present, was absent"));
                    }
                    else if (claimed == QueryexNullity.Null && !value.IsAbsent)
                    {
                        violations.Add(Describe(expression, row, node, "claimed never present, was " + value));
                    }
                }
            }

            return violations;
        }

        /// <summary>Every function the registry declares that this reading does not cover.</summary>
        /// <returns>The names.</returns>
        public static IReadOnlyList<string> UncoveredFunctions()
        {
            List<string> missing = [];
            foreach (FunctionDefinition definition in FunctionRegistry.All)
            {
                if (!Interpreter.Covered.Contains(definition.Name))
                {
                    missing.Add(definition.Name);
                }
            }

            return missing;
        }

        /// <summary>
        ///     Whether this reading can evaluate a corpus case at all.
        /// </summary>
        /// <param name="entry">The case.</param>
        /// <returns>True when the case is one this reading runs.</returns>
        /// <remarks>
        ///     Stated once and read by everything that sweeps the corpus, so that a check claiming
        ///     to have covered a case and the sweep that would have caught a fault in it cannot
        ///     disagree about which cases those are.
        /// </remarks>
        public static bool CanEvaluate(ExpressionCase entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            // A case that is expected not to compile has no tree to evaluate; the rest are shapes
            // this reading has no fixture rows or bindings for.
            return entry.Diagnostics.Count == 0
                && entry.Parameters.Count == 0
                && entry.Root == "Invoice"
                && !entry.Directions;
        }

        /// <summary>
        ///     Every function the registry declares that no corpus case actually evaluates.
        /// </summary>
        /// <returns>The names.</returns>
        /// <remarks>
        ///     The other two checks compare the registry against a list this reading keeps, which
        ///     is a claim about the reading rather than the reading itself: a name added to that
        ///     list without an arm in the evaluator would satisfy both and still evaluate nothing.
        ///     This one runs the evaluator over the corpus and reports what it never reached, so
        ///     the claim is only ever as good as a case that exercises it — and an uncovered name
        ///     surfaces as this list rather than as an exception from whichever suite happened to
        ///     touch it first.
        /// </remarks>
        public static IReadOnlyList<string> UnexercisedFunctions()
        {
            HashSet<string> evaluated = new(StringComparer.OrdinalIgnoreCase);
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                if (!CanEvaluate(entry))
                {
                    continue;
                }

                InterpreterContext context = entry.HasUser
                    ? InterpreterContext.Fixed
                    : InterpreterContext.Fixed with { UserId = null };

                BoundExpression bound = Bind(
                    entry.Text,
                    LedgerFixture.Invoice,
                    entry.Mode,
                    entry.HasGroupingKeys,
                    entry.HasUser);

                // One row is enough: the sweep visits every node whatever the row's values are, so
                // a function under a branch this row does not take is still reached.
                LedgerRow row = LedgerData.Rows(LedgerFixture.Invoice)[0];
                Interpreter interpreter = new(row, context)
                {
                    Group = LedgerData.Rows(LedgerFixture.Invoice),
                };

                interpreter.Sweep(bound.Items);

                foreach (TypedExpr node in interpreter.Readings.Keys)
                {
                    if (node is TypedCall call)
                    {
                        evaluated.Add(call.Definition.Name);
                    }
                }
            }

            List<string> unexercised = [];
            foreach (FunctionDefinition definition in FunctionRegistry.All)
            {
                if (!evaluated.Contains(definition.Name))
                {
                    unexercised.Add(definition.Name);
                }
            }

            return unexercised;
        }

        /// <summary>Every function this reading covers that the registry does not declare.</summary>
        /// <returns>The names.</returns>
        public static IReadOnlyList<string> UnknownFunctions()
        {
            HashSet<string> declared = new(StringComparer.OrdinalIgnoreCase);
            foreach (FunctionDefinition definition in FunctionRegistry.All)
            {
                declared.Add(definition.Name);
            }

            return [.. Interpreter.Covered.Where(name => !declared.Contains(name))];
        }

        /// <summary>Binds one expression, refusing anything that did not compile.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="root">The entity paths resolve from.</param>
        /// <param name="mode">The position the expression is written for.</param>
        /// <param name="hasGroupingKeys">Whether the enclosing query groups by anything.</param>
        /// <param name="hasUser">Whether execution will have a signed-in user.</param>
        /// <param name="declarations">The declared parameters, when the expression names any.</param>
        /// <returns>The bound expression.</returns>
        /// <exception cref="ArgumentException">The expression did not compile.</exception>
        private static BoundExpression Bind(
            string expression,
            EntityDescriptor root,
            QueryexMode mode,
            bool hasGroupingKeys,
            bool hasUser = true,
            IReadOnlyList<QueryexParameterDeclaration>? declarations = null)
        {
            DiagnosticSink sink = new();
            bool compiled = Compiler.TryCompile(
                expression,
                new BindingContext
                {
                    Schema = LedgerFixture.Schema,
                    Root = root,
                    Mode = mode,
                    HasUser = hasUser,
                    HasGroupingKeys = hasGroupingKeys,
                    Parameters = Symbols.From(declarations ?? []),
                },
                directions: false,
                QueryexLimits.Default,
                sink,
                DiagnosticLocation.None,
                out _,
                out BoundExpression? bound);

            return compiled && bound is not null
                ? bound
                : throw new ArgumentException(
                    "The expression did not compile: "
                        + string.Join(", ", sink.Drain().Select(d => d.Code)),
                    nameof(expression));
        }

        /// <summary>Describes one broken claim.</summary>
        /// <param name="expression">The expression text.</param>
        /// <param name="row">The row it was evaluated against.</param>
        /// <param name="node">The node whose claim broke.</param>
        /// <param name="problem">What went wrong.</param>
        /// <returns>The description.</returns>
        private static string Describe(string expression, LedgerRow row, TypedExpr node, string problem)
        {
            string fragment = node.Span.Length > 0 && node.Span.Start + node.Span.Length <= expression.Length
                ? expression.Substring(node.Span.Start, node.Span.Length)
                : node.Kind.ToString();

            return "row " + row[row.Entity.Key.Name] + ": '" + fragment + "' " + problem;
        }
    }
}
