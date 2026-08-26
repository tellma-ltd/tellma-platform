// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Properties
{
    /// <summary>
    ///     Things that have to be true of every expression, rather than of the ones somebody wrote
    ///     down.
    /// </summary>
    /// <remarks>
    ///     The corpus pins what particular inputs do. These pin what all of them do, which is where
    ///     the mistakes nobody thought to write a case for show up.
    /// </remarks>
    public sealed class PropertyTests
    {
        /// <summary>How many expressions each generated check tries.</summary>
        private const int Attempts = 400;

        /// <summary>How long the robustness check may take before it is called a hang.</summary>
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     Printing an expression and parsing it again gives back the same expression.
        /// </summary>
        /// <param name="seed">Which run of generated expressions to try.</param>
        /// <remarks>
        ///     Printed with every grouping written out, so this does not check the precedence table
        ///     by using the precedence table: the printed form says what it means without relying on
        ///     the reader knowing which operator binds tighter.
        /// </remarks>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Printing_AndParsingAgain_GivesBackTheSameExpression(int seed)
        {
            ExpressionGenerator generator = new(seed);
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                string text = generator.Predicate(attempt % 4);
                ProbeSyntax parsed = QueryexProbe.Parse(text);
                Assert.Empty(parsed.Diagnostics);

                ProbeSyntax reparsed = QueryexProbe.Parse(parsed.ExplicitText);
                Assert.Empty(reparsed.Diagnostics);
                Assert.Equal(parsed.ExplicitText, reparsed.ExplicitText);
                Assert.True(
                    QueryexProbe.AreEquivalent(text, parsed.ExplicitText),
                    text + " printed as " + parsed.ExplicitText);
            }
        }

        /// <summary>Every corpus expression survives the same round trip.</summary>
        [Fact]
        public void EveryCorpusExpression_SurvivesTheRoundTrip()
        {
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                ProbeSyntax parsed = QueryexProbe.Parse(entry.Text, entry.Limits);
                if (parsed.Diagnostics.Count > 0)
                {
                    continue;
                }

                ProbeSyntax reparsed = QueryexProbe.Parse(parsed.ExplicitText);
                Assert.Empty(reparsed.Diagnostics);
                Assert.Equal(parsed.ExplicitText, reparsed.ExplicitText);
            }
        }

        /// <summary>The same query compiles to the same bytes, however it is asked for.</summary>
        /// <remarks>
        ///     Checked across fresh engines as well as within one, because an engine remembers what
        ///     it has already compiled and a check that only asked twice would be checking the
        ///     memory rather than the compiler.
        /// </remarks>
        [Fact]
        public void Compilation_IsDeterministic()
        {
            foreach (QueryCase entry in QueryCorpus.All)
            {
                if (entry.Diagnostics.Count > 0)
                {
                    continue;
                }

                string first = Compile(new QueryexEngine(), entry, ordinal: 0);
                string second = Compile(new QueryexEngine(), entry, ordinal: 0);
                Assert.Equal(first, second);
            }
        }

        /// <summary>Two compilations differ only in the names the ordinal decides.</summary>
        [Fact]
        public void BatchOrdinal_ChangesOnlyTheNames()
        {
            foreach (QueryCase entry in QueryCorpus.All)
            {
                if (entry.Diagnostics.Count > 0)
                {
                    continue;
                }

                string zero = Compile(new QueryexEngine(), entry, ordinal: 0);
                string seven = Compile(new QueryexEngine(), entry, ordinal: 7);
                Assert.Equal(zero, seven.Replace("@qx7_", "@qx0_", StringComparison.Ordinal));
            }
        }

        /// <summary>No value the writer cannot repeat is written more than once.</summary>
        /// <remarks>
        ///     Read off the lowered form rather than off the text, because two identical pieces of
        ///     SQL may be two different subexpressions that happen to look alike, and the question
        ///     is whether one of them was written twice.
        /// </remarks>
        [Fact]
        public void NoComputedValue_IsWrittenTwice()
        {
            List<string> findings = [];
            foreach (QueryCase entry in QueryCorpus.All)
            {
                if (entry.Diagnostics.Count > 0)
                {
                    continue;
                }

                findings.AddRange(ProbePlan
                    .Duplicated(entry.Spec, LedgerFixture.Schema, entry.Parameters)
                    .Select(finding => entry.Id + ": " + finding));
            }

            Assert.Empty(findings);
        }

        /// <summary>Guarded comparisons over computed operands write each operand once.</summary>
        /// <param name="filter">The predicate.</param>
        [Theory]
        [InlineData("Rate + 1 = Amount * 2")]
        [InlineData("Rate + 1 != Amount * 2")]
        [InlineData("Rate + 1 > Amount * 2")]
        [InlineData("Memo || 'x' = Notes || 'y'")]
        [InlineData("coalesce(Rate, Amount) + 1 in (1, 2, null)")]
        [InlineData("upper(Memo) = lower(Notes) and trim(Memo) != Notes")]
        public void GuardedComparisons_WriteEachOperandOnce(string filter)
        {
            IReadOnlyList<string> findings = ProbePlan.Duplicated(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Leaf(filter),
                },
                LedgerFixture.Schema);

            Assert.Empty(findings);
        }

        /// <summary>Nothing a user can type makes the engine throw or hang.</summary>
        /// <param name="seed">Which run of generated rubbish to try.</param>
        [Theory]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        public void Rubbish_ComesBackAsDiagnostics(int seed)
        {
            ExpressionGenerator generator = new(seed);
            var clock = Stopwatch.StartNew();

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                string text = generator.Rubbish(attempt % 120);
                QueryexResult<ValidatedExpression> result = Engine.Validate(
                    text,
                    new ValidationOptions
                    {
                        LanguageVersion = QueryexLanguage.Version,
                        Schema = LedgerFixture.Schema,
                        Root = LedgerFixture.Invoice,
                        Mode = QueryexMode.Filter,
                    });

                Assert.True(result.Succeeded || result.Diagnostics.Count > 0);
                Assert.True(
                    clock.Elapsed < Budget,
                    "the engine took longer than " + Budget + " on generated input");
            }
        }

        /// <summary>Discovering rubbish is just as safe as validating it.</summary>
        [Fact]
        public void DiscoveringRubbish_IsAlsoSafe()
        {
            ExpressionGenerator generator = new(21);
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                DiscoveryResult result = Engine.Discover(
                    generator.Rubbish(attempt % 90),
                    new DiscoveryOptions
                    {
                        Schema = LedgerFixture.Schema,
                        Root = LedgerFixture.Invoice,
                    });

                Assert.NotNull(result.Parameters);
            }
        }

        /// <summary>Deeply nested input is refused rather than overrunning the stack.</summary>
        [Fact]
        public void DeepNesting_IsRefused()
        {
            string text = new string('(', 5000) + "Amount" + new string(')', 5000);
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                text,
                new ValidationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, d => d.Code.StartsWith("QX5", StringComparison.Ordinal));
        }

        /// <summary>Compiles one case, for the checks that compare two compilations.</summary>
        /// <param name="engine">The engine to compile with.</param>
        /// <param name="entry">The case.</param>
        /// <param name="ordinal">The query's position among the ones executed together.</param>
        /// <returns>The SQL.</returns>
        private static string Compile(QueryexEngine engine, QueryCase entry, int ordinal)
        {
            QueryexResult<CompiledQuery> result = engine.CompileQuery(
                entry.Spec,
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Parameters = entry.Parameters,
                    HasUser = entry.HasUser,
                    Limits = entry.Limits,
                    BatchOrdinal = ordinal,
                });

            Assert.True(result.Succeeded, entry.Id);
            return result.Value.Sql
                + string.Join(
                    string.Empty,
                    result.Value.Parameters.Select(slot =>
                        "\n" + slot.Name + " " + slot.StoreType.Family.ToString()
                            + " " + slot.Origin.ToString()));
        }
    }
}
