// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Data;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Data.Saves;

namespace Tellma.Core.Tests.Data.Writes
{
    /// <summary>
    ///     The update statement a batch composes, as golden SQL: by ids, all or nothing, by query, an
    ///     activatable tree's recount, an unstamped table; the parameters in the text's order; the
    ///     recompile threshold; the receipt and the missing ids read from canned result sets; and the
    ///     refusals at compose time.
    /// </summary>
    public sealed class UpdateStatementTests : IDisposable
    {
        /// <summary>The composition over the save model.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void An_update_by_ids_is_one_statement_that_skips_the_rows_already_holding_the_values()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<Crate>.ByIds([1, 2], null, null, Assign(("Name", "x"))));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Crates] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_t0))
                ;
                UPDATE t SET t.[Name] = @tb0_p0, t.[ModifiedAt] = @tb0_now, t.[ModifiedById] = @tb0_p1
                OUTPUT inserted.[Id]
                FROM [sv].[Crates] AS t
                WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys)
                  AND EXISTS (SELECT t.[Name] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT @tb0_p0 COLLATE Latin1_General_100_BIN2);

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // The ids, the assigned value, then the user who stamps the rows.
            Assert.Equal(["@tb0_t0", "@tb0_p0", "@tb0_p1"], statement.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal(2, statement.Parameters[0].Rows);
            Assert.Equal(("x", 7), (Scalar(statement, "@tb0_p0"), Scalar(statement, "@tb0_p1")));
            Assert.Equal([new TableName("sv", "Crates")], statement.Writes);
            Assert.Equal(typeof(Crate), statement.EntityType);
            Assert.False(statement.MayRunTwice);
        }

        [Fact]
        public void An_all_or_nothing_update_checks_the_readable_ids_in_a_statement_of_its_own_then_the_grant()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<Crate>.ByIds(
                [1, 2], FilterTree.Leaf("Name != 'hidden'"), FilterTree.Leaf("OuterId > 0"), Assign(("Name", "x"), ("OuterId", null)), AllOrNothing: true));

            // --- The not-found check: the ids the read filter hides are missing too; it writes nothing ---
            Assert.Equal(2, batch.Statements.Count);
            Assert.Equal(
                """
                SELECT s.[Id] FROM @tb0_t0 AS s WHERE s.[Id] NOT IN (
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Crates] AS [T]
                WHERE (([T].[Name] <> @qx0_p0) AND ([T].[Id] IN (SELECT [Id] FROM @tb0_t0)))
                );
                IF @@ROWCOUNT > 0 THROW 50404, N'Entity.NotFound', 1;

                """,
                batch.Statements[0].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@tb0_t0", "@qx0_p0"], batch.Statements[0].Parameters.Select(static parameter => parameter.Name));
            Assert.Empty(batch.Statements[0].Writes);

            // --- The update: the grant filter over the same ids, every one of them or none ---
            Assert.Equal(
                """
                DECLARE @tb1_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb1_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb1_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Crates] AS [T]
                WHERE ((([T].[OuterId] IS NOT NULL) AND ([T].[OuterId] > @qx1_p0)) AND ([T].[Id] IN (SELECT [Id] FROM @tb0_t0)))
                ;
                IF (SELECT COUNT(*) FROM @tb1_keys) <> (SELECT COUNT(*) FROM @tb0_t0) THROW 50403, N'RowSecurity', 1;
                UPDATE t SET t.[Name] = @tb1_p0, t.[OuterId] = @tb1_p1, t.[ModifiedAt] = @tb1_now, t.[ModifiedById] = @tb1_p2
                OUTPUT inserted.[Id]
                FROM [sv].[Crates] AS t
                WHERE t.[Id] IN (SELECT [Id] FROM @tb1_keys)
                  AND EXISTS (SELECT t.[Name] COLLATE Latin1_General_100_BIN2, t.[OuterId] EXCEPT SELECT @tb1_p0 COLLATE Latin1_General_100_BIN2, @tb1_p1);

                """,
                batch.Statements[1].Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@tb1_p0", "@tb1_p1", "@tb1_p2", "@qx1_p0"], batch.Statements[1].Parameters.Select(static parameter => parameter.Name));
            Assert.Null(Scalar(batch.Statements[1], "@tb1_p1"));
        }

        [Fact]
        public void An_update_by_query_caps_and_counts_the_keys_it_captures()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<Ledger>.ByQuery(FilterTree.Leaf("Memo = 'x'"), null, Assign(("Memo", "m")), Cap: 10, ExpectedCount: 3));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb0_keys TABLE ([Id] bigint PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Ledgers] AS [T]
                WHERE (([T].[Memo] IS NOT NULL) AND ([T].[Memo] = @qx0_p0))
                ORDER BY [c0] ASC
                OFFSET 0 ROWS FETCH NEXT @qx0_take ROWS ONLY
                ;
                IF (SELECT COUNT(*) FROM @tb0_keys) > @tb0_p2 THROW 50413, N'Update.CapExceeded', 1;
                IF (SELECT COUNT(*) FROM @tb0_keys) <> @tb0_p3 THROW 50428, N'Update.CountMismatch', 1;
                UPDATE t SET t.[Memo] = @tb0_p0, t.[ModifiedAt] = @tb0_now, t.[ModifiedById] = @tb0_p1
                OUTPUT inserted.[Id]
                FROM [sv].[Ledgers] AS t
                WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys)
                  AND EXISTS (SELECT t.[Memo] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT @tb0_p0 COLLATE Latin1_General_100_BIN2);

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // The value, the user, the cap and the expected count; the capture takes one past the cap.
            Assert.Equal(["@tb0_p0", "@tb0_p1", "@tb0_p2", "@tb0_p3", "@qx0_p0", "@qx0_take"], statement.Parameters.Select(static parameter => parameter.Name));
            Assert.Equal((10, 3, 11), (Scalar(statement, "@tb0_p2"), Scalar(statement, "@tb0_p3"), Scalar(statement, "@qx0_take")));
        }

        [Fact]
        public void Activating_a_tree_row_recounts_the_active_counts_of_its_ancestors()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<Area>.ByIds([3], null, null, Assign(("IsActive", false))));

            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[Areas] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_t0))
                ;
                UPDATE t SET t.[IsActive] = @tb0_p0, t.[ModifiedAt] = @tb0_now, t.[ModifiedById] = @tb0_p1
                OUTPUT inserted.[Id]
                FROM [sv].[Areas] AS t
                WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys)
                  AND EXISTS (SELECT t.[IsActive] EXCEPT SELECT @tb0_p0);
                DECLARE @tb0_old TABLE ([Node] hierarchyid NOT NULL);
                INSERT INTO @tb0_old ([Node]) SELECT t.[Node] FROM [sv].[Areas] AS t JOIN @tb0_keys AS a ON a.[Id] = t.[Id];
                DECLARE @tb0_anc TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);
                WITH tb0_anc ([Node]) AS (
                    SELECT [Node] FROM @tb0_old
                    UNION SELECT t.[Node] FROM [sv].[Areas] AS t JOIN @tb0_keys AS a ON a.[Id] = t.[Id]
                    UNION ALL SELECT x.[Node].GetAncestor(1) FROM tb0_anc AS x WHERE x.[Node].GetLevel() > 1)
                INSERT INTO @tb0_anc ([Node]) SELECT DISTINCT [Node] FROM tb0_anc
                OPTION (MAXRECURSION 31);
                UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
                FROM [sv].[Areas] AS t
                JOIN @tb0_anc AS n ON n.[Node] = t.[Node]
                CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
                             FROM [sv].[Areas] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];

                """,
                Assert.Single(batch.Statements).Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void A_tree_update_that_assigns_no_active_flag_recounts_nothing()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<Area>.ByIds([3], null, null, Assign(("Name", "n"))));

            Assert.DoesNotContain("SubtreeCount", Assert.Single(batch.Statements).Text, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unstamped_update_of_a_table_without_audit_columns_assigns_only_its_values()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Update(new UpdateSpec<CrateExtra>.ByIds([3], null, null, Assign(("Note", "n")), Stamp: false));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_keys TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_keys ([Id])
                SELECT
                    [T].[Id] AS [c0]
                FROM [sv].[CrateExtras] AS [T]
                WHERE ([T].[Id] IN (SELECT [Id] FROM @tb0_t0))
                ;
                UPDATE t SET t.[Note] = @tb0_p0
                OUTPUT inserted.[Id]
                FROM [sv].[CrateExtras] AS t
                WHERE t.[Id] IN (SELECT [Id] FROM @tb0_keys)
                  AND EXISTS (SELECT t.[Note] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT @tb0_p0 COLLATE Latin1_General_100_BIN2);

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(["@tb0_t0", "@tb0_p0"], statement.Parameters.Select(static parameter => parameter.Name));
        }

        [Fact]
        public void Every_statement_of_an_update_recompiles_above_the_threshold_and_none_at_it()
        {
            int threshold = _harness.Executor.Saves.LargeBatchThreshold;

            DataBatch atThreshold = _harness.Batch(BatchPurpose.Persist);
            _ = atThreshold.Update(new UpdateSpec<Crate>.ByIds(Ids(threshold), FilterTree.Leaf("Name != 'x'"), null, Assign(("Name", "x")), AllOrNothing: true));
            Assert.All(atThreshold.Statements, static statement => Assert.DoesNotContain("RECOMPILE", statement.Text, StringComparison.Ordinal));

            DataBatch above = _harness.Batch(BatchPurpose.Persist);
            _ = above.Update(new UpdateSpec<Crate>.ByIds(Ids(threshold + 1), FilterTree.Leaf("Name != 'x'"), null, Assign(("Name", "x")), AllOrNothing: true));

            // The not-found check's select; the key capture and the update.
            Assert.Equal(1, Hints(above.Statements[0].Text));
            Assert.Equal(2, Hints(above.Statements[1].Text));
        }

        [Fact]
        public async Task The_receipt_names_the_rows_the_update_wrote_as_the_keys_type()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            BatchResult<UpdateReceipt> narrow = batch.Update(new UpdateSpec<Crate>.ByIds([1, 2], null, null, Assign(("Name", "x"))));
            BatchResult<UpdateReceipt> wide = batch.Update(new UpdateSpec<Ledger>.ByIds([1L, 2L], null, null, Assign(("Memo", "x"))));

            using DataTable crates = IdSet(1);
            using DataTable ledgers = IdSet(2L);
            using DataTableReader reader = new([crates, ledgers]);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, Token);
            await batch.Statements[1].Reader!(cursor, Token);
            await cursor.EndAsync(Token);
            cursor.ApplyCompletions();

            Assert.Equal([1], narrow.Value.UpdatedIds);
            Assert.Equal([2L], wide.Value.UpdatedIds);
            Assert.Equal(2, cursor.RowsRead);
        }

        [Fact]
        public async Task The_missing_ids_of_an_all_or_nothing_update_are_read_for_the_exception()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            BatchResult<UpdateReceipt> receipt = batch.Update(new UpdateSpec<Crate>.ByIds([1, 2], null, null, Assign(("Name", "x")), AllOrNothing: true));

            using DataTable missing = IdSet(2);
            using DataTableReader reader = new(missing);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, Token);

            Assert.Equal([2L], cursor.MissingIds!);
            Assert.False(receipt.IsCompleted);
        }

        [Fact]
        public async Task An_activation_records_one_tree_recompute_once_it_ran()
        {
            using MetricCollector<long> recomputes = new(
                _harness.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.TreeRecomputes);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = batch.Update(new UpdateSpec<Area>.ByIds([3], null, null, Assign(("IsActive", true))));

            using DataTable updated = IdSet(3);
            using DataTableReader reader = new(updated);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, Token);
            cursor.ApplyCompletions();
            foreach (Func<CancellationToken, Task> step in batch.ExecutedSteps)
            {
                await step(Token);
            }

            CollectedMeasurement<long> measurement = Assert.Single(recomputes.GetMeasurementSnapshot());
            Assert.Equal((1L, "sv.Area"), (measurement.Value, measurement.Tags[DataTelemetryNames.EntityTag]));
        }

        [Theory]
        [InlineData("Id", "is the key")]
        [InlineData("CreatedAt", "is kept by the data layer")]
        [InlineData("ModifiedById", "is kept by the data layer")]
        [InlineData("LabelId", "is a blob reference")]
        [InlineData("Missing", "is not one of its properties")]
        public void An_assignment_the_update_cannot_make_is_refused_and_nothing_appended(string property, string reason)
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            ArgumentException refusal = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign((property, 1)))));

            Assert.Contains(reason, refusal.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
        }

        [Theory]
        [InlineData("ParentId", "re-parents a tree row")]
        [InlineData("SubtreeCount", "is kept by the data layer")]
        [InlineData("Code", "is write-once")]
        public void A_tree_column_or_a_write_once_column_is_refused(string property, string reason)
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            ArgumentException refusal = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Region>.ByIds([1], null, null, Assign((property, "1")))));

            Assert.Contains(reason, refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_value_its_column_does_not_hold_is_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign(("Name", 5)))));
            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign(("Name", null)))));
            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign())));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void Ids_that_are_null_repeated_or_not_integers_are_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([1, 1], null, null, Assign(("Name", "x")))));
            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([null!], null, null, Assign(("Name", "x")))));
            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds(["1"], null, null, Assign(("Name", "x")))));
            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<Crate>.ByIds([5_000_000_000L], null, null, Assign(("Name", "x")))));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_stamped_update_of_a_table_without_audit_columns_and_a_negative_cap_are_refused()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Assert.Throws<ArgumentException>(() => batch.Update(new UpdateSpec<CrateExtra>.ByIds([1], null, null, Assign(("Note", "n")))));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.Update(new UpdateSpec<Ledger>.ByQuery(FilterTree.Leaf("Memo = 'x'"), null, Assign(("Memo", "m")), -1, 0)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => batch.Update(new UpdateSpec<Ledger>.ByQuery(FilterTree.Leaf("Memo = 'x'"), null, Assign(("Memo", "m")), 5, -1)));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void An_update_is_refused_on_a_read_or_validate_batch()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Read).Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign(("Name", "x")))));
            _ = Assert.Throws<InvalidOperationException>(() => _harness.Batch(BatchPurpose.Validate).Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign(("Name", "x")))));
        }

        [Fact]
        public async Task An_update_needs_the_batch_in_one_transaction()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            batch.TransactionMode = TransactionMode.None;
            _ = batch.Update(new UpdateSpec<Crate>.ByIds([1], null, null, Assign(("Name", "x"))));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(Token));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>The assignments of an update.</summary>
        /// <param name="assignments">Property name and value pairs.</param>
        /// <returns>The assignments.</returns>
        private static Dictionary<string, object?> Assign(params (string Property, object? Value)[] assignments)
        {
            return assignments.ToDictionary(static pair => pair.Property, static pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>The ids 1 to a count.</summary>
        /// <param name="count">The count.</param>
        /// <returns>The ids.</returns>
        private static object[] Ids(int count)
        {
            return [.. Enumerable.Range(1, count).Cast<object>()];
        }

        /// <summary>A result set of one id column.</summary>
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

        /// <summary>The recompile hints in a statement's text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The count.</returns>
        private static int Hints(string text)
        {
            return text.Split("OPTION (RECOMPILE)").Length - 1;
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
