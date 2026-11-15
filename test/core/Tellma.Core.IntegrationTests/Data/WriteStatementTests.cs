// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Trees;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     The update, delete and tree statements against a fixture database: the not-found, row
    ///     security and stamp checks; the cap and the count of a query's keys; children, siblings and
    ///     blob captures gone with their root in one round trip; a referenced row; a tree's paths,
    ///     cycle fence, depth limit and counts after a save, an activation and a delete with
    ///     descendants; and the verify statement repairing drift.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class WriteStatementTests(SaveDatabase fixture) : IClassFixture<SaveDatabase>, IAsyncLifetime
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
        public async Task A_delete_by_ids_of_a_missing_or_a_hidden_row_is_not_found_and_deletes_nothing()
        {
            Widget shown = NewWidget("w1", "shown");
            Widget hidden = NewWidget("w2", "hidden");
            _ = await CreateAsync(shown, hidden);

            BatchAssertionFailedException absent = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByIds([shown.Id, 999_999], null, null))));
            BatchAssertionFailedException concealed = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByIds([shown.Id, hidden.Id], FilterTree.Leaf("Name != 'hidden'"), null))));

            Assert.Equal((TellmaSqlErrors.NotFound, TellmaSqlErrors.NotFoundCode), (absent.Number, absent.Code));
            Assert.Equal([999_999L], absent.MissingIds);
            Assert.Equal([hidden.Id], concealed.MissingIds);
            Assert.Equal(2, await CountAsync("[fixture].[Widgets]"));
        }

        [Fact]
        public async Task A_delete_by_ids_of_a_row_outside_the_grant_filter_is_refused_and_deletes_nothing()
        {
            Widget granted = NewWidget("w1", "granted");
            Widget other = NewWidget("w2", "other");
            _ = await CreateAsync(granted, other);

            RowSecurityException refusal = await Assert.ThrowsAsync<RowSecurityException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByIds([granted.Id, other.Id], null, FilterTree.Leaf("Name = 'granted'")))));

            Assert.NotNull(refusal.StatementOrdinal);
            Assert.Equal(2, await CountAsync("[fixture].[Widgets]"));
        }

        [Fact]
        public async Task A_delete_with_a_stale_stamp_is_a_conflict_that_names_the_last_modifier()
        {
            Widget widget = NewWidget("w1", "widget");
            _ = await CreateAsync(widget);
            DateTimeOffset read = widget.ModifiedAt;
            BatchResult<UpdateReceipt>? renamed = null;
            _ = await RunAsync(batch => renamed = batch.Update(new UpdateSpec<Widget>.ByIds([widget.Id], null, null, new Dictionary<string, object?> { ["Name"] = "renamed" })));
            DateTimeOffset stored = await StampAsync(widget.Id);

            ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByIds([widget.Id], null, null, [read]))));

            ConflictRow row = Assert.Single(conflict.Conflicts);
            Assert.Equal((widget.Id, false, stored, WellKnownIds.SystemUserId), (row.Id, row.IsMissing, row.ModifiedAt, row.ModifiedById));
            Assert.Equal([widget.Id], renamed!.Value.UpdatedIds);

            // --- The stamp it now holds deletes it ---
            _ = await RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByIds([widget.Id], null, null, [stored])));
            Assert.Equal(0, await CountAsync("[fixture].[Widgets]"));
        }

        [Fact]
        public async Task A_delete_takes_the_children_grandchildren_and_sibling_with_its_root_and_captures_their_blobs_in_one_round_trip()
        {
            await fixture.SeedBlobsAsync(11, 12);
            WidgetPart part = new() { Ordinal = 1, Notes = "a", DrawingId = 12, PartNotes = [new WidgetPartNote { Text = "note" }] };
            Widget widget = NewWidget("w1", "widget", part);
            widget.ImageId = 11;
            Widget kept = NewWidget("w2", "kept", new WidgetPart { Ordinal = 1, Notes = "b" });
            _ = await CreateAsync(widget, kept);
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [fixture].[WidgetExtras] ([Id], [Note]) VALUES ({widget.Id}, N'extra');"));

            // --- The delete, and the captures read back in the same batch ---
            DeleteHandle? handle = null;
            BatchResult<RawResult>? captured = null;
            BatchOutcome outcome = await RunAsync(batch =>
            {
                handle = batch.Delete(new DeleteSpec<Widget>.ByIds([widget.Id], null, null));
                captured = batch.Sql(
                    $"SELECT (SELECT CONCAT([RowId], ':', [OldBlobId], ':', ISNULL(CAST([NewBlobId] AS varchar(11)), '-')) FROM {handle.Captures[0].Identifier}) + '|' + (SELECT CONCAT([RowId], ':', [OldBlobId], ':', ISNULL(CAST([NewBlobId] AS varchar(11)), '-')) FROM {handle.Captures[1].Identifier}) AS [Captured];",
                    new SqlOptions(resultSets: 1));
            });

            Assert.Equal(1, outcome.RoundTrips);
            Assert.Equal([widget.Id], handle!.Receipt.Value.DeletedIds);
            Assert.Equal($"{widget.Id}:11:-|{part.Id}:12:-", ((string[])captured!.Value.ResultSets[0].GetBuffer(0))[0]);
            Assert.Equal((1, 1, 0, 0), (await CountAsync("[fixture].[Widgets]"), await CountAsync("[fixture].[WidgetParts]"), await CountAsync("[fixture].[WidgetPartNotes]"), await CountAsync("[fixture].[WidgetExtras]")));
        }

        [Fact]
        public async Task A_delete_by_query_past_its_cap_or_of_another_count_is_refused_and_one_that_matches_deletes_its_rows()
        {
            _ = await CreateAsync(NewWidget("w1", "x"), NewWidget("w2", "x"), NewWidget("w3", "x"), NewWidget("w4", "y"));
            var named = FilterTree.Leaf("Name = 'x'");

            BatchAssertionFailedException capped = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByQuery(named, null, Cap: 2, ExpectedCount: 3))));
            BatchAssertionFailedException miscounted = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Widget>.ByQuery(named, null, Cap: 5, ExpectedCount: 2))));
            Assert.Equal(4, await CountAsync("[fixture].[Widgets]"));

            DeleteHandle? handle = null;
            BatchOutcome outcome = await RunAsync(batch => handle = batch.Delete(new DeleteSpec<Widget>.ByQuery(named, null, Cap: 5, ExpectedCount: 3)));

            Assert.Equal((TellmaSqlErrors.LimitExceeded, TellmaSqlErrors.DeleteCapExceededCode), (capped.Number, capped.Code));
            Assert.Equal((TellmaSqlErrors.CountMismatch, TellmaSqlErrors.DeleteCountMismatchCode), (miscounted.Number, miscounted.Code));
            Assert.Equal((1, 3), (outcome.RoundTrips, handle!.Receipt.Value.DeletedIds.Count));
            Assert.Equal(1, await CountAsync("[fixture].[Widgets]"));
        }

        [Fact]
        public async Task A_row_another_row_references_is_a_foreign_key_violation()
        {
            Node target = NewNode("target", null);
            _ = await CreateAsync(target);
            Node referrer = NewNode("referrer", null);
            referrer.Links = [new Link { OtherNodeId = target.Id }];
            _ = await CreateAsync(referrer);

            ForeignKeyViolationException violation = await Assert.ThrowsAsync<ForeignKeyViolationException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Node>.ByIds([target.Id], null, null))));

            Assert.Contains("Links", violation.ConstraintName, StringComparison.Ordinal);

            // The referrer goes with its own link.
            _ = await RunAsync(batch => batch.Delete(new DeleteSpec<Node>.ByIds([referrer.Id], null, null)));
            Assert.Equal((1, 0), (await CountAsync("[fixture].[Nodes]"), await CountAsync("[fixture].[Links]")));
        }

        [Fact]
        public async Task An_all_or_nothing_update_refuses_a_missing_row_and_a_row_outside_its_grant()
        {
            Widget first = NewWidget("w1", "a");
            Widget second = NewWidget("w2", "b");
            _ = await CreateAsync(first, second);
            Dictionary<string, object?> rename = new() { ["Name"] = "c" };

            BatchAssertionFailedException missing = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Update(new UpdateSpec<Widget>.ByIds([first.Id, 999_999], null, null, rename, AllOrNothing: true))));
            _ = await Assert.ThrowsAsync<RowSecurityException>(
                () => RunAsync(batch => batch.Update(new UpdateSpec<Widget>.ByIds([first.Id, second.Id], null, FilterTree.Leaf("Name = 'a'"), rename, AllOrNothing: true))));
            BatchAssertionFailedException capped = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Update(new UpdateSpec<Widget>.ByQuery(FilterTree.Leaf("Name != 'z'"), null, rename, Cap: 1, ExpectedCount: 2))));

            Assert.Equal([999_999L], missing.MissingIds);
            Assert.Equal(TellmaSqlErrors.UpdateCapExceededCode, capped.Code);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Name] = 'c';"));

            // --- Without all-or-nothing, the rows outside the grant are skipped ---
            BatchResult<UpdateReceipt>? receipt = null;
            _ = await RunAsync(batch => receipt = batch.Update(new UpdateSpec<Widget>.ByIds([first.Id, second.Id], null, FilterTree.Leaf("Name = 'a'"), rename)));
            Assert.Equal([first.Id], receipt!.Value.UpdatedIds);
        }

        [Fact]
        public async Task A_repeated_deactivation_writes_no_row_and_no_history_and_stamps_once()
        {
            Widget widget = NewWidget("w1", "widget");
            _ = await CreateAsync(widget);
            Dictionary<string, object?> deactivate = new() { ["IsActive"] = false };

            BatchResult<UpdateReceipt>? first = null;
            BatchResult<UpdateReceipt>? again = null;
            BatchOutcome outcome = await RunAsync(batch => first = batch.Update(new UpdateSpec<Widget>.ByIds([widget.Id], null, null, deactivate)));
            DateTimeOffset stamped = await StampAsync(widget.Id);
            _ = await RunAsync(batch => again = batch.Update(new UpdateSpec<Widget>.ByIds([widget.Id], null, null, deactivate)));

            Assert.Equal(1, outcome.RoundTrips);
            Assert.Equal([widget.Id], first!.Value.UpdatedIds);
            Assert.Empty(again!.Value.UpdatedIds);
            Assert.Equal(stamped, await StampAsync(widget.Id));
            Assert.NotEqual(widget.ModifiedAt, stamped);
            Assert.Equal(1, await ScalarAsync<int>(string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [fixture].[WidgetsHistory] WHERE [Id] = {widget.Id};")));
        }

        [Fact]
        public async Task A_tree_save_paths_its_rows_by_id_and_a_move_repaths_the_subtree_and_recounts_both_ancestors()
        {
            Node root = NewNode("root", null);
            Node other = NewNode("other", null);
            _ = await CreateAsync(root, other);
            Node child = NewNode("child", root.Id);
            _ = await CreateAsync(child);
            Node grandchild = NewNode("grandchild", child.Id);
            _ = await CreateAsync(grandchild);
            Assert.Equal($"/{root.Id}/{child.Id}/{grandchild.Id}/", await PathAsync(grandchild.Id));

            child.ParentId = other.Id;
            _ = await SaveAsync(child);

            Assert.Equal($"/{other.Id}/{child.Id}/", await PathAsync(child.Id));
            Assert.Equal($"/{other.Id}/{child.Id}/{grandchild.Id}/", await PathAsync(grandchild.Id));
            Assert.Equal("child:2:2|grandchild:1:1|other:3:3|root:1:1", await CountsAsync());
        }

        [Fact]
        public async Task A_saved_row_under_another_moved_row_of_its_payload_is_pathed_once_under_the_new_parent()
        {
            // root > a > b > c, and other: the payload moves a under other and renames c, whose parent b is not in the payload.
            Node root = NewNode("root", null);
            Node other = NewNode("other", null);
            _ = await CreateAsync(root, other);
            Node a = NewNode("a", root.Id);
            _ = await CreateAsync(a);
            Node b = NewNode("b", a.Id);
            _ = await CreateAsync(b);
            Node c = NewNode("c", b.Id);
            _ = await CreateAsync(c);

            a.ParentId = other.Id;
            c.Name = "renamed";
            _ = await SaveAsync(a, c);

            Assert.Equal($"/{other.Id}/{a.Id}/", await PathAsync(a.Id));
            Assert.Equal($"/{other.Id}/{a.Id}/{b.Id}/", await PathAsync(b.Id));
            Assert.Equal($"/{other.Id}/{a.Id}/{b.Id}/{c.Id}/", await PathAsync(c.Id));
            Assert.Equal("renamed", await ScalarAsync<string>(string.Create(CultureInfo.InvariantCulture, $"SELECT [Name] FROM [fixture].[Nodes] WHERE [Id] = {c.Id};")));
            Assert.Equal("a:3:3|b:2:2|c:1:1|other:4:4|root:1:1", await CountsAsync());
        }

        [Fact]
        public async Task An_update_by_query_of_another_count_is_refused_and_one_that_matches_updates_its_rows()
        {
            Widget first = NewWidget("w1", "a");
            Widget second = NewWidget("w2", "b");
            Widget third = NewWidget("w3", "z");
            _ = await CreateAsync(first, second, third);
            Dictionary<string, object?> rename = new() { ["Name"] = "c" };
            var query = FilterTree.Leaf("Name != 'z'");

            // --- Another count than expected, under the cap: refused, nothing written ---
            BatchAssertionFailedException mismatch = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Update(new UpdateSpec<Widget>.ByQuery(query, null, rename, Cap: 5, ExpectedCount: 3))));
            Assert.Equal(TellmaSqlErrors.UpdateCountMismatchCode, mismatch.Code);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Name] = 'c';"));

            // --- The expected count, stamped: the matching rows take the value, the others keep theirs ---
            BatchResult<UpdateReceipt>? receipt = null;
            _ = await RunAsync(batch => receipt = batch.Update(new UpdateSpec<Widget>.ByQuery(query, null, rename, Cap: 2, ExpectedCount: 2)));
            Assert.Equal([first.Id, second.Id], receipt!.Value.UpdatedIds.Order());
            Assert.Equal("w1:c|w2:c|w3:z", await ScalarAsync<string>("SELECT STRING_AGG(CONCAT([Subject], ':', [Name]), '|') WITHIN GROUP (ORDER BY [Subject]) FROM [fixture].[Widgets];"));
            Assert.NotEqual(first.ModifiedAt, await StampAsync(first.Id));
        }

        [Fact]
        public async Task A_save_that_closes_a_cycle_is_refused_by_the_fence()
        {
            Node root = NewNode("root", null);
            _ = await CreateAsync(root);
            Node child = NewNode("child", root.Id);
            _ = await CreateAsync(child);

            root.ParentId = child.Id;
            TreeCycleException cycle = await Assert.ThrowsAsync<TreeCycleException>(() => SaveAsync(root));

            Assert.Equal(typeof(Node), cycle.EntityType);
            Assert.Equal($"/{root.Id}/", await PathAsync(root.Id));
        }

        [Fact]
        public async Task A_chain_at_the_depth_limit_is_saved_and_one_level_deeper_is_refused()
        {
            // The fixture's tree is eight levels deep at most.
            Node[] chain = [.. Enumerable.Range(1, 9).Select(static level => NewNode(string.Create(CultureInfo.InvariantCulture, $"n{level}"), null))];
            _ = await CreateAsync(chain[0]);
            for (int level = 1; level < 8; level++)
            {
                chain[level].ParentId = chain[level - 1].Id;
                _ = await CreateAsync(chain[level]);
            }

            chain[8].ParentId = chain[7].Id;
            TreeDepthExceededException deep = await Assert.ThrowsAsync<TreeDepthExceededException>(() => CreateAsync(chain[8]));

            Assert.Equal(typeof(Node), deep.EntityType);
            Assert.Equal(8, await CountAsync("[fixture].[Nodes]"));
            Assert.Equal(8, await ScalarAsync<int>(string.Create(CultureInfo.InvariantCulture, $"SELECT [SubtreeCount] FROM [fixture].[Nodes] WHERE [Id] = {chain[0].Id};")));
        }

        [Fact]
        public async Task A_deactivation_recounts_the_active_counts_of_the_ancestors_in_one_round_trip()
        {
            (Node root, Node child, _) = await ChainAsync();

            BatchOutcome outcome = await RunAsync(batch => batch.Update(new UpdateSpec<Node>.ByIds([child.Id], null, null, new Dictionary<string, object?> { ["IsActive"] = false })));
            Assert.Equal(1, outcome.RoundTrips);
            Assert.Equal("child:2:1|grandchild:1:1|root:3:2", await CountsAsync());

            _ = await RunAsync(batch => batch.Update(new UpdateSpec<Node>.ByIds([child.Id, root.Id], null, null, new Dictionary<string, object?> { ["IsActive"] = true })));
            Assert.Equal("child:2:2|grandchild:1:1|root:3:3", await CountsAsync());
        }

        [Fact]
        public async Task A_delete_with_descendants_refuses_a_hidden_root_or_a_closure_outside_its_grant_and_else_deletes_the_subtree()
        {
            (Node root, Node child, Node grandchild) = await ChainAsync();

            BatchAssertionFailedException hidden = await Assert.ThrowsAsync<BatchAssertionFailedException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Node>.WithDescendants([child.Id], FilterTree.Leaf("Code != 'child'"), null))));
            _ = await Assert.ThrowsAsync<RowSecurityException>(
                () => RunAsync(batch => batch.Delete(new DeleteSpec<Node>.WithDescendants([child.Id], null, FilterTree.Leaf("Code != 'grandchild'")))));
            Assert.Equal([child.Id], hidden.MissingIds);
            Assert.Equal(3, await CountAsync("[fixture].[Nodes]"));

            DeleteHandle? handle = null;
            BatchOutcome outcome = await RunAsync(batch => handle = batch.Delete(new DeleteSpec<Node>.WithDescendants([child.Id], null, null)));

            Assert.Equal(1, outcome.RoundTrips);
            Assert.Equal([child.Id, grandchild.Id], handle!.Receipt.Value.DeletedIds.Order());
            Assert.Equal("root:1:1", await CountsAsync());
            Assert.Equal($"/{root.Id}/", await PathAsync(root.Id));
        }

        [Fact]
        public async Task A_delete_of_a_parent_without_its_descendants_is_a_foreign_key_violation()
        {
            (Node root, _, _) = await ChainAsync();

            _ = await Assert.ThrowsAsync<ForeignKeyViolationException>(() => RunAsync(batch => batch.Delete(new DeleteSpec<Node>.ByIds([root.Id], null, null))));

            Assert.Equal(3, await CountAsync("[fixture].[Nodes]"));
        }

        [Fact]
        public async Task The_verify_statement_repairs_drifted_counts_once_logs_the_repair_and_then_finds_none()
        {
            (Node root, _, _) = await ChainAsync();
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [fixture].[Nodes] SET [SubtreeCount] = 9, [ActiveSubtreeCount] = 0 WHERE [Id] = {root.Id};"));

            int repaired = await VerifyAsync();
            int again = await VerifyAsync();

            Assert.Equal((1, 0), (repaired, again));
            Assert.Equal("child:2:2|grandchild:1:1|root:3:3", await CountsAsync());
            FakeLogRecord record = Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "TreeVerify.Repaired");
            Assert.Equal(LogLevel.Warning, record.Level);
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

        /// <summary>A new node.</summary>
        /// <param name="code">The unique code, also its name.</param>
        /// <param name="parentId">The parent, or null for a root.</param>
        /// <returns>The node.</returns>
        private static Node NewNode(string code, int? parentId)
        {
            return new Node { Code = code, Name = code, ParentId = parentId };
        }

        /// <summary>Creates the chain root, child and grandchild, one save each.</summary>
        /// <returns>The nodes.</returns>
        private async Task<(Node Root, Node Child, Node Grandchild)> ChainAsync()
        {
            Node root = NewNode("root", null);
            _ = await CreateAsync(root);
            Node child = NewNode("child", root.Id);
            _ = await CreateAsync(child);
            Node grandchild = NewNode("grandchild", child.Id);
            _ = await CreateAsync(grandchild);
            return (root, child, grandchild);
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

        /// <summary>Saves existing rows in a persist batch of their own.</summary>
        /// <typeparam name="TEntity">The root entity.</typeparam>
        /// <param name="rows">The rows.</param>
        /// <returns>The receipt.</returns>
        private async Task<SaveReceipt> SaveAsync<TEntity>(params TEntity[] rows)
            where TEntity : class
        {
            SaveHandle? handle = null;
            _ = await RunAsync(batch => handle = batch.Save(rows));
            return handle!.Receipt.Value;
        }

        /// <summary>Composes and runs one persist batch.</summary>
        /// <param name="compose">Appends the batch's statements.</param>
        /// <returns>The batch's outcome.</returns>
        private async Task<BatchOutcome> RunAsync(Action<IDataBatch> compose)
        {
            await using AsyncServiceScope scope = CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            compose(batch);
            return await batch.ExecuteAsync(Token);
        }

        /// <summary>
        ///     Opens a scope of the system user whose request context carries the tenant's settings,
        ///     which the update and delete statements compile their filters under.
        /// </summary>
        /// <returns>The scope.</returns>
        private AsyncServiceScope CreateScope()
        {
            AsyncServiceScope scope = fixture.CreateScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            holder.Set(holder.Current with
            {
                TenantSettings = new TenantSettings
                {
                    TenantId = FixtureTenant.TenantId,
                    Tag = VersionTag.New(),
                    Names = ["Fixture", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false)],
                    Shape = MultilingualShape.All,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = DateTimeOffset.UnixEpoch,
                    Entries = new Dictionary<string, System.Text.Json.JsonElement>(),
                },
            });
            return scope;
        }

        /// <summary>Runs the verify statement of the node tree in a maintenance batch of its own.</summary>
        /// <returns>How many rows it repaired.</returns>
        private async Task<int> VerifyAsync()
        {
            await using AsyncServiceScope scope = CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            EntityMetadata nodes = scope.ServiceProvider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Node));
            BatchResult<int> repaired = fixture.Tenant.Services.GetRequiredService<TreeStatements>().Verify(batch, nodes);
            BatchOutcome outcome = await batch.ExecuteAsync(Token);
            Assert.Equal(1, outcome.RoundTrips);
            return repaired.Value;
        }

        /// <summary>The node path of a node.</summary>
        /// <param name="id">The node.</param>
        /// <returns>The path.</returns>
        private Task<string> PathAsync(int id)
        {
            return ScalarAsync<string>(string.Create(CultureInfo.InvariantCulture, $"SELECT [Node].ToString() FROM [fixture].[Nodes] WHERE [Id] = {id};"));
        }

        /// <summary>Every node's counts, <c>code:subtree:active</c>, in code order.</summary>
        /// <returns>The counts.</returns>
        private Task<string> CountsAsync()
        {
            return ScalarAsync<string>("SELECT STRING_AGG(CONCAT([Code], ':', [SubtreeCount], ':', [ActiveSubtreeCount]), '|') WITHIN GROUP (ORDER BY [Code]) FROM [fixture].[Nodes];");
        }

        /// <summary>The stored stamp of a widget.</summary>
        /// <param name="id">The widget.</param>
        /// <returns>The stamp.</returns>
        private Task<DateTimeOffset> StampAsync(int id)
        {
            return ScalarAsync<DateTimeOffset>(string.Create(CultureInfo.InvariantCulture, $"SELECT [ModifiedAt] FROM [fixture].[Widgets] WHERE [Id] = {id};"));
        }

        /// <summary>The rows of a table.</summary>
        /// <param name="table">The table, delimited.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(string table)
        {
            return ScalarAsync<int>($"SELECT COUNT(*) FROM {table};");
        }

        /// <summary>Runs a scalar query against the fixture database.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="sql">The query.</param>
        /// <returns>The result.</returns>
        private async Task<T> ScalarAsync<T>(string sql)
        {
            return (await SqlServerFixture.ScalarAsync<T>(fixture.ConnectionString, sql))!;
        }
    }
}
