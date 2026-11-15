// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Data;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Trees;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Data.Saves;

namespace Tellma.Core.Tests.Data.Trees
{
    /// <summary>
    ///     The tree statements, as golden SQL: the paths, the cycle fence and the recount a tree save
    ///     runs after its root update, under the ordinal of its statement; and the verify statement of
    ///     an activatable and a plain tree, with the count it reads back, the metrics and the warning
    ///     of a repair, and its refusals.
    /// </summary>
    public sealed class TreeStatementTests : IDisposable
    {
        /// <summary>The composition over the save model.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The composed tree statements.</summary>
        private TreeStatements Statements => _harness.Services.GetRequiredService<TreeStatements>();

        [Fact]
        public void A_tree_save_walks_the_paths_fences_cycles_repaths_and_recounts_after_its_root_update()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Save([new Region { Id = 5, Name = "x" }]);

            // The walks stop one level below the depth limit: a fifth level exhausts them.
            string text = Assert.Single(batch.Statements).Text;
            Assert.EndsWith(
                """
                DECLARE @tb0_aff TABLE ([Id] int PRIMARY KEY);
                INSERT INTO @tb0_aff ([Id])
                SELECT [Id] FROM @tb0_t0
                UNION SELECT [Id] FROM @tb0_t1
                UNION SELECT d.[Id] FROM [sv].[Regions] AS d
                      WHERE EXISTS (SELECT 1 FROM [sv].[Regions] AS b JOIN @tb0_t1 AS s ON s.[Id] = b.[Id]
                                    WHERE d.[Node].IsDescendantOf(b.[Node]) = 1);
                DECLARE @tb0_paths TABLE ([Id] int PRIMARY KEY, [Path] varchar(892) NOT NULL);
                WITH tb0_walk ([Id], [Path]) AS (
                    SELECT t.[Id], CAST(ISNULL(p.[Node].ToString(), '/') + CAST(t.[Id] AS varchar(20)) + '/' AS varchar(892))
                    FROM [sv].[Regions] AS t
                    JOIN @tb0_aff AS a ON a.[Id] = t.[Id]
                    LEFT JOIN [sv].[Regions] AS p WITH (UPDLOCK, ROWLOCK, FORCESEEK) ON p.[Id] = t.[ParentId]
                    WHERE t.[ParentId] IS NULL OR t.[ParentId] NOT IN (SELECT [Id] FROM @tb0_aff)
                    UNION ALL
                    SELECT c.[Id], CAST(x.[Path] + CAST(c.[Id] AS varchar(20)) + '/' AS varchar(892))
                    FROM [sv].[Regions] AS c
                    JOIN @tb0_aff AS a ON a.[Id] = c.[Id]
                    JOIN tb0_walk AS x ON x.[Id] = c.[ParentId]
                )
                INSERT INTO @tb0_paths ([Id], [Path]) SELECT [Id], [Path] FROM tb0_walk
                OPTION (MAXRECURSION 3);
                IF (SELECT COUNT(*) FROM @tb0_paths) <> (SELECT COUNT(*) FROM @tb0_aff)
                    THROW 50422, N'Tree.Cycle', 1;
                DECLARE @tb0_old TABLE ([Node] hierarchyid NOT NULL);
                INSERT INTO @tb0_old ([Node]) SELECT t.[Node] FROM [sv].[Regions] AS t JOIN @tb0_aff AS a ON a.[Id] = t.[Id];
                UPDATE t SET t.[Node] = hierarchyid::Parse(x.[Path])
                FROM [sv].[Regions] AS t JOIN @tb0_paths AS x ON x.[Id] = t.[Id]
                WHERE t.[Node] <> hierarchyid::Parse(x.[Path]);
                DECLARE @tb0_anc TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);
                WITH tb0_anc ([Node]) AS (
                    SELECT [Node] FROM @tb0_old
                    UNION SELECT t.[Node] FROM [sv].[Regions] AS t JOIN @tb0_aff AS a ON a.[Id] = t.[Id]
                    UNION ALL SELECT x.[Node].GetAncestor(1) FROM tb0_anc AS x WHERE x.[Node].GetLevel() > 1)
                INSERT INTO @tb0_anc ([Node]) SELECT DISTINCT [Node] FROM tb0_anc
                OPTION (MAXRECURSION 3);
                UPDATE t SET t.[SubtreeCount] = x.[Cnt]
                FROM [sv].[Regions] AS t
                JOIN @tb0_anc AS n ON n.[Node] = t.[Node]
                CROSS APPLY (SELECT COUNT(*) AS [Cnt] FROM [sv].[Regions] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt];
                SELECT @tb0_now AS [Stamp], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'U') AS [ChildUpdated], (SELECT COUNT(*) FROM @tb0_touched WHERE [Op] = 'D') AS [Deleted];

                """,
                text.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_tree_statements_of_a_save_take_the_ordinal_of_its_statement()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            _ = batch.Sql($"SELECT 1;", null);

            _ = batch.Save([new Area { Id = 5, Name = "x" }]);

            string text = batch.Statements[1].Text;
            Assert.Contains("WITH tb1_walk ([Id], [Path]) AS (", text, StringComparison.Ordinal);
            Assert.Contains("WITH tb1_anc ([Node]) AS (", text, StringComparison.Ordinal);
            Assert.Contains("DECLARE @tb1_aff TABLE ([Id] int PRIMARY KEY);", text, StringComparison.Ordinal);
            Assert.DoesNotContain("tb#_", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_verify_statement_recounts_every_row_of_an_activatable_tree_and_selects_how_many_it_repaired()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Statements.Verify(batch, Metadata<Area>());

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                UPDATE t SET t.[SubtreeCount] = x.[Cnt], t.[ActiveSubtreeCount] = x.[ActiveCnt]
                FROM [sv].[Areas] AS t
                CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.[IsActive] = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]
                             FROM [sv].[Areas] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt] OR t.[ActiveSubtreeCount] <> x.[ActiveCnt];
                SELECT @@ROWCOUNT AS [Repaired];

                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("sv", "Areas")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(typeof(Area), statement.EntityType);
        }

        [Fact]
        public void The_verify_statement_of_a_plain_tree_keeps_the_subtree_count_alone()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Statements.Verify(batch, Metadata<Region>());

            Assert.Equal(
                """
                UPDATE t SET t.[SubtreeCount] = x.[Cnt]
                FROM [sv].[Regions] AS t
                CROSS APPLY (SELECT COUNT(*) AS [Cnt] FROM [sv].[Regions] AS d WHERE d.[Node].IsDescendantOf(t.[Node]) = 1) AS x
                WHERE t.[SubtreeCount] <> x.[Cnt];
                SELECT @@ROWCOUNT AS [Repaired];

                """,
                Assert.Single(batch.Statements).Text,
                ignoreLineEndingDifferences: true);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(3, true)]
        public async Task A_verify_reads_its_repairs_meters_the_recount_and_warns_of_a_repair(int repaired, bool warned)
        {
            IMeterFactory meters = _harness.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> recomputes = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.TreeRecomputes);
            using MetricCollector<long> repairs = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.TreeRepairs);
            FakeLogger<TreeStatements> logger = new();
            TreeStatements statements = new(_harness.Services.GetRequiredService<DataMetrics>(), logger);
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            BatchResult<int> result = statements.Verify(batch, Metadata<Area>());

            // --- The round trip: the one count ---
            using DataTable count = new();
            _ = count.Columns.Add("Repaired", typeof(int));
            _ = count.Rows.Add(repaired);
            using DataTableReader reader = new(count);
            ResultSetCursor cursor = new(reader);
            await batch.Statements[0].Reader!(cursor, Token);
            cursor.ApplyCompletions();
            foreach (Func<CancellationToken, Task> step in batch.ExecutedSteps)
            {
                await step(Token);
            }

            Assert.Equal(repaired, result.Value);
            Assert.Equal("sv.Area", Assert.Single(recomputes.GetMeasurementSnapshot()).Tags[DataTelemetryNames.EntityTag]);
            Assert.Equal(warned ? [repaired] : [], repairs.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            if (warned)
            {
                FakeLogRecord record = Assert.Single(logger.Collector.GetSnapshot());
                Assert.Equal((LogLevel.Warning, "TreeVerify.Repaired"), (record.Level, record.Id.Name));
                Assert.Contains("[sv].[Areas]", record.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(logger.Collector.GetSnapshot());
            }
        }

        [Fact]
        public void A_verify_of_a_table_that_is_not_a_tree_or_on_a_read_batch_is_refused()
        {
            _ = Assert.Throws<ArgumentException>(() => Statements.Verify(_harness.Batch(BatchPurpose.Maintenance), Metadata<Crate>()));
            _ = Assert.Throws<InvalidOperationException>(() => Statements.Verify(_harness.Batch(BatchPurpose.Read), Metadata<Area>()));
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>An entity's metadata.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>The metadata.</returns>
        private EntityMetadata Metadata<TEntity>()
        {
            return _harness.Executor.Queries.Metadata.Get(typeof(TEntity));
        }
    }
}
