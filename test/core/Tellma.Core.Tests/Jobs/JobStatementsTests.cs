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
    ///     The lease statements with no database: the golden text of the claim, the renewal, the
    ///     fenced progress append and the completion, the table each writes, whether each may run
    ///     twice, and the rows each binds, the completion's bound when the batch is sent rather than
    ///     when it was appended.
    /// </summary>
    public sealed class JobStatementsTests : IDisposable
    {
        /// <summary>A claim's token.</summary>
        private static readonly Guid Token = new("9f0c5b1e-3a52-4c1d-8e7b-0d6a2f4c9b31");

        /// <summary>A batch harness with Core's table types.</summary>
        private readonly BatchHarness _harness = new();

        // --- The claim ---

        [Fact]
        public void A_claim_collects_the_tokens_earlier_rows_leases_the_room_left_and_returns_every_row_in_due_order()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            ClaimStatement claim = JobStatements.Claim(batch, "acme.ship", 25, Token, 300, "host-a/41");

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                DECLARE @tb0_claimed TABLE ([Id] int NOT NULL PRIMARY KEY);
                INSERT INTO @tb0_claimed ([Id])
                SELECT [j].[Id] FROM [core].[Jobs] AS [j]
                WHERE [j].[LeaseToken] = @tb0_p2 AND [j].[LeaseExpiresAt] >= @tb0_now;
                DECLARE @tb0_room int = @tb0_p1 - (SELECT COUNT(*) FROM @tb0_claimed);
                WITH [due] AS (
                    SELECT TOP (@tb0_room) [j].*
                    FROM [core].[Jobs] AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)
                    WHERE [j].[Status] IN ('Pending', 'Running')
                      AND [j].[HandlerKey] = @tb0_p0
                      AND [j].[DueAt] <= @tb0_now
                      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < @tb0_now)
                    ORDER BY [j].[DueAt], [j].[Id])
                UPDATE [due]
                SET [Status] = 'Running', [LeaseToken] = @tb0_p2, [LeaseExpiresAt] = DATEADD(second, @tb0_p3, @tb0_now),
                    [LeaseOwner] = @tb0_p4, [Attempts] = [Attempts] + 1, [StartedAt] = COALESCE([StartedAt], @tb0_now)
                OUTPUT [inserted].[Id] INTO @tb0_claimed;
                SELECT [j].* FROM [core].[Jobs] AS [j] INNER JOIN @tb0_claimed AS [c] ON [c].[Id] = [j].[Id] ORDER BY [j].[DueAt], [j].[Id];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- Idempotent under its token, so the executor may run it again; its table variable is named for the entity load ---
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
            Assert.Equal("@tb0_claimed", claim.ClaimedTable);
            Assert.False(claim.Rows.IsCompleted);
            Assert.Equal(["acme.ship", 25, Token, 300, "host-a/41"], statement.Parameters.Cast<ScalarParameter>().Select(static parameter => parameter.Value));
        }

        [Theory]
        [InlineData(0, 300)]
        [InlineData(501, 300)]
        [InlineData(1, 14)]
        public void A_claim_outside_the_batch_size_or_lease_range_is_refused_before_anything_is_appended(int size, int leaseSeconds)
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => JobStatements.Claim(batch, "acme.ship", size, Token, leaseSeconds, "host-a/41"));
            Assert.Empty(batch.Statements);
        }

        // --- The renewal ---

        [Fact]
        public void A_renewal_extends_the_leases_by_token_writes_the_buffered_progress_and_returns_the_rows_still_leased()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);
            Guid other = new("4b8d7e21-6c0f-4f3a-9d15-7e2a1c8b5f60");

            _ = JobStatements.Renew(
                batch,
                [new JobLeaseList { Id = Token, LeaseSeconds = 300 }, new JobLeaseList { Id = other, LeaseSeconds = 60 }],
                [new JobProgressList { Id = 11, ProgressPercent = 40, ProgressKey = "Acme_Shipping", ProgressArgumentsJson = /*lang=json,strict*/ """{"count":3}""" }]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [LeaseExpiresAt] = DATEADD(second, [t].[LeaseSeconds], @tb0_now),
                    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
                    [ProgressKey] = COALESCE([p].[ProgressKey], [j].[ProgressKey]),
                    [ProgressArgumentsJson] = CASE WHEN [p].[ProgressKey] IS NULL THEN [j].[ProgressArgumentsJson] ELSE [p].[ProgressArgumentsJson] END,
                    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
                OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]
                FROM [core].[Jobs] AS [j]
                INNER JOIN @tb0_t0 AS [t] ON [t].[Id] = [j].[LeaseToken]
                LEFT JOIN @tb0_t1 AS [p] ON [p].[Id] = [j].[Id]
                WHERE [j].[Status] = 'Running';
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.True(statement.MayRunTwice);

            // --- The tokens and the progress, each as its own table ---
            Assert.Equal([Token, other], Rows(statement, 0).Select(static row => row["Id"]));
            IReadOnlyDictionary<string, object?> progress = Assert.Single(Rows(statement, 1));
            Assert.Equal(11, progress["Id"]);
            Assert.Equal((byte)40, progress["ProgressPercent"]);
            Assert.Equal("Acme_Shipping", progress["ProgressKey"]);
            Assert.Null(progress["StateJson"]);
        }

        // --- The fenced progress append ---

        [Fact]
        public void A_progress_append_writes_the_rows_leased_under_its_token_and_throws_the_lease_fence_when_one_is_not()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            JobStatements.AppendProgress(batch, Token, [new JobProgressList { Id = 11, StateJson = /*lang=json,strict*/ """{"page":4}""" }]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                UPDATE [j] SET [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),
                    [ProgressKey] = COALESCE([p].[ProgressKey], [j].[ProgressKey]),
                    [ProgressArgumentsJson] = CASE WHEN [p].[ProgressKey] IS NULL THEN [j].[ProgressArgumentsJson] ELSE [p].[ProgressArgumentsJson] END,
                    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [p] ON [p].[Id] = [j].[Id]
                WHERE [j].[LeaseToken] = @tb0_p0 AND [j].[Status] = 'Running';
                IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb0_t0) THROW 50422, N'Job.LeaseLost', 1;
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);

            // --- Never run twice: it rides the caller's transaction ---
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.Null(statement.Reader);
        }

        // --- The completion ---

        [Fact]
        public void A_completion_records_each_outcome_on_the_jobs_leased_under_its_token_and_throws_the_lease_fence_when_one_is_not()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = JobStatements.Complete(batch, Token, [JobOutcome.Succeeded(null).ToRow(11)]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DECLARE @tb0_now datetimeoffset(3) = SYSUTCDATETIME();
                UPDATE [j]
                SET [Status] = [o].[Status],
                    [DueAt] = CASE WHEN [o].[RetryAfterSeconds] IS NULL THEN [j].[DueAt] ELSE DATEADD(second, [o].[RetryAfterSeconds], @tb0_now) END,
                    [Attempts] = CASE WHEN [o].[Released] = 1 THEN [j].[Attempts] - 1 ELSE [j].[Attempts] END,
                    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,
                    [CompletedAt] = CASE WHEN [o].[Status] IN ('Succeeded', 'Failed', 'Cancelled') THEN @tb0_now ELSE NULL END,
                    [ErrorCode] = [o].[ErrorCode], [ErrorArgumentsJson] = [o].[ErrorArgumentsJson], [ErrorTraceId] = [o].[ErrorTraceId],
                    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]), [PendingConfirmationsJson] = [o].[PendingConfirmationsJson],
                    [ProgressPercent] = CASE WHEN [o].[Status] = 'Succeeded' THEN 100 ELSE [j].[ProgressPercent] END
                OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]
                FROM [core].[Jobs] AS [j] INNER JOIN @tb0_t0 AS [o] ON [o].[Id] = [j].[Id]
                WHERE [j].[LeaseToken] = @tb0_p0 AND [j].[Status] = 'Running';
                IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tb0_t0) THROW 50422, N'Job.LeaseLost', 1;
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
        }

        [Fact]
        public void A_completions_outcomes_are_bound_when_the_batch_is_sent_so_the_worker_fills_them_after_the_handler_appended()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            List<JobOutcomeList> outcomes = [];

            // --- Appended empty, before the handler's statements ---
            _ = JobStatements.Complete(batch, Token, outcomes);
            _ = batch.Sql($"SELECT 1 AS [HandlerWork]", options: null);
            outcomes.Add(JobOutcome.Released().ToRow(11));
            outcomes.Add(JobOutcome.Retry(30, new JobOutcomeError(JobErrorCodes.Internal, null, "0af7651916cd43dd8448eb211c80319c"), /*lang=json,strict*/ """{"page":2}""").ToRow(12));

            // --- The rows read now are the ones the worker filled ---
            List<IReadOnlyDictionary<string, object?>> rows = Rows(batch.Statements[0], 0);
            Assert.Equal([11, 12], rows.Select(static row => row["Id"]));
            Assert.Equal([true, false], rows.Select(static row => row["Released"]));
            Assert.Equal([null, 30], rows.Select(static row => row["RetryAfterSeconds"]));
            Assert.Equal([null, JobErrorCodes.Internal], rows.Select(static row => row["ErrorCode"]));
            Assert.Equal([null, "0af7651916cd43dd8448eb211c80319c"], rows.Select(static row => row["ErrorTraceId"]));
            Assert.Equal([null, /*lang=json,strict*/ """{"page":2}"""], rows.Select(static row => row["StateJson"]));
            Assert.Equal(2, Assert.IsType<TableParameter>(batch.Statements[0].Parameters[1]).Rows);
        }

        [Fact]
        public void Each_outcome_maps_to_its_row()
        {
            JobOutcomeError error = new("Acme.Refused", /*lang=json,strict*/ """{"reason":"closed"}""", "0af7651916cd43dd8448eb211c80319c");

            // --- Status, release, delay, error group, checkpoint and pending record, per outcome ---
            Assert.Equivalent(
                new JobOutcomeList { Id = 1, Status = JobStatus.Succeeded, StateJson = "{}" },
                JobOutcome.Succeeded("{}").ToRow(1));
            Assert.Equivalent(
                new JobOutcomeList { Id = 2, Status = JobStatus.Pending, RetryAfterSeconds = 45, ErrorCode = error.Code, ErrorArgumentsJson = error.ArgumentsJson, ErrorTraceId = error.TraceId, StateJson = "{}" },
                JobOutcome.Retry(45, error, "{}").ToRow(2));
            Assert.Equivalent(
                new JobOutcomeList { Id = 3, Status = JobStatus.Pending, Released = true },
                JobOutcome.Released().ToRow(3));
            Assert.Equivalent(
                new JobOutcomeList { Id = 4, Status = JobStatus.Failed, ErrorCode = error.Code, ErrorArgumentsJson = error.ArgumentsJson, ErrorTraceId = error.TraceId, StateJson = "{}" },
                JobOutcome.Failed(error, "{}").ToRow(4));
            Assert.Equivalent(
                new JobOutcomeList { Id = 5, Status = JobStatus.Cancelled, ErrorCode = JobErrorCodes.Cancelled, StateJson = "{}" },
                JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, null), "{}").ToRow(5));
            Assert.Equivalent(
                new JobOutcomeList { Id = 6, Status = JobStatus.PendingConfirmation, RetryAfterSeconds = 0, StateJson = "{}", PendingConfirmationsJson = /*lang=json,strict*/ """{"count":1,"items":[]}""" },
                JobOutcome.PendingConfirmation(/*lang=json,strict*/ """{"count":1,"items":[]}""", "{}").ToRow(6));
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
