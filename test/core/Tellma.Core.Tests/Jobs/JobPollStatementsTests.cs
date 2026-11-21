// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The poll's own statements with no database: the golden text of the gap check and hold, the
    ///     backlog and orphan samples, the retired-key cancel and the heartbeat stamp, the table each
    ///     writes, that each may run again, and what each binds.
    /// </summary>
    public sealed class JobPollStatementsTests : IDisposable
    {
        /// <summary>A batch harness with Core's table types.</summary>
        private readonly BatchHarness _harness = new();

        [Fact]
        public void The_gap_check_holds_the_overdue_work_after_a_gap_and_reads_the_stamp_the_clock_and_every_held_count()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            BatchResult<JobGapCheck> check = JobStatements.GapCheckAndHold(batch, TimeSpan.FromHours(6));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_last datetimeoffset(3) = (SELECT [LastTickAt] FROM [core].[JobWorkerState] WHERE [Id] = 1);
                DECLARE @tb0_gap bit = CASE WHEN @tb0_last < DATEADD(minute, -@tb0_p0, @tb0_now) THEN 1 ELSE 0 END;
                IF @tb0_gap = 1
                    UPDATE [j]
                    SET [Status] = 'Held', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
                        [ErrorCode] = N'Jobs.HeldAfterGap', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
                    FROM [core].[Jobs] AS [j]
                    WHERE [j].[Status] IN ('Pending', 'Running')
                      AND [j].[DueAt] < DATEADD(minute, -@tb0_p0, @tb0_now)
                      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb0_now);
                SELECT @tb0_last AS [PreviousTickAt], @tb0_now AS [Now];
                SELECT [h].[RequestedById], COUNT(*) AS [HeldCount]
                FROM [core].[Jobs] AS [h]
                WHERE @tb0_gap = 1 AND [h].[Status] = 'Held'
                GROUP BY [h].[RequestedById];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- The threshold binds in minutes; the statement re-runs safely ---
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal([360], statement.Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
            Assert.False(check.IsCompleted);
        }

        [Fact]
        public void A_threshold_that_is_not_whole_minutes_binds_rounded_up()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.GapCheckAndHold(batch, TimeSpan.FromMinutes(90.5));

            Assert.Equal([91], batch.Statements[0].Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Fact]
        public void A_gap_is_a_stamp_older_than_the_threshold_and_its_held_count_sums_every_requester()
        {
            DateTimeOffset now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);
            var threshold = TimeSpan.FromHours(6);
            JobHeldCount[] held = [new(101, 2), new(null, 5)];

            Assert.True(new JobGapCheck(now.AddHours(-7), now, threshold, held).GapDetected);
            Assert.Equal(7, new JobGapCheck(now.AddHours(-7), now, threshold, held).HeldCount);
            Assert.True(new JobGapCheck(now.AddHours(-7), now, threshold, []).GapDetected);

            // --- A stamp within the threshold, or none at all (a tenant never polled), is no gap ---
            Assert.False(new JobGapCheck(now.AddHours(-6), now, threshold, []).GapDetected);
            Assert.False(new JobGapCheck(null, now, threshold, []).GapDetected);
        }

        [Fact]
        public void The_backlog_sample_seeks_each_key_s_oldest_available_job_and_ages_it_on_the_database_clock()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.BacklogSample(batch, ["acme.ship", "acme.digest"]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                SELECT [k].[Id] AS [HandlerKey], DATEDIFF_BIG(millisecond, [o].[DueAt], @tb0_now) AS [AgeMs]
                FROM @tb0_t0 AS [k]
                CROSS APPLY (
                    SELECT TOP (1) [j].[DueAt]
                    FROM [core].[Jobs] AS [j]
                    WHERE [j].[Status] IN ('Pending', 'Running')
                      AND [j].[HandlerKey] = [k].[Id]
                      AND [j].[DueAt] <= @tb0_now
                      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb0_now)
                    ORDER BY [j].[DueAt]) AS [o];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- A read: it writes nothing and re-runs safely ---
            Assert.Empty(statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(["acme.ship", "acme.digest"], Keys(statement));
        }

        [Fact]
        public void The_orphan_sample_counts_the_pending_jobs_an_hour_overdue_of_no_key_the_instance_knows()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.OrphanSample(batch, ["acme.ship", "acme.retired"]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                SELECT COUNT(*) AS [Orphaned]
                FROM [core].[Jobs] AS [j]
                WHERE [j].[Status] = 'Pending' AND [j].[DueAt] < DATEADD(hour, -1, SYSUTCDATETIME())
                  AND NOT EXISTS (SELECT 1 FROM @tb0_t0 AS [k] WHERE [k].[Id] = [j].[HandlerKey]);
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Empty(statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(["acme.ship", "acme.retired"], Keys(statement));
        }

        [Fact]
        public void The_retired_key_cancel_ends_every_unfinished_job_of_a_retired_key_without_a_live_lease_and_outputs_its_former_status()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.RetiredKeyCancel(batch, ["acme.retired"]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [Status] = 'Cancelled', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [CompletedAt] = @tb0_now,
                    [ErrorCode] = N'Jobs.HandlerRetired', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL, [PendingConfirmationsJson] = NULL
                OUTPUT [inserted].[Id], [deleted].[Status]
                FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
                INNER JOIN @tb0_t0 AS [k] ON [k].[Id] = [j].[HandlerKey]
                WHERE [j].[Status] IN ('Pending', 'Held', 'PendingConfirmation', 'Running')
                  AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb0_now);
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(["acme.retired"], Keys(statement));
        }

        [Fact]
        public void The_heartbeat_stamp_writes_at_most_once_a_minute_and_returns_the_stamp_it_wrote()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            BatchResult<DateTimeOffset?> stamp = JobStatements.HeartbeatStamp(batch, "host-a/41");

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [core].[JobWorkerState] SET [LastTickAt] = @tb0_now, [LastTickOwner] = @tb0_p0
                OUTPUT [inserted].[LastTickAt]
                WHERE [Id] = 1 AND ([LastTickAt] IS NULL OR [LastTickAt] < DATEADD(second, -60, @tb0_now));
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "JobWorkerState")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(["host-a/41"], statement.Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
            Assert.False(stamp.IsCompleted);
        }

        [Fact]
        public void Statements_appended_in_poll_order_take_consecutive_names()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.GapCheckAndHold(batch, TimeSpan.FromHours(6));
            _ = JobStatements.BacklogSample(batch, ["acme.ship"]);
            _ = JobStatements.HeartbeatStamp(batch, "host-a/41");

            // --- Each block's names are its own, so the round trip's statements never collide ---
            Assert.Equal(3, batch.Statements.Count);
            Assert.StartsWith("DECLARE @tb1_now", batch.Statements[1].Text, StringComparison.Ordinal);
            Assert.StartsWith("DECLARE @tb2_now", batch.Statements[2].Text, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>The keys a statement's one table parameter binds.</summary>
        /// <param name="statement">The statement.</param>
        /// <returns>The keys, in order.</returns>
        private static List<string> Keys(BatchStatement statement)
        {
            TableParameter table = Assert.Single(statement.Parameters.OfType<TableParameter>());
            List<string> keys = [];

            // The binder streams one record it refills per row, so each key is read as it passes.
            foreach (SqlDataRecord record in (IEnumerable<SqlDataRecord>)table.Create().Value)
            {
                keys.Add(record.GetString(0));
            }

            return keys;
        }
    }
}
