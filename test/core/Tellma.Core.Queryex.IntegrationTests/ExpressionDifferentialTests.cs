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
    ///     Runs every expression the corpus declares on a real server, over rows, and compares each
    ///     value against the second implementation.
    /// </summary>
    /// <remarks>
    ///     The query corpus exercises the shapes a statement can take; this exercises what the
    ///     expressions inside one mean. Without it every emit template is pinned only by the text it
    ///     produces, so a template that names the wrong backend function — the opposite case
    ///     conversion, the wrong rounding, the arguments the other way round — agrees with its own
    ///     snapshot and is caught by nothing.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class ExpressionDifferentialTests(SqlServerFixture server)
    {
        /// <summary>The server the fixture put the rows on.</summary>
        private readonly SqlServerFixture _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>The values the declared parameters are given.</summary>
        private static InterpreterContext Context => InterpreterContext.Fixed with
        {
            Parameters = new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase)
            {
                ["From"] = QxValue.Date(new DateOnly(2024, 1, 1)),
                ["To"] = QxValue.Date(new DateOnly(2025, 12, 31)),
                ["X"] = QxValue.Number(new System.Data.SqlTypes.SqlDecimal(3m)),
                ["N"] = QxValue.Number(new System.Data.SqlTypes.SqlDecimal(2m)),
            },
        };

        /// <summary>Every case this suite can run, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        /// <remarks>
        ///     A case is runnable when it compiles, sits in a position a select list can hold, and
        ///     names no parameter this suite has no value for. What is left out is left out by a
        ///     rule rather than one at a time, and the count is pinned below so the rule cannot
        ///     quietly widen until nothing runs.
        /// </remarks>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (ExpressionCase entry in Runnable())
            {
                data.Add(entry.Id);
            }

            return data;
        }

        /// <summary>Each case answers the same on the server as it does in memory.</summary>
        /// <param name="id">The case's name.</param>
        /// <returns>A task that completes when the case has been checked.</returns>
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Expression_AnswersTheSameOnTheServer(string id)
        {
            ExpressionCase entry = ExpressionCorpus.All.Single(c => c.Id == id);
            EntityDescriptor root = LedgerFixture.Schema.FindEntity(entry.Root)!;
            InterpreterContext context = entry.HasUser ? Context : Context with { UserId = null };

            // Ordered by the root's key so the two answers line up row by row, and read alongside
            // the key so a failure names the row it is about.
            QuerySpec spec = new()
            {
                Root = root,
                Select = root.Key.Name + ", " + entry.Text,
                OrderBy = root.Key.Name,
            };

            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Parameters = entry.Parameters,
                    HasUser = entry.HasUser,
                    Limits = entry.Limits,
                });

            Assert.True(
                result.Succeeded,
                id + ": " + string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            IReadOnlyList<IReadOnlyList<QxValue>> expected =
                ReferenceQuery.Run(spec, context, entry.Parameters);

            IReadOnlyList<IReadOnlyList<QxValue>> actual =
                await DifferentialTests.ExecuteOn(_server, result.Value, context);

            Assert.Equal(expected.Count, actual.Count);
            Assert.NotEmpty(actual);

            for (int row = 0; row < expected.Count; row++)
            {
                DifferentialTests.AssertSame(
                    expected[row][1],
                    actual[row][1],
                    id + " row " + row.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        ///     The suite runs the corpus, not a handful of it.
        /// </summary>
        /// <remarks>
        ///     A rule that decides what to run can widen one exclusion at a time until it runs
        ///     nothing, and every case it stopped running still passes. The floor is what stops
        ///     that: it fails when coverage falls, and it is raised deliberately when it rises.
        /// </remarks>
        [Fact]
        public void MostOfTheCorpus_IsActuallyRun()
        {
            int compiles = ExpressionCorpus.All.Count(entry => entry.Diagnostics.Count == 0);
            int runnable = Runnable().Count();

            Assert.True(
                runnable >= 40,
                runnable.ToString(CultureInfo.InvariantCulture) + " of " +
                compiles.ToString(CultureInfo.InvariantCulture) + " compiling cases run");
        }

        /// <summary>The cases this suite can put in a select list and run.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<ExpressionCase> Runnable()
        {
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                bool runnable = entry.Diagnostics.Count == 0
                    && !entry.Directions
                    && !entry.HasGroupingKeys
                    && entry.Mode.Grouping == QueryexGrouping.Row
                    && entry.Items is null or 1
                    && entry.Parameters.All(p => Context.Parameters.ContainsKey(p.Name));

                if (runnable)
                {
                    yield return entry;
                }
            }
        }
    }
}
