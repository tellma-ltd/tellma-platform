// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Query statements against a seeded fixture database: rows pages with their counts and
    ///     ancestors, capped counts, key-set restrictions over every list type, entity queries with
    ///     their collections and related entities, declared parameters, the multilingual shapes,
    ///     constants, arithmetic failures, computed facets and the round-trip budgets.
    /// </summary>
    /// <param name="fixture">The class's seeded database.</param>
    [Trait("Category", "Integration")]
    public sealed class DataQueryTests(DataQueryDatabase fixture) : IClassFixture<DataQueryDatabase>, IAsyncLifetime
    {
        /// <summary>The widgets table, written by the delete test.</summary>
        private static readonly TableName Widgets = new("fixture", "Widgets");

        /// <summary>The widget extras table, which the delete cascades to.</summary>
        private static readonly TableName WidgetExtras = new("fixture", "WidgetExtras");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            fixture.ResetRoundTrips();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        // ===================== Rows, counts and budgets =====================

        [Fact]
        public async Task A_grid_page_with_its_count_and_ancestors_is_one_round_trip()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            QuerySpec spec = new()
            {
                Root = Entity(scope, "fixture.Node"),
                Select = "Id, Code",
                Filter = FilterTree.Leaf("Code = 'N111' or Code = 'N12'"),
                OrderBy = "Code",
                Skip = 0,
                Take = 10,
            };

            BatchResult<RowPage> page = batch.Rows(spec, null, new RowQueryOptions { CountCap = 100, IncludeAncestors = true });
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal(["N111", "N12"], Strings(page.Value.Rows, 1));
            Assert.Equal(2, page.Value.Count);

            // The nodes above the page, none of the page's own.
            Assert.Equal(["N1", "N11"], Strings(Assert.IsType<QueryRowSet>(page.Value.Ancestors), 1));
            Assert.Equal(1, fixture.RoundTrips);
        }

        [Fact]
        public async Task A_count_above_its_cap_reads_the_cap_plus_one()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            QuerySpec spec = new() { Root = Entity(scope, "fixture.Widget"), Select = "Id", OrderBy = "Id", Skip = 0, Take = 1 };

            BatchResult<int> capped = batch.Count(spec, null, cap: 2);
            BatchResult<int> under = batch.Count(spec, null, cap: 10);
            BatchResult<RowPage> page = batch.Rows(spec, null, new RowQueryOptions { CountCap = 3 });
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal((3, 4, 4), (capped.Value, under.Value, page.Value.Count));
            Assert.Equal(1, page.Value.Rows.RowCount);
        }

        [Fact]
        public async Task A_grouped_page_counts_its_groups()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            QuerySpec spec = new()
            {
                Root = Entity(scope, "fixture.Widget"),
                Select = "Kind, count()",
                Aggregate = true,
                OrderBy = "Kind",
                Skip = 0,
                Take = 1,
            };

            BatchResult<RowPage> page = batch.Rows(spec, null, new RowQueryOptions { CountCap = 10 });
            BatchResult<RowPage> capped = batch.Rows(spec, null, new RowQueryOptions { CountCap = 1 });
            BatchResult<RowPage> unordered = batch.Rows(
                spec with { Select = "OptionalKind, count()", OrderBy = null, Skip = null, Take = null }, null, new RowQueryOptions { CountCap = 1 });
            _ = await batch.ExecuteAsync(Token);

            // Plain and Fancy: one group on the page, two counted, and the cap plus one when capped at
            // one. The unordered page's three groups (Fancy, Plain, none) are counted whole and capped.
            Assert.Equal(["Fancy"], Strings(page.Value.Rows, 0));
            Assert.Equal((2, 2), (page.Value.Count, capped.Value.Count));
            Assert.Equal((3, 2), (unordered.Value.Rows.RowCount, unordered.Value.Count));
        }

        [Fact]
        public async Task An_assertion_passes_on_its_count_and_throws_its_error_and_code_on_any_other()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            QuerySpec spec = new() { Root = Entity(scope, "fixture.Widget"), Select = "Id", Filter = FilterTree.Leaf("Kind = 'Plain'") };

            IDataBatch holds = Batch(scope);
            holds.Assert(spec, null, 2, 50600, "Fixture.Count");
            BatchResult<RowPage> after = holds.Rows(spec, null, null);
            _ = await holds.ExecuteAsync(Token);
            Assert.Equal(2, after.Value.Rows.RowCount);

            IDataBatch fails = Batch(scope);
            fails.Assert(spec, null, 3, 50600, "Fixture.Count");
            BatchAssertionFailedException thrown = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => fails.ExecuteAsync(Token));
            Assert.Equal((50600, "Fixture.Count"), (thrown.Number, thrown.Code));
        }

        [Fact]
        public async Task Two_pages_of_one_query_share_one_plan()
        {
            for (int skip = 0; skip < 2; skip++)
            {
                await using AsyncServiceScope scope = fixture.CreateScope();
                IDataBatch batch = Batch(scope);
                BatchResult<RowPage> page = batch.Rows(
                    new QuerySpec { Root = Entity(scope, "fixture.Shipment"), Select = "Id, State", OrderBy = "Id", Skip = skip, Take = 1 }, null, null);
                _ = await batch.ExecuteAsync(Token);
                Assert.Equal([skip == 0 ? DataQueryDatabase.FirstShipmentId : DataQueryDatabase.SecondShipmentId], Longs(page.Value.Rows, 0));
            }

            // Skip and take are parameters, so both batches are one text and one cached plan.
            int plans = await SqlServerFixture.ScalarAsync<int>(
                fixture.Tenant.ConnectionString,
                """
                SELECT COUNT(DISTINCT [s].[plan_handle])
                FROM sys.dm_exec_query_stats AS [s]
                CROSS APPLY sys.dm_exec_sql_text([s].[sql_handle]) AS [t]
                CROSS APPLY sys.dm_exec_plan_attributes([s].[plan_handle]) AS [a]
                WHERE [a].[attribute] = N'dbid' AND CAST([a].[value] AS int) = DB_ID()
                  AND [t].[text] LIKE N'%FROM ![fixture!].![Shipments!]%FETCH NEXT%' ESCAPE N'!';
                """);
            Assert.Equal(1, plans);
        }

        [Fact]
        public async Task Details_by_id_with_children_related_and_an_extra_statement_are_one_round_trip()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            SqlIdentifier ids = batch.Tvp<IdList>([new() { Id = 1 }]);

            BatchResult<EntityQueryResult<Widget>> details = batch.Query(new EntityQuery<Widget>
            {
                Select = "Name, CreatedBy.Name, Extra.Note",
                Restrictions = [new KeySetRestriction("Id", ids.Name)],
                Children = ["Parts.PartNotes"],
            });
            BatchResult<QueryRowSet> extra = batch.EvaluateConstant([new ConstantItem("1 + 1", QueryexType.QxNumeric)], QueryexLanguage.Version);
            _ = await batch.ExecuteAsync(Token);

            Widget widget = Assert.Single(details.Value.Entities);
            Assert.Equal([11, 12], widget.Parts!.Select(static part => part.Id));
            Assert.Equal(["first"], details.Value.Related.Sets["fixture.WidgetExtra"].Entities.Cast<WidgetExtra>().Select(static row => row.Note));
            Assert.Equal(1, extra.Value.RowCount);
            Assert.Equal(1, fixture.RoundTrips);
        }

        // ===================== Restrictions and parameters =====================

        [Fact]
        public async Task A_restriction_reads_its_keys_from_any_list_type_or_a_batch_table()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            var batch = (DataBatch)Batch(scope);
            EntityDescriptor widget = Entity(scope, "fixture.Widget");

            SqlIdentifier ids = batch.Tvp<IdList>([new() { Id = 1 }, new() { Id = 3 }]);
            SqlIdentifier codes = batch.Tvp<StringList>([new() { Id = "W2" }, new() { Id = "W4" }]);
            SqlIdentifier days = batch.Tvp<DateList>([new() { Id = new DateOnly(2026, 3, 1) }, new() { Id = new DateOnly(2026, 3, 3) }]);
            SqlIdentifier shipments = batch.Tvp<BigIdList>([new() { Id = DataQueryDatabase.SecondShipmentId }]);
            SqlIdentifier declared = batch.DeclareIdTable();
            _ = batch.Sql($"INSERT INTO {declared} ([Id]) VALUES (2), (3);", null);

            BatchResult<RowPage> byIds = batch.Rows(Restricted(widget, "Id", ids), null, null);
            BatchResult<RowPage> byCodes = batch.Rows(Restricted(widget, "Code", codes), null, null);
            BatchResult<RowPage> byDays = batch.Rows(Restricted(widget, "DueOn", days), null, null);
            BatchResult<RowPage> byBigIds = batch.Rows(Restricted(Entity(scope, "fixture.Shipment"), "Id", shipments), null, null);
            BatchResult<RowPage> byDeclared = batch.Rows(Restricted(widget, "Id", declared), null, null);

            // A page that captured its keys: the parts of the widgets priced 10 or more, 1, 3 and 4.
            BatchResult<RowPage> priced = batch.Rows(
                new QuerySpec { Root = widget, Select = "Id", Filter = FilterTree.Leaf("Price >= 10") }, null, new RowQueryOptions { CaptureKeys = true });
            SqlIdentifier captured = Assert.IsType<SqlIdentifier>(batch.KeysOf(priced));
            BatchResult<RowPage> parts = batch.Rows(
                new QuerySpec
                {
                    Root = Entity(scope, "fixture.WidgetPart"),
                    Select = "Id",
                    OrderBy = "Id",
                    Restrictions = [new KeySetRestriction("WidgetId", captured.Name)],
                },
                null,
                null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([1, 3], Ints(byIds.Value.Rows, 0));
            Assert.Equal([2, 4], Ints(byCodes.Value.Rows, 0));
            Assert.Equal([1, 3], Ints(byDays.Value.Rows, 0));
            Assert.Equal([DataQueryDatabase.SecondShipmentId], Longs(byBigIds.Value.Rows, 0));
            Assert.Equal([2, 3], Ints(byDeclared.Value.Rows, 0));
            Assert.Equal([11, 12], Ints(parts.Value.Rows, 0));
            Assert.Equal(1, fixture.RoundTrips);
        }

        [Fact]
        public async Task A_text_list_restricts_a_binary_collated_column_by_its_exact_bytes()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            var batch = (DataBatch)Batch(scope);
            SqlIdentifier subjects = batch.Tvp<StringList>([new() { Id = "Sub-A" }]);

            // The list's column takes the database's collation and the subject column its own: compared
            // bare, the server refuses the two as a collation conflict.
            BatchResult<RowPage> users = batch.Rows(Restricted(Entity(scope, "core.User"), "Subject", subjects), null, null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([2], Ints(users.Value.Rows, 0));
        }

        [Fact]
        public async Task Text_of_two_collations_in_one_comparison_is_refused_before_the_server_sees_it()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            // The subject is binary-collated and the email takes the database's collation.
            ArgumentException refusal = Assert.Throws<ArgumentException>(() => batch.Rows(
                new QuerySpec { Root = Entity(scope, "core.User"), Select = "Id", Filter = FilterTree.Leaf("Subject = Email") }, null, null));

            Assert.Contains("QX3203", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_user_stamp_is_read_with_its_users_name()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> stamps = batch.Rows(
                new QuerySpec { Root = Entity(scope, "core.UserStamp"), Select = "UserId, User.Name, InboxVersion", Filter = FilterTree.Leaf("UserId >= 2"), OrderBy = "UserId" },
                null,
                null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([2, 3], Ints(stamps.Value.Rows, 0));
            Assert.Equal(["Upper", "Lower"], Strings(stamps.Value.Rows, 1));
            Assert.Equal([4L, 0L], Longs(stamps.Value.Rows, 2));
        }

        [Fact]
        public async Task An_omitted_parameter_drops_its_criterion_and_a_null_one_matches_nothing()
        {
            QueryexParameterDeclaration minimum = new("Min", QueryexType.QxNumeric, IsNotNull: false);
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            QuerySpec spec = new() { Root = Entity(scope, "fixture.Widget"), Select = "Id", Filter = FilterTree.Leaf("Price >= @Min"), OrderBy = "Id" };

            BatchResult<RowPage> omitted = batch.Rows(spec, new QueryArguments().Omit(minimum), null);
            BatchResult<RowPage> nulled = batch.Rows(spec, new QueryArguments().Add(minimum, null), null);
            BatchResult<RowPage> bound = batch.Rows(spec, new QueryArguments().Add(minimum, 30m), null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([1, 2, 3, 4], Ints(omitted.Value.Rows, 0));
            Assert.Empty(Ints(nulled.Value.Rows, 0));
            Assert.Equal([3, 4], Ints(bound.Value.Rows, 0));
        }

        [Fact]
        public async Task A_via_filter_on_notes_through_their_part_and_widget_keeps_the_matching_widgets_notes()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> notes = batch.Rows(
                new QuerySpec
                {
                    Root = Entity(scope, "fixture.WidgetPartNote"),
                    Select = "Id",
                    OrderBy = "Id",
                    Filter = FilterTree.Via("WidgetPart.Widget", FilterTree.Leaf("Code = 'W1'")),
                },
                null,
                null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([111, 112], Ints(notes.Value.Rows, 0));
        }

        // ===================== Entity queries =====================

        [Fact]
        public async Task Children_and_grandchildren_are_stitched_by_parent_key()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<EntityQueryResult<Widget>> query = batch.Query(new EntityQuery<Widget>
            {
                Select = "Code",
                Filter = FilterTree.Leaf("Id <= 3"),
                OrderBy = "Id",
                Children = ["Parts.PartNotes"],
            });
            _ = await batch.ExecuteAsync(Token);

            Widget[] widgets = [.. query.Value.Entities];
            Assert.Equal(["W1", "W2", "W3"], widgets.Select(static widget => widget.Code));
            Assert.Equal([11, 12], widgets[0].Parts!.Select(static part => part.Id));
            Assert.Equal(["n111", "n112"], widgets[0].Parts![0].PartNotes!.Select(static note => note.Text));
            Assert.Empty(widgets[0].Parts![1].PartNotes!);
            Assert.Equal([211], widgets[1].Parts!.SelectMany(static part => part.PartNotes!).Select(static note => note.Id));
            Assert.Empty(widgets[2].Parts!);
        }

        [Fact]
        public async Task A_sibling_is_left_joined_and_deleting_its_owner_deletes_it()
        {
            await using (AsyncServiceScope scope = fixture.CreateScope())
            {
                IDataBatch batch = Batch(scope);
                BatchResult<RowPage> rows = batch.Rows(
                    new QuerySpec { Root = Entity(scope, "fixture.Widget"), Select = "Id, Extra.Note", OrderBy = "Id" }, null, null);
                _ = await batch.ExecuteAsync(Token);

                // Widget 3's sibling row holds a null note; widgets 2 and 4 have none.
                Assert.Equal([1, 2, 3, 4], Ints(rows.Value.Rows, 0));
                Assert.Equal(["first", null, null, null], Strings(rows.Value.Rows, 1));
            }

            // A widget of this test's own, with its sibling row, deleted through a declared write.
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                "INSERT INTO [fixture].[Widgets] ([Id], [Kind], [Name], [Subject], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (90, 'Plain', N'Doomed', N's90', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);",
                "INSERT INTO [fixture].[WidgetExtras] ([Id], [Note]) VALUES (90, N'gone');");
            await using (AsyncServiceScope scope = fixture.CreateScope())
            {
                IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {90};", new SqlOptions(writes: new HashSet<TableName> { Widgets, WidgetExtras }));
                _ = await batch.ExecuteAsync(Token);
            }

            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [fixture].[WidgetExtras] WHERE [Id] = 90;"));
        }

        [Fact]
        public async Task Related_entities_are_one_instance_per_id_with_the_projection_they_carry()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<EntityQueryResult<Widget>> query = batch.Query(new EntityQuery<Widget> { Select = "Name, CreatedBy.Name, ModifiedBy.Name", OrderBy = "Id" });
            _ = await batch.ExecuteAsync(Token);

            // Every widget was created and modified by the system user: one instance for eight references.
            Assert.Equal(4, query.Value.Entities.Count);
            Assert.All(query.Value.Entities, static widget => Assert.Equal(1, widget.CreatedById));
            RelatedEntitySet users = query.Value.Related.Sets["core.User"];
            Assert.Equal([1], users.Entities.Cast<User>().Select(static user => user.Id));
            Assert.Equal(["Id", "Name"], users.Projection.Order(StringComparer.Ordinal));
            Assert.Equal("System", users.Entities.Cast<User>().Single().Name);
        }

        [Fact]
        public async Task A_nullable_enum_is_stored_by_name_and_reads_back_null_or_its_member()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<EntityQueryResult<Widget>> query = batch.Query(new EntityQuery<Widget> { Select = "Kind, OptionalKind", OrderBy = "Id" });
            BatchResult<RowPage> fancy = batch.Rows(
                new QuerySpec { Root = Entity(scope, "fixture.Widget"), Select = "Id", Filter = FilterTree.Leaf("OptionalKind = 'Fancy'") }, null, null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([WidgetKind.Fancy, null, WidgetKind.Plain, null], query.Value.Entities.Select(static widget => widget.OptionalKind));
            Assert.Equal([1], Ints(fancy.Value.Rows, 0));
            Assert.Equal(
                "varchar",
                await SqlServerFixture.ScalarAsync<string>(
                    fixture.Tenant.ConnectionString,
                    "SELECT [DATA_TYPE] FROM [INFORMATION_SCHEMA].[COLUMNS] WHERE [TABLE_SCHEMA] = N'fixture' AND [TABLE_NAME] = N'Widgets' AND [COLUMN_NAME] = N'OptionalKind';"));
        }

        [Fact]
        public async Task Json_columns_are_read_only_when_every_property_is_asked_for()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);
            SqlIdentifier ids = batch.Tvp<IdList>([new() { Id = 1 }]);
            EntityQuery<Widget> query = new() { Select = "Name", Restrictions = [new KeySetRestriction("Id", ids.Name)], Children = ["Parts"] };

            BatchResult<EntityQueryResult<Widget>> without = batch.Query(query);
            BatchResult<EntityQueryResult<Widget>> with = batch.Query(new EntityQuery<Widget>
            {
                Select = query.Select,
                Restrictions = query.Restrictions,
                Children = query.Children,
                AllProperties = true,
            });
            _ = await batch.ExecuteAsync(Token);

            Widget plain = Assert.Single(without.Value.Entities);
            Assert.Null(plain.Settings);
            Assert.Null(plain.Parts![0].Settings);

            Widget full = Assert.Single(with.Value.Entities);
            Assert.Equal(/*lang=json,strict*/ """{"a":1}""", full.Settings);
            Assert.Equal([/*lang=json,strict*/ """{"p":1}""", null], full.Parts!.Select(static part => part.Settings));
        }

        // ===================== Shapes, constants and arithmetic =====================

        [Fact]
        public async Task The_three_shapes_differ_only_in_the_name_twins_they_admit()
        {
            // Under one language only the first twin is a property; under two, the second joins it.
            await using (AsyncServiceScope primary = fixture.CreateScope(MultilingualShape.Primary))
            {
                IDataBatch batch = Batch(primary);
                _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = Entity(primary, "fixture.Widget", MultilingualShape.Primary), Select = "Name2" }, null, null));
            }

            await using (AsyncServiceScope secondary = fixture.CreateScope(MultilingualShape.PrimaryAndSecondary))
            {
                IDataBatch batch = Batch(secondary);
                EntityDescriptor widget = Entity(secondary, "fixture.Widget", MultilingualShape.PrimaryAndSecondary);
                _ = Assert.Throws<ArgumentException>(() => batch.Rows(new QuerySpec { Root = widget, Select = "Name3" }, null, null));
                BatchResult<RowPage> names = batch.Rows(new QuerySpec { Root = widget, Select = "Name, Name2", Filter = FilterTree.Leaf("Id = 1") }, null, null);
                _ = await batch.ExecuteAsync(Token);
                Assert.Equal(["ألفا"], Strings(names.Value.Rows, 1));
            }

            await using AsyncServiceScope all = fixture.CreateScope(MultilingualShape.All);
            IQueryexSchemaProvider schemas = all.ServiceProvider.GetRequiredService<IQueryexSchemaProvider>();
            string[] narrow = Properties(schemas.GetSchema(MultilingualShape.Primary));
            string[] wide = Properties(schemas.GetSchema(MultilingualShape.All));
            string[] added = [.. wide.Except(narrow)];
            Assert.Contains("fixture.Widget.Name2", added);
            Assert.Contains("fixture.Widget.Name3", added);
            Assert.All(added, static name => Assert.Matches(@"\.Name[23]$", name));
            Assert.Empty(narrow.Except(wide));
        }

        [Fact]
        public async Task A_constant_beside_a_query_sees_the_same_today()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> rows = batch.Rows(
                new QuerySpec { Root = Entity(scope, "fixture.Widget"), Select = "Id, today()", OrderBy = "Id", Take = 1 }, null, null);
            BatchResult<QueryRowSet> constant = batch.EvaluateConstant([new ConstantItem("today()", QueryexType.QxDate)], QueryexLanguage.Version);
            _ = await batch.ExecuteAsync(Token);

            DateOnly fromConstant = Assert.Single((DateOnly[])constant.Value.GetBuffer(0));
            Assert.Equal(DataQueryDatabase.Today, fromConstant);
            Assert.Equal([fromConstant], (DateOnly[])rows.Value.Rows.GetBuffer(1));
        }

        [Fact]
        public async Task A_division_by_zero_and_a_decimal_overflow_surface_as_arithmetic_failures()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            EntityDescriptor widget = Entity(scope, "fixture.Widget");

            IDataBatch divides = Batch(scope);
            _ = divides.Rows(new QuerySpec { Root = widget, Select = "Price / (Id - Id)", Filter = FilterTree.Leaf("Id = 1") }, null, null);
            ArithmeticFailureException byZero = await Assert.ThrowsAsync<ArithmeticFailureException>(() => divides.ExecuteAsync(Token));
            Assert.Equal(ArithmeticFailureException.DivideByZero, byZero.Reason);

            IDataBatch overflows = Batch(scope);
            _ = overflows.Rows(
                new QuerySpec { Root = widget, Select = "Price * 10000000000000000000000000000000000000", Filter = FilterTree.Leaf("Id = 1") }, null, null);
            ArithmeticFailureException tooLarge = await Assert.ThrowsAsync<ArithmeticFailureException>(() => overflows.ExecuteAsync(Token));
            Assert.Equal(ArithmeticFailureException.Overflow, tooLarge.Reason);
        }

        [Fact]
        public async Task A_computed_decimal_reports_the_precision_and_scale_the_server_gives_it()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> rows = batch.Rows(
                new QuerySpec { Root = Entity(scope, "fixture.Widget"), Select = "Price * Price", Filter = FilterTree.Leaf("Id = 1") }, null, null);
            _ = await batch.ExecuteAsync(Token);

            // The server's own typing of the same product is the oracle.
            QueryColumn column = Assert.Single(rows.Value.Rows.Columns);
            int precision = await SqlServerFixture.ScalarAsync<int>(
                fixture.Tenant.ConnectionString,
                "SELECT CAST(SQL_VARIANT_PROPERTY(CAST([Price] * [Price] AS sql_variant), 'Precision') AS int) FROM [fixture].[Widgets] WHERE [Id] = 1;");
            int scale = await SqlServerFixture.ScalarAsync<int>(
                fixture.Tenant.ConnectionString,
                "SELECT CAST(SQL_VARIANT_PROPERTY(CAST([Price] * [Price] AS sql_variant), 'Scale') AS int) FROM [fixture].[Widgets] WHERE [Id] = 1;");
            Assert.Equal((precision, scale), (column.Precision, column.Scale));
            Assert.Equal(110.25m, Assert.Single((decimal[])rows.Value.Rows.GetBuffer(0)));
        }

        [Fact]
        public async Task A_nodes_level_is_the_servers_own_level()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> rows = batch.Rows(new QuerySpec { Root = Entity(scope, "fixture.Node"), Select = "Id, level(Node)", OrderBy = "Id" }, null, null);
            _ = await batch.ExecuteAsync(Token);

            string expected = await SqlServerFixture.ScalarAsync<string>(
                fixture.Tenant.ConnectionString,
                "SELECT STRING_AGG(CAST([Node].GetLevel() AS nvarchar(5)), N',') WITHIN GROUP (ORDER BY [Id]) FROM [fixture].[Nodes];") ?? string.Empty;
            Array levels = rows.Value.Rows.GetBuffer(1);
            Assert.Equal(expected, string.Join(",", levels.Cast<object>()));
            Assert.Equal("1,2,3,1,2", expected);
        }

        [Fact]
        public async Task A_rows_query_reads_a_nodes_position_as_its_path()
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch batch = Batch(scope);

            BatchResult<RowPage> rows = batch.Rows(new QuerySpec { Root = Entity(scope, "fixture.Node"), Select = "Id, Node", OrderBy = "Id" }, null, null);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal(["/1/", "/1/1/", "/1/1/1/", "/2/", "/1/2/"], Strings(rows.Value.Rows, 1));
        }

        // ===================== Helpers =====================

        /// <summary>A read batch of a scope's tenant database.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The batch.</returns>
        private static IDataBatch Batch(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
        }

        /// <summary>An entity of a shape's query schema.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="name">The entity's name.</param>
        /// <param name="shape">The shape; the scope's own when the default.</param>
        /// <returns>The entity.</returns>
        private static EntityDescriptor Entity(AsyncServiceScope scope, string name, MultilingualShape shape = MultilingualShape.All)
        {
            return scope.ServiceProvider.GetRequiredService<IQueryexSchemaProvider>().GetSchema(shape).FindEntity(name)
                ?? throw new InvalidOperationException($"No entity {name}.");
        }

        /// <summary>A spec of an entity's keys, restricted on one path to a table's keys, in key order.</summary>
        /// <param name="root">The entity.</param>
        /// <param name="path">The restricted path.</param>
        /// <param name="table">The table.</param>
        /// <returns>The spec.</returns>
        private static QuerySpec Restricted(EntityDescriptor root, string path, SqlIdentifier table)
        {
            return new QuerySpec { Root = root, Select = "Id", OrderBy = "Id", Restrictions = [new KeySetRestriction(path, table.Name)] };
        }

        /// <summary>Every property of a schema, qualified by its entity's name.</summary>
        /// <param name="schema">The schema.</param>
        /// <returns>The qualified names.</returns>
        private static string[] Properties(QueryexSchema schema)
        {
            return [.. schema.Entities.SelectMany(static entity => entity.Properties.Select(property => $"{entity.Name}.{property.Name}"))];
        }

        /// <summary>An <c>int</c> column's values.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static int[] Ints(QueryRowSet rows, int column)
        {
            return (int[])rows.GetBuffer(column);
        }

        /// <summary>A <c>bigint</c> column's values.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static long[] Longs(QueryRowSet rows, int column)
        {
            return (long[])rows.GetBuffer(column);
        }

        /// <summary>A text column's values, null where the row holds none.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static List<string?> Strings(QueryRowSet rows, int column)
        {
            string?[] values = (string?[])rows.GetBuffer(column);
            return [.. values.Select((value, row) => rows.IsNull(column, row) ? null : value)];
        }
    }
}
