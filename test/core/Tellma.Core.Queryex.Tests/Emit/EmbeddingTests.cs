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
            Assert.False(query.Recompile);
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

            // A node's key bounds a range the backend estimates well enough without knowing it, so
            // a statement whose only declarations are keys is planned once like any other.
            Assert.False(query.Recompile);
            Assert.Equal(query.Prologue + query.Body + ";\n", query.Sql);
        }

        /// <summary>
        ///     A filter that reads a date computed in the prologue has the statement planned afresh:
        ///     the hint follows the body, never inside it, so the body stays embeddable.
        /// </summary>
        /// <param name="filter">The row filter, or null.</param>
        /// <param name="having">The group filter, or null.</param>
        [Theory]
        [InlineData("PostingDate >= startOfMonth(today(), 'uq')", null)]
        [InlineData("PostingDate >= startOfMonth(today(), 'uq') or Amount > 0", null)]
        [InlineData(null, "max(PostingDate) >= startOfYear(today(), 'uq')")]
        [InlineData("diffMonths(PostingDate, startOfYear(today(), 'uq'), 'uq') >= 1", null)]
        [InlineData("PostingDate >= addMonths(addDays(startOfYear(today(), 'uq'), 1), 1, 'et')", null)]
        public void AComputedValueInAFilter_IsPlannedAfresh(string? filter, string? having)
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = having is not null,
                Select = having is null ? "Id" : "Centre.Name, count()",
                Filter = filter is null ? null : FilterTree.Leaf(filter),
                Having = having is null ? null : FilterTree.Leaf(having),
            });

            Assert.True(query.Recompile);
            Assert.StartsWith("DECLARE @qx0_v0 date = ", query.Prologue, StringComparison.Ordinal);
            Assert.DoesNotContain("OPTION", query.Body, StringComparison.Ordinal);
            Assert.Equal(query.Prologue + query.Body + "OPTION (RECOMPILE)\n;\n", query.Sql);
        }

        /// <summary>A filter inside a semi-join is a filter too.</summary>
        [Fact]
        public void AComputedValueInASemiJoin_IsPlannedAfresh()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists(
                    "InvoiceLine",
                    "Invoice",
                    FilterTree.Leaf("Invoice.PostingDate >= startOfMonth(today(), 'uq')")),
            });

            Assert.True(query.Recompile);
            Assert.EndsWith("OPTION (RECOMPILE)\n;\n", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A date computed in the prologue but read only where no estimate depends on it, or not
        ///     read at all once the filter folds, leaves the statement planned once.
        /// </summary>
        /// <param name="select">The select list.</param>
        /// <param name="filter">The row filter, or null.</param>
        /// <param name="aggregate">Whether the query groups.</param>
        [Theory]
        [InlineData("Id, diffDays(startOfYear(today(), 'uq'), PostingDate)", null, false)]
        [InlineData("sum(Amount, PostingDate >= startOfMonth(today(), 'uq'))", null, true)]
        [InlineData("if(PostingDate >= startOfQuarter(today(), 'uq'), 'this quarter', 'earlier'), count()", null, true)]
        [InlineData("Id", "true or PostingDate >= startOfMonth(today(), 'uq')", false)]
        public void AComputedValueOutsideAFilter_IsPlannedOnce(string select, string? filter, bool aggregate)
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = aggregate,
                Select = select,
                Filter = filter is null ? null : FilterTree.Leaf(filter),
            });

            Assert.False(query.Recompile);
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
