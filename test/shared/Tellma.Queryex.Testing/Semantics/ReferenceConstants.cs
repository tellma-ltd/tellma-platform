// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Pipeline;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     A constant statement, answered from memory.
    /// </summary>
    /// <remarks>
    ///     The other half of the differential check for expressions that read no row: the compiler
    ///     turns the items into one statement a server answers with one row, and this evaluates the
    ///     same items by following the rules directly, with no row to read at all.
    /// </remarks>
    public static class ReferenceConstants
    {
        /// <summary>The stages, shared because this reading only ever asks them to bind.</summary>
        private static readonly ExpressionCompiler Compiler = new(new QueryexEngineOptions());

        /// <summary>Evaluates every item of one statement.</summary>
        /// <param name="items">The items, in order.</param>
        /// <param name="context">What the evaluation depends on.</param>
        /// <returns>The one row: a value per item.</returns>
        /// <exception cref="InvalidOperationException">An item did not bind.</exception>
        public static IReadOnlyList<QxValue> Evaluate(IReadOnlyList<ConstantItem> items, InterpreterContext context)
        {
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(context);

            DiagnosticSink sink = new();
            BindingContext binding = ConstantCompiler.Context(
                LedgerFixture.Schema,
                QueryexLanguage.Version,
                hasUser: context.UserId is not null);

            // One reading for the whole statement, and no row for it to look at.
            Interpreter interpreter = new(context);
            List<QxValue> values = [];
            for (int index = 0; index < items.Count; index++)
            {
                BoundExpression bound = ConstantCompiler.Bind(
                    Compiler,
                    items[index],
                    binding,
                    QueryexLimits.Default,
                    sink,
                    DiagnosticLocation.Constant(index))
                    ?? throw new InvalidOperationException(
                        "The item did not bind, so there is nothing to evaluate: "
                        + string.Join(", ", sink.Drain().Select(diagnostic => diagnostic.Code + "@" + diagnostic.Location)));

                values.Add(interpreter.Evaluate(bound.Items[0].Expression));
            }

            return values;
        }
    }
}
