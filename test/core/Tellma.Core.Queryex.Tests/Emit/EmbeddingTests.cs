// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     The two parts a host embeds a compiled statement in its own SQL with: the declarations it
    ///     depends on, and the statement without its terminator.
    /// </summary>
    /// <remarks>
    ///     Every stored snapshot checks that the parts make up the batch. These pin what each part
    ///     holds when there is something to declare and when there is not.
    /// </remarks>
    public sealed class EmbeddingTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>A statement that declares nothing has an empty prologue and a body that is the batch.</summary>
        [Fact]
        public void NothingDeclared_LeavesThePrologueEmpty()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Amount > 0"),
            });

            Assert.Equal(string.Empty, query.Prologue);
            Assert.Equal(
                """
                SELECT
                    [T].[DocumentId] AS [c0]
                FROM [gl].[Documents] AS [T]
                WHERE ([T].[Amount] > @qx0_p0)

                """.ReplaceLineEndings("\n"),
                query.Body);
            Assert.Equal(query.Body + ";\n", query.Sql);
        }

        /// <summary>
        ///     A statement that hoists lookups declares them in the prologue, followed by one empty
        ///     line, and its body reads them.
        /// </summary>
        [Fact]
        public void HoistedLookups_AreThePrologue()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("descendantOf(Account.Concept, 'Assets')"),
                OrderBy = "Id",
                Take = 10,
            });

            Assert.Equal(
                "DECLARE @qx0_v0 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x] WHERE [x].[Concept] = @qx0_p0);\n\n",
                query.Prologue);
            Assert.StartsWith("SELECT\n", query.Body, StringComparison.Ordinal);
            Assert.Contains("@qx0_v0", query.Body, StringComparison.Ordinal);
            Assert.EndsWith("FETCH NEXT @qx0_take ROWS ONLY\n", query.Body, StringComparison.Ordinal);
            Assert.Equal(query.Prologue + query.Body + ";\n", query.Sql);
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }
    }
}
