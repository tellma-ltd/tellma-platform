// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     What folding is allowed to remove, and what it is not.
    /// </summary>
    /// <remarks>
    ///     Folding is where the two failure modes meet. Removing too little leaves a statement
    ///     reading tables it does not use and ordering by values that order nothing, which the
    ///     backend refuses outright. Removing too much — or folding to the wrong identity — turns a
    ///     criterion that grants nothing into one that grants everything.
    /// </remarks>
    public sealed class FoldingTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     A connective whose operands all fold away collapses to its own identity: a
        ///     conjunction to true, a disjunction to false. The disjunction is the one that matters
        ///     — it is the shape a set of access-control criteria takes, and folding it the other
        ///     way would admit every row to a reader entitled to none.
        /// </summary>
        /// <param name="expression">The predicate.</param>
        /// <param name="expected">The condition it must compile to.</param>
        [Theory]
        [InlineData("false or false", "(1 = 0)")]
        [InlineData("false or false or false", "(1 = 0)")]
        [InlineData("(false or false) or (false or false)", "(1 = 0)")]
        [InlineData("true and true", "(1 = 1)")]
        [InlineData("true and true and true", "(1 = 1)")]
        [InlineData("true or false", "(1 = 1)")]
        [InlineData("false and true", "(1 = 0)")]
        public void AConnectiveThatFoldsAway_TakesItsOwnIdentity(string expression, string expected)
        {
            Assert.Contains("WHERE " + expected, Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(expression),
            }), StringComparison.Ordinal);
        }

        /// <summary>
        ///     A join reached only by an operand that folded away is not read by the statement, and
        ///     an unread join is not merely untidy: on a mandatory navigation it silently drops
        ///     every row whose row on the other side is missing.
        /// </summary>
        [Fact]
        public void AJoinNothingReads_IsNotEmitted()
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Centre.Name = 'x' or true"),
            });

            Assert.DoesNotContain("JOIN", sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The order of the operands does not decide it. Folding stops at the first operand that
        ///     settles the answer, so the joins an earlier operand claimed are released afterwards
        ///     rather than noted as it went.
        /// </summary>
        [Fact]
        public void AJoinNothingReads_IsNotEmittedWhicheverOperandSettlesIt()
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("true or Centre.Name = 'x'"),
            });

            Assert.DoesNotContain("JOIN", sql, StringComparison.Ordinal);
        }

        /// <summary>A parameter nothing binds against is not declared either.</summary>
        [Fact]
        public void AParameterNothingReads_IsNotBound()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Code = 'x' or true"),
            });

            Assert.Empty(query.Parameters);
        }

        /// <summary>A join something does read is still emitted, so pruning is not simply removal.</summary>
        [Fact]
        public void AJoinSomethingReads_SurvivesPruning()
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Centre.Name = 'x' and true"),
            });

            Assert.Contains("JOIN [gl].[Segments] AS [P1]", sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An ordering term that reads nothing per row orders nothing. Written out it is worse
        ///     than useless: the backend refuses a bare parameter in an ordering position, and
        ///     refuses a constant one outright.
        /// </summary>
        /// <param name="orderBy">The ordering clause.</param>
        [Theory]
        [InlineData("1")]
        [InlineData("'a'")]
        [InlineData("Id is null or true")]
        [InlineData("1 + 2 desc")]
        public void AnOrderingTermThatOrdersNothing_IsDropped(string orderBy)
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                OrderBy = orderBy,
            });

            Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
        }

        /// <summary>An ordering term that does order something is kept.</summary>
        [Fact]
        public void AnOrderingTermThatOrdersSomething_IsKept()
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                OrderBy = "Code desc",
            });

            Assert.Contains("ORDER BY [T].[DocCode] DESC", sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A grouping key keeps reading its columns whatever its answer turns out to be. The
        ///     backend refuses a grouping expression that reads none, and dropping the key instead
        ///     would make a grouped query over no rows return one row rather than none — which is
        ///     exactly the case the nullity analysis promises an aggregation cannot be absent in.
        /// </summary>
        /// <param name="select">The select list.</param>
        /// <param name="key">The expression the grouping must still contain.</param>
        [Theory]
        [InlineData("Amount is null, sum(Amount)", "[T].[Amount] IS NULL")]
        [InlineData("Code = 'x', sum(Amount)", "[T].[DocCode] = @qx0_p0")]
        [InlineData("Code is null, sum(Amount)", "[T].[DocCode] IS NULL")]
        public void AGroupingKeyThatWouldFold_KeepsReadingItsColumns(string select, string key)
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = select,
                Aggregate = true,
            });

            int grouping = sql.IndexOf("GROUP BY", StringComparison.Ordinal);
            Assert.True(grouping >= 0, sql);
            Assert.Contains(key, sql[grouping..], StringComparison.Ordinal);
        }

        /// <summary>
        ///     The grouped select item and the grouping key are the same expression, because the
        ///     backend requires a non-aggregated item to appear in the grouping exactly as written.
        /// </summary>
        [Fact]
        public void AGroupingKey_IsWrittenIdenticallyInBothPlaces()
        {
            string sql = Sql(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Amount is null, sum(Amount)",
                Aggregate = true,
            });

            const string Key = "CASE WHEN ([T].[Amount] IS NULL) THEN 1 ELSE 0 END";
            Assert.Contains(Key + " AS [c0]", sql, StringComparison.Ordinal);
            Assert.Contains("GROUP BY " + Key, sql, StringComparison.Ordinal);
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                spec,
                new QueryCompilationOptions { Schema = LedgerFixture.Schema });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>Compiles a query and returns the statement it produced.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The SQL.</returns>
        private static string Sql(QuerySpec spec)
        {
            return Compile(spec).Sql;
        }
    }
}
