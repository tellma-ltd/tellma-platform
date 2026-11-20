// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The scheduler's statements with no database: the golden text of both tick steps, the
    ///     read-only variant of the first, the pause, the state upsert, the paused reason's clear and
    ///     the run-now insert, the tables each writes, whether each may run twice, and what each binds.
    /// </summary>
    public sealed class ScheduleStatementsTests : IDisposable
    {
        /// <summary>A tick's token.</summary>
        private static readonly Guid Token = new("2c6f1a9e-7b34-4d85-a0e2-5f9c3b7d1e48");

        /// <summary>A batch harness with Core's table types.</summary>
        private readonly BatchHarness _harness = new();

        // --- Tick step 1 ---

        [Fact]
        public void Tick_step_one_collects_the_tokens_earlier_rows_then_leases_uninitialised_built_ins_then_due_rows_and_returns_every_schedule()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            BatchResult<IReadOnlyList<ScheduleTickRow>> rows = JobStatements.TickStep1(batch, Token, readOnlyKeys: null);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_due TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);
                INSERT INTO @tb0_due ([ScheduleId])
                SELECT [s].[ScheduleId] FROM [core].[ScheduleStates] AS [s]
                WHERE [s].[LeaseToken] = @tb0_p0 AND [s].[LeaseExpiresAt] >= @tb0_now;
                DECLARE @tb0_room int = 50 - (SELECT COUNT(*) FROM @tb0_due);
                UPDATE TOP (@tb0_room) [s] SET [LeaseToken] = @tb0_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb0_now)
                OUTPUT [inserted].[ScheduleId] INTO @tb0_due
                FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE [s].[ScheduleId] <= 999 AND [s].[NextDueAt] IS NULL
                  AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb0_now)
                  AND EXISTS (SELECT 1 FROM [core].[Schedules] AS [x] WHERE [x].[Id] = [s].[ScheduleId] AND [x].[IsActive] = 1);
                SET @tb0_room = 50 - (SELECT COUNT(*) FROM @tb0_due);
                WITH [d] AS (
                    SELECT TOP (@tb0_room) [s].*
                    FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= @tb0_now
                      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb0_now)
                    ORDER BY [s].[NextDueAt])
                UPDATE [d] SET [LeaseToken] = @tb0_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb0_now)
                OUTPUT [inserted].[ScheduleId] INTO @tb0_due;
                SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],
                       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId], [s].[Name], [s].[Name2], [s].[Name3],
                       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], @tb0_now AS [Now]
                FROM @tb0_due AS [d]
                INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [d].[ScheduleId]
                INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]
                INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- Idempotent under its token, so a re-run of the poll returns the same rows; the token alone binds ---
            Assert.Equal([new TableName("core", "ScheduleStates")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
            Assert.Equal([Token], statement.Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
            Assert.False(rows.IsCompleted);
        }

        [Fact]
        public void The_read_only_variant_of_tick_step_one_keeps_both_branches_to_the_keys_that_run_when_read_only()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.TickStep1(batch, Token, ["core.job-retention", "core.blob-sweep"]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_due TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);
                INSERT INTO @tb0_due ([ScheduleId])
                SELECT [s].[ScheduleId] FROM [core].[ScheduleStates] AS [s]
                WHERE [s].[LeaseToken] = @tb0_p0 AND [s].[LeaseExpiresAt] >= @tb0_now;
                DECLARE @tb0_room int = 50 - (SELECT COUNT(*) FROM @tb0_due);
                UPDATE TOP (@tb0_room) [s] SET [LeaseToken] = @tb0_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb0_now)
                OUTPUT [inserted].[ScheduleId] INTO @tb0_due
                FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
                WHERE [s].[ScheduleId] <= 999 AND [s].[NextDueAt] IS NULL
                  AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb0_now)
                  AND EXISTS (SELECT 1 FROM [core].[Schedules] AS [x] WHERE [x].[Id] = [s].[ScheduleId] AND [x].[IsActive] = 1)
                  AND EXISTS (SELECT 1 FROM [core].[Schedules] AS [k] WHERE [k].[Id] = [s].[ScheduleId] AND [k].[HandlerKey] IN (SELECT [Id] FROM @tb0_t0));
                SET @tb0_room = 50 - (SELECT COUNT(*) FROM @tb0_due);
                WITH [d] AS (
                    SELECT TOP (@tb0_room) [s].*
                    FROM [core].[ScheduleStates] AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= @tb0_now
                      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < @tb0_now)
                      AND EXISTS (SELECT 1 FROM [core].[Schedules] AS [k] WHERE [k].[Id] = [s].[ScheduleId] AND [k].[HandlerKey] IN (SELECT [Id] FROM @tb0_t0))
                    ORDER BY [s].[NextDueAt])
                UPDATE [d] SET [LeaseToken] = @tb0_p0, [LeaseExpiresAt] = DATEADD(second, 30, @tb0_now)
                OUTPUT [inserted].[ScheduleId] INTO @tb0_due;
                SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],
                       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId], [s].[Name], [s].[Name2], [s].[Name3],
                       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], @tb0_now AS [Now]
                FROM @tb0_due AS [d]
                INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [d].[ScheduleId]
                INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]
                INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.True(statement.MayRunTwice);
            Assert.Equal(["core.job-retention", "core.blob-sweep"], Rows(statement, 0).Select(static row => row["Id"]));
        }

        // --- Tick step 2 ---

        [Fact]
        public void Tick_step_two_fences_on_the_lease_reserves_ids_inserts_what_the_overlap_rule_admits_advances_the_states_and_returns_the_fired_jobs()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            DateTimeOffset now = new(2026, 3, 2, 10, 45, 0, TimeSpan.Zero);
            Job[] jobs =
            [
                new() { Id = 1, HandlerKey = "fixture.scheduled", DueAt = now, RunAsUserId = 101, RequestedById = 101, ScheduleId = 1000, ScheduledFor = now.AddMinutes(-15), CreatedAt = now },
                new() { Id = 2, HandlerKey = "fixture.scheduled", DueAt = now, RunAsUserId = 101, RequestedById = 101, ScheduleId = 1000, ScheduledFor = now, CreatedAt = now },
            ];
            ScheduleNextList[] next = [new() { ScheduleId = 1000, NextDueAt = now.AddMinutes(15), LastScheduledFor = now }, new() { ScheduleId = 1, NextDueAt = now.AddHours(1) }];

            BatchResult<IReadOnlyList<ScheduleFired>> fired = JobStatements.TickStep2(batch, Token, jobs, next);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_fired TABLE ([ScheduleId] int NOT NULL, [JobId] int NOT NULL);
                IF (SELECT COUNT(*) FROM [core].[ScheduleStates] AS [st] WITH (UPDLOCK, ROWLOCK) INNER JOIN @tb0_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
                    WHERE [st].[LeaseToken] = @tb0_p0 AND [st].[LeaseExpiresAt] >= @tb0_now) <> (SELECT COUNT(*) FROM @tb0_t1)
                    THROW 50422, N'Schedule.TickLeaseLost', 1;
                DECLARE @tb0_first sql_variant, @tb0_n int = (SELECT COUNT(*) FROM @tb0_t0);
                IF @tb0_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb0_n, @range_first_value = @tb0_first OUTPUT;
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
                OUTPUT [inserted].[ScheduleId], [inserted].[Id] INTO @tb0_fired
                SELECT CAST(@tb0_first AS int) + ROW_NUMBER() OVER (ORDER BY [r].[ScheduleId], [r].[ScheduledFor]) - 1,
                       [r].[HandlerKey], 'Pending', [r].[DueAt], 0, [r].[ArgumentsJson], [r].[RunAsUserId], [r].[RequestedById], [r].[ScheduleId], [r].[ScheduledFor], @tb0_now
                FROM @tb0_t0 AS [r]
                INNER JOIN [core].[Schedules] AS [s] ON [s].[Id] = [r].[ScheduleId]
                INNER JOIN [core].[ScheduleStates] AS [st] ON [st].[ScheduleId] = [r].[ScheduleId] AND [st].[LeaseToken] = @tb0_p0 AND [st].[LeaseExpiresAt] >= @tb0_now
                WHERE [s].[OverlapPolicy] = 'Allow'
                   OR NOT EXISTS (SELECT 1 FROM [core].[Jobs] AS [x] WHERE [x].[ScheduleId] = [r].[ScheduleId] AND [x].[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation'));
                UPDATE [st]
                SET [NextDueAt] = [n].[NextDueAt],
                    [LastScheduledFor] = COALESCE([n].[LastScheduledFor], [st].[LastScheduledFor]),
                    [LastFiredAt] = CASE WHEN [f].[ScheduleId] IS NOT NULL THEN @tb0_now ELSE [st].[LastFiredAt] END,
                    [LastJobId] = COALESCE([f].[JobId], [st].[LastJobId]),
                    [LastSkippedAt] = CASE WHEN [f].[ScheduleId] IS NULL AND EXISTS (SELECT 1 FROM @tb0_t0 AS [r] WHERE [r].[ScheduleId] = [st].[ScheduleId]) THEN @tb0_now ELSE [st].[LastSkippedAt] END,
                    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL
                FROM [core].[ScheduleStates] AS [st]
                INNER JOIN @tb0_t1 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]
                LEFT JOIN (SELECT [ScheduleId], MAX([JobId]) AS [JobId] FROM @tb0_fired GROUP BY [ScheduleId]) AS [f] ON [f].[ScheduleId] = [st].[ScheduleId]
                WHERE [st].[LeaseToken] = @tb0_p0;
                SELECT [ScheduleId], [JobId] FROM @tb0_fired ORDER BY [JobId];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- It inserts jobs, so it never runs twice; the jobs bind with their ordinals as ids ---
            Assert.Equal([new TableName("core", "Jobs"), new TableName("core", "ScheduleStates")], statement.Writes.OrderBy(static table => table.Name));
            Assert.False(statement.MayRunTwice);
            Assert.Equal([Token], statement.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
            List<IReadOnlyDictionary<string, object?>> bound = Rows(statement, 0);
            Assert.Equal([1, 2], bound.Select(static row => row["Id"]));
            Assert.Equal([now.AddMinutes(-15), now], bound.Select(static row => row["ScheduledFor"]));
            Assert.Equal([1000, 1], Rows(statement, 1).Select(static row => row["ScheduleId"]));
            Assert.False(fired.IsCompleted);
        }

        // --- The pause, the upsert, the clear ---

        [Theory]
        [InlineData(SchedulePausedReason.OwnerInactive)]
        [InlineData(SchedulePausedReason.Exhausted)]
        [InlineData(SchedulePausedReason.HandlerRetired)]
        public void The_pause_deactivates_the_schedules_still_active_with_the_reason_stamped_by_the_system_user(SchedulePausedReason reason)
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.Pause(batch, reason, [1000, 1001, 1000]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                $"""
                DECLARE @tb0_now datetimeoffset(7) = SYSUTCDATETIME();
                UPDATE [s] SET [IsActive] = 0, [PausedReason] = '{reason}', [ModifiedAt] = @tb0_now, [ModifiedById] = 1
                FROM [core].[Schedules] AS [s] INNER JOIN @tb0_t0 AS [p] ON [p].[Id] = [s].[Id]
                WHERE [s].[IsActive] = 1;
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Schedules")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.Equal([1000, 1001], Rows(statement, 0).Select(static row => row["Id"]));
        }

        [Fact]
        public void The_state_upsert_updates_the_rows_that_exist_releasing_any_tick_lease_and_inserts_the_rest()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            DateTimeOffset next = new(2026, 3, 3, 3, 0, 0, TimeSpan.Zero);

            JobStatements.UpsertScheduleStates(batch, [new ScheduleNextList { ScheduleId = 1000, NextDueAt = next }, new ScheduleNextList { ScheduleId = 1001 }]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                UPDATE [st] SET [NextDueAt] = [n].[NextDueAt], [LeaseToken] = NULL, [LeaseExpiresAt] = NULL
                FROM [core].[ScheduleStates] AS [st] INNER JOIN @tb0_t0 AS [n] ON [n].[ScheduleId] = [st].[ScheduleId];
                INSERT INTO [core].[ScheduleStates] ([ScheduleId], [NextDueAt])
                SELECT [n].[ScheduleId], [n].[NextDueAt] FROM @tb0_t0 AS [n]
                WHERE NOT EXISTS (SELECT 1 FROM [core].[ScheduleStates] AS [st] WHERE [st].[ScheduleId] = [n].[ScheduleId]);
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "ScheduleStates")], statement.Writes);
            Assert.Equal([next, null], Rows(statement, 0).Select(static row => row["NextDueAt"]));
        }

        [Fact]
        public void The_paused_reason_clear_touches_only_schedules_whose_reason_is_set()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.ClearPausedReason(batch, [1000]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                "UPDATE [s] SET [PausedReason] = NULL FROM [core].[Schedules] AS [s] INNER JOIN @tb0_t0 AS [p] ON [p].[Id] = [s].[Id] WHERE [s].[PausedReason] IS NOT NULL;",
                statement.Text);
            Assert.Equal([new TableName("core", "Schedules")], statement.Writes);
        }

        // --- Run-now ---

        [Fact]
        public void The_run_now_insert_takes_its_id_from_the_sequence_and_leaves_the_occurrence_null()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            BatchResult<int> job = JobStatements.RunNow(batch, 1000, 1, 101, "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_jobIds TABLE ([JobId] int NOT NULL);
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])
                OUTPUT [inserted].[Id] INTO @tb0_jobIds
                SELECT NEXT VALUE FOR [core].[sq_Jobs], [s].[HandlerKey], 'Pending', @tb0_now, 0, [s].[ArgumentsJson], @tb0_p3, @tb0_p1, @tb0_p2, [s].[Id], NULL, @tb0_now
                FROM [core].[Schedules] AS [s] WHERE [s].[Id] = @tb0_p0;
                SELECT [JobId] FROM @tb0_jobIds;
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.Equal(
                [1000, 1, 101, "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"],
                statement.Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
            Assert.False(job.IsCompleted);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>The rows one of a statement's table parameters binds, each copied column by column.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="index">The table parameter's position among the statement's table parameters.</param>
        /// <returns>The rows.</returns>
        private static List<IReadOnlyDictionary<string, object?>> Rows(BatchStatement statement, int index)
        {
            TableParameter table = statement.Parameters.OfType<TableParameter>().ElementAt(index);
            List<IReadOnlyDictionary<string, object?>> rows = [];

            // The binder streams one record it refills per row, so each is copied as it passes.
            foreach (SqlDataRecord record in (IEnumerable<SqlDataRecord>)table.Create().Value)
            {
                Dictionary<string, object?> row = new(StringComparer.Ordinal);
                for (int ordinal = 0; ordinal < record.FieldCount; ordinal++)
                {
                    row[record.GetName(ordinal)] = record.IsDBNull(ordinal) ? null : record.GetValue(ordinal);
                }

                rows.Add(row);
            }

            return rows;
        }
    }
}
