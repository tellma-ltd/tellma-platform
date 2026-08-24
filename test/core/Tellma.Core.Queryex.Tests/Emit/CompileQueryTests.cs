// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>Compiles whole queries and pins the SQL they produce.</summary>
    public sealed class CompileQueryTests
    {
        /// <summary>The engine under test, shared because it is stateless apart from its caches.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Compiles a query and returns it, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="parameters">The declared parameters.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(
            QuerySpec spec,
            params QueryexParameterDeclaration[] parameters)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Parameters = parameters,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>A bare projection reads columns of the root and nothing else.</summary>
        [Fact]
        public void Projection_ReadsTheRootAlone()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Code",
            });

            Assert.Equal(
                """
                SELECT
                    [T].[DocumentId] AS [c0],
                    [T].[DocCode] AS [c1]
                FROM [gl].[Documents] AS [T]
                ;

                """.ReplaceLineEndings("\n"),
                query.Sql);

            Assert.Empty(query.Parameters);
            Assert.Equal(["Id"], query.Columns[0].Path);
        }

        /// <summary>A navigation becomes one join, whose kind follows the foreign key.</summary>
        [Fact]
        public void Navigation_BecomesOneJoinOfTheRightKind()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Name, Centre.Name",
            });

            Assert.Equal(
                """
                SELECT
                    [P1].[AgentName] AS [c0],
                    [P2].[SegmentName] AS [c1]
                FROM [gl].[Documents] AS [T]
                LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
                INNER JOIN [gl].[Segments] AS [P2] ON [P2].[SegmentId] = [T].[SegmentId]
                ;

                """.ReplaceLineEndings("\n"),
                query.Sql);
        }

        /// <summary>A comparison against a column that cannot be absent needs no guard.</summary>
        [Fact]
        public void Comparison_OverPresentOperands_NeedsNoGuard()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Amount > 100"),
            });

            Assert.Contains("WHERE ([T].[Amount] > @qx0_p0)\n", query.Sql, StringComparison.Ordinal);
            Assert.Equal(QueryexStoreType.QxDecimal(3, 0), query.Parameters[0].StoreType);
        }

        /// <summary>A comparison against a column that may be absent is guarded.</summary>
        [Fact]
        public void Comparison_OverAbsentableOperand_IsGuarded()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Rate = 1.5"),
            });

            Assert.Contains(
                "WHERE (([T].[Rate] IS NOT NULL) AND ([T].[Rate] = @qx0_p0))\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>Two columns that may both be absent compare equal when both are.</summary>
        [Fact]
        public void Comparison_OverTwoAbsentableColumns_TreatsAbsenceAsEqual()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Memo = Notes"),
            });

            Assert.Contains("IS NULL", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>A written value binds in the family of the column it is compared against.</summary>
        [Fact]
        public void Literal_AdoptsTheComparedColumnsFamily()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Code = 'INV-1'"),
            });

            Assert.Equal(QueryexStoreType.QxVarChar(8000), query.Parameters[0].StoreType);
        }

        /// <summary>A truth-valued column read as a predicate compares against one.</summary>
        [Fact]
        public void BoolColumn_InPredicatePosition_ComparesAgainstOne()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("IsPosted"),
            });

            Assert.Contains("WHERE ([T].[IsPosted] = 1)\n", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>A truth-valued column that may be absent is tested for presence first.</summary>
        [Fact]
        public void AbsentableBoolColumn_InPredicatePosition_TestsPresence()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("IsApproved"),
            });

            Assert.Contains(
                "WHERE ([T].[IsApproved] IS NOT NULL AND [T].[IsApproved] = 1)\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>A grouped query derives its keys from the select list.</summary>
        [Fact]
        public void Aggregate_DerivesGroupingKeysFromTheSelectList()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount), count()",
            });

            Assert.Contains("GROUP BY [P1].[SegmentName]\n", query.Sql, StringComparison.Ordinal);
            Assert.Contains("SUM([T].[Amount]) AS [c1]", query.Sql, StringComparison.Ordinal);
            Assert.Contains("COUNT_BIG(*) AS [c2]", query.Sql, StringComparison.Ordinal);
            Assert.True(query.Columns[0].IsGroupingKey);
            Assert.False(query.Columns[1].IsGroupingKey);
        }

        /// <summary>A total over whole numbers is widened before it is taken.</summary>
        [Fact]
        public void Sum_OverWholeNumbers_Widens()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "sum(Count)",
            });

            Assert.Contains(
                "SUM(CAST([T].[LineCount] AS decimal(38, 0)))",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>Dividing two whole numbers widens one side so the remainder survives.</summary>
        [Fact]
        public void Division_OverWholeNumbers_Widens()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Count / CreatedById",
            });

            Assert.Contains(
                "(CAST([T].[LineCount] AS decimal(19, 0)) / [T].[CreatedById])",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>A paged query appends the root key so its pages do not overlap.</summary>
        [Fact]
        public void Paging_AppendsTheRootKey()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code",
                OrderBy = "PostingDate desc",
                Skip = 20,
                Take = 10,
            });

            Assert.Contains(
                "ORDER BY [T].[PostingDate] DESC, [T].[DocumentId] ASC\n",
                query.Sql,
                StringComparison.Ordinal);

            Assert.Contains(
                "OFFSET @qx0_p0 ROWS FETCH NEXT @qx0_p1 ROWS ONLY\n",
                query.Sql,
                StringComparison.Ordinal);

            Assert.Equal(QueryexStoreType.QxInt, query.Parameters[0].StoreType);
        }

        /// <summary>An ordering term that repeats a select item orders by that item's name.</summary>
        [Fact]
        public void Ordering_ThatRepeatsASelectItem_UsesItsName()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code, PostingDate",
                OrderBy = "PostingDate desc",
            });

            Assert.Contains("ORDER BY [c1] DESC\n", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>A guard that needs an operand twice gives it a name first.</summary>
        [Fact]
        public void Guard_OverComputedOperand_BindsIt()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Rate + 1 > 2"),
            });

            Assert.Contains("CROSS APPLY (VALUES (", query.Sql, StringComparison.Ordinal);
            Assert.Contains("([B1].[v] IS NOT NULL)", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>A hierarchy test looks its node up once, before the statement runs.</summary>
        [Fact]
        public void HierarchyTest_HoistsItsLookup()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("descendantOf(Account.Concept, 'Assets')"),
            });

            Assert.StartsWith(
                "DECLARE @qx0_v0 hierarchyid = (SELECT [x].[TreeNode] FROM [gl].[Accounts] AS [x]"
                    + " WHERE [x].[Concept] = @qx0_p0);\n",
                query.Sql,
                StringComparison.Ordinal);

            Assert.Contains(
                "[P1].[TreeNode].IsDescendantOf(@qx0_v0) = 1",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>A composed filter conjoins whole trees rather than text.</summary>
        [Fact]
        public void FilterTree_ComposesWithoutRebindingPrecedence()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("IsPosted or Amount > 0"),
                    FilterTree.Leaf("Count > 1"),
                ]),
            });

            Assert.Contains(
                "WHERE ((([T].[IsPosted] = 1) OR ([T].[Amount] > @qx0_p0))"
                    + " AND ([T].[LineCount] > @qx0_p1))\n",
                query.Sql,
                StringComparison.Ordinal);

            Assert.Equal(2, query.Parameters.Count);
        }

        /// <summary>An empty set of permissions denies rather than grants.</summary>
        [Fact]
        public void FilterTree_EmptyDisjunction_Denies()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Or([]),
            });

            Assert.Contains("WHERE (1 = 0)\n", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>A membership test over present values stays a single seekable list.</summary>
        [Fact]
        public void Membership_OverPresentElements_StaysOneList()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Count in (1, 2, 3)"),
            });

            Assert.Contains(
                "WHERE ([T].[LineCount] IN (@qx0_p0, @qx0_p1, @qx0_p2))\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>An absent element keeps the rest of the list seekable.</summary>
        [Fact]
        public void Membership_WithAnAbsentElement_KeepsTheListSeekable()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Rate in (1, 2, null)"),
            });

            Assert.Contains("IN (@qx0_p0, @qx0_p1)", query.Sql, StringComparison.Ordinal);
            Assert.Contains("([T].[Rate] IS NULL)", query.Sql, StringComparison.Ordinal);
        }

        /// <summary>The same value written twice becomes one parameter.</summary>
        [Fact]
        public void Literals_ThatAreEqual_ShareOneSlot()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Count > 5 and CreatedById > 5"),
            });

            Assert.Single(query.Parameters);
        }

        /// <summary>A declared parameter carries its name to the host.</summary>
        [Fact]
        public void DeclaredParameter_CarriesItsName()
        {
            CompiledQuery query = Compile(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Leaf("PostingDate >= @From"),
                },
                new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: true));

            Assert.Equal(QueryexParameterOrigin.Declared, query.Parameters[0].Origin);
            Assert.Equal("From", query.Parameters[0].DeclaredName);
        }

        /// <summary>Paging without an ordering is refused.</summary>
        [Fact]
        public void Paging_WithoutOrdering_IsRefused()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Take = 10,
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.False(result.Succeeded);
            Assert.Equal("QX4006", result.Diagnostics[0].Code);
        }

        /// <summary>An ordering term that would widen the grouping is refused.</summary>
        [Fact]
        public void Ordering_ThatWouldWidenTheGrouping_IsRefused()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "Centre.Name, sum(Amount)",
                    OrderBy = "Customer.Name",
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.False(result.Succeeded);
            Assert.Equal("QX4005", result.Diagnostics[0].Code);
        }

        /// <summary>The same ordering term twice is refused.</summary>
        [Fact]
        public void Ordering_Repeated_IsRefused()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    OrderBy = "PostingDate, PostingDate desc",
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.False(result.Succeeded);
            Assert.Equal("QX4008", result.Diagnostics[0].Code);
        }

        /// <summary>The same query compiles to the same bytes, however many times it is asked for.</summary>
        [Fact]
        public void Compilation_IsDeterministic()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Name, sum(Amount)",
                Aggregate = true,
                Filter = FilterTree.Leaf("PostingDate >= '2024-01-01'"),
                OrderBy = "sum(Amount) desc",
            };

            string first = Compile(spec).Sql;
            string second = new QueryexEngine().CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                }).Value!.Sql;

            Assert.Equal(first, second);
        }

        /// <summary>The batch ordinal keeps two compiled queries' names apart.</summary>
        [Fact]
        public void BatchOrdinal_NamespacesEveryEmittedName()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Leaf("Amount > 100"),
                },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    BatchOrdinal = 3,
                });

            Assert.True(result.Succeeded);
            Assert.Equal("@qx3_p0", result.Value.Parameters[0].Name);
        }
    }
}
