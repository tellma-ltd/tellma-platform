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
    ///     Runs every constant statement the corpus declares on a real server and compares its one
    ///     row against the second implementation.
    /// </summary>
    /// <remarks>
    ///     A statement with no source is a shape of its own on the backend: whether a select list
    ///     alone answers with exactly one row, and whether the forms that state a computed operand
    ///     once mean what the guarded forms mean, is only settled by running it.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConstantDifferentialTests(SqlServerFixture server)
    {
        /// <summary>The server the fixture put the rows on.</summary>
        private readonly SqlServerFixture _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>Every case that compiles, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (ConstantCase entry in ConstantCorpus.All)
            {
                if (entry.Diagnostics.Count == 0)
                {
                    data.Add(entry.Id);
                }
            }

            return data;
        }

        /// <summary>Each case answers with one row, the same on the server as in memory.</summary>
        /// <param name="id">The case's name.</param>
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Case_AnswersTheSameOnTheServer(string id)
        {
            ConstantCase entry = ConstantCorpus.All.Single(c => c.Id == id);
            InterpreterContext context = entry.HasUser
                ? InterpreterContext.Fixed
                : InterpreterContext.Fixed with { UserId = null };

            QueryexResult<CompiledQuery> result = _engine.EvaluateConstant(
                entry.Items,
                entry.Options(LedgerFixture.Schema));

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            IReadOnlyList<QxValue> expected = ReferenceConstants.Evaluate(entry.Items, context);
            IReadOnlyList<IReadOnlyList<QxValue>> actual = await DifferentialTests.ExecuteOn(
                _server,
                result.Value,
                context);

            // No source, so no rows to multiply or to filter away: always exactly one.
            IReadOnlyList<QxValue> row = Assert.Single(actual);
            Assert.Equal(expected.Count, row.Count);

            for (int column = 0; column < expected.Count; column++)
            {
                DifferentialTests.AssertSame(
                    expected[column],
                    row[column],
                    id + " item " + column.ToString(CultureInfo.InvariantCulture));

                // What the column promises about absence holds for the value that came back.
                QueryexNullity nullity = result.Value.Columns[column].Nullity;
                Assert.False(
                    nullity == QueryexNullity.NotNull && row[column].IsAbsent,
                    id + " item " + column.ToString(CultureInfo.InvariantCulture) + " promised a value");

                Assert.False(
                    nullity == QueryexNullity.Null && !row[column].IsAbsent,
                    id + " item " + column.ToString(CultureInfo.InvariantCulture) + " promised no value");
            }
        }

        /// <summary>
        ///     A constant statement and a query in one batch, bound from the same values, see the
        ///     same day: the value the statement answers is the value the query computes.
        /// </summary>
        [Fact]
        public async Task AStatementAndAQuery_BoundAlike_SeeOneDay()
        {
            ConstantItem[] items = [new ConstantItem("startOfMonth(today())", QueryexType.QxDate)];
            QueryexResult<CompiledQuery> statement = _engine.EvaluateConstant(
                items,
                new ConstantCompilationOptions
                {
                    Schema = LedgerFixture.Schema,
                    QueryexVersion = QueryexLanguage.Version,
                    BatchOrdinal = 1,
                });

            QueryexResult<CompiledQuery> query = _engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "startOfMonth(today())", OrderBy = "Id", Take = 1 },
                new QueryCompilationOptions
                {
                    Schema = LedgerFixture.Schema,
                    QueryexVersion = QueryexLanguage.Version,
                });

            Assert.True(statement.Succeeded);
            Assert.True(query.Succeeded);

            InterpreterContext context = InterpreterContext.Fixed;
            IReadOnlyList<QxValue> fromStatement = Assert.Single(
                await DifferentialTests.ExecuteOn(_server, statement.Value, context));

            IReadOnlyList<QxValue> fromQuery = Assert.Single(
                await DifferentialTests.ExecuteOn(_server, query.Value, context));

            DifferentialTests.AssertSame(fromQuery[0], fromStatement[0], "the first day of the month");
            DifferentialTests.AssertSame(
                ReferenceConstants.Evaluate(items, context)[0],
                fromStatement[0],
                "the first day of the month");
        }
    }
}
