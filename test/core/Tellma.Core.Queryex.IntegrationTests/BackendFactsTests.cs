// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     The claims the engine makes about the backend, checked against a real one.
    /// </summary>
    /// <remarks>
    ///     Each of these is a promise the compiler relies on and cannot verify by itself: that a
    ///     result never comes back missing where the analysis said it would not, that a division
    ///     keeps its fractional digits, that nothing the engine emits changes meaning with a
    ///     connection setting, and that the zone names it resolves to are names the server has.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class BackendFactsTests(SqlServerFixture server)
    {
        /// <summary>
        ///     An expression per function whose result is declared always present, written so that
        ///     everything it reads is missing.
        /// </summary>
        private static readonly Dictionary<string, string> Totality = new(StringComparer.OrdinalIgnoreCase)
        {
            ["contains"] = "contains(Memo, Notes)",
            ["startsWith"] = "startsWith(Memo, Notes)",
            ["endsWith"] = "endsWith(Memo, Notes)",
            ["descendantOf"] = "descendantOf(Account.Concept, cast(null, 'string'))",
            ["ancestorOf"] = "ancestorOf(Account.Concept, cast(null, 'string'))",
        };

        /// <summary>The server the fixture put the rows on.</summary>
        private readonly SqlServerFixture _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>Every function that promises a result has an expression that tests the promise.</summary>
        [Fact]
        public void EveryAlwaysPresentResult_HasAWayToTestIt()
        {
            List<string> untested = [];
            foreach (ProbeFunction function in ProbeRegistry.Functions)
            {
                bool promises = function.Signatures.Any(s => s.Nullity == "AlwaysNotNull");
                if (promises && !Totality.ContainsKey(function.Name))
                {
                    untested.Add(function.Name);
                }
            }

            Assert.Empty(untested);
        }

        /// <summary>A result declared always present is always present, on real rows.</summary>
        /// <remarks>
        ///     The promise is load-bearing: guards are omitted wherever it is made, so a function
        ///     that came back missing would take rows out of a report without a word. Checked over
        ///     rows that really do carry nothing, because an absent literal folds away before the
        ///     emitted pattern is ever reached.
        /// </remarks>
        [Fact]
        public async Task EveryAlwaysPresentResult_ReallyIsPresent()
        {
            foreach ((string name, string expression) in Totality)
            {
                QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                    new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id", Filter = FilterTree.Leaf(expression) },
                    new QueryCompilationOptions
                    {
                        LanguageVersion = QueryexLanguage.Version,
                        Schema = LedgerFixture.Schema,
                    });

                Assert.True(result.Succeeded, name);

                // Written as a count of the rows the predicate keeps and the rows it does not: a
                // result that came back missing would be counted by neither, and the two would not
                // add up to the rows there are.
                IReadOnlyList<string> kept = await Counted(expression, negated: false);
                IReadOnlyList<string> dropped = await Counted(expression, negated: true);

                Assert.Equal(
                    LedgerData.Rows(LedgerFixture.Invoice).Count,
                    int.Parse(kept[0], CultureInfo.InvariantCulture)
                        + int.Parse(dropped[0], CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Division and averaging keep the fractional digits the language promises.</summary>
        [Fact]
        public async Task FractionalDigits_SurviveDivision()
        {
            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Count / CreatedById",
                    Filter = FilterTree.Leaf("Id = 1"),
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.True(result.Succeeded);

            IReadOnlyList<string> scale = await Scalar(
                result.Value,
                "SELECT CONVERT(nvarchar(20), SQL_VARIANT_PROPERTY(q.[c0], 'Scale')) FROM (");

            Assert.True(
                int.Parse(scale[0], CultureInfo.InvariantCulture) >= 6,
                "a quotient came back with only " + scale[0] + " fractional digits");
        }

        /// <summary>Nothing the engine emits changes meaning with a connection setting.</summary>
        /// <remarks>
        ///     The settings chosen are the ones that would actually change an answer: which day a
        ///     week starts on, how a written date is read, how text is joined to something missing,
        ///     and what happens when a number is rounded away.
        /// </remarks>
        [Theory]
        [InlineData("SET DATEFIRST 1; SET LANGUAGE us_english; SET DATEFORMAT mdy;")]
        [InlineData("SET DATEFIRST 7; SET LANGUAGE British; SET DATEFORMAT dmy;")]
        [InlineData("SET CONCAT_NULL_YIELDS_NULL ON; SET ANSI_WARNINGS ON; SET NUMERIC_ROUNDABORT OFF;")]
        public async Task Answers_DoNotDependOnConnectionSettings(string settings)
        {
            foreach (QueryCase entry in QueryCorpus.All)
            {
                if (entry.Diagnostics.Count > 0 || entry.Spec.OrderBy is null)
                {
                    continue;
                }

                QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                    entry.Spec,
                    new QueryCompilationOptions
                    {
                        LanguageVersion = QueryexLanguage.Version,
                        Schema = LedgerFixture.Schema,
                        Parameters = entry.Parameters,
                        HasUser = entry.HasUser,
                    });

                Assert.True(result.Succeeded, entry.Id);

                IReadOnlyList<string> plain = await Rendered(result.Value, string.Empty);
                IReadOnlyList<string> altered = await Rendered(result.Value, settings);
                Assert.Equal(plain, altered);
            }
        }

        /// <summary>Every zone the embedded table resolves to is a zone the server has.</summary>
        /// <remarks>
        ///     The table and the server are refreshed on different schedules. A name that has drifted
        ///     out of one of them is a run-time failure the first time somebody asks for that zone,
        ///     which is exactly the kind of thing that should fail here instead.
        /// </remarks>
        [Fact]
        public async Task EveryResolvedZone_ExistsOnTheServer()
        {
            IReadOnlyList<string> known = await _server.ReadColumnAsync(
                "SELECT name FROM sys.time_zone_info;");

            HashSet<string> onTheServer = [.. known];
            string[] missing = [.. QueryexProbe.BackendZoneNames.Where(name => !onTheServer.Contains(name))];

            Assert.Empty(missing);
            Assert.True(QueryexProbe.ZoneIdentifierCount > 400, "the embedded table looks truncated");
        }

        /// <summary>Reads one column of a compiled query as text, under given settings.</summary>
        /// <param name="query">The compiled query.</param>
        /// <param name="settings">The settings to apply first, or nothing.</param>
        /// <returns>One line per row.</returns>
        private async Task<IReadOnlyList<string>> Rendered(CompiledQuery query, string settings)
        {
            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = settings + "\n" + query.Sql;
            SqlBinding.Bind(command, query, InterpreterContext.Fixed with
            {
                Parameters = new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["From"] = QxValue.Date(new DateOnly(2024, 1, 1)),
                    ["To"] = QxValue.Date(new DateOnly(2025, 12, 31)),
                },
            });

            List<string> rows = [];
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                List<string> values = [];
                for (int index = 0; index < reader.FieldCount; index++)
                {
                    values.Add(reader.IsDBNull(index)
                        ? string.Empty
                        : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
                }

                rows.Add(string.Join('|', values));
            }

            return rows;
        }

        /// <summary>Counts the rows one predicate keeps, or the rows it does not.</summary>
        /// <param name="expression">The predicate.</param>
        /// <param name="negated">Whether to count the rows it does not keep.</param>
        /// <returns>The count, as text.</returns>
        private async Task<IReadOnlyList<string>> Counted(string expression, bool negated)
        {
            QueryexResult<CompiledQuery> counting = _engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "count()",
                    Filter = negated
                        ? FilterTree.Not(FilterTree.Leaf(expression))
                        : FilterTree.Leaf(expression),
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.True(counting.Succeeded, expression);
            return await Rendered(counting.Value, string.Empty);
        }

        /// <summary>Reads one scalar computed over a compiled query's first column.</summary>
        /// <param name="query">The compiled query.</param>
        /// <param name="prefix">The text that opens the wrapping query.</param>
        /// <returns>The values.</returns>
        private async Task<IReadOnlyList<string>> Scalar(CompiledQuery query, string prefix)
        {
            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = prefix + query.Sql.TrimEnd('\n', ';') + ") AS q;";
            SqlBinding.Bind(command, query, InterpreterContext.Fixed);

            List<string> values = [];
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.IsDBNull(0)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty);
            }

            return values;
        }
    }
}
