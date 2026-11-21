// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     The paging slots: fixed names, last in the parameter list, and out of what a compilation
    ///     is remembered under.
    /// </summary>
    public sealed class PagingTests
    {
        /// <summary>A paged query over the fixture, at a given page.</summary>
        /// <param name="skip">Rows to skip.</param>
        /// <param name="take">Rows to take.</param>
        /// <returns>The query.</returns>
        private static QuerySpec Page(int? skip, int? take)
        {
            return new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code",
                Filter = FilterTree.Leaf("Amount > 25 and Count != 25"),
                OrderBy = "PostingDate desc",
                Skip = skip,
                Take = take,
            };
        }

        /// <summary>
        ///     The two slots are named for what they hold, listed after every other slot, skip before
        ///     take, and never shared with an equal literal.
        /// </summary>
        [Fact]
        public void TheSlots_AreNamedAndLast()
        {
            CompiledQuery query = Compile(new QueryexEngine(), Page(25, 25), batchOrdinal: 2);

            Assert.Equal(
                ["@qx2_p0", "@qx2_skip", "@qx2_take"],
                query.Parameters.Select(slot => slot.Name));

            QueryexParameterSlot skip = query.Parameters[1];
            QueryexParameterSlot take = query.Parameters[2];
            Assert.Equal(QueryexParameterOrigin.Literal, skip.Origin);
            Assert.Equal(QueryexStoreType.QxInt, skip.StoreType);
            Assert.Equal(QueryexType.QxNumeric, skip.Type);
            Assert.Equal(25, skip.Value);
            Assert.Equal(25, take.Value);
            Assert.Contains(
                "OFFSET @qx2_skip ROWS FETCH NEXT @qx2_take ROWS ONLY\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>Only the slots a query asks for exist, and taking without skipping starts at zero.</summary>
        [Fact]
        public void OnlyThePresentSlots_Exist()
        {
            QueryexEngine engine = new();

            CompiledQuery taking = Compile(engine, Page(null, 5));
            Assert.Equal("@qx0_take", taking.Parameters[^1].Name);
            Assert.DoesNotContain(taking.Parameters, slot => slot.Name.EndsWith("_skip", StringComparison.Ordinal));
            Assert.Contains("OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY\n", taking.Sql, StringComparison.Ordinal);

            CompiledQuery skipping = Compile(engine, Page(5, null));
            Assert.Equal("@qx0_skip", skipping.Parameters[^1].Name);
            Assert.DoesNotContain(skipping.Parameters, slot => slot.Name.EndsWith("_take", StringComparison.Ordinal));
            Assert.Contains("OFFSET @qx0_skip ROWS\n", skipping.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Two pages of one query are one compilation: the second is served from the cache with
        ///     the same SQL, and each caller still gets its own page.
        /// </summary>
        [Fact]
        public void TwoPages_ShareOneCompilation()
        {
            QueryexEngine engine = new();

            Assert.True(ProbeCacheKeys.ShareQueryEntry(Page(0, 10), Page(10, 10), Options()));

            CompiledQuery first = Compile(engine, Page(0, 10));
            CompiledQuery second = Compile(engine, Page(10, 10));
            CompiledQuery firstAgain = Compile(engine, Page(0, 10));

            // The very same text: the second call found the first one's compilation.
            Assert.Same(first.Sql, second.Sql);
            Assert.Same(first.Body, second.Body);
            Assert.Same(first.Columns, second.Columns);

            Assert.Equal([0, 10], first.Parameters.TakeLast(2).Select(slot => slot.Value));
            Assert.Equal([10, 10], second.Parameters.TakeLast(2).Select(slot => slot.Value));
            Assert.Equal([0, 10], firstAgain.Parameters.TakeLast(2).Select(slot => slot.Value));

            // Everything before the paging slots is the cached compilation's own.
            Assert.Equal(first.Parameters.SkipLast(2), second.Parameters.SkipLast(2));
        }

        /// <summary>Whether a query pages, and how, is still part of what it is remembered under.</summary>
        [Fact]
        public void ThePresenceOfPaging_IsPartOfTheKey()
        {
            Assert.False(ProbeCacheKeys.ShareQueryEntry(Page(null, null), Page(0, 10), Options()));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(Page(5, null), Page(null, 5), Options()));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(Page(5, null), Page(5, 5), Options()));
            Assert.True(ProbeCacheKeys.ShareQueryEntry(Page(null, 3), Page(null, 7), Options()));
        }

        /// <summary>A hit re-stamps only the paging slots, even when a literal has the same value.</summary>
        [Fact]
        public void AHit_RestampsOnlyThePagingSlots()
        {
            QueryexEngine engine = new();
            Compile(engine, Page(25, 25));
            CompiledQuery hit = Compile(engine, Page(1, 2));

            Assert.Equal(
                [("@qx0_p0", (object?)new System.Data.SqlTypes.SqlDecimal(25)), ("@qx0_skip", 1), ("@qx0_take", 2)],
                hit.Parameters.Select(slot => (slot.Name, slot.Value)));
        }

        /// <summary>The paging slots count toward the parameter ceiling like every other slot.</summary>
        [Fact]
        public void ThePagingSlots_CountTowardTheCeiling()
        {
            QueryexResult<CompiledQuery> result = new QueryexEngine().CompileQuery(
                Page(0, 10),
                Options() with { Limits = QueryexLimits.Default with { MaxParameters = 2 } });

            Assert.False(result.Succeeded);
            Assert.Equal("QX5007", Assert.Single(result.Diagnostics).Code);
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="engine">The engine.</param>
        /// <param name="spec">The query.</param>
        /// <param name="batchOrdinal">The query's position among those executed together.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QueryexEngine engine, QuerySpec spec, int batchOrdinal = 0)
        {
            QueryexResult<CompiledQuery> result = engine.CompileQuery(
                spec,
                Options() with { BatchOrdinal = batchOrdinal });

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
