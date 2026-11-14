// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.Tests.Properties
{
    /// <summary>
    ///     Binding arguments into a text keeps its meaning, over generated texts and arguments.
    /// </summary>
    /// <remarks>
    ///     The second reading answers both sides: the original with its arguments and omitted
    ///     parameters — which it applies while it answers, never by rewriting the text — and the
    ///     printed text with none. The same check against a real server is the differential suite's.
    /// </remarks>
    public sealed class ParameterBindingPropertyTests
    {
        /// <summary>How many cases each seed tries.</summary>
        private const int Attempts = 150;

        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     Binding then compiling admits the rows that compiling with the arguments admits, and
        ///     the printed text reads back as the tree that was printed.
        /// </summary>
        /// <param name="seed">Which run of generated cases to try.</param>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void BindingThenCompiling_EqualsCompilingWithTheArguments(int seed)
        {
            foreach (BindingCase entry in BindingCases.Generate(seed, Attempts))
            {
                QueryexResult<string> bound = Engine.BindParameters(entry.Filter, entry.Options);
                Assert.True(bound.Succeeded, entry.Id + ": " + entry.Filter);

                // The printed text is self-contained: it compiles with nothing declared.
                QuerySpec rewritten = BindingCase.Query(bound.Value);
                QueryexResult<CompiledQuery> compiled = Engine.CompileQuery(
                    rewritten,
                    new QueryCompilationOptions { Schema = LedgerFixture.Schema, QueryexVersion = QueryexLanguage.Version });
                Assert.True(
                    compiled.Succeeded,
                    entry.Id + ": " + bound.Value + " — " + string.Join(", ", compiled.Diagnostics.Select(d => d.Code)));

                IReadOnlyList<IReadOnlyList<QxValue>> expected = ReferenceQuery.Run(
                    entry.Spec,
                    entry.Context(InterpreterContext.Fixed),
                    BindingCases.Parameters);

                IReadOnlyList<IReadOnlyList<QxValue>> actual = ReferenceQuery.Run(rewritten, InterpreterContext.Fixed);

                Assert.True(
                    Keys(expected).SequenceEqual(Keys(actual)),
                    entry.Id + ": " + entry.Filter + " bound as " + bound.Value + " admits "
                        + string.Join(",", Keys(actual)) + " rather than " + string.Join(",", Keys(expected)));

                Assert.True(ProbeParameterBinding.RoundTrips(entry.Filter, entry.Options), entry.Id + ": " + entry.Filter);
            }
        }

        /// <summary>
        ///     A measure's aggregation over the rows its condition admits equals the measure over
        ///     every row.
        /// </summary>
        /// <param name="seed">Which run of generated arguments to try.</param>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void AMeasuresCondition_AdmitsTheRowsItAggregates(int seed)
        {
            IReadOnlyList<BindingCase> cases = BindingCases.Generate(seed, Attempts);
            for (int index = 0; index < cases.Count; index++)
            {
                BindingCase entry = cases[index];
                (string measure, string unconditioned) = BindingCases.Measures[index % BindingCases.Measures.Count];

                QueryexResult<MeasureRows> condition = Engine.MeasureCondition(measure, entry.Options);
                Assert.True(condition.Succeeded);

                QxValue expected = Assert.Single(Assert.Single(ReferenceQuery.Run(
                    new QuerySpec { Root = LedgerFixture.Invoice, Select = measure, Aggregate = true },
                    entry.Context(InterpreterContext.Fixed),
                    BindingCases.Parameters)));

                QxValue actual = Assert.Single(Assert.Single(ReferenceQuery.Run(
                    new QuerySpec
                    {
                        Root = LedgerFixture.Invoice,
                        Select = unconditioned,
                        Aggregate = true,
                        Filter = condition.Value.Condition is string text ? FilterTree.Leaf(text) : null,
                    },
                    InterpreterContext.Fixed)));

                Assert.True(
                    Same(expected, actual),
                    entry.Id + ": " + measure + " over " + (condition.Value.Condition ?? "every row")
                        + " gave " + actual + " rather than " + expected);
            }
        }

        /// <summary>The keys of the rows a query over invoices returned.</summary>
        /// <param name="rows">The rows, each its key alone.</param>
        /// <returns>The keys, as text.</returns>
        private static IEnumerable<string> Keys(IReadOnlyList<IReadOnlyList<QxValue>> rows)
        {
            return rows.Select(row => row[0].ToString());
        }

        /// <summary>Whether two answers are the same value, numbers compared by value whatever their scale.</summary>
        /// <param name="expected">One answer.</param>
        /// <param name="actual">The other.</param>
        /// <returns>True when they are.</returns>
        private static bool Same(QxValue expected, QxValue actual)
        {
            return expected.IsAbsent || actual.IsAbsent
                ? expected.IsAbsent == actual.IsAbsent
                : expected.Type == QueryexType.QxNumeric
                    ? expected.AsNumber.CompareTo(actual.AsNumber) == 0
                    : Equals(expected.Raw, actual.Raw);
        }
    }
}
