// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.3, §6.9 and §6.10: the poll round trip's statements beside the claims and the
    // tick: the gap check and hold first, the backlog and orphan samples, the retired-key cancel, and
    // the heartbeat stamp last. Every one tolerates the executor's re-run of the round trip (§6.8):
    // the hold changes only rows still queued, its counts are read from the table, the samples only
    // read, the cancel changes only rows not yet cancelled, and the stamp is the last statement.
    /// <content>The poll round trip's statements beside the claims.</content>
    internal static partial class JobStatements
    {
        /// <summary>The worker state's table.</summary>
        public static TableName WorkerState { get; } = new("core", JobWorkerStateConfiguration.Table);

        /// <summary>The writes of a statement that writes the worker state's table alone.</summary>
        private static readonly FrozenSet<TableName> WorkerStateWrites = new[] { WorkerState }.ToFrozenSet();

        /// <summary>
        ///     Appends the gap check and hold: when the last poll's stamp is older than the threshold, it
        ///     holds every queued job overdue by more than it (leaving a running row whose lease holds),
        ///     and returns the stamp, the database's clock, and every held row's count per requester.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="gapThreshold">How long the worker may have been away, bound in whole minutes, rounded up.</param>
        /// <returns>The check, once the batch executed.</returns>
        public static BatchResult<JobGapCheck> GapCheckAndHold(DataBatch batch, TimeSpan gapThreshold)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gapThreshold, TimeSpan.Zero);

            BatchResult<JobGapCheck> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names and the threshold in minutes ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string last = draft.Name(ReservedSqlNames.StatementNames.Last);
                string gap = draft.Name(ReservedSqlNames.StatementNames.Gap);
                int thresholdMinutes = (int)Math.Ceiling(gapThreshold.TotalMinutes);
                string minutes = draft.Scalar(SqlDbType.Int, thresholdMinutes);

                // --- The text: the check, the hold, the stamp and the clock, the counts ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {last} datetimeoffset(3) = (SELECT [LastTickAt] FROM {WorkerState.Delimited} WHERE [Id] = {JobWorkerState.RowId});\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {gap} bit = CASE WHEN {last} < DATEADD(minute, -{minutes}, {now}) THEN 1 ELSE 0 END;\n")
                    .Append(CultureInfo.InvariantCulture, $"IF {gap} = 1\n")
                    .Append("    UPDATE [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"    SET [Status] = '{JobStatus.Held}', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,\n")
                    .Append(CultureInfo.InvariantCulture, $"        [ErrorCode] = N'{JobErrorCodes.HeldAfterGap}', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"    FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"    WHERE [j].[Status] IN ('{JobStatus.Pending}', '{JobStatus.Running}')\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND [j].[DueAt] < DATEADD(minute, -{minutes}, {now})\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < {now});\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT {last} AS [PreviousTickAt], {now} AS [Now];\n")
                    .Append("SELECT [h].[RequestedById], COUNT(*) AS [HeldCount]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [h]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE {gap} = 1 AND [h].[Status] = '{JobStatus.Held}'\n")
                    .Append("GROUP BY [h].[RequestedById];");

                // --- The reader: the stamp and the clock, then the counts ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader clock = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await clock.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The gap check at {ordinal} returned no clock row."));
                    }

                    DateTimeOffset? previous = clock.IsDBNull(0) ? null : clock.GetFieldValue<DateTimeOffset>(0);
                    DateTimeOffset at = clock.GetFieldValue<DateTimeOffset>(1);

                    DbDataReader counts = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<JobHeldCount> held = [];
                    while (await counts.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        held.Add(new JobHeldCount(counts.IsDBNull(0) ? null : counts.GetInt32(0), counts.GetInt32(1)));
                    }

                    cursor.CountRows(1 + held.Count);
                    cursor.Complete(result, new JobGapCheck(previous, at, TimeSpan.FromMinutes(thresholdMinutes), held));
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §6.3: a seek per key on IX_Jobs_Available, never a MIN across keys. The predicate
        // is the claim's availability predicate (the index's filter literals verbatim, and the lease
        // column the index includes), so the seek is covered and the sample sees exactly what a
        // claim could lease: a pending row, or a running row whose lease lapsed. The age is the
        // database's clock less the row's due time, so no instance's clock skews it.
        /// <summary>
        ///     Appends the backlog sample: for each key, how long its oldest available job is overdue,
        ///     in milliseconds; a key with nothing overdue returns no row.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="handlerKeys">The keys to sample: every key this instance registers.</param>
        /// <returns>The overdue age of each key with an overdue job, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<JobBacklogSample>> BacklogSample(DataBatch batch, IReadOnlyList<string> handlerKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(handlerKeys);

            BatchResult<IReadOnlyList<JobBacklogSample>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The clock and the keys ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string keys = draft.Table(Keys(handlerKeys));

                // --- The text: the oldest overdue available row of each key ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [k].[Id] AS [HandlerKey], DATEDIFF_BIG(millisecond, [o].[DueAt], {now}) AS [AgeMs]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {keys} AS [k]\n")
                    .Append("CROSS APPLY (\n")
                    .Append("    SELECT TOP (1) [j].[DueAt]\n")
                    .Append(CultureInfo.InvariantCulture, $"    FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"    WHERE [j].[Status] IN ('{JobStatus.Pending}', '{JobStatus.Running}')\n")
                    .Append("      AND [j].[HandlerKey] = [k].[Id]\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND [j].[DueAt] <= {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < {now})\n")
                    .Append("    ORDER BY [j].[DueAt]) AS [o];");

                // --- The reader: each overdue key's age ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), writes: null, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<JobBacklogSample> samples = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        samples.Add(new JobBacklogSample(set.GetString(0), TimeSpan.FromMilliseconds(set.GetInt64(1))));
                    }

                    cursor.CountRows(samples.Count);
                    cursor.Complete(result, samples);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>
        ///     Appends the orphan sample: the pending jobs overdue by more than an hour whose key is none
        ///     of <paramref name="knownKeys" />.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="knownKeys">The keys this instance registers or retires.</param>
        /// <returns>The count, once the batch executed.</returns>
        public static BatchResult<long> OrphanSample(DataBatch batch, IReadOnlyList<string> knownKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(knownKeys);

            BatchResult<long> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The keys ---
                string keys = draft.Table(Keys(knownKeys));

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append("SELECT COUNT(*) AS [Orphaned]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[Status] = '{JobStatus.Pending}' AND [j].[DueAt] < DATEADD(hour, -1, SYSUTCDATETIME())\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND NOT EXISTS (SELECT 1 FROM {keys} AS [k] WHERE [k].[Id] = [j].[HandlerKey]);");

                // --- The reader: the count ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), writes: null, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    long count = await set.ReadAsync(cancellationToken).ConfigureAwait(false) ? set.GetInt32(0) : 0;
                    cursor.CountRows(1);
                    cursor.Complete(result, count);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §6.9: a retired key's queued, held and waiting rows end Cancelled with
        // Jobs.HandlerRetired, and a running row only once its lease lapsed; the output names each
        // row's status before the cancel, so the worker withdraws the confirmation notice of each row
        // that was waiting for one.
        /// <summary>
        ///     Appends the retired-key cancel: it cancels every job of a retired key that has not ended
        ///     and holds no live lease, and returns each with the status it had.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="retiredKeys">The keys this instance retires.</param>
        /// <returns>The cancelled jobs, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<RetiredJobCancelled>> RetiredKeyCancel(DataBatch batch, IReadOnlyList<string> retiredKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(retiredKeys);

            BatchResult<IReadOnlyList<RetiredJobCancelled>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The clock and the keys ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string keys = draft.Table(Keys(retiredKeys));

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append("UPDATE [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"SET [Status] = '{JobStatus.Cancelled}', [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL, [CompletedAt] = {now},\n")
                    .Append(CultureInfo.InvariantCulture, $"    [ErrorCode] = N'{JobErrorCodes.HandlerRetired}', [ErrorArgumentsJson] = NULL, [ErrorTraceId] = NULL, [PendingConfirmationsJson] = NULL\n")
                    .Append("OUTPUT [inserted].[Id], [deleted].[Status]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {keys} AS [k] ON [k].[Id] = [j].[HandlerKey]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[Status] IN ('{JobStatus.Pending}', '{JobStatus.Held}', '{JobStatus.PendingConfirmation}', '{JobStatus.Running}')\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < {now});");

                // --- The reader: each cancelled row and its former status ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<RetiredJobCancelled> cancelled = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancelled.Add(new RetiredJobCancelled(set.GetInt32(0), Enum.Parse<JobStatus>(set.GetString(1), ignoreCase: false)));
                    }

                    cursor.CountRows(cancelled.Count);
                    cursor.Complete(result, cancelled);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §6.10: the poll's last statement, so a re-run of a round trip that failed before
        // it sees the gap again, throttled to one write per minute per tenant. The stamp it wrote is
        // returned, so the worker logs the heartbeat the log-based alert reads only when it wrote.
        /// <summary>
        ///     Appends the heartbeat stamp: it records this poll and its owner on the worker state's row,
        ///     unless the row was stamped within the last minute, and returns the stamp it wrote.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="owner">The poll's lease owner.</param>
        /// <returns>The stamp written; null when the throttle left the row alone, once the batch executed.</returns>
        public static BatchResult<DateTimeOffset?> HeartbeatStamp(DataBatch batch, string owner)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(owner);

            BatchResult<DateTimeOffset?> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The clock and the owner ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string holder = draft.Scalar(SqlDbType.NVarChar, owner, JobConfiguration.LeaseOwnerLength);

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"UPDATE {WorkerState.Delimited} SET [LastTickAt] = {now}, [LastTickOwner] = {holder}\n")
                    .Append("OUTPUT [inserted].[LastTickAt]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [Id] = {JobWorkerState.RowId} AND ([LastTickAt] IS NULL OR [LastTickAt] < DATEADD(second, -60, {now}));");

                // --- The reader: the stamp, when one was written ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), WorkerStateWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    DateTimeOffset? stamped = await set.ReadAsync(cancellationToken).ConfigureAwait(false) ? set.GetFieldValue<DateTimeOffset>(0) : null;
                    cursor.CountRows(stamped is null ? 0 : 1);
                    cursor.Complete(result, stamped);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>Handler keys as the rows of a <c>StringList</c> parameter.</summary>
        /// <param name="keys">The keys.</param>
        /// <returns>The rows.</returns>
        private static List<StringList> Keys(IReadOnlyList<string> keys)
        {
            return [.. keys.Select(static key => new StringList { Id = key })];
        }
    }

    /// <summary>What the gap check found.</summary>
    /// <param name="PreviousTickAt">The stamp of the last poll; null for a tenant never polled.</param>
    /// <param name="Now">The database's clock when the check ran.</param>
    /// <param name="Threshold">The threshold the check bound, in whole minutes.</param>
    /// <param name="Held">Every held job's count per requester; empty unless the check found a gap.</param>
    internal sealed record JobGapCheck(DateTimeOffset? PreviousTickAt, DateTimeOffset Now, TimeSpan Threshold, IReadOnlyList<JobHeldCount> Held)
    {
        /// <summary>Whether the check found the worker away past the threshold, as the statement decided it.</summary>
        public bool GapDetected => PreviousTickAt < Now - Threshold;

        /// <summary>The held jobs, every requester's and those of none.</summary>
        public int HeldCount => Held.Sum(static count => count.Count);
    }

    /// <summary>How many held jobs one requester has.</summary>
    /// <param name="RequestedById">The requester; null for the jobs no user requested.</param>
    /// <param name="Count">The held jobs.</param>
    internal sealed record JobHeldCount(int? RequestedById, int Count);

    /// <summary>How long one key's oldest available job is overdue.</summary>
    /// <param name="HandlerKey">The key.</param>
    /// <param name="Age">How long past its due time the job is.</param>
    internal sealed record JobBacklogSample(string HandlerKey, TimeSpan Age);

    /// <summary>One job of a retired key that the retired-key cancel ended.</summary>
    /// <param name="Id">The job.</param>
    /// <param name="PreviousStatus">Its status before the cancel.</param>
    internal sealed record RetiredJobCancelled(int Id, JobStatus PreviousStatus);
}
