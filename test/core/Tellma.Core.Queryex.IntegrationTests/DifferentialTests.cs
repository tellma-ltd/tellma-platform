// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Runs every query the corpus declares on a real server and compares the answers against
    ///     the second implementation.
    /// </summary>
    /// <remarks>
    ///     This is the check that the compiler and the language agree. Everything else the suites do
    ///     pins what the compiler produces; only this says whether what it produces means what it is
    ///     supposed to mean.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class DifferentialTests(SqlServerFixture server)
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
            },
        };

        /// <summary>Every case that compiles, as theory arguments.</summary>
        /// <returns>The cases.</returns>
        public static TheoryData<string> Cases()
        {
            TheoryData<string> data = [];
            foreach (QueryCase entry in QueryCorpus.All)
            {
                if (entry.Diagnostics.Count == 0)
                {
                    data.Add(entry.Id);
                }
            }

            return data;
        }

        /// <summary>The rows the fixture put on the server are the rows it holds in memory.</summary>
        /// <remarks>
        ///     Checked before anything else, because a divergence in the data would otherwise show up
        ///     as dozens of confusing differences between the two implementations rather than as the
        ///     one thing that is actually wrong.
        /// </remarks>
        [Fact]
        public async Task StoredRows_MatchTheRowsInMemory()
        {
            foreach (EntityDescriptor entity in LedgerFixture.Schema.Entities)
            {
                IReadOnlyList<string> counted = await _server.ReadColumnAsync(
                    "SELECT COUNT_BIG(*) FROM " + entity.Source + ";");

                Assert.Equal(
                    LedgerData.Rows(entity).Count.ToString(CultureInfo.InvariantCulture),
                    counted[0]);
            }

            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id, Code, Amount, Rate, Memo, Notes, PostingDate, DueDate, IsPosted,"
                        + " IsApproved, Count, ExternalId, PostedOn",
                    OrderBy = "Id",
                },
                new QueryCompilationOptions { Schema = LedgerFixture.Schema });

            Assert.True(result.Succeeded);
            await Compare(result.Value, QueryCorpus.All[0].Spec with
            {
                Select = result.Value.Columns[0].Text,
            });
        }

        /// <summary>Each case answers the same on the server as it does in memory.</summary>
        /// <param name="id">The case's name.</param>
        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Case_AnswersTheSameOnTheServer(string id)
        {
            QueryCase entry = QueryCorpus.All.Single(c => c.Id == id);
            InterpreterContext context = entry.HasUser ? Context : Context with { UserId = null };

            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                entry.Spec,
                new QueryCompilationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Parameters = entry.Parameters,
                    HasUser = entry.HasUser,
                    Limits = entry.Limits,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            IReadOnlyList<IReadOnlyList<QxValue>> expected = ReferenceQuery.Run(
                entry.Spec,
                context,
                entry.Parameters);

            IReadOnlyList<IReadOnlyList<QxValue>> actual = await Execute(result.Value, context);

            if (entry.Spec.OrderBy is null)
            {
                // A query that asks for no order gets none, so the two answers are the same answer
                // when they hold the same rows. Comparing them position by position would be
                // checking something the language does not promise.
                expected = Canonical(expected);
                actual = Canonical(actual);
            }

            Assert.Equal(expected.Count, actual.Count);

            // A case that matched nothing compared nothing. Two implementations agree trivially on
            // an empty answer, so a case that returns none is not evidence about anything — and it
            // becomes one silently, the moment the fixture's rows drift away from what it asks for.
            Assert.True(actual.Count > 0 || entry.MatchesNothing, id + " returned no rows");

            for (int row = 0; row < expected.Count; row++)
            {
                for (int column = 0; column < expected[row].Count; column++)
                {
                    AssertSame(
                        expected[row][column],
                        actual[row][column],
                        id + " row " + row.ToString(CultureInfo.InvariantCulture)
                            + " column " + column.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>Puts a set of rows in an order that does not depend on who produced them.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The same rows, ordered.</returns>
        private static IReadOnlyList<IReadOnlyList<QxValue>> Canonical(
            IReadOnlyList<IReadOnlyList<QxValue>> rows)
        {
            return [.. rows.OrderBy(Rendered, StringComparer.Ordinal)];
        }

        /// <summary>Renders one row so that two of them can be ordered against each other.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The rendering.</returns>
        private static string Rendered(IReadOnlyList<QxValue> row)
        {
            return string.Join(
                '\u001f',
                row.Select(value => value.IsAbsent
                    ? string.Empty
                    : value.Type == QueryexType.QxNumeric
                        ? value.AsNumber.ToString().TrimEnd('0').TrimEnd('.')
                        : value.ToString()));
        }

        /// <summary>Compares one compiled query's answers against the second implementation's.</summary>
        /// <param name="query">The compiled query.</param>
        /// <param name="spec">The query it came from.</param>
        /// <returns>A task that completes when the comparison is done.</returns>
        private async Task Compare(CompiledQuery query, QuerySpec spec)
        {
            IReadOnlyList<IReadOnlyList<QxValue>> actual = await Execute(query, Context);
            Assert.Equal(LedgerData.Rows(spec.Root).Count, actual.Count);
        }

        /// <summary>Runs one compiled query on the server.</summary>
        /// <param name="query">The compiled query.</param>
        /// <param name="context">What the values depend on.</param>
        /// <returns>The rows it returned.</returns>
        private Task<IReadOnlyList<IReadOnlyList<QxValue>>> Execute(
            CompiledQuery query,
            InterpreterContext context)
        {
            return ExecuteOn(_server, query, context);
        }

        /// <summary>Runs one compiled query on a given server.</summary>
        /// <param name="server">The server.</param>
        /// <param name="query">The compiled query.</param>
        /// <param name="context">What the values depend on.</param>
        /// <returns>The rows it returned.</returns>
        /// <remarks>Shared with the expression suite, which executes queries the same way.</remarks>
        internal static async Task<IReadOnlyList<IReadOnlyList<QxValue>>> ExecuteOn(
            SqlServerFixture server,
            CompiledQuery query,
            InterpreterContext context)
        {
            await using SqlConnection connection = await server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query.Sql;
            SqlBinding.Bind(command, query, context);

            List<IReadOnlyList<QxValue>> rows = [];
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(SqlBinding.Read(reader, query.Columns));
            }

            return rows;
        }

        /// <summary>Whether two numbers are the same number, as far as the language says.</summary>
        /// <param name="expected">What the second implementation said.</param>
        /// <param name="actual">What the server said.</param>
        /// <returns>True when they are.</returns>
        /// <remarks>
        ///     Equal outright, or equal in every digit the language promises. Division is where the
        ///     difference shows: the guarantee is six fractional digits and no more, and the two
        ///     implementations reach that guarantee by different routes — the backend's own decimal
        ///     algebra derives a scale from the operands' declared widths, which the second reading
        ///     has no reason to reproduce. Holding them to agreement past the guarantee would be
        ///     pinning a number the language never promised; holding them to less would let a
        ///     genuine disagreement through.
        /// </remarks>
        private static bool SameNumber(SqlDecimal expected, SqlDecimal actual)
        {
            if (expected.CompareTo(actual) == 0)
            {
                return true;
            }

            const int Guaranteed = 6;
            return (expected.Scale > Guaranteed || actual.Scale > Guaranteed)
                && SqlDecimal.Round(expected, Guaranteed)
                    .CompareTo(SqlDecimal.Round(actual, Guaranteed)) == 0;
        }

        /// <summary>Asserts that two values are the same value.</summary>
        /// <param name="expected">What the second implementation said.</param>
        /// <param name="actual">What the server said.</param>
        /// <param name="where">Which value it is, for the message.</param>
        /// <remarks>Shared with the expression suite, which compares values the same way.</remarks>
        internal static void AssertSame(QxValue expected, QxValue actual, string where)
        {
            Assert.True(
                expected.IsAbsent == actual.IsAbsent,
                where + ": expected " + expected + " but the server said " + actual);

            if (expected.IsAbsent)
            {
                return;
            }

            bool same = expected.Type switch
            {
                // Compared as numbers rather than as written forms: the two implementations may
                // arrive at the same number with a different number of trailing zeros, and that is
                // not a difference the language recognises.
                QueryexType.QxNumeric => SameNumber((SqlDecimal)expected.Raw!, actual.AsNumber),
                // Ordinally, not under the collation. The collation decides what the language calls
                // equal; this is asking whether the two implementations produced the same value, and
                // a case-insensitive answer to that would let a function that changed the case of
                // its result agree with one that did not.
                QueryexType.QxString =>
                    string.Equals(expected.AsText, actual.AsText, StringComparison.Ordinal),
                QueryexType.QxBool or QueryexType.QxGuid or QueryexType.QxDate
                    or QueryexType.QxDateTime or QueryexType.QxDateTimeOffset
                    or QueryexType.QxHierarchyId or QueryexType.QxGeography =>
                    Equals(expected.Raw, actual.Raw),
                _ => Equals(expected.Raw, actual.Raw),
            };

            Assert.True(same, where + ": expected " + expected + " but the server said " + actual);
        }
    }
}
