// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Compiled statements embedded in SQL of a host's own, the way a host counts, captures keys
    ///     and composes: prologue first, then the body where a derived table or an insert source goes.
    /// </summary>
    /// <remarks>
    ///     A snapshot shows that the body is the text it should be; only a server says that the text
    ///     is a valid derived table, which is the promise a host builds a capped count or a key
    ///     capture on.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class EmbeddingTests(LedgerDatabase server)
    {
        /// <summary>The server the fixture put the rows on.</summary>
        private readonly LedgerDatabase _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>An unordered statement counts as a derived table, to the rows it returns.</summary>
        [Fact]
        public async Task AnUnorderedBody_CountsAsADerivedTable()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Customer.Name",
                Filter = FilterTree.Leaf("Amount > 0 or IsPosted"),
            };

            await AssertCountMatches(spec);
        }

        /// <summary>
        ///     An ordered statement that pages, and declares a lookup ahead of itself, counts as a
        ///     derived table too: the backend accepts an ordering there because the statement offsets.
        /// </summary>
        [Fact]
        public async Task APagedBodyWithAPrologue_CountsAsADerivedTable()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Code",
                Filter = FilterTree.Leaf("descendantOf(Account.Concept, 'Assets') or Amount > 0"),
                OrderBy = "PostingDate desc",
                Skip = 1,
                Take = 3,
            };

            await AssertCountMatches(spec);
        }

        /// <summary>
        ///     A statement planned afresh runs on its own with its hint, and counts as a derived table
        ///     once the host writes the hint into its own statement's one clause, beside its own hint.
        /// </summary>
        /// <param name="hint">The clause the host's statement carries.</param>
        /// <returns>A task that completes when both runs have been compared.</returns>
        [Theory]
        [InlineData("OPTION (RECOMPILE)")]
        [InlineData("OPTION (RECOMPILE, MAXRECURSION 32)")]
        public async Task ABodyPlannedAfresh_CountsWithTheHostsHint(string hint)
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("PostingDate >= startOfYear(today(), 'uq')"),
            };

            CompiledQuery query = Compile(spec, batchOrdinal: 0);
            Assert.True(query.Recompile);

            IReadOnlyList<IReadOnlyList<QxValue>> alone =
                await DifferentialTests.ExecuteOn(_server, query, InterpreterContext.Fixed);
            Assert.Equal(ReferenceQuery.Run(spec, InterpreterContext.Fixed).Count, alone.Count);

            await AssertCountMatches(spec, hint);
        }

        /// <summary>
        ///     Keys captured into a table by one compiled statement restrict a second compiled
        ///     statement in the same batch, each written prologue first.
        /// </summary>
        [Fact]
        public async Task CapturedKeys_RestrictASecondStatement()
        {
            QuerySpec capture = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("descendantOf(Account.Concept, 'Assets')"),
            };

            QuerySpec restricted = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Code",
                Filter = FilterTree.Leaf("IsPosted"),
                Restrictions = [new KeySetRestriction("Id", "@tb0_keys")],
            };

            CompiledQuery first = Compile(capture, batchOrdinal: 0);
            CompiledQuery second = Compile(restricted, batchOrdinal: 1);

            string batch = "DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);\n"
                + first.Prologue
                + "INSERT INTO @tb0_keys ([Id])\n" + first.Body + ";\n"
                + second.Prologue
                + second.Body + ";\n";

            List<string> codes = [];
            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = batch;
            SqlBinding.Bind(command, first, InterpreterContext.Fixed);
            SqlBinding.Bind(command, second, InterpreterContext.Fixed);

            await using (SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    codes.Add(reader.GetString(0));
                }
            }

            // The same answer read from memory: the posted rows among those the capture kept.
            HashSet<int> captured =
            [
                .. ReferenceQuery.Run(capture, InterpreterContext.Fixed)
                    .Select(row => int.Parse(row[0].AsNumber.ToString(), CultureInfo.InvariantCulture)),
            ];

            KeySet keys = new(QueryexStoreType.QxInt, [.. captured.Select(key => QxValue.Number(key))]);
            IReadOnlyList<IReadOnlyList<QxValue>> expected = ReferenceQuery.Run(
                restricted,
                InterpreterContext.Fixed with
                {
                    KeySets = new Dictionary<string, KeySet>(StringComparer.OrdinalIgnoreCase) { ["@tb0_keys"] = keys },
                });

            Assert.NotEmpty(captured);
            Assert.NotEmpty(codes);
            Assert.Equal(
                expected.Select(row => row[0].AsText).Order(StringComparer.Ordinal),
                codes.Order(StringComparer.Ordinal));
        }

        /// <summary>Counts a compiled statement's rows from outside it and compares the count.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="hint">The clause the counting statement carries, or nothing.</param>
        /// <returns>A task that completes when the comparison is done.</returns>
        private async Task AssertCountMatches(QuerySpec spec, string hint = "")
        {
            CompiledQuery query = Compile(spec, batchOrdinal: 0);

            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query.Prologue + "SELECT COUNT_BIG(*) FROM (\n" + query.Body + ") AS [q]\n" + hint + ";\n";
            SqlBinding.Bind(command, query, InterpreterContext.Fixed);

            long counted = (long)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            int expected = ReferenceQuery.Run(spec, InterpreterContext.Fixed).Count;

            Assert.True(expected > 0, "the case matched no rows, so the count compared nothing");
            Assert.Equal(expected, counted);
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="batchOrdinal">The query's position among those executed together.</param>
        /// <returns>The compiled query.</returns>
        private CompiledQuery Compile(QuerySpec spec, int batchOrdinal)
        {
            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    BatchOrdinal = batchOrdinal,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }
    }
}
