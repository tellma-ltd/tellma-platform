// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Data;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Data.Saves;

namespace Tellma.Core.Tests.Data.Writes
{
    /// <summary>
    ///     The delete statement a batch composes, as golden SQL: by ids with its checks and stamps, by
    ///     query, a tree's roots with their descendants, a tree's rows by ids; the captures and the
    ///     keys the handle names; the receipt, the conflicts and the metrics read from canned result
    ///     sets; and the refusals at compose time.
    /// </summary>
    public sealed class DeleteStatementTests : IDisposable
    {
        /// <summary>The composition over the save model.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void A_delete_by_ids_captures_its_keys_then_checks_the_grant_and_the_stamps_and_deletes_children_first()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            DeleteHandle handle = batch.Delete(new DeleteSpec<Crate>.ByIds(
                [1, 2], FilterTree.Leaf("Name != 'hidden'"), FilterTree.Leaf("OuterId > 0"), [DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch]));

            // --- The keys: the existing rows the read filter shows, U-locked; every other id is missing ---
            Assert.Equal(2, batch.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT t.[Id] FROM [sv].[Crates] AS t WITH (UPDLOCK, ROWLOCK) JOIN @tb0_t0 AS s ON s.[Id] = t.[Id]
                WHERE t.[Id] IN (
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Crates] AS [T]
                WHERE (([T].[Name] <> @qx0_p0) AND ([T].[Id] IN (SELECT [Id] FROM @tb0_t0)))
                );
                SELECT s.[Id] FROM @tb0_t0 AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM @tb0_keys);
                IF @@ROWCOUNT > 0 THROW 50404, N'Entity.NotFound', 1;

                """,
                batch.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@tb0_t0", "@qx0_p0"], batch.Statements[0].Parameters.Select(static parameter => parameter.Name));
            Assert.Empty(batch.Statements[0].Writes);

            // --- The delete: the grant, the stamps, the grandchildren, the children, the root ---
            Assert.Equal(
                """
                DECLARE @tb1_blob_sv_Crates_LabelId TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);
                DECLARE @tb1_blob_sv_Compartments_PhotoId TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);
                DECLARE @tb1_n int;
                SELECT @tb1_n = COUNT(*) FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Crates] AS [T]
                WHERE ((([T].[OuterId] IS NOT NULL) AND ([T].[OuterId] > @qx1_p0)) AND ([T].[Id] IN (SELECT [Id] FROM @tb0_keys)))
                ) AS [q];
                IF @tb1_n <> (SELECT COUNT(*) FROM @tb0_keys) THROW 50403, N'RowSecurity', 1;
                DECLARE @tb1_conflicts TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb1_conflicts ([Id]) SELECT t.[Id] FROM [sv].[Crates] AS t JOIN @tb0_t0 AS s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt];
                IF EXISTS (SELECT 1 FROM @tb1_conflicts)
                BEGIN
                    SELECT c.[Id], 'C' AS [Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]
                    FROM @tb1_conflicts AS c JOIN [sv].[Crates] AS t ON t.[Id] = c.[Id] LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];
                    DECLARE @tb1_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb1_conflicts) AS nvarchar(10))
                        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP (90) [Id] FROM @tb1_conflicts ORDER BY [Id]) AS c), N'')
                        + N'],"missing":[]}';
                    THROW 50409, @tb1_msg, 1;
                END;
                DELETE c
                FROM [sv].[Items] AS c WHERE c.[CompartmentId] IN (SELECT x.[Id] FROM [sv].[Compartments] AS x WHERE x.[CrateId] IN (SELECT [Id] FROM @tb0_keys));
                DELETE c
                OUTPUT deleted.[Id], deleted.[PhotoId], NULL INTO @tb1_blob_sv_Compartments_PhotoId ([RowId], [OldBlobId], [NewBlobId])
                FROM [sv].[Compartments] AS c WHERE c.[CrateId] IN (SELECT [Id] FROM @tb0_keys);
                DELETE t
                OUTPUT deleted.[Id], deleted.[LabelId], NULL INTO @tb1_blob_sv_Crates_LabelId ([RowId], [OldBlobId], [NewBlobId])
                OUTPUT deleted.[Id]
                FROM [sv].[Crates] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys);

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@qx1_p0"], batch.Statements[1].Parameters.Select(static parameter => parameter.Name));

            // The sibling goes with its owner by cascade, so the delete writes its table too.
            Assert.Equal(
                [new TableName("sv", "Compartments"), new TableName("sv", "CrateExtras"), new TableName("sv", "Crates"), new TableName("sv", "Items")],
                batch.Statements[1].Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.All(batch.Statements, static statement => Assert.Equal(typeof(Crate), statement.EntityType));

            // --- The handle: the keys and one capture per blob column, which a later statement may name ---
            Assert.Equal("@tb0_keys", handle.DeletedIds.Name);
            Assert.Equal(
                [
                    new ColumnCapture(new TableName("sv", "Crates"), "LabelId", new SqlIdentifier("@tb1_blob_sv_Crates_LabelId")),
                    new ColumnCapture(new TableName("sv", "Compartments"), "PhotoId", new SqlIdentifier("@tb1_blob_sv_Compartments_PhotoId")),
                ],
                handle.Captures);
            _ = batch.Sql($"SELECT [RowId] FROM {handle.Captures[1].Identifier} UNION ALL SELECT [Id] FROM {handle.DeletedIds};", new SqlOptions(resultSets: 1));
        }

        [Fact]
        public void A_delete_by_query_caps_and_counts_its_keys_in_one_statement()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Ledger>.ByQuery(FilterTree.Leaf("Memo = 'x'"), null, Cap: 10, ExpectedCount: 3));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] bigint PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Ledgers] AS [T]
                WHERE (([T].[Memo] IS NOT NULL) AND ([T].[Memo] = @qx0_p0))
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                ;
                IF (SELECT COUNT(*) FROM @tb0_keys) > @tb0_p0 THROW 50413, N'Delete.CapExceeded', 1;
                IF (SELECT COUNT(*) FROM @tb0_keys) <> @tb0_p1 THROW 50428, N'Delete.CountMismatch', 1;
                DELETE t
                OUTPUT deleted.[Id]
                FROM [sv].[Ledgers] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys);

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@tb0_p0", "@tb0_p1", "@qx0_p0", "@qx0_take"], statement.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal((10, 3, 11), (Scalar(statement, "@tb0_p0"), Scalar(statement, "@tb0_p1"), Scalar(statement, "@qx0_take")));
        }

        [Fact]
        public void A_delete_with_descendants_checks_its_roots_then_deletes_their_closure_and_recounts_the_ancestors()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Area>.WithDescendants([3], null, FilterTree.Leaf("IsActive")));

            Assert.Equal(2, batch.Statements.Count);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                SELECT s.[Id] FROM @tb0_t0 AS s WHERE s.[Id] NOT IN (
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Areas] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_t0))
                );
                IF @@ROWCOUNT > 0 THROW 50404, N'Entity.NotFound', 1;
                INSERT INTO @tb0_keys ([Id])
                SELECT d.[Id] FROM [sv].[Areas] AS d WITH (UPDLOCK, ROWLOCK)
                WHERE EXISTS (SELECT 1 FROM [sv].[Areas] AS a WITH (UPDLOCK, ROWLOCK) JOIN @tb0_t0 AS s ON s.[Id] = a.[Id]
                              WHERE d.[Node].IsDescendantOf(a.[Node]) = 1);

                """,
                batch.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                """
                DECLARE @tb1_n int;
                SELECT @tb1_n = COUNT(*) FROM (
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Areas] AS [T]
                WHERE (([T].[IsActive] = 1) AND ([T].[Id] IN (SELECT [Id] FROM @tb0_keys)))
                ) AS [q];
                IF @tb1_n <> (SELECT COUNT(*) FROM @tb0_keys) THROW 50403, N'RowSecurity', 1;
                DECLARE @tb1_old TABLE ([Node] hierarchyid NOT NULL);
                INSERT INTO @tb1_old ([Node]) SELECT t.[Node] FROM [sv].[Areas] AS t JOIN @tb0_keys AS a ON a.[Id] = t.[Id];
                DELETE t
                OUTPUT deleted.[Id]
                FROM [sv].[Areas] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys);
                DECLARE @tb1_anc TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);
                WITH tb1_anc ([Node]) AS (
                    SELECT [Node] FROM @tb1_old
                    UNION ALL SELECT x.[Node].GetAncestor(1) FROM tb1_anc AS x WHERE x.[Node].GetLevel() > 1)
                INSERT INTO @tb1_anc ([Node]) SELECT DISTINCT [Node] FROM tb1_anc
                OPTION (MAXRECURSION 31);
                UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
                FROM [sv].[Areas] AS t
                JOIN @tb1_anc AS n ON n.[Node] = t.[Node]
                CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
                             FROM [sv].[Areas] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void A_tree_delete_by_ids_keeps_the_old_nodes_and_recounts_a_plain_tree_within_its_depth()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Region>.ByIds([3], null, null));

            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT t.[Id] FROM [sv].[Regions] AS t WITH (UPDLOCK, ROWLOCK) JOIN @tb0_t0 AS s ON s.[Id] = t.[Id];
                SELECT s.[Id] FROM @tb0_t0 AS s WHERE s.[Id] NOT IN (SELECT [Id] FROM @tb0_keys);
                IF @@ROWCOUNT > 0 THROW 50404, N'Entity.NotFound', 1;

                """,
                batch.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                """
                DECLARE @tb1_old TABLE ([Node] hierarchyid NOT NULL);
                INSERT INTO @tb1_old ([Node]) SELECT t.[Node] FROM [sv].[Regions] AS t JOIN @tb0_keys AS a ON a.[Id] = t.[Id];
                DELETE t
                OUTPUT deleted.[Id]
                FROM [sv].[Regions] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys);
                DECLARE @tb1_anc TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);
                WITH tb1_anc ([Node]) AS (
                    SELECT [Node] FROM @tb1_old
                    UNION ALL SELECT x.[Node].GetAncestor(1) FROM tb1_anc AS x WHERE x.[Node].GetLevel() > 1)
                INSERT INTO @tb1_anc ([Node]) SELECT DISTINCT [Node] FROM tb1_anc
                OPTION (MAXRECURSION 3);
                UPDATE t SET t.[SubtreeCount] = x.[Cnt]
                FROM [sv].[Regions] AS t
                JOIN @tb1_anc AS n ON n.[Node] = t.[Node]
                CROSS APPLY (SELECT COUNT(*) AS [Cnt] FROM [sv].[Regions] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt];

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void A_long_key_binds_its_ids_as_a_big_id_list()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Ledger>.ByIds([3L, 4], null, null));

            Assert.StartsWith("DECLARE @tb0_keys TABLE ([Id] bigint PRIMARY KEY);", batch.Statements[0].Text, StringComparison.Ordinal);
            Assert.Equal(2, batch.Statements[0].Parameters[0].Rows);
        }

        [Fact]
        public void A_sibling_with_a_blob_column_is_deleted_before_its_owner_and_its_blobs_captured()
        {
            // The sibling's table belongs to another stack: the delete still needs no authority over it.
            using BatchHarness siblingOwned = new(new MapOwners(new() { [new TableName("sv", "GaugeCards")] = "cards" }), compose: SaveModel.Compose);
            DataBatch batch = siblingOwned.Batch(BatchPurpose.Persist);

            DeleteHandle handle = batch.Delete(new DeleteSpec<Gauge>.ByIds([1], null, null));

            // A cascade reports no rows, so the sibling leaves explicitly, its blob read out, before its owner.
            Assert.Equal(
                """
                DECLARE @tb1_blob_sv_GaugeCards_ScanId TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);
                DELETE c
                OUTPUT deleted.[Id], deleted.[ScanId], NULL INTO @tb1_blob_sv_GaugeCards_ScanId ([RowId], [OldBlobId], [NewBlobId])
                FROM [sv].[GaugeCards] AS c WHERE c.[Id] IN (SELECT [Id] FROM @tb0_keys);
                DELETE t
                OUTPUT deleted.[Id]
                FROM [sv].[Gauges] AS t WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys);

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("sv", "GaugeCards"), new TableName("sv", "Gauges")], batch.Statements[1].Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.Equal(
                [new ColumnCapture(new TableName("sv", "GaugeCards"), "ScanId", new SqlIdentifier("@tb1_blob_sv_GaugeCards_ScanId"))],
                handle.Captures);

            // A sibling without one still leaves by its cascade.
            DataBatch crate = siblingOwned.Batch(BatchPurpose.Persist);
            _ = crate.Delete(new DeleteSpec<Crate>.ByIds([1], null, null));
            Assert.DoesNotContain("[sv].[CrateExtras]", crate.Statements[1].Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_sibling_owned_by_a_stack_never_refuses_its_owners_delete_but_an_owned_aggregate_table_does()
        {
            // The sibling's rows go by the database's cascade, so its stack never authorizes the delete.
            using BatchHarness siblingOwned = new(new MapOwners(new() { [new TableName("sv", "CrateExtras")] = "extras" }), compose: SaveModel.Compose);
            DataBatch batch = siblingOwned.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Crate>.ByIds([1], null, null));

            Assert.Contains(new TableName("sv", "CrateExtras"), batch.Statements[^1].Writes);

            // A table the delete's own statements name needs the caller's authority.
            using BatchHarness childOwned = new(new MapOwners(new() { [new TableName("sv", "Items")] = "items" }), compose: SaveModel.Compose);
            DataBatch refused = childOwned.Batch(BatchPurpose.Persist);

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => refused.Delete(new DeleteSpec<Crate>.ByIds([1], null, null)));

            Assert.Contains("sv.Items", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(refused.Statements);
        }

        [Fact]
        public void Every_statement_of_a_delete_recompiles_above_the_threshold()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            object[] ids = [.. Enumerable.Range(1, _harness.Executor.Saves.LargeBatchThreshold + 1).Cast<object>()];

            _ = batch.Delete(new DeleteSpec<Ledger>.ByIds(ids, null, null));

            // The key capture; the root's delete.
            Assert.Contains("JOIN @tb0_t0 AS s ON s.[Id] = t.[Id]\nOPTION (RECOMPILE);", batch.Statements[0].Text, StringComparison.Ordinal);
            Assert.EndsWith("WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys)\nOPTION (RECOMPILE);\n", batch.Statements[1].Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_receipt_names_the_deleted_roots_and_the_steps_record_them_and_the_recount()
        {
            IMeterFactory meters = _harness.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> deleted = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.RowsDeleted);
            using MetricCollector<long> recomputes = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.TreeRecomputes);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            DeleteHandle handle = batch.Delete(new DeleteSpec<Area>.ByIds([3, 4], null, null));

            // --- The round trip: the keys' check finds nothing missing, the delete returns its roots ---
            using DataTable missing = IdSet<int>();
            using DataTable roots = IdSet(3, 4);
            using DataTableReader reader = new([missing, roots]);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, Token);
            await batch.Statements[1].Reader!(cursor, Token);
            await cursor.EndAsync(Token);
            cursor.ApplyCompletions();
            foreach (Func<CancellationToken, Task> step in batch.ExecutedSteps)
            {
                await step(Token);
            }

            Assert.Equal([3, 4], handle.Receipt.Value.DeletedIds.Order());
            Assert.Empty(cursor.MissingIds!);
            Assert.Equal(2L, Assert.Single(deleted.GetMeasurementSnapshot()).Value);
            Assert.Equal("sv.Area", Assert.Single(recomputes.GetMeasurementSnapshot()).Tags[DataTelemetryNames.EntityTag]);
        }

        [Fact]
        public async Task A_stamp_conflict_is_read_for_the_exception_and_completes_no_receipt()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            DeleteHandle handle = batch.Delete(new DeleteSpec<Crate>.ByIds([7], null, null, [DateTimeOffset.UnixEpoch]));

            using DataTable conflicts = new();
            _ = conflicts.Columns.Add("Id", typeof(int));
            _ = conflicts.Columns.Add("Reason", typeof(string));
            _ = conflicts.Columns.Add("ModifiedAt", typeof(DateTimeOffset));
            _ = conflicts.Columns.Add("ModifiedById", typeof(int));
            _ = conflicts.Columns.Add("Name", typeof(string));
            _ = conflicts.Columns.Add("Name2", typeof(string));
            _ = conflicts.Columns.Add("Name3", typeof(string));
            _ = conflicts.Rows.Add(7, "C", DateTimeOffset.UnixEpoch, 3, "Someone", DBNull.Value, DBNull.Value);
            using DataTable missing = IdSet<int>();
            using DataTableReader reader = new([missing, conflicts]);
            ResultSetCursor cursor = new(reader);

            await batch.Statements[0].Reader!(cursor, Token);
            await batch.Statements[1].Reader!(cursor, Token);
            cursor.ApplyCompletions();

            Assert.Equal([new ConflictRow(7, false, DateTimeOffset.UnixEpoch, 3, "Someone", null, null)], cursor.Conflicts!);
            Assert.False(handle.Receipt.IsCompleted);
        }

        [Fact]
        public void Stamps_that_do_not_pair_with_the_ids_or_that_the_entity_cannot_compare_are_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<Crate>.ByIds([1, 2], null, null, [DateTimeOffset.UnixEpoch])));
            _ = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<Crate>.ByIds([1], null, null, [null])));
            _ = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<Ledger>.ByIds([1L], null, null, [DateTimeOffset.UnixEpoch])));
            _ = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<CrateExtra>.ByIds([1], null, null, [DateTimeOffset.UnixEpoch])));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void Descendants_of_a_table_that_is_not_a_tree_and_repeated_ids_are_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            ArgumentException refusal = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<Crate>.WithDescendants([1], null, null)));
            _ = Assert.Throws<ArgumentException>(() => batch.Delete(new DeleteSpec<Crate>.ByIds([1, 1], null, null)));

            Assert.Contains("not a tree", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_delete_is_refused_on_a_read_or_validate_batch()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Read).Delete(new DeleteSpec<Ledger>.ByIds([1L], null, null)));
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Validate).Delete(new DeleteSpec<Ledger>.ByIds([1L], null, null)));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A result set of one id column.</summary>
        /// <typeparam name="T">The id type.</typeparam>
        /// <param name="ids">The ids.</param>
        /// <returns>The table.</returns>
        private static DataTable IdSet<T>(params T[] ids)
            where T : struct
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(T));
            foreach (T id in ids)
            {
                _ = table.Rows.Add(id);
            }

            return table;
        }

        /// <summary>A scalar parameter's value.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The value.</returns>
        private static object? Scalar(BatchStatement statement, string name)
        {
            return Assert.IsType<ScalarParameter>(statement.Parameters.Single(parameter => parameter.Name == name)).Value;
        }
    }
}
