// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
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
                ORDER BY [c0] ASC
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

            // The page binds nothing; the twin and the count bind today() from the batch's context.
            Assert.Empty(batch.Statements[1].Parameters);
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
                ORDER BY [c0] ASC
                ;

                """,
                batch.Statements[2].Text,
                ignoreLineEndingDifferences: true);

            // The user's filter narrows the page, never the ancestors.
            Assert.Contains("@qx0_p0", batch.Statements[0].Text, StringComparison.Ordinal);
            Assert.Empty(batch.Statements[2].Parameters);
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
