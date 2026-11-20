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
    ///     The job actions' and the retention's statements with no database: the golden text of the
    ///     retry, the cancel, the resume and the confirm over the target ids, of the retention stop
    ///     and of each retention band's delete page, the tables each writes, that each may run twice,
    ///     and the values each binds.
    /// </summary>
    public sealed class JobAdminStatementsTests : IDisposable
    {
        /// <summary>The job table.</summary>
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>A batch harness with Core's table types.</summary>
        private readonly BatchHarness _harness = new();

        // --- The actions ---

        [Fact]
        public void A_retry_makes_each_failed_target_pending_and_due_now_with_its_attempts_and_error_cleared()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.Retry(batch, [7, 3]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j] SET [Status] = 'Pending', [Attempts] = 0, [DueAt] = @tb0_now, [CompletedAt] = NULL, [CancelRequestedAt] = NULL,
                    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [i] ON [i].[Id] = [j].[Id]
                WHERE [j].[Status] = 'Failed';
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            AssertIdempotentOnJobs(statement);
            Assert.Equal([7, 3], Rows(statement, 0).Select(static row => row["Id"]));
        }

        [Fact]
        public void A_cancel_flags_a_running_row_re_queues_a_checkpointed_or_waiting_row_flagged_and_cancels_any_other()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.Cancel(batch, [5]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [Status] = CASE [k].[Kind] WHEN 'F' THEN [j].[Status] WHEN 'Q' THEN 'Pending' ELSE 'Cancelled' END,
                    [DueAt] = CASE [k].[Kind] WHEN 'Q' THEN @tb0_now ELSE [j].[DueAt] END,
                    [CancelRequestedAt] = CASE [k].[Kind] WHEN 'C' THEN [j].[CancelRequestedAt] ELSE @tb0_now END,
                    [CompletedAt] = CASE [k].[Kind] WHEN 'C' THEN @tb0_now ELSE [j].[CompletedAt] END,
                    [ErrorCode] = CASE [k].[Kind] WHEN 'C' THEN N'Jobs.Cancelled' ELSE [j].[ErrorCode] END,
                    [ErrorArgumentsJson] = CASE [k].[Kind] WHEN 'C' THEN NULL ELSE [j].[ErrorArgumentsJson] END,
                    [ErrorTraceId] = CASE [k].[Kind] WHEN 'C' THEN NULL ELSE [j].[ErrorTraceId] END,
                    [PendingConfirmationsJson] = NULL
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [i] ON [i].[Id] = [j].[Id]
                CROSS APPLY (SELECT CASE WHEN [j].[Status] = 'Running' OR ([j].[Status] = 'Pending' AND [j].[CancelRequestedAt] IS NOT NULL) THEN 'F'
                                         WHEN [j].[Status] = 'PendingConfirmation' OR [j].[StateJson] IS NOT NULL THEN 'Q' ELSE 'C' END) AS [k] ([Kind])
                WHERE [j].[Status] IN ('Pending', 'Held', 'Running', 'PendingConfirmation');
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            AssertIdempotentOnJobs(statement);
            Assert.Equal([5], Rows(statement, 0).Select(static row => row["Id"]));
        }

        [Fact]
        public void A_resume_makes_each_held_target_pending_and_due_now_with_its_flag_and_error_cleared()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.Resume(batch, [9]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j] SET [Status] = 'Pending', [DueAt] = @tb0_now, [CancelRequestedAt] = NULL,
                    [ErrorCode] = NULL, [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [i] ON [i].[Id] = [j].[Id]
                WHERE [j].[Status] = 'Held';
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            AssertIdempotentOnJobs(statement);
        }

        [Fact]
        public void A_confirm_merges_the_recorded_keys_for_the_requester_when_the_confirmed_keys_cover_them()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.Confirm(batch, [4, 6], ["Acme.Overdraft", "Acme.Backdated", "Acme.Overdraft"], 101);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [ConfirmedKeys] = CONCAT_WS(',', [j].[ConfirmedKeys], (SELECT STRING_AGG([w].[Key], ',') FROM (SELECT DISTINCT [Key]
                        FROM OPENJSON([j].[PendingConfirmationsJson], '$.items') WITH ([Key] nvarchar(450) '$.key')) AS [w])),
                    [Status] = 'Pending', [Attempts] = 0, [DueAt] = @tb0_now, [PendingConfirmationsJson] = NULL
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [i] ON [i].[Id] = [j].[Id]
                WHERE [j].[Status] = 'PendingConfirmation' AND [j].[RequestedById] = @tb0_p0
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON([j].[PendingConfirmationsJson], '$.items') WITH ([Key] nvarchar(450) '$.key') AS [w]
                                  WHERE NOT EXISTS (SELECT 1 FROM @tb0_t1 AS [k] WHERE [k].[Id] = [w].[Key]));
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            AssertIdempotentOnJobs(statement);

            // --- The targets, the keys once each, and the caller ---
            Assert.Equal([4, 6], Rows(statement, 0).Select(static row => row["Id"]));
            Assert.Equal(["Acme.Overdraft", "Acme.Backdated"], Rows(statement, 1).Select(static row => row["Id"]));
            Assert.Equal([101], statement.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        // --- The retention ---

        [Fact]
        public void A_retention_stop_re_queues_a_page_of_the_jobs_waiting_past_the_period_flagged_and_reads_them_with_their_requesters()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            BatchResult<IReadOnlyList<StoppedJob>> stopped = JobStatements.RetentionStop(batch, TimeSpan.FromDays(30));

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE TOP (1000) [j]
                SET [Status] = 'Pending', [DueAt] = @tb0_now, [CancelRequestedAt] = @tb0_now, [PendingConfirmationsJson] = NULL
                OUTPUT [inserted].[Id], [inserted].[RequestedById]
                FROM [core].[Jobs] AS [j]
                WHERE [j].[Status] = 'PendingConfirmation' AND [j].[DueAt] < DATEADD(second, -@tb0_p0, @tb0_now);
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            AssertIdempotentOnJobs(statement);
            Assert.NotNull(statement.Reader);
            Assert.False(stopped.IsCompleted);
            Assert.Equal([2_592_000], statement.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Fact]
        public void A_succeeded_page_deletes_a_page_of_the_succeeded_jobs_past_the_period_and_writes_every_table_it_names()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            HashSet<TableName> writes = [Jobs, new("core", "ScheduleStates"), new("jobs", "Shipments")];

            _ = JobStatements.RetentionPage(batch, succeeded: true, TimeSpan.FromDays(7), writes);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DELETE TOP (1000) FROM [core].[Jobs]
                WHERE [Status] IN ('Succeeded') AND [CompletedAt] < DATEADD(second, -@tb0_p0, SYSUTCDATETIME());
                SELECT @@ROWCOUNT AS [Deleted];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- The rows whose keys lose the deleted jobs are writes too ---
            Assert.Equivalent(writes, statement.Writes, strict: true);
            Assert.True(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
            Assert.Equal([604_800], statement.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Fact]
        public void An_ended_page_deletes_a_page_of_the_failed_and_cancelled_jobs_past_the_period()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = JobStatements.RetentionPage(batch, succeeded: false, TimeSpan.FromDays(30.5), new HashSet<TableName> { Jobs });

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DELETE TOP (1000) FROM [core].[Jobs]
                WHERE [Status] IN ('Failed', 'Cancelled') AND [CompletedAt] < DATEADD(second, -@tb0_p0, SYSUTCDATETIME());
                SELECT @@ROWCOUNT AS [Deleted];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([2_635_200], statement.Parameters.OfType<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Fact]
        public void A_retention_period_the_statements_cannot_bind_is_refused_before_anything_is_appended()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => JobStatements.RetentionStop(batch, TimeSpan.FromSeconds(-1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => JobStatements.RetentionPage(batch, succeeded: true, TimeSpan.FromSeconds((double)int.MaxValue + 1), new HashSet<TableName> { Jobs }));
            Assert.Empty(batch.Statements);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>Asserts a statement writes the job table alone and may run twice.</summary>
        /// <param name="statement">The statement.</param>
        private static void AssertIdempotentOnJobs(BatchStatement statement)
        {
            Assert.Equal([Jobs], statement.Writes);
            Assert.True(statement.MayRunTwice);
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
