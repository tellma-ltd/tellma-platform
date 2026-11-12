// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Texts with their arguments printed in as literals answer on a real server exactly as the
    ///     originals do with those arguments bound.
    /// </summary>
    /// <remarks>
    ///     The two statements share nothing but their meaning: one binds every argument as a slot
    ///     and folds the omitted parameters away while compiling, the other compiles a text with
    ///     neither, every value a literal the server reads for itself. Agreement on every generated
    ///     case is what makes the printed text safe to run on its own.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class ParameterBindingDifferentialTests(LedgerDatabase server)
    {
        /// <summary>How many cases each seed tries.</summary>
        private const int Attempts = 60;

        /// <summary>The server the fixture put the rows on.</summary>
        private readonly LedgerDatabase _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>Binding then compiling equals compiling with the arguments, on the server.</summary>
        /// <param name="seed">Which run of generated cases to try.</param>
        /// <returns>A task that completes when every case has been compared.</returns>
        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        public async Task BindingThenCompiling_AnswersAsCompilingWithTheArguments(int seed)
        {
            int nonEmpty = 0;
            foreach (BindingCase entry in BindingCases.Generate(seed, Attempts))
            {
                InterpreterContext context = entry.Context(InterpreterContext.Fixed);

                // The original, its arguments bound as slots and its omitted parameters folded by
                // the compilation.
                QueryexResult<CompiledQuery> original = _engine.CompileQuery(
                    entry.Spec,
                    new QueryCompilationOptions
                    {
                        Schema = LedgerFixture.Schema,
                        QueryexVersion = QueryexLanguage.Version,
                        Parameters = BindingCases.Parameters,
                        OmittedParameters = entry.Omitted,
                    });
                Assert.True(original.Succeeded, entry.Id + ": " + entry.Filter);

                // The printed text, with nothing declared.
                QueryexResult<string> bound = _engine.BindParameters(entry.Filter, entry.Options);
                Assert.True(bound.Succeeded, entry.Id + ": " + entry.Filter);
                QueryexResult<CompiledQuery> rewritten = _engine.CompileQuery(
                    BindingCase.Query(bound.Value),
                    new QueryCompilationOptions { Schema = LedgerFixture.Schema, QueryexVersion = QueryexLanguage.Version });
                Assert.True(rewritten.Succeeded, entry.Id + ": " + bound.Value);

                IReadOnlyList<string> expected = Keys(
                    await DifferentialTests.ExecuteOn(_server, original.Value, context));
                IReadOnlyList<string> actual = Keys(
                    await DifferentialTests.ExecuteOn(_server, rewritten.Value, InterpreterContext.Fixed));

                Assert.True(
                    expected.SequenceEqual(actual),
                    entry.Id + ": " + entry.Filter + " bound as " + bound.Value + " admits "
                        + string.Join(",", actual) + " rather than " + string.Join(",", expected));

                // And both are what the language says.
                Assert.Equal(Keys(ReferenceQuery.Run(entry.Spec, context, BindingCases.Parameters)), expected);

                nonEmpty += expected.Count > 0 ? 1 : 0;
            }

            // Two texts agree trivially on admitting nothing, so most cases must admit something.
            Assert.True(nonEmpty > Attempts / 2, "only " + nonEmpty.ToString(CultureInfo.InvariantCulture) + " cases admitted a row");
        }

        /// <summary>
        ///     A measure's aggregation over the rows its condition admits equals the measure over
        ///     every row, on the server.
        /// </summary>
        /// <param name="seed">Which run of generated arguments to try.</param>
        /// <returns>A task that completes when every measure has been compared.</returns>
        [Theory]
        [InlineData(21)]
        [InlineData(22)]
        public async Task AMeasuresCondition_AdmitsTheRowsItAggregates(int seed)
        {
            IReadOnlyList<BindingCase> cases = BindingCases.Generate(seed, 2 * BindingCases.Measures.Count);
            for (int index = 0; index < cases.Count; index++)
            {
                BindingCase entry = cases[index];
                (string measure, string unconditioned) = BindingCases.Measures[index % BindingCases.Measures.Count];
                InterpreterContext context = entry.Context(InterpreterContext.Fixed);

                QuerySpec whole = new() { Root = LedgerFixture.Invoice, Select = measure, Aggregate = true };
                QueryexResult<CompiledQuery> measured = _engine.CompileQuery(
                    whole,
                    new QueryCompilationOptions
                    {
                        Schema = LedgerFixture.Schema,
                        QueryexVersion = QueryexLanguage.Version,
                        Parameters = BindingCases.Parameters,
                        OmittedParameters = entry.Omitted,
                    });
                Assert.True(measured.Succeeded, entry.Id + ": " + measure);

                QueryexResult<MeasureRows> condition = _engine.MeasureCondition(measure, entry.Options);
                Assert.True(condition.Succeeded, entry.Id + ": " + measure);

                QueryexResult<CompiledQuery> drilled = _engine.CompileQuery(
                    new QuerySpec
                    {
                        Root = LedgerFixture.Invoice,
                        Select = unconditioned,
                        Aggregate = true,
                        Filter = condition.Value.Condition is string text ? FilterTree.Leaf(text) : null,
                    },
                    new QueryCompilationOptions { Schema = LedgerFixture.Schema, QueryexVersion = QueryexLanguage.Version });
                Assert.True(drilled.Succeeded, entry.Id + ": " + condition.Value.Condition);

                QxValue expected = Assert.Single(Assert.Single(
                    await DifferentialTests.ExecuteOn(_server, measured.Value, context)));
                QxValue actual = Assert.Single(Assert.Single(
                    await DifferentialTests.ExecuteOn(_server, drilled.Value, InterpreterContext.Fixed)));

                string where = entry.Id + ": " + measure + " over " + (condition.Value.Condition ?? "every row");
                DifferentialTests.AssertSame(expected, actual, where);
                DifferentialTests.AssertSame(
                    Assert.Single(Assert.Single(ReferenceQuery.Run(whole, context, BindingCases.Parameters))),
                    expected,
                    where);
            }
        }

        /// <summary>The keys of the rows a query over invoices returned.</summary>
        /// <param name="rows">The rows, each its key alone.</param>
        /// <returns>The keys, as text.</returns>
        private static IReadOnlyList<string> Keys(IReadOnlyList<IReadOnlyList<QxValue>> rows)
        {
            return [.. rows.Select(row => row[0].ToString())];
        }
    }
}
