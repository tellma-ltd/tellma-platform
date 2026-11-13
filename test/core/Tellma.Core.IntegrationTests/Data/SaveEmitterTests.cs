// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     The save statement against a fixture database: unchanged rows skipped without history, a
    ///     case-only edit written, children and grandchildren stamping their root, unsupplied parents
    ///     untouched, the concurrency guard's conflicts and missing rows, two racing saves, the
    ///     columns a save never sets, the blob captures, the recompile threshold and a tree payload
    ///     with temporary ids.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class SaveEmitterTests(SaveDatabase fixture) : IClassFixture<SaveDatabase>, IAsyncLifetime
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task An_unchanged_save_writes_nothing_no_history_and_no_stamp()
        {
            Widget widget = NewWidget("w1", "widget", NewPart(1, "a"));
            SaveReceipt created = await CreateAsync(widget);
            DateTimeOffset stored = await StoredStampAsync(widget.Id);

            SaveReceipt receipt = await SaveAsync([widget]);

            Assert.Equal((2, 0, 0), (created.Inserted, created.Updated, created.Deleted));
            Assert.Equal((0, 0, 0), (receipt.Inserted, receipt.Updated, receipt.Deleted));
            Assert.Equal(stored, await StoredStampAsync(widget.Id));
            Assert.Equal(stored, widget.ModifiedAt);
            Assert.Equal(0, await HistoryCountAsync(widget.Id));
        }

        [Fact]
        public async Task A_case_only_edit_is_written_stamped_and_leaves_a_history_row()
        {
            Widget widget = NewWidget("w1", "widget");
            _ = await CreateAsync(widget);
            DateTimeOffset before = widget.ModifiedAt;

            widget.Name = "Widget";
            SaveReceipt receipt = await SaveAsync([widget]);

            Assert.Equal(1, receipt.Updated);
            Assert.Equal("Widget", await ScalarAsync<string>($"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};"));
            Assert.Equal(receipt.Stamp, await StoredStampAsync(widget.Id));
            Assert.Equal((receipt.Stamp, WellKnownIds.SystemUserId), (widget.ModifiedAt, widget.ModifiedById));
            Assert.True(receipt.Stamp > before);
            Assert.Equal(1, await HistoryCountAsync(widget.Id));
        }

        [Fact]
        public async Task A_parent_whose_only_change_is_a_child_is_stamped()
        {
            Widget widget = NewWidget("w1", "widget", NewPart(1, "a"), NewPart(2, "b"));
            _ = await CreateAsync(widget);

            widget.Parts![1].Notes = "B";
            SaveReceipt receipt = await SaveAsync([widget]);

            // The stamped root and the updated child; the unchanged child is skipped.
            Assert.Equal((0, 2, 0), (receipt.Inserted, receipt.Updated, receipt.Deleted));
            Assert.Equal(receipt.Stamp, await StoredStampAsync(widget.Id));
            Assert.Equal("a|B", await ScalarAsync<string>($"SELECT STRING_AGG([Notes], '|') WITHIN GROUP (ORDER BY [Ordinal]) FROM [fixture].[WidgetParts] WHERE [WidgetId] = {widget.Id};"));
        }

        [Fact]
        public async Task An_unsupplied_parent_is_untouched_and_an_empty_collection_deletes_every_child()
        {
            Widget kept = NewWidget("w1", "kept", NewPart(1, "a"), NewPart(2, "b"));
            Widget emptied = NewWidget("w2", "emptied", NewPart(1, "c"), NewPart(2, "d"), NewPart(3, "e"));
            _ = await CreateAsync(kept, emptied);
            DateTimeOffset keptStamp = kept.ModifiedAt;

            kept.Parts = null;
            emptied.Parts = [];
            SaveReceipt receipt = await SaveAsync([kept, emptied]);

            // The emptied widget lost its three parts and was stamped; the other was neither read for children nor stamped.
            Assert.Equal((0, 1, 3), (receipt.Inserted, receipt.Updated, receipt.Deleted));
            Assert.Equal(2, await ScalarAsync<int>($"SELECT COUNT(*) FROM [fixture].[WidgetParts] WHERE [WidgetId] = {kept.Id};"));
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM [fixture].[WidgetParts] WHERE [WidgetId] = {emptied.Id};"));
            Assert.Equal(keptStamp, await StoredStampAsync(kept.Id));
            Assert.Equal(receipt.Stamp, await StoredStampAsync(emptied.Id));
        }

        [Fact]
        public async Task A_grandchild_change_stamps_the_root_and_a_removed_child_takes_its_grandchildren()
        {
            WidgetPart first = NewPart(1, "a");
            first.PartNotes = [new WidgetPartNote { Text = "note" }];
            WidgetPart second = NewPart(2, "b");
            second.PartNotes = [new WidgetPartNote { Text = "other" }, new WidgetPartNote { Text = "more" }];
            Widget widget = NewWidget("w1", "widget", first, second);
            SaveReceipt created = await CreateAsync(widget);
            Assert.Equal(6, created.Inserted);

            // --- A grandchild's edit: the root is stamped through the part it belongs to ---
            first.PartNotes[0].Text = "Note";
            SaveReceipt edited = await SaveAsync([widget]);
            Assert.Equal((0, 2, 0), (edited.Inserted, edited.Updated, edited.Deleted));
            Assert.Equal(edited.Stamp, await StoredStampAsync(widget.Id));
            Assert.Equal("Note", await ScalarAsync<string>($"SELECT [Text] FROM [fixture].[WidgetPartNotes] WHERE [Id] = {first.PartNotes[0].Id};"));

            // --- A grandchild's removal is a delete that stamps the root ---
            second.PartNotes.RemoveAt(1);
            SaveReceipt removed = await SaveAsync([widget]);
            Assert.Equal((0, 1, 1), (removed.Inserted, removed.Updated, removed.Deleted));
            Assert.Equal(removed.Stamp, await StoredStampAsync(widget.Id));

            // --- A removed part takes its notes with it, deepest first ---
            widget.Parts!.Remove(second);
            SaveReceipt dropped = await SaveAsync([widget]);
            Assert.Equal((0, 1, 1), (dropped.Inserted, dropped.Updated, dropped.Deleted));
            Assert.Equal(1, await QueryAsync<int>("SELECT COUNT(*) FROM [fixture].[WidgetPartNotes];"));
        }

        [Fact]
        public async Task A_row_deleted_under_the_save_is_a_missing_conflict_and_override_never_resurrects_it()
        {
            Widget widget = NewWidget("w1", "widget", NewPart(1, "a"));
            _ = await CreateAsync(widget);
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, "DELETE FROM [fixture].[WidgetParts];", "DELETE FROM [fixture].[Widgets];");

            widget.Name = "changed";
            foreach (ConcurrencyMode mode in new[] { ConcurrencyMode.Check, ConcurrencyMode.Override })
            {
                ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SaveAsync([widget], mode));

                ConflictRow row = Assert.Single(conflict.Conflicts);
                Assert.Equal((widget.Id, true, null, null), (row.Id, row.IsMissing, row.ModifiedAt, row.ModifiedById));
            }

            Assert.Equal(0, await QueryAsync<int>("SELECT COUNT(*) FROM [fixture].[Widgets];"));
            Assert.Equal(0, await QueryAsync<int>("SELECT COUNT(*) FROM [fixture].[WidgetParts];"));
        }

        [Fact]
        public async Task A_stale_stamp_is_a_conflict_that_names_the_last_modifier_and_override_writes_over_it()
        {
            Widget widget = NewWidget("w1", "widget");
            _ = await CreateAsync(widget);
            Widget stale = Copy(widget);
            widget.Name = "first";
            SaveReceipt first = await SaveAsync([widget]);

            // --- Under Check: the stored stamp moved, so nothing is written ---
            stale.Name = "second";
            ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SaveAsync([stale]));
            ConflictRow row = Assert.Single(conflict.Conflicts);
            Assert.Equal((stale.Id, false, first.Stamp, WellKnownIds.SystemUserId), (row.Id, row.IsMissing, row.ModifiedAt, row.ModifiedById));
            Assert.NotNull(row.Name);
            Assert.Equal("first", await ScalarAsync<string>($"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};"));

            // --- Under Override the stale copy wins ---
            SaveReceipt overridden = await SaveAsync([stale], ConcurrencyMode.Override);
            Assert.Equal(1, overridden.Updated);
            Assert.Equal("second", await ScalarAsync<string>($"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};"));
        }

        [Fact]
        public async Task Two_racing_saves_of_one_row_produce_exactly_one_conflict()
        {
            Widget widget = NewWidget("w1", "widget");
            _ = await CreateAsync(widget);
            Widget racer = Copy(widget);

            // --- The gate: an application lock the first save waits on inside its transaction, holding its row locks ---
            await using Microsoft.Data.SqlClient.SqlConnection gate = new(fixture.ConnectionString);
            await gate.OpenAsync(Token);
            await ExecuteOnAsync(gate, "EXEC sp_getapplock @Resource = N'fx-race', @LockMode = 'Exclusive', @LockOwner = 'Session';");

            widget.Name = "first";
            Task<SaveReceipt> held = SaveAsync([widget], gated: true);
            await WaitForAsync("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE [resource_type] = 'APPLICATION' AND [request_status] = 'WAIT';");

            // --- The second save's guard waits on the row the first one updated ---
            racer.Name = "second";
            Task<SaveReceipt> blocked = SaveAsync([racer]);
            await WaitForAsync("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE [resource_type] <> 'APPLICATION' AND [request_status] = 'WAIT' AND [resource_database_id] = DB_ID();");

            await ExecuteOnAsync(gate, "EXEC sp_releaseapplock @Resource = N'fx-race', @LockOwner = 'Session';");

            // --- The first commits; the second reads the new stamp under its U lock and conflicts ---
            SaveReceipt receipt = await held;
            ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => blocked);
            ConflictRow row = Assert.Single(conflict.Conflicts);
            Assert.Equal((widget.Id, false, receipt.Stamp), (row.Id, row.IsMissing, row.ModifiedAt));
            Assert.Equal("first", await ScalarAsync<string>($"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};"));
        }

        [Fact]
        public async Task Write_once_and_server_owned_values_a_client_sends_are_overwritten()
        {
            // --- On insert the statement stamps the audit columns, whatever the row holds ---
            Widget widget = NewWidget("w1", "widget");
            widget.CreatedAt = DateTimeOffset.UnixEpoch;
            widget.CreatedById = 42;
            SaveReceipt created = await CreateAsync(widget);
            Assert.Equal(
                created.Stamp,
                await ScalarAsync<DateTimeOffset>($"SELECT [CreatedAt] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id} AND [ModifiedAt] = [CreatedAt] AND [CreatedById] = 1 AND [ModifiedById] = 1;"));
            Assert.Equal((created.Stamp, WellKnownIds.SystemUserId), (widget.CreatedAt, widget.CreatedById));

            // --- On update the stored write-once and server-owned values stand ---
            widget.Subject = "changed";
            widget.IsActive = false;
            widget.CreatedAt = DateTimeOffset.UnixEpoch;
            widget.CreatedById = 42;
            widget.Name = "edited";
            SaveReceipt updated = await SaveAsync([widget]);

            Assert.Equal(1, updated.Updated);
            Assert.Equal(
                "w1|1|edited",
                await ScalarAsync<string>($"SELECT CONCAT([Subject], '|', CAST([IsActive] AS int), '|', [Name]) FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};"));
            Assert.Equal(created.Stamp, await ScalarAsync<DateTimeOffset>($"SELECT [CreatedAt] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id} AND [CreatedById] = 1;"));
        }

        [Fact]
        public async Task The_blob_captures_hold_the_old_and_new_ids_on_insert_update_and_child_delete()
        {
            await fixture.SeedBlobsAsync(11, 12, 13, 14);
            WidgetPart part = NewPart(1, "a");
            part.DrawingId = 12;
            Widget widget = NewWidget("w1", "widget", part);
            widget.ImageId = 11;

            // --- Insert: no old id ---
            (string Widgets, string Parts) inserted = await CapturedAsync(widget, create: true);
            Assert.Equal(($"{widget.Id}:-:11", $"{part.Id}:-:12"), inserted);

            // --- Update: the old and the new id ---
            widget.ImageId = 13;
            part.DrawingId = 14;
            Assert.Equal(($"{widget.Id}:11:13", $"{part.Id}:12:14"), await CapturedAsync(widget));

            // --- Child delete: the old id, no new one; the root, touched, keeps its image ---
            widget.Parts = [];
            Assert.Equal(($"{widget.Id}:13:13", $"{part.Id}:14:-"), await CapturedAsync(widget));
        }

        [Fact]
        public async Task A_save_above_the_large_batch_threshold_recompiles_and_one_at_it_does_not()
        {
            Widget[] atThreshold = [.. Enumerable.Range(1, 1000).Select(static index => NewWidget(string.Create(CultureInfo.InvariantCulture, $"a{index}"), "widget"))];
            Widget[] above = [.. Enumerable.Range(1, 1001).Select(static index => NewWidget(string.Create(CultureInfo.InvariantCulture, $"b{index}"), "widget"))];

            Assert.False(await CreateRecompilingAsync(atThreshold));
            Assert.True(await CreateRecompilingAsync(above));
            Assert.Equal(2001, await QueryAsync<int>("SELECT COUNT(*) FROM [fixture].[Widgets];"));
        }

        [Fact]
        public async Task A_tree_payload_with_temporary_ids_is_inserted_with_its_references_resolved()
        {
            Node root = new() { Id = -1, Code = "root", Name = "root" };
            Node leaf = new() { Id = -2, Code = "leaf", Name = "leaf", ParentId = -1, Links = [new Link { OtherNodeId = -1 }] };

            SaveReceipt receipt = await CreateAsync(leaf, root);

            Assert.Equal(3, receipt.Inserted);
            Assert.Equal((root.Id, root.Id, leaf.Id), (leaf.ParentId, leaf.Links[0].OtherNodeId, leaf.Links[0].NodeId));
            Assert.Equal(
                string.Create(CultureInfo.InvariantCulture, $"/0/{root.Id}/|/0/{leaf.Id}/"),
                await QueryAsync<string>("SELECT STRING_AGG([Node].ToString(), '|') WITHIN GROUP (ORDER BY [Code] DESC) FROM [fixture].[Nodes];"));
        }

        /// <summary>A new widget.</summary>
        /// <param name="subject">The write-once subject, unique.</param>
        /// <param name="name">The name.</param>
        /// <param name="parts">The parts.</param>
        /// <returns>The widget.</returns>
        private static Widget NewWidget(string subject, string name, params WidgetPart[] parts)
        {
            return new Widget { Subject = subject, Name = name, Parts = [.. parts] };
        }

        /// <summary>A new part.</summary>
        /// <param name="ordinal">The ordinal, unique within its widget.</param>
        /// <param name="notes">The notes.</param>
        /// <returns>The part.</returns>
        private static WidgetPart NewPart(int ordinal, string notes)
        {
            return new WidgetPart { Ordinal = ordinal, Notes = notes };
        }

        /// <summary>A copy of a widget's own columns, as another client read them.</summary>
        /// <param name="widget">The widget.</param>
        /// <returns>The copy, with no collection supplied.</returns>
        private static Widget Copy(Widget widget)
        {
            return new Widget
            {
                Id = widget.Id,
                Subject = widget.Subject,
                Name = widget.Name,
                CreatedAt = widget.CreatedAt,
                CreatedById = widget.CreatedById,
                ModifiedAt = widget.ModifiedAt,
                ModifiedById = widget.ModifiedById,
            };
        }

        /// <summary>Reserves ids for new rows, then saves them: the reservation first, then the persist.</summary>
        /// <typeparam name="TEntity">The root entity.</typeparam>
        /// <param name="rows">The rows.</param>
        /// <returns>The receipt.</returns>
        private async Task<SaveReceipt> CreateAsync<TEntity>(params TEntity[] rows)
            where TEntity : class
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
            IIdAllocator allocator = scope.ServiceProvider.GetRequiredService<IIdAllocator>();

            IDataBatch validate = database.CreateBatch(BatchPurpose.Validate);
            using IdReservation reservation = allocator.Reserve(validate, typeof(TEntity), rows);
            if (!reservation.IsImmediate)
            {
                _ = await validate.ExecuteAsync(Token);
            }

            reservation.Assign(rows);
            IDataBatch persist = database.CreateBatch(BatchPurpose.Persist);
            SaveHandle handle = persist.Save(rows);
            _ = await persist.ExecuteAsync(Token);
            return handle.Receipt.Value;
        }

        /// <summary>Creates widgets and reports whether the save statement carried the recompile hint.</summary>
        /// <param name="widgets">The widgets.</param>
        /// <returns>Whether it recompiled.</returns>
        private async Task<bool> CreateRecompilingAsync(Widget[] widgets)
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
            IIdAllocator allocator = scope.ServiceProvider.GetRequiredService<IIdAllocator>();
            IDataBatch validate = database.CreateBatch(BatchPurpose.Validate);
            using IdReservation reservation = allocator.Reserve(validate, typeof(Widget), widgets);
            _ = await validate.ExecuteAsync(Token);
            reservation.Assign(widgets);

            var persist = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
            SaveHandle handle = persist.Save(widgets);
            _ = await persist.ExecuteAsync(Token);
            Assert.Equal(widgets.Length, handle.Receipt.Value.Inserted);
            return persist.Statements.Single(static statement => statement.Text.Contains("_saved", StringComparison.Ordinal))
                .Text.Contains("OPTION (RECOMPILE)", StringComparison.Ordinal);
        }

        /// <summary>Saves existing rows in a persist batch of their own.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="mode">The concurrency mode.</param>
        /// <param name="gated">Whether the batch then waits on the test's application lock, inside its transaction.</param>
        /// <returns>The receipt.</returns>
        private async Task<SaveReceipt> SaveAsync(Widget[] rows, ConcurrencyMode mode = ConcurrencyMode.Check, bool gated = false)
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            IDataBatch persist = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            SaveHandle handle = persist.Save(rows, mode);
            if (gated)
            {
                _ = persist.Sql($"DECLARE @gate int; EXEC @gate = sp_getapplock @Resource = N'fx-race', @LockMode = 'Shared', @LockOwner = 'Transaction', @LockTimeout = 60000;", null);
            }

            _ = await persist.ExecuteAsync(Token);
            return handle.Receipt.Value;
        }

        /// <summary>Saves a widget, then reads both blob captures back in the same batch.</summary>
        /// <param name="widget">The widget.</param>
        /// <param name="create">Whether the widget and its parts are new, so ids are reserved first.</param>
        /// <returns>Each capture's rows, <c>row:old:new</c> with <c>-</c> for none.</returns>
        private async Task<(string Widgets, string Parts)> CapturedAsync(Widget widget, bool create = false)
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
            IDataBatch validate = database.CreateBatch(BatchPurpose.Validate);
            using IdReservation? reservation = create ? scope.ServiceProvider.GetRequiredService<IIdAllocator>().Reserve(validate, typeof(Widget), [widget]) : null;
            if (reservation is not null)
            {
                if (!reservation.IsImmediate)
                {
                    _ = await validate.ExecuteAsync(Token);
                }

                reservation.Assign([widget]);
            }

            IDataBatch persist = database.CreateBatch(BatchPurpose.Persist);
            SaveHandle handle = persist.Save([widget]);
            BatchResult<RawResult> widgets = persist.Sql(Rows(handle.Captures[0].Identifier), new SqlOptions(resultSets: 1));
            BatchResult<RawResult> parts = persist.Sql(Rows(handle.Captures[1].Identifier), new SqlOptions(resultSets: 1));
            _ = await persist.ExecuteAsync(Token);

            Assert.Equal(
                [new ColumnCapture(new TableName("fixture", "Widgets"), "ImageId", handle.Captures[0].Identifier), new ColumnCapture(new TableName("fixture", "WidgetParts"), "DrawingId", handle.Captures[1].Identifier)],
                handle.Captures);
            return (Text(widgets.Value), Text(parts.Value));

            static FormattableString Rows(SqlIdentifier capture)
            {
                return $"SELECT STRING_AGG(CONCAT([RowId], ':', ISNULL(CAST([OldBlobId] AS varchar(11)), '-'), ':', ISNULL(CAST([NewBlobId] AS varchar(11)), '-')), ',') WITHIN GROUP (ORDER BY [RowId]) AS [Rows] FROM {capture};";
            }

            static string Text(RawResult result)
            {
                return ((string[])result.ResultSets[0].GetBuffer(0))[0];
            }
        }

        /// <summary>The stored stamp of a widget.</summary>
        /// <param name="id">The widget.</param>
        /// <returns>The stamp.</returns>
        private Task<DateTimeOffset> StoredStampAsync(int id)
        {
            return ScalarAsync<DateTimeOffset>($"SELECT [ModifiedAt] FROM [fixture].[Widgets] WHERE [Id] = {id};");
        }

        /// <summary>The history rows of a widget.</summary>
        /// <param name="id">The widget.</param>
        /// <returns>The count.</returns>
        private Task<int> HistoryCountAsync(int id)
        {
            return ScalarAsync<int>($"SELECT COUNT(*) FROM [fixture].[WidgetsHistory] WHERE [Id] = {id};");
        }

        /// <summary>Runs a scalar query against the fixture database.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="sql">The query.</param>
        /// <returns>The result.</returns>
        private Task<T> ScalarAsync<T>(FormattableString sql)
        {
            return QueryAsync<T>(sql.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Runs a scalar query of fixed text against the fixture database.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="sql">The query.</param>
        /// <returns>The result.</returns>
        private async Task<T> QueryAsync<T>(string sql)
        {
            return (await SqlServerFixture.ScalarAsync<T>(fixture.ConnectionString, sql))!;
        }

        /// <summary>Runs a statement on the test's own connection.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="sql">The statement.</param>
        /// <returns>A task that completes when it ran.</returns>
        private static async Task ExecuteOnAsync(Microsoft.Data.SqlClient.SqlConnection connection, string sql)
        {
            await using Microsoft.Data.SqlClient.SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            _ = await command.ExecuteNonQueryAsync(Token);
        }

        /// <summary>Polls a count until it is positive: another session reached the wait the test needs.</summary>
        /// <param name="countSql">The count.</param>
        /// <returns>A task that completes when the count is positive.</returns>
        private async Task WaitForAsync(string countSql)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, countSql) == 0)
            {
                Assert.True(DateTime.UtcNow < deadline, $"No session reached the expected wait: {countSql}");
                await Task.Delay(20, Token);
            }
        }
    }
}
