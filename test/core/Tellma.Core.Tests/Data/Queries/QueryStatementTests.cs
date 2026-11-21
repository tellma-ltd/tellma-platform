// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Queries
{
    /// <summary>
    ///     The query statements a batch composes, as golden SQL: a rows page with its key capture,
    ///     capped count and ancestors, a count, an assertion, a constant statement and an entity
    ///     query with its collections; the binding of arguments; and the refusals at compose time.
    /// </summary>
    public sealed class QueryStatementTests : IDisposable
    {
        /// <summary>The composition over the query model.</summary>
        private readonly BatchHarness _harness = new(compose: QueryModel.Compose);

        [Fact]
        public void A_rows_query_is_one_compiled_statement_with_its_slots_bound()
        {
            DataBatch batch = _harness.Batch();

            _ = batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name, Price", Filter = FilterTree.Leaf("Price > 5"), OrderBy = "Name", Take = 20 }, null, null);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                SELECT
                    [T].[Name] AS [c0],
                    [T].[Price] AS [c1]
                FROM [qx].[Gadgets] AS [T]
                WHERE (([T].[Price] IS NOT NULL) AND ([T].[Price] > @qx0_p0))
                ORDER BY [c0] ASC, [T].[Id] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                ;

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(typeof(Gadget), statement.EntityType);
            Assert.Equal(["@qx0_p0", "@qx0_take"], statement.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal(20, Scalar(statement, "@qx0_take").Value);
        }

        [Fact]
        public void Key_capture_count_and_page_are_three_statements_and_the_page_reads_the_captured_keys()
        {
            DataBatch batch = _harness.Batch();

            BatchResult<RowPage> page = batch.Rows(
                new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name, Price", Filter = FilterTree.Leaf("Price > 5 and DueOn < today()"), OrderBy = "Name", Skip = 10, Take = 20 },
                null,
                new RowQueryOptions { CountCap = 100, CaptureKeys = true });

            Assert.Equal(3, batch.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE ((([T].[Price] IS NOT NULL) AND ([T].[Price] > @qx0_p0)) AND (([T].[DueOn] IS NOT NULL) AND ([T].[DueOn] < @qx0_p1)))
                ORDER BY [T].[Name] ASC, [c0] ASC
                OFFSET @qx0_skip ROWS FETCH NEXT @qx0_take ROWS ONLY
                ;

                """,
                batch.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                """
                SELECT
                    [T].[Name] AS [c0],
                    [T].[Price] AS [c1]
                FROM [qx].[Gadgets] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_keys))
                ORDER BY [c0] ASC, [T].[Id] ASC
                OFFSET @qx1_skip ROWS
                ;

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                """
                SELECT COUNT(*) AS [Count] FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE ((([T].[Price] IS NOT NULL) AND ([T].[Price] > @qx2_p0)) AND (([T].[DueOn] IS NOT NULL) AND ([T].[DueOn] < @qx2_p1)))
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx2_take ROWS ONLY
                ) AS [q]
                ;

                """,
                batch.Statements[2].Text,
                ignoreLineEndingDifferences: true);

            // The page binds only its zero skip; the twin and the count bind today() from the batch's context.
            Assert.Equal(0, Assert.IsType<ScalarParameter>(Assert.Single(batch.Statements[1].Parameters)).Value);
            Assert.Equal(_harness.Context.Today.ToDateTime(TimeOnly.MinValue), Scalar(batch.Statements[0], "@qx0_p1").Value);
            Assert.Equal(101, Scalar(batch.Statements[2], "@qx2_take").Value);
            Assert.Equal(new SqlIdentifier("@tb0_keys"), batch.KeysOf(page));
        }

        [Fact]
        public void A_later_statement_restricts_on_captured_keys_and_its_own_twin_carries_the_restriction()
        {
            DataBatch batch = _harness.Batch();
            BatchResult<RowPage> roots = batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name" }, null, new RowQueryOptions { CaptureKeys = true });
            SqlIdentifier keys = Assert.IsType<SqlIdentifier>(batch.KeysOf(roots));

            BatchResult<RowPage> parts = batch.Rows(
                new QuerySpec { Root = Entity("qx.GadgetPart"), Select = "Ordinal", Restrictions = [new KeySetRestriction("GadgetId", keys.Name)] },
                null,
                new RowQueryOptions { CaptureKeys = true });

            Assert.Equal(4, batch.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb2_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb2_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[GadgetParts] AS [T]
                WHERE ([T].[GadgetId] IN (SELECT [Id] FROM @tb0_keys))
                ;

                """,
                batch.Statements[2].Text,
                ignoreLineEndingDifferences: true);
            Assert.Contains("WHERE ([T].[Id] IN (SELECT [Id] FROM @tb2_keys))", batch.Statements[3].Text, StringComparison.Ordinal);
            Assert.Equal(new SqlIdentifier("@tb2_keys"), batch.KeysOf(parts));

            // A raw statement may name a captured table.
            _ = batch.Sql($"SELECT COUNT(*) FROM {keys};", new SqlOptions(resultSets: 1));
            Assert.Equal("SELECT COUNT(*) FROM @tb0_keys;", batch.Statements[^1].Text);
        }

        [Fact]
        public void Ancestors_climb_from_the_captured_page_and_read_under_their_own_filter()
        {
            DataBatch batch = _harness.Batch();

            _ = batch.Rows(
                new QuerySpec { Root = Entity("qx.Zone"), Select = "Code, Name", Filter = FilterTree.Leaf("Code = 'x'"), OrderBy = "Code", Take = 20 },
                null,
                new RowQueryOptions { IncludeAncestors = true, AncestorsFilter = FilterTree.Leaf("IsActive") });

            Assert.Equal(3, batch.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb2_anc TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb2_anc ([Id])
                SELECT [a].[Id] FROM [qx].[Zones] AS [a]
                WHERE [a].[Id] NOT IN (SELECT [Id] FROM @tb0_keys)
                  AND EXISTS (SELECT 1 FROM [qx].[Zones] AS [p] JOIN @tb0_keys AS [k] ON [k].[Id] = [p].[Id]
                              WHERE [p].[Node].IsDescendantOf([a].[Node]) = 1 AND [p].[Id] <> [a].[Id]);
                SELECT
                    [T].[Code] AS [c0],
                    [T].[Name] AS [c1]
                FROM [qx].[Zones] AS [T]
                WHERE (([T].[IsActive] = 1) AND ([T].[Id] IN (SELECT [Id] FROM @tb2_anc)))
                ORDER BY [c0] ASC, [T].[Id] ASC
                OFFSET @qx2_skip ROWS
                ;

                """,
                batch.Statements[2].Text,
                ignoreLineEndingDifferences: true);

            // The user's filter narrows the page, never the ancestors, which bind only their zero skip.
            Assert.Contains("@qx0_p0", batch.Statements[0].Text, StringComparison.Ordinal);
            Assert.Equal("@qx2_skip", Assert.Single(batch.Statements[2].Parameters).Name);
        }

        [Fact]
        public void An_ordering_that_holds_the_key_anywhere_keeps_it_once_on_every_unpaged_read()
        {
            DataBatch batch = _harness.Batch();

            // The key first, then another column: the capture's page, a tree's ancestors and an
            // entity query's root page each compile with the key ordered once, as the paged spec.
            _ = batch.Rows(
                new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name", OrderBy = "Id desc, Name", Take = 5 }, null, new RowQueryOptions { CaptureKeys = true });
            _ = batch.Rows(
                new QuerySpec { Root = Entity("qx.Zone"), Select = "Code", OrderBy = "Id, Code", Take = 5 }, null, new RowQueryOptions { IncludeAncestors = true });
            _ = batch.Query(new EntityQuery<Gadget> { Select = "Name", OrderBy = "Name, Id\tdesc", Take = 5, Children = ["Parts"] });
            _ = batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Price" }, null, new RowQueryOptions { CaptureKeys = true });

            Assert.Equal(10, batch.Statements.Count);

            // The capture: the twin keeps "Id desc" where the author put it and breaks no tie
            // after it, and so does the page that reads the captured keys.
            Assert.Contains("ORDER BY [c0] DESC, [T].[Name] ASC\nOFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY\n", Text(batch, 0), StringComparison.Ordinal);
            Assert.Equal(
                """
                SELECT
                    [T].[Name] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_keys))
                ORDER BY [T].[Id] DESC, [c0] ASC
                OFFSET @qx1_skip ROWS
                ;

                """,
                Text(batch, 1));

            // The ancestors of a page ordered "Id, Code" are read in that order, the key once.
            Assert.Contains("ORDER BY [c0] ASC, [T].[Code] ASC\nOFFSET 0 ROWS FETCH NEXT @qx2_take ROWS ONLY\n", Text(batch, 2), StringComparison.Ordinal);
            Assert.Contains("ORDER BY [T].[Id] ASC, [c0] ASC\nOFFSET @qx3_skip ROWS\n", Text(batch, 3), StringComparison.Ordinal);
            Assert.EndsWith(
                """
                SELECT
                    [T].[Code] AS [c0]
                FROM [qx].[Zones] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb4_anc))
                ORDER BY [T].[Id] ASC, [c0] ASC
                OFFSET @qx4_skip ROWS
                ;

                """,
                Text(batch, 4),
                StringComparison.Ordinal);

            // The entity query: the key last, descending and spelled with a tab, still counts as
            // the key; the root page and the children it loads are each ordered once.
            Assert.Contains("ORDER BY [T].[Name] ASC, [c0] DESC\nOFFSET 0 ROWS FETCH NEXT @qx5_take ROWS ONLY\n", Text(batch, 5), StringComparison.Ordinal);
            Assert.EndsWith(
                """
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb5_keys))
                ORDER BY [c0] ASC, [c1] DESC
                OFFSET @qx6_skip ROWS
                ;

                """,
                Text(batch, 6),
                StringComparison.Ordinal);
            Assert.EndsWith(
                """
                WHERE ([T].[GadgetId] IN (SELECT [Id] FROM @tb5_keys))
                ORDER BY [c0] ASC
                ;

                """,
                Text(batch, 7),
                StringComparison.Ordinal);

            // An unordered capture has no paged order to keep: neither its twin nor its page sorts.
            Assert.DoesNotContain("ORDER BY", Text(batch, 8), StringComparison.Ordinal);
            Assert.Equal(
                """
                SELECT
                    [T].[Price] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb8_keys))
                ;

                """,
                Text(batch, 9));
        }

        [Fact]
        public void A_grouped_page_counts_its_groups_up_to_the_cap_plus_one()
        {
            DataBatch batch = _harness.Batch();
            QuerySpec spec = new() { Root = Entity("qx.Gadget"), Select = "Kind, count()", Aggregate = true, Filter = FilterTree.Leaf("Price > 5"), OrderBy = "Kind", Skip = 20, Take = 20 };

            _ = batch.Rows(spec, null, new RowQueryOptions { CountCap = 100 });

            Assert.Equal(2, batch.Statements.Count);
            Assert.Equal(
                """
                SELECT COUNT(*) AS [Count] FROM (
                SELECT
                    [T].[Kind] AS [c0],
                    COUNT_BIG(*) AS [c1]
                FROM [qx].[Gadgets] AS [T]
                WHERE (([T].[Price] IS NOT NULL) AND ([T].[Price] > @qx1_p0))
                GROUP BY [T].[Kind]
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx1_take ROWS ONLY
                ) AS [q]
                ;

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(101, Scalar(batch.Statements[1], "@qx1_take").Value);

            // Groups have no keys to capture or climb from, and a standalone count counts an entity's rows.
            _ = Assert.Throws<ArgumentException>(() => batch.Rows(spec, null, new RowQueryOptions { CountCap = 100, CaptureKeys = true }));
            _ = Assert.Throws<ArgumentException>(() => batch.Count(spec, null));
            Assert.Equal(2, batch.Statements.Count);

            // An unordered grouped page cannot be limited, so its count counts every group.
            _ = batch.Rows(spec with { OrderBy = null, Skip = null, Take = null }, null, new RowQueryOptions { CountCap = 100 });
            Assert.Equal(4, batch.Statements.Count);
            Assert.DoesNotContain("FETCH", batch.Statements[3].Text, StringComparison.Ordinal);
            Assert.StartsWith("SELECT COUNT(*) AS [Count] FROM (", batch.Statements[3].Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_recompiled_filter_keeps_its_hint_on_each_statement_that_reads_the_body()
        {
            QuerySpec spec = new() { Root = Entity("qx.Gadget"), Select = "Name", Filter = FilterTree.Leaf("DueOn >= startOfMonth(today(), 'uq')"), OrderBy = "Name", Take = 20 };

            DataBatch rows = _harness.Batch();
            _ = rows.Rows(spec, null, new RowQueryOptions { CaptureKeys = true, CountCap = 100 });
            Assert.Equal(3, rows.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                DECLARE @qx0_v0 date = (SELECT [M].[StartDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);

                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE (([T].[DueOn] IS NOT NULL) AND (@qx0_v0 IS NOT NULL) AND ([T].[DueOn] >= @qx0_v0))
                ORDER BY [T].[Name] ASC, [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                OPTION (RECOMPILE)
                ;

                """,
                rows.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                """
                DECLARE @qx2_v0 date = (SELECT [M].[StartDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx2_p1 AND [M].[StartDate] <= @qx2_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx2_p0 < [M].[EndDate]);

                SELECT COUNT(*) AS [Count] FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE (([T].[DueOn] IS NOT NULL) AND (@qx2_v0 IS NOT NULL) AND ([T].[DueOn] >= @qx2_v0))
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx2_take ROWS ONLY
                ) AS [q]
                OPTION (RECOMPILE)
                ;

                """,
                rows.Statements[2].Text,
                ignoreLineEndingDifferences: true);

            DataBatch asserted = _harness.Batch();
            asserted.Assert(spec, null, 1, 50404, "Entity.NotFound");
            Assert.Equal(
                """
                DECLARE @tb0_count int;
                DECLARE @qx0_v0 date = (SELECT [M].[StartDate] FROM (SELECT TOP (1) [M].[Year], [M].[Month], [M].[StartDate], [M].[EndDate] FROM [core].[CalendarMonths] AS [M] WHERE [M].[Calendar] = @qx0_p1 AND [M].[StartDate] <= @qx0_p0 ORDER BY [M].[StartDate] DESC) AS [M] WHERE @qx0_p0 < [M].[EndDate]);

                SELECT @tb0_count = COUNT(*) FROM (
                SELECT
                    [T].[Name] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE (([T].[DueOn] IS NOT NULL) AND (@qx0_v0 IS NOT NULL) AND ([T].[DueOn] >= @qx0_v0))
                ORDER BY [c0] ASC, [T].[Id] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                ) AS [q]
                OPTION (RECOMPILE);
                IF @tb0_count <> @tb0_p0 THROW 50404, N'Entity.NotFound', 1;

                """,
                Assert.Single(asserted.Statements).Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void A_restriction_on_a_column_of_its_own_collation_converts_the_keys_to_it()
        {
            DataBatch batch = _harness.Batch();
            SqlIdentifier serials = batch.Tvp<StringList>([new() { Id = "a" }]);
            SqlIdentifier codes = batch.Tvp<StringList>([new() { Id = "b" }]);

            _ = batch.Rows(
                new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name", Restrictions = [new KeySetRestriction("Serial", serials.Name), new KeySetRestriction("Code", codes.Name)] },
                null,
                null);

            string text = batch.Statements[^1].Text;
            Assert.Contains($"([T].[Serial] IN (SELECT [Id] COLLATE Latin1_General_100_BIN2 FROM {serials.Name}))", text, StringComparison.Ordinal);
            Assert.Contains($"([T].[Code] IN (SELECT [Id] FROM {codes.Name}))", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_query_call_over_the_parameter_budget_leaves_the_batch_as_it_was()
        {
            DataBatch batch = _harness.Batch();
            batch.Limits = QueryexLimits.Default with { MaxListItems = 2000, MaxParameters = 2000 };
            QuerySpec Listed(int count)
            {
                return new()
                {
                    Root = Entity("qx.Gadget"),
                    Select = "Name",
                    Filter = FilterTree.Leaf($"Id in ({string.Join(", ", Enumerable.Range(1, count))})"),
                    OrderBy = "Name",
                    Take = 20,
                };
            }

            // Each statement fits on its own; the twin and the count together do not.
            _ = Assert.Throws<InvalidOperationException>(() => batch.Rows(Listed(1100), null, new RowQueryOptions { CountCap = 100, CaptureKeys = true }));
            Assert.Empty(batch.Statements);

            _ = batch.Rows(Listed(500), null, new RowQueryOptions { CountCap = 100, CaptureKeys = true });
            Assert.Equal([0, 1, 2], batch.Statements.Select(static statement => statement.Ordinal));
        }

        [Fact]
        public void A_count_counts_the_keys_up_to_the_cap_plus_one()
        {
            DataBatch batch = _harness.Batch();

            _ = batch.Count(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name", Filter = FilterTree.Leaf("Name = 'x'"), OrderBy = "Name", Skip = 5 }, null, 50);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                SELECT COUNT(*) AS [Count] FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                WHERE ([T].[Name] = @qx0_p0)
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                ) AS [q]
                ;

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(51, Scalar(statement, "@qx0_take").Value);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.Count(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Id" }, null, -1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.Count(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Id" }, null, int.MaxValue));
        }

        [Fact]
        public void An_assertion_throws_its_error_when_the_count_differs()
        {
            DataBatch batch = _harness.Batch();

            batch.Assert(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Id", OrderBy = "Name" }, null, 3, 50404, "Entity.NotFound");

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                IF (SELECT COUNT(*) FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [qx].[Gadgets] AS [T]
                ) AS [q]) <> @tb0_p0 THROW 50404, N'Entity.NotFound', 1;

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(3, Scalar(statement, "@tb0_p0").Value);
        }

        [Fact]
        public void An_assertion_refuses_an_error_outside_the_bands_and_a_code_that_is_not_one()
        {
            DataBatch batch = _harness.Batch();
            QuerySpec spec = new() { Root = Entity("qx.Gadget"), Select = "Id" };

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.Assert(spec, null, 1, 50000 - 1, "Code"));
            _ = Assert.Throws<ArgumentException>(() => batch.Assert(spec, null, 1, 50404, "It's"));
            _ = Assert.Throws<ArgumentException>(() => batch.Assert(spec, null, 1, 50404, " "));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_constant_statement_binds_the_context_values_of_the_batch()
        {
            DataBatch batch = _harness.Batch();

            _ = batch.EvaluateConstant([new ConstantItem("today()", QueryexType.QxDate), new ConstantItem("1 + 2", QueryexType.QxNumeric)], QueryexLanguage.Version);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.StartsWith("SELECT\n", statement.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.Contains(statement.Parameters, parameter => parameter is ScalarParameter { Type: SqlDbType.Date } date && Equals(date.Value, _harness.Context.Today.ToDateTime(TimeOnly.MinValue)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.EvaluateConstant([new ConstantItem("1", QueryexType.QxNumeric)], 999));
        }

        [Fact]
        public void An_entity_query_completes_its_select_and_loads_every_collection_with_their_json_columns()
        {
            DataBatch batch = _harness.Batch();

            _ = batch.Query(new EntityQuery<Gadget> { Select = "Name, Zone.Name, CreatedBy.Name, Extra.Note", Children = ["Parts.PartNotes"], AllProperties = true });

            Assert.Equal(
                [
                    """
                    DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                    INSERT INTO @tb0_keys ([Id])
                    SELECT
                        [T].[Id] AS [c0]
                    FROM [qx].[Gadgets] AS [T]
                    ;

                    """,
                    """
                    SELECT
                        [T].[Name] AS [c0],
                        [P2].[Name] AS [c1],
                        [P1].[Name] AS [c2],
                        [P3].[Note] AS [c3],
                        [T].[Id] AS [c4],
                        [T].[ZoneId] AS [c5],
                        [P2].[Id] AS [c6],
                        [T].[CreatedById] AS [c7],
                        [P1].[Id] AS [c8],
                        [P3].[Id] AS [c9],
                        [T].[Settings] AS [c10]
                    FROM [qx].[Gadgets] AS [T]
                    INNER JOIN [core].[Users] AS [P1] ON [P1].[Id] = [T].[CreatedById]
                    LEFT JOIN [qx].[Zones] AS [P2] ON [P2].[Id] = [T].[ZoneId]
                    LEFT JOIN [qx].[GadgetExtras] AS [P3] ON [P3].[Id] = [T].[Id]
                    WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_keys))
                    ;

                    """,
                    """
                    DECLARE @tb2_keys TABLE ([Id] int PRIMARY KEY);
                    INSERT INTO @tb2_keys ([Id])
                    SELECT
                        [T].[Id] AS [c0]
                    FROM [qx].[GadgetParts] AS [T]
                    WHERE ([T].[GadgetId] IN (SELECT [Id] FROM @tb0_keys))
                    ;

                    """,
                    """
                    SELECT
                        [T].[Id] AS [c0],
                        [T].[GadgetId] AS [c1],
                        [T].[Ordinal] AS [c2],
                        [T].[Notes] AS [c3],
                        [T].[Settings] AS [c4]
                    FROM [qx].[GadgetParts] AS [T]
                    WHERE ([T].[Id] IN (SELECT [Id] FROM @tb2_keys))
                    ORDER BY [c0] ASC
                    OFFSET @qx3_skip ROWS
                    ;

                    """,
                    """
                    SELECT
                        [T].[Id] AS [c0],
                        [T].[GadgetPartId] AS [c1],
                        [T].[Text] AS [c2]
                    FROM [qx].[GadgetPartNotes] AS [T]
                    WHERE ([T].[GadgetPartId] IN (SELECT [Id] FROM @tb2_keys))
                    ORDER BY [c0] ASC
                    ;

                    """,
                ],
                batch.Statements.Select(static statement => statement.Text.ReplaceLineEndings("\n")));
            Assert.Equal(
                [typeof(Gadget), typeof(Gadget), typeof(GadgetPart), typeof(GadgetPart), typeof(GadgetPartNote)],
                batch.Statements.Select(static statement => statement.EntityType));
        }

        [Fact]
        public void Without_the_flag_no_json_column_is_read_and_roots_fixed_by_ids_restrict_their_collections_on_the_ids()
        {
            DataBatch batch = _harness.Batch();
            SqlIdentifier ids = batch.DeclareIdTable();

            _ = batch.Query(new EntityQuery<Gadget> { Select = "Code", Restrictions = [new KeySetRestriction("Id", ids.Name)], Children = ["Parts"] });

            // No capture: the ids table, the roots and the parts.
            Assert.Equal(3, batch.Statements.Count);
            Assert.DoesNotContain("Settings", batch.Statements[1].Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings", batch.Statements[2].Text, StringComparison.Ordinal);
            Assert.Contains("WHERE ([T].[GadgetId] IN (SELECT [Id] FROM @tb0_ids))", batch.Statements[2].Text, StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_query_with_ancestors_always_captures_its_roots()
        {
            DataBatch batch = _harness.Batch();
            SqlIdentifier ids = batch.DeclareIdTable();

            BatchResult<EntityQueryResult<Zone>> zones = batch.Query(new EntityQuery<Zone> { Select = "Code", Restrictions = [new KeySetRestriction("Id", ids.Name)], IncludeAncestors = true });

            Assert.Equal(4, batch.Statements.Count);
            Assert.StartsWith("DECLARE @tb1_keys TABLE", batch.Statements[1].Text, StringComparison.Ordinal);
            Assert.Contains("@tb1_keys", batch.Statements[3].Text, StringComparison.Ordinal);
            Assert.Equal(new SqlIdentifier("@tb1_keys"), batch.KeysOf(zones));
        }

        [Fact]
        public void An_omitted_parameter_folds_out_and_a_typed_null_binds_sql_null()
        {
            QueryexParameterDeclaration minimum = new("Min", QueryexType.QxNumeric, IsNotNull: false);
            QuerySpec spec = new() { Root = Entity("qx.Gadget"), Select = "Name", Filter = FilterTree.Leaf("Price >= @Min") };

            DataBatch omitted = _harness.Batch();
            _ = omitted.Rows(spec, new QueryArguments().Omit(minimum), null);
            BatchStatement folded = Assert.Single(omitted.Statements);
            Assert.DoesNotContain("WHERE", folded.Text, StringComparison.Ordinal);
            Assert.Empty(folded.Parameters);

            DataBatch nulled = _harness.Batch();
            _ = nulled.Rows(spec, new QueryArguments().Add(minimum, null), null);
            BatchStatement bound = Assert.Single(nulled.Statements);
            ScalarParameter parameter = Assert.IsType<ScalarParameter>(Assert.Single(bound.Parameters));
            Assert.Null(parameter.Value);
            Assert.Equal(SqlDbType.Decimal, parameter.Type);
            Assert.Contains(parameter.Name, bound.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_batch_limits_admit_what_the_default_profile_refuses()
        {
            QuerySpec spec = new()
            {
                Root = Entity("qx.Gadget"),
                Select = "Name",
                Filter = FilterTree.Leaf($"Id in ({string.Join(", ", Enumerable.Range(1, 200))})"),
            };

            DataBatch refused = _harness.Batch();
            _ = Assert.Throws<ArgumentException>(() => refused.Rows(spec, null, null));
            Assert.Empty(refused.Statements);

            DataBatch admitted = _harness.Batch();
            admitted.Limits = QueryexLimits.Default with { MaxListItems = 512, MaxParameters = 1024 };
            _ = admitted.Rows(spec, null, null);
            _ = Assert.Single(admitted.Statements);
        }

        [Fact]
        public void An_identical_query_shares_its_handle_until_a_declared_write()
        {
            DataBatch batch = _harness.Batch();
            QuerySpec spec = new() { Root = Entity("qx.Gadget"), Select = "Name", Filter = FilterTree.Leaf("Price > @Min") };
            QueryexParameterDeclaration minimum = new("Min", QueryexType.QxNumeric, IsNotNull: false);

            BatchResult<RowPage> first = batch.Rows(spec, new QueryArguments().Add(minimum, 5m), null);
            Assert.Same(first, batch.Rows(spec with { }, new QueryArguments().Add(minimum, 5m), null));
            Assert.NotSame(first, batch.Rows(spec, new QueryArguments().Add(minimum, 6m), null));
            Assert.NotSame(first, batch.Rows(spec, new QueryArguments().Add(minimum, 5m), new RowQueryOptions { CountCap = 10 }));
            BatchResult<int> count = batch.Count(spec, new QueryArguments().Add(minimum, 5m));
            Assert.Same(count, batch.Count(spec, new QueryArguments().Add(minimum, 5m)));
            BatchResult<EntityQueryResult<Gadget>> entities = batch.Query(new EntityQuery<Gadget> { Select = "Name" });
            Assert.Same(entities, batch.Query(new EntityQuery<Gadget> { Select = "Name" }));
            int appended = batch.Statements.Count;

            _ = batch.Sql($"UPDATE [qx].[Gadgets] SET [Price] = 1;", new SqlOptions(writes: new HashSet<TableName> { new("qx", "Gadgets") }));

            Assert.NotSame(first, batch.Rows(spec, new QueryArguments().Add(minimum, 5m), null));
            Assert.NotSame(entities, batch.Query(new EntityQuery<Gadget> { Select = "Name" }));
            Assert.Equal(appended + 3, batch.Statements.Count);
        }

        [Fact]
        public void A_refused_statement_leaves_the_batch_as_it_was()
        {
            DataBatch batch = _harness.Batch();

            // The twin compiles; the ancestors of a flat entity cannot, so neither is appended.
            _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name" }, null, new RowQueryOptions { IncludeAncestors = true }));
            _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Nope" }, null, null));
            _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name", Filter = FilterTree.Leaf("Price > @Undeclared") }, null, null));
            _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Kind", Aggregate = true }, null, new RowQueryOptions { CaptureKeys = true }));
            Assert.Empty(batch.Statements);

            // The next statement takes the first ordinal.
            _ = batch.Rows(new QuerySpec { Root = Entity("qx.Gadget"), Select = "Name" }, null, null);
            Assert.Equal(0, Assert.Single(batch.Statements).Ordinal);
        }

        [Fact]
        public void An_entity_query_refuses_a_computed_item_an_unknown_collection_and_ancestors_of_a_flat_entity()
        {
            DataBatch batch = _harness.Batch();

            ArgumentException computed = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Gadget> { Select = "Name, Price * 2" }));
            Assert.Contains("Price * 2", computed.Message, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Gadget> { Select = "Name", Children = ["Parts.Nope"] }));
            _ = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Gadget> { Select = "Name", IncludeAncestors = true }));
            _ = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Gadget> { Select = "Nope" }));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void An_entity_query_refuses_a_column_its_class_does_not_hold_and_a_rows_query_reads_it()
        {
            DataBatch batch = _harness.Batch();

            // The tree's node is a column of the schema, never a property of the class.
            ArgumentException node = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Zone> { Select = "Code, Node" }));
            Assert.Contains("'Node'", node.Message, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Gadget> { Select = "Name, Zone.Node" }));
            _ = Assert.Throws<ArgumentException>(() => batch.Query(new EntityQuery<Zone> { Select = "Parent.Node" }));
            Assert.Empty(batch.Statements);

            _ = batch.Rows(new QuerySpec { Root = Entity("qx.Zone"), Select = "Code, Node" }, null, null);
            _ = Assert.Single(batch.Statements);
        }

        [Fact]
        public void A_query_statement_compiles_against_the_schema_of_the_tenant_shape()
        {
            _harness.Shape = MultilingualShape.All;
            DataBatch batch = _harness.Batch();

            _ = batch.Query(new EntityQuery<Gadget> { Select = "Name3" });

            Assert.Contains("[T].[Name3]", Assert.Single(batch.Statements).Text, StringComparison.Ordinal);
            _harness.Shape = MultilingualShape.Primary;
            _ = Assert.Throws<ArgumentException>(() => _harness.Batch().Query(new EntityQuery<Gadget> { Select = "Name3" }));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A scalar parameter of a statement, by name.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The parameter.</returns>
        private static ScalarParameter Scalar(BatchStatement statement, string name)
        {
            return Assert.IsType<ScalarParameter>(statement.Parameters.Single(parameter => parameter.Name == name));
        }

        /// <summary>A statement's text, with LF line endings.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="index">The statement's position.</param>
        /// <returns>The text.</returns>
        private static string Text(DataBatch batch, int index)
        {
            return batch.Statements[index].Text.ReplaceLineEndings("\n");
        }

        /// <summary>An entity of the primary shape's schema.</summary>
        /// <param name="name">The entity's name.</param>
        /// <returns>The entity.</returns>
        private EntityDescriptor Entity(string name)
        {
            return _harness.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(_harness.Shape).FindEntity(name)
                ?? throw new InvalidOperationException($"No entity {name}.");
        }
    }
}
