// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Saves;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Saves
{
    /// <summary>
    ///     The save statement a batch composes, as golden SQL: a root with a child, a grandchild and
    ///     blob columns; a root with a long key; a tree's provisional node; the conflict-message cap;
    ///     the recompile threshold; the parameters in the plan's order; the receipt read from canned
    ///     result sets; the captures the handle lists; and the refusals at compose time.
    /// </summary>
    public sealed class SaveStatementTests : IDisposable
    {
        /// <summary>The composition over the save model.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        private IdAllocator Allocator => _harness.Executor.Allocator;

        [Fact]
        public void A_root_with_children_grandchildren_and_blob_columns_is_one_statement()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Save([new Crate { Id = 5, Name = "a", Compartments = [] }]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb0_new TABLE ([Id] int NOT NULL PRIMARY KEY);
                DECLARE @tb0_saved TABLE ([Id] int NOT NULL PRIMARY KEY);
                DECLARE @tb0_touched TABLE ([Id] int NOT NULL, [Collection] tinyint NOT NULL, [Op] char(1) NOT NULL);
                DECLARE @tb0_conflicts TABLE ([Id] int PRIMARY KEY, [Reason] char(1) NOT NULL);
                DECLARE @tb0_blob_sv_Crates_LabelId TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);
                DECLARE @tb0_blob_sv_Compartments_PhotoId TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);
                INSERT INTO @tb0_saved ([Id]) SELECT [Id] FROM @tb0_t0 UNION SELECT [Id] FROM @tb0_t1;
                INSERT INTO @tb0_conflicts ([Id], [Reason])
                SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN 'M' ELSE 'C' END
                FROM @tb0_t1 AS s
                LEFT JOIN [sv].[Crates] AS t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
                WHERE t.[Id] IS NULL OR (@tb0_p0 = 0 AND t.[ModifiedAt] <> s.[ModifiedAt]);
                IF EXISTS (SELECT 1 FROM @tb0_conflicts)
                BEGIN
                    SELECT c.[Id], c.[Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]
                    FROM @tb0_conflicts AS c
                    LEFT JOIN [sv].[Crates] AS t ON t.[Id] = c.[Id]
                    LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];
                    DECLARE @tb0_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb0_conflicts) AS nvarchar(10))
                        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP (90) [Id] FROM @tb0_conflicts WHERE [Reason] = 'C' ORDER BY [Id]) AS c), N'')
                        + N'],"missing":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP (90) [Id] FROM @tb0_conflicts WHERE [Reason] = 'M' ORDER BY [Id]) AS m), N'')
                        + N']}';
                    THROW 50409, @tb0_msg, 1;
                END;
                DECLARE @tb0_cap0 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL, [Old_LabelId] int NULL, [New_LabelId] int NULL);
                INSERT INTO [sv].[Crates] ([Id], [Name], [OuterId], [LabelId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                OUTPUT inserted.[Id], NULL, 'I', NULL, inserted.[LabelId] INTO @tb0_cap0 ([Id], [ParentId], [Op], [Old_LabelId], [New_LabelId])
                SELECT s.[Id], s.[Name], s.[OuterId], s.[LabelId], @tb0_now, @tb0_p1, @tb0_now, @tb0_p1
                FROM @tb0_t0 AS s;
                INSERT INTO @tb0_new ([Id]) SELECT [Id] FROM @tb0_cap0;
                INSERT INTO @tb0_blob_sv_Crates_LabelId ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], [Old_LabelId], [New_LabelId] FROM @tb0_cap0;
                DECLARE @tb0_cap1 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL, [Old_PhotoId] int NULL, [New_PhotoId] int NULL);
                DECLARE @tb0_gone0 TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_gone0 ([Id])
                SELECT c.[Id] FROM [sv].[Compartments] AS c
                WHERE c.[CrateId] IN (SELECT [Id] FROM @tb0_t2)
                  AND NOT EXISTS (SELECT 1 FROM @tb0_t3 AS s WHERE s.[Id] = c.[Id])
                  AND NOT EXISTS (SELECT 1 FROM @tb0_t4 AS s WHERE s.[Id] = c.[Id]);
                DELETE g
                FROM [sv].[Items] AS g WHERE g.[CompartmentId] IN (SELECT [Id] FROM @tb0_gone0);
                DELETE c
                OUTPUT deleted.[Id], deleted.[CrateId], 'D', deleted.[PhotoId], NULL INTO @tb0_cap1 ([Id], [ParentId], [Op], [Old_PhotoId], [New_PhotoId])
                FROM [sv].[Compartments] AS c WHERE c.[Id] IN (SELECT [Id] FROM @tb0_gone0);
                INSERT INTO @tb0_touched ([Id], [Collection], [Op]) SELECT [ParentId], 0, [Op] FROM @tb0_cap1;
                INSERT INTO @tb0_blob_sv_Compartments_PhotoId ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], [Old_PhotoId], [New_PhotoId] FROM @tb0_cap1;
                DECLARE @tb0_cap2 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL, [Old_PhotoId] int NULL, [New_PhotoId] int NULL);
                UPDATE c SET c.[Position] = s.[Position], c.[PhotoId] = s.[PhotoId]
                OUTPUT inserted.[Id], inserted.[CrateId], 'U', deleted.[PhotoId], inserted.[PhotoId] INTO @tb0_cap2 ([Id], [ParentId], [Op], [Old_PhotoId], [New_PhotoId])
                FROM [sv].[Compartments] AS c JOIN @tb0_t4 AS s ON s.[Id] = c.[Id]
                WHERE c.[CrateId] IN (SELECT [Id] FROM @tb0_t2) AND c.[CrateId] = s.[CrateId]
                  AND EXISTS (SELECT s.[Position], s.[PhotoId]
                              EXCEPT SELECT c.[Position], c.[PhotoId]);
                INSERT INTO @tb0_touched ([Id], [Collection], [Op]) SELECT [ParentId], 0, [Op] FROM @tb0_cap2;
                INSERT INTO @tb0_blob_sv_Compartments_PhotoId ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], [Old_PhotoId], [New_PhotoId] FROM @tb0_cap2;
                DECLARE @tb0_cap3 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL, [Old_PhotoId] int NULL, [New_PhotoId] int NULL);
                INSERT INTO [sv].[Compartments] ([Id], [CrateId], [Position], [PhotoId])
                OUTPUT inserted.[Id], inserted.[CrateId], 'I', NULL, inserted.[PhotoId] INTO @tb0_cap3 ([Id], [ParentId], [Op], [Old_PhotoId], [New_PhotoId])
                SELECT s.[Id], s.[CrateId], s.[Position], s.[PhotoId] FROM @tb0_t3 AS s;
                INSERT INTO @tb0_touched ([Id], [Collection], [Op]) SELECT [ParentId], 0, [Op] FROM @tb0_cap3;
                INSERT INTO @tb0_blob_sv_Compartments_PhotoId ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], [Old_PhotoId], [New_PhotoId] FROM @tb0_cap3;
                DECLARE @tb0_cap4 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL);
                DELETE c
                OUTPUT deleted.[Id], deleted.[CompartmentId], 'D' INTO @tb0_cap4 ([Id], [ParentId], [Op])
                FROM [sv].[Items] AS c
                WHERE c.[CompartmentId] IN (SELECT [Id] FROM @tb0_t5)
                  AND NOT EXISTS (SELECT 1 FROM @tb0_t6 AS s WHERE s.[Id] = c.[Id])
                  AND NOT EXISTS (SELECT 1 FROM @tb0_t7 AS s WHERE s.[Id] = c.[Id]);
                INSERT INTO @tb0_touched ([Id], [Collection], [Op])
                SELECT a1.[CrateId], 0, x.[Op] FROM @tb0_cap4 AS x
                JOIN (SELECT [Id], [CrateId] FROM @tb0_t3 UNION ALL SELECT [Id], [CrateId] FROM @tb0_t4) AS a1 ON a1.[Id] = x.[ParentId];
                DECLARE @tb0_cap5 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL);
                UPDATE c SET c.[Label] = s.[Label]
                OUTPUT inserted.[Id], inserted.[CompartmentId], 'U' INTO @tb0_cap5 ([Id], [ParentId], [Op])
                FROM [sv].[Items] AS c JOIN @tb0_t7 AS s ON s.[Id] = c.[Id]
                WHERE c.[CompartmentId] IN (SELECT [Id] FROM @tb0_t5) AND c.[CompartmentId] = s.[CompartmentId]
                  AND EXISTS (SELECT s.[Label] COLLATE Latin1_General_100_BIN2
                              EXCEPT SELECT c.[Label] COLLATE Latin1_General_100_BIN2);
                INSERT INTO @tb0_touched ([Id], [Collection], [Op])
                SELECT a1.[CrateId], 0, x.[Op] FROM @tb0_cap5 AS x
                JOIN (SELECT [Id], [CrateId] FROM @tb0_t3 UNION ALL SELECT [Id], [CrateId] FROM @tb0_t4) AS a1 ON a1.[Id] = x.[ParentId];
                DECLARE @tb0_cap6 TABLE ([Id] int NOT NULL, [ParentId] int NULL, [Op] char(1) NOT NULL);
                INSERT INTO [sv].[Items] ([Id], [CompartmentId], [Label])
                OUTPUT inserted.[Id], inserted.[CompartmentId], 'I' INTO @tb0_cap6 ([Id], [ParentId], [Op])
                SELECT s.[Id], s.[CompartmentId], s.[Label] FROM @tb0_t6 AS s;
                INSERT INTO @tb0_touched ([Id], [Collection], [Op])
                SELECT a1.[CrateId], 0, x.[Op] FROM @tb0_cap6 AS x
                JOIN (SELECT [Id], [CrateId] FROM @tb0_t3 UNION ALL SELECT [Id], [CrateId] FROM @tb0_t4) AS a1 ON a1.[Id] = x.[ParentId];
                UPDATE t SET t.[Name] = s.[Name], t.[OuterId] = s.[OuterId], t.[LabelId] = s.[LabelId], t.[ModifiedAt] = @tb0_now, t.[ModifiedById] = @tb0_p1
                OUTPUT inserted.[Id], deleted.[LabelId], inserted.[LabelId] INTO @tb0_blob_sv_Crates_LabelId ([RowId], [OldBlobId], [NewBlobId])
                OUTPUT inserted.[Id]
                FROM [sv].[Crates] AS t JOIN @tb0_t1 AS s ON s.[Id] = t.[Id]
                WHERE EXISTS (SELECT s.[Name] COLLATE Latin1_General_100_BIN2, s.[OuterId], s.[LabelId]
                              EXCEPT SELECT t.[Name] COLLATE Latin1_General_100_BIN2, t.[OuterId], t.[LabelId])
                   OR t.[Id] IN (SELECT [Id] FROM @tb0_touched);
                SELECT @tb0_now AS [Stamp], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'U') AS [ChildUpdated], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'D') AS [Deleted];

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // Each node binds its parents (a child only), its new rows and its existing rows, in pre-order; then the override bit and the user.
            Assert.Equal(
                ["@tb0_t0", "@tb0_t1", "@tb0_t2", "@tb0_t3", "@tb0_t4", "@tb0_t5", "@tb0_t6", "@tb0_t7", "@tb0_p0", "@tb0_p1"],
                statement.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal([new TableName("sv", "Compartments"), new TableName("sv", "Crates"), new TableName("sv", "Items")], statement.Writes.OrderBy(static table => table.Name));
            Assert.False(statement.MayRunTwice);
        }

        [Fact]
        public void A_root_with_a_long_key_caps_each_conflict_list_at_fifty_ids()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Save([new Ledger { Id = 5 }]);

            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb0_new TABLE ([Id] bigint NOT NULL PRIMARY KEY);
                DECLARE @tb0_saved TABLE ([Id] bigint NOT NULL PRIMARY KEY);
                DECLARE @tb0_touched TABLE ([Id] bigint NOT NULL, [Collection] tinyint NOT NULL, [Op] char(1) NOT NULL);
                DECLARE @tb0_conflicts TABLE ([Id] bigint PRIMARY KEY, [Reason] char(1) NOT NULL);
                INSERT INTO @tb0_saved ([Id]) SELECT [Id] FROM @tb0_t0 UNION SELECT [Id] FROM @tb0_t1;
                INSERT INTO @tb0_conflicts ([Id], [Reason])
                SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN 'M' ELSE 'C' END
                FROM @tb0_t1 AS s
                LEFT JOIN [sv].[Ledgers] AS t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]
                WHERE t.[Id] IS NULL OR (@tb0_p0 = 0 AND t.[ModifiedAt] <> s.[ModifiedAt]);
                IF EXISTS (SELECT 1 FROM @tb0_conflicts)
                BEGIN
                    SELECT c.[Id], c.[Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]
                    FROM @tb0_conflicts AS c
                    LEFT JOIN [sv].[Ledgers] AS t ON t.[Id] = c.[Id]
                    LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];
                    DECLARE @tb0_msg nvarchar(2048) = N'{"count":' + CAST((SELECT COUNT(*) FROM @tb0_conflicts) AS nvarchar(10))
                        + N',"conflicts":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP (50) [Id] FROM @tb0_conflicts WHERE [Reason] = 'C' ORDER BY [Id]) AS c), N'')
                        + N'],"missing":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP (50) [Id] FROM @tb0_conflicts WHERE [Reason] = 'M' ORDER BY [Id]) AS m), N'')
                        + N']}';
                    THROW 50409, @tb0_msg, 1;
                END;
                INSERT INTO [sv].[Ledgers] ([Id], [Memo], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                OUTPUT inserted.[Id] INTO @tb0_new ([Id])
                SELECT s.[Id], s.[Memo], @tb0_now, @tb0_p1, @tb0_now, @tb0_p1
                FROM @tb0_t0 AS s;
                UPDATE t SET t.[Memo] = s.[Memo], t.[ModifiedAt] = @tb0_now, t.[ModifiedById] = @tb0_p1
                OUTPUT inserted.[Id]
                FROM [sv].[Ledgers] AS t JOIN @tb0_t1 AS s ON s.[Id] = t.[Id]
                WHERE EXISTS (SELECT s.[Memo] COLLATE Latin1_General_100_BIN2
                              EXCEPT SELECT t.[Memo] COLLATE Latin1_General_100_BIN2)
                   OR t.[Id] IN (SELECT [Id] FROM @tb0_touched);
                SELECT @tb0_now AS [Stamp], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'U') AS [ChildUpdated], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'D') AS [Deleted];

                """,
                Assert.Single(batch.Statements).Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void The_conflict_cap_fits_both_lists_of_the_widest_ids_in_the_message()
        {
            SaveEmitter emitter = _harness.Services.GetRequiredService<SaveEmitter>();

            // (2048 - 48) / 2 / (digits + 1): ten digits and a comma for an int, nineteen for a long.
            Assert.Equal(90, emitter.ConflictCap(isLong: false));
            Assert.Equal(50, emitter.ConflictCap(isLong: true));
        }

        [Fact]
        public void A_tree_root_is_inserted_with_a_provisional_node()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Save([new Area { Id = 5, Name = "x" }]);

            string text = Assert.Single(batch.Statements).Text;
            Assert.Contains(
                "INSERT INTO [sv].[Areas] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById], [ParentId], [SubtreeCount], [IsActive], [ActiveSubtreeCount], [Node])",
                text,
                StringComparison.Ordinal);
            Assert.Contains("hierarchyid::Parse('/0/' + CAST(s.[Id] AS varchar(20)) + '/')", text, StringComparison.Ordinal);

            // The node is the tree statements' to maintain: never in a SET list.
            Assert.DoesNotContain("t.[Node] =", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_data_modifying_statement_recompiles_above_the_threshold_and_none_at_it()
        {
            int threshold = _harness.Services.GetRequiredService<SaveEmitter>().LargeBatchThreshold;
            Assert.Equal(1000, threshold);

            DataBatch atThreshold = _harness.Batch(BatchPurpose.Persist);
            _ = atThreshold.Save([.. Enumerable.Range(1, threshold).Select(static id => new Ledger { Id = id })]);
            Assert.DoesNotContain("OPTION (RECOMPILE)", Assert.Single(atThreshold.Statements).Text, StringComparison.Ordinal);

            DataBatch above = _harness.Batch(BatchPurpose.Persist);
            _ = above.Save([.. Enumerable.Range(1, threshold + 1).Select(static id => new Ledger { Id = id })]);
            string text = Assert.Single(above.Statements).Text;

            // The saved-set fill, the guard's insert, the root insert and the root update; never the guard's SELECT.
            Assert.Equal(4, text.Split("\nOPTION (RECOMPILE);\n").Length - 1);
            Assert.Contains("FROM @tb0_t0 AS s\nOPTION (RECOMPILE);", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_child_table_above_the_threshold_recompiles_the_whole_statement()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            Crate crate = new() { Id = 5, Name = "a", Compartments = [.. Enumerable.Range(1, 1001).Select(static id => new Compartment { Id = id, CrateId = 5 })] };

            _ = batch.Save([crate]);

            // Every data-modifying statement recompiles, the root's as well as the child's.
            string text = Assert.Single(batch.Statements).Text;
            Assert.Contains("FROM @tb0_t0 AS s\nOPTION (RECOMPILE);", text, StringComparison.Ordinal);
            Assert.Contains("FROM @tb0_t3 AS s\nOPTION (RECOMPILE);", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_handle_lists_one_capture_per_blob_column_and_the_root_sets()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            SaveHandle handle = batch.Save([new Crate { Id = 5, Name = "a" }]);

            Assert.Equal("@tb0_saved", handle.SavedIds.Name);
            Assert.Equal("@tb0_new", handle.NewIds.Name);
            Assert.Equal("@tb0_touched", handle.TouchedIds.Name);
            Assert.Equal(
                [
                    new ColumnCapture(new TableName("sv", "Crates"), "LabelId", new SqlIdentifier("@tb0_blob_sv_Crates_LabelId")),
                    new ColumnCapture(new TableName("sv", "Compartments"), "PhotoId", new SqlIdentifier("@tb0_blob_sv_Compartments_PhotoId")),
                ],
                handle.Captures);

            // A later statement may name what the save declared.
            _ = batch.Sql($"SELECT [RowId] FROM {handle.Captures[1].Identifier} UNION ALL SELECT [Id] FROM {handle.TouchedIds};", new SqlOptions(resultSets: 1));
        }

        [Fact]
        public void A_root_with_no_blob_column_lists_no_capture()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            Assert.Empty(batch.Save([new Ledger { Id = 5 }]).Captures);
        }

        [Fact]
        public void Rows_are_sorted_into_new_and_existing_rows_per_table_with_the_parents_that_supplied_a_collection()
        {
            Allocator.Replenish(1, typeof(Crate), 100, 199);
            Allocator.Replenish(1, typeof(Compartment), 300, 399);
            DataBatch validate = _harness.Batch(BatchPurpose.Validate);
            Crate created = new() { Name = "new", Compartments = [new Compartment { Position = 1 }] };
            Compartment added = new() { Position = 2 };
            Crate edited = new() { Id = 7, Name = "old", Compartments = [new Compartment { Id = 70, CrateId = 7, Position = 1 }, added] };
            Crate untouched = new() { Id = 8, Name = "kept" };

            // --- Only the new rows get ids: the reservation is served from the buffer ---
            using IdReservation reservation = Allocator.Reserve(validate, typeof(Crate), [created, edited, untouched]);
            Assert.True(reservation.IsImmediate);
            reservation.Assign([created, edited, untouched]);

            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = batch.Save([created, edited, untouched]);

            // t0/t1: new and existing roots; t2: the parents that supplied compartments; t3/t4; t5..t7 the items, unsupplied.
            IReadOnlyList<BatchParameter> parameters = batch.Statements[0].Parameters;
            Assert.Equal([1, 2, 2, 2, 1, 0, 0, 0], parameters.Take(8).Select(static parameter => parameter.Rows));
            Assert.Equal(false, ((ScalarParameter)parameters[8]).Value);
            Assert.Equal(7, ((ScalarParameter)parameters[9]).Value);
            Assert.Equal(100, created.Id);
            Assert.Equal([300, 301], [created.Compartments[0].Id, added.Id]);
            Assert.Equal(7, added.CrateId);
        }

        [Fact]
        public async Task The_receipt_counts_new_rows_stamped_roots_child_updates_and_deletes_and_stamps_the_rows()
        {
            Allocator.Replenish(1, typeof(Crate), 100, 199);
            Allocator.Replenish(1, typeof(Compartment), 300, 399);
            Crate created = new() { Name = "new", Compartments = [new Compartment { Position = 1 }, new Compartment { Position = 2 }] };
            Crate edited = new() { Id = 7, Name = "old", ModifiedAt = DateTimeOffset.UnixEpoch, ModifiedById = 3 };
            Crate skipped = new() { Id = 8, Name = "same", ModifiedAt = DateTimeOffset.UnixEpoch, ModifiedById = 3 };
            using IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Crate), [created, edited, skipped]);
            reservation.Assign([created, edited, skipped]);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            SaveHandle handle = batch.Save([created, edited, skipped]);

            // --- The round trip: the stamped ids (the edited root), then the receipt row ---
            DateTimeOffset stamp = new DateTimeOffset(2026, 11, 12, 8, 30, 15, TimeSpan.Zero).AddTicks(1234567);
            using DataTable stamped = new();
            _ = stamped.Columns.Add("Id", typeof(int));
            _ = stamped.Rows.Add(7);
            using DataTable receipt = new();
            _ = receipt.Columns.Add("Stamp", typeof(DateTimeOffset));
            _ = receipt.Columns.Add("ChildUpdated", typeof(int));
            _ = receipt.Columns.Add("Deleted", typeof(int));
            _ = receipt.Rows.Add(stamp, 3, 4);
            using DataTableReader reader = new([stamped, receipt]);
            ResultSetCursor cursor = new(reader);

            await batch.Statements[0].Reader!(cursor, Token);
            await cursor.EndAsync(Token);
            Assert.False(handle.Receipt.IsCompleted);
            cursor.ApplyCompletions();

            // Inserted from the new-rows tables; Updated is the stamped roots plus the child updates.
            Assert.Equal(new SaveReceipt(stamp, Inserted: 3, Updated: 4, Deleted: 4), handle.Receipt.Value);
            Assert.Equal(2, cursor.RowsRead);

            // --- The completion steps stamp the rows: all four columns on a new root, the modified pair on a stamped one ---
            foreach (Func<CancellationToken, Task> step in batch.ExecutedSteps)
            {
                await step(Token);
            }

            Assert.Equal((stamp, 7, stamp, 7), (created.CreatedAt, created.CreatedById, created.ModifiedAt, created.ModifiedById));
            Assert.Equal((stamp, 7), (edited.ModifiedAt, edited.ModifiedById));
            Assert.Equal((DateTimeOffset.UnixEpoch, 3), (skipped.ModifiedAt, skipped.ModifiedById));

            // The inserted rows are existing rows from now on.
            Assert.Null(Allocator.HoldOf(created));
            Assert.Null(Allocator.HoldOf(created.Compartments[0]));
        }

        [Fact]
        public async Task A_conflict_set_is_read_for_the_exception_and_completes_no_receipt()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            SaveHandle handle = batch.Save([new Crate { Id = 7, Name = "old" }, new Crate { Id = 9, Name = "gone" }]);

            using DataTable conflicts = new();
            _ = conflicts.Columns.Add("Id", typeof(int));
            _ = conflicts.Columns.Add("Reason", typeof(string));
            _ = conflicts.Columns.Add("ModifiedAt", typeof(DateTimeOffset));
            _ = conflicts.Columns.Add("ModifiedById", typeof(int));
            _ = conflicts.Columns.Add("Name", typeof(string));
            _ = conflicts.Columns.Add("Name2", typeof(string));
            _ = conflicts.Columns.Add("Name3", typeof(string));
            _ = conflicts.Rows.Add(7, "C", DateTimeOffset.UnixEpoch, 3, "Someone", DBNull.Value, DBNull.Value);
            _ = conflicts.Rows.Add(9, "M", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            using DataTableReader reader = new(conflicts);
            ResultSetCursor cursor = new(reader);

            await batch.Statements[0].Reader!(cursor, Token);
            cursor.ApplyCompletions();

            Assert.Equal(
                [new ConflictRow(7, false, DateTimeOffset.UnixEpoch, 3, "Someone", null, null), new ConflictRow(9, true, null, null, null, null, null)],
                cursor.Conflicts);
            Assert.False(handle.Receipt.IsCompleted);
        }

        [Fact]
        public void A_save_is_refused_on_a_read_or_validate_batch()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Read).Save([new Ledger { Id = 5 }]));
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Validate).Save([new Ledger { Id = 5 }]));
        }

        [Fact]
        public void A_child_entity_is_never_saved_as_a_root()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => batch.Save([new Compartment { Id = 5, CrateId = 1 }]));

            Assert.Contains("not a top-level entity", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_row_without_an_id_or_with_a_duplicate_id_is_refused_and_the_ordinal_given_back()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Assert.Throws<ArgumentException>(() => batch.Save([new Ledger()]));
            _ = Assert.Throws<ArgumentException>(() => batch.Save([new Ledger { Id = 5 }, new Ledger { Id = 5 }]));
            _ = Assert.Throws<ArgumentException>(() => batch.Save([new Crate { Id = 5, Compartments = [null!] }]));
            Assert.Empty(batch.Statements);

            // The next statement takes ordinal 0.
            Assert.Equal("@tb0_saved", batch.Save([new Ledger { Id = 5 }]).SavedIds.Name);
        }

        [Fact]
        public void A_child_whose_parent_key_is_not_its_parents_id_is_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            ArgumentException refusal = Assert.Throws<ArgumentException>(
                () => batch.Save([new Crate { Id = 5, Compartments = [new Compartment { Id = 50, CrateId = 6 }] }]));

            Assert.Contains("parent key", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_row_whose_reservation_gave_its_ids_back_is_refused()
        {
            Allocator.Replenish(1, typeof(Ledger), 100, 199);
            Ledger ledger = new();
            IdReservation reservation = Allocator.Reserve(_harness.Batch(BatchPurpose.Validate), typeof(Ledger), [ledger]);
            reservation.Assign([ledger]);
            reservation.Dispose();

            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Persist).Save([ledger]));
        }

        [Fact]
        public async Task A_save_needs_the_batch_in_one_transaction()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            batch.TransactionMode = TransactionMode.None;
            _ = batch.Save([new Ledger { Id = 5 }]);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(Token));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }
    }
}
