// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     Reading the root columns a schema leaves out on purpose, beside the ones it declares.
    /// </summary>
    public sealed class UndeclaredColumnTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     Each column is read on the root after the compiled items and described as text that
        ///     may be absent, with no path and no grouping.
        /// </summary>
        [Fact]
        public void EachColumn_FollowsTheCompiledItems()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Amount, Customer.Name",
                UndeclaredColumns = ["ExtrasJson", "AuditJson"],
            });

            Assert.Equal(
                """
                SELECT
                    [T].[DocumentId] AS [c0],
                    [T].[Amount] AS [c1],
                    [P1].[AgentName] AS [c2],
                    [T].[ExtrasJson] AS [c3],
                    [T].[AuditJson] AS [c4]
                FROM [gl].[Documents] AS [T]
                LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
                ;

                """.ReplaceLineEndings("\n"),
                query.Sql);

            Assert.Equal(5, query.Columns.Count);
            Assert.Equal(
                new QueryexColumn(3, "ExtrasJson", QueryexType.QxString, QueryexNullity.Nullable, null, false),
                query.Columns[3]);
            Assert.Equal(
                new QueryexColumn(4, "AuditJson", QueryexType.QxString, QueryexNullity.Nullable, null, false),
                query.Columns[4]);
        }

        /// <summary>
        ///     Nothing else changes: the joins, the parameters, and the ordering of a paged query are
        ///     those of the same query without the list.
        /// </summary>
        [Fact]
        public void NothingElse_Changes()
        {
            QuerySpec plain = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Code, Customer.Name",
                Filter = FilterTree.Leaf("Amount > 10"),
                OrderBy = "Code",
                Skip = 2,
                Take = 3,
            };

            CompiledQuery without = Compile(plain);
            CompiledQuery widened = Compile(plain with { UndeclaredColumns = ["ExtrasJson"] });

            Assert.Equal(
                without.Sql,
                widened.Sql.Replace(",\n    [T].[ExtrasJson] AS [c2]", string.Empty, StringComparison.Ordinal));
            Assert.Equal(without.Parameters, widened.Parameters);
            Assert.Equal(without.Columns, widened.Columns.Take(2));
        }

        /// <summary>A name that could not reach SQL as written is a caller error.</summary>
        /// <param name="name">The name.</param>
        [Theory]
        [InlineData("")]
        [InlineData("9Extras")]
        [InlineData("Extras Json")]
        [InlineData("Extras]Json")]
        [InlineData("[ExtrasJson]")]
        [InlineData("T.ExtrasJson")]
        [InlineData("Extras-Json")]
        [InlineData("Éxtras")]
        public void AMalformedName_IsACallerError(string name)
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    UndeclaredColumns = ["ExtrasJson", name],
                },
                Options()));

            Assert.Contains("UndeclaredColumns[1]", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>A name listed twice, ignoring case, is a caller error.</summary>
        [Fact]
        public void ADuplicateName_IsACallerError()
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    UndeclaredColumns = ["ExtrasJson", "AuditJson", "EXTRASJSON"],
                },
                Options()));

            Assert.Contains("UndeclaredColumns[2]", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>A grouped query cannot read an undeclared column.</summary>
        [Fact]
        public void AGroupedQuery_IsACallerError()
        {
            Assert.Throws<ArgumentException>(() => Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "Centre.Name, count()",
                    UndeclaredColumns = ["ExtrasJson"],
                },
                Options()));
        }

        /// <summary>The list refuses a missing name where it is supplied.</summary>
        [Fact]
        public void ANullName_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                UndeclaredColumns = [null!],
            });
        }

        /// <summary>The columns, in order, are part of what a compilation is remembered under.</summary>
        [Fact]
        public void TheColumns_ArePartOfTheCacheKey()
        {
            QuerySpec plain = new() { Root = LedgerFixture.Invoice, Select = "Id" };

            Assert.False(ProbeCacheKeys.ShareQueryEntry(plain, plain with { UndeclaredColumns = ["ExtrasJson"] }, Options()));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(
                plain with { UndeclaredColumns = ["ExtrasJson", "AuditJson"] },
                plain with { UndeclaredColumns = ["AuditJson", "ExtrasJson"] },
                Options()));
            Assert.True(ProbeCacheKeys.ShareQueryEntry(
                plain with { UndeclaredColumns = ["ExtrasJson"] },
                plain with { UndeclaredColumns = ["ExtrasJson"] },
                Options()));
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(spec, Options());
            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>The options every compilation here runs under.</summary>
        /// <returns>The options.</returns>
        private static QueryCompilationOptions Options()
        {
            return new QueryCompilationOptions
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
            };
        }
    }
}
