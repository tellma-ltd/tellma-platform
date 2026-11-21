// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §14.4: the retention's pages, each its own maintenance round trip, its cut-off
    // computed from the database clock inside the statement. The stop re-queues a row that waited
    // too long for confirmation flagged, so its next claim cancels it; a re-run after an ambiguous
    // failure finds those rows pending and outputs nothing. A delete page removes only ended rows, so
    // a re-run deletes the next page at most, which the loop would have deleted anyway.
    internal static partial class JobStatements
    {
        /// <summary>The most rows one retention page stops or deletes.</summary>
        public const int RetentionPageSize = 1_000;

        /// <summary>
        ///     Appends one page of the retention stop: up to <see cref="RetentionPageSize" /> jobs that
        ///     have waited for confirmation longer than <paramref name="failedRetention" /> go back to
        ///     pending, due now and flagged for cancellation, their warnings dropped.
        /// </summary>
        /// <param name="batch">The page's maintenance batch.</param>
        /// <param name="failedRetention">How long a job may wait for confirmation, measured from when it began to wait.</param>
        /// <returns>The jobs stopped, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<StoppedJob>> RetentionStop(DataBatch batch, TimeSpan failedRetention)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchResult<IReadOnlyList<StoppedJob>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The clock's name and the period in seconds ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string seconds = draft.Scalar(SqlDbType.Int, Seconds(failedRetention));

                // --- The text: a seek on IX_Jobs_PendingConfirmation, whose filter literal it repeats ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"UPDATE TOP ({RetentionPageSize}) [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"SET [Status] = '{JobStatus.Pending}', [DueAt] = {now}, [CancelRequestedAt] = {now}, [PendingConfirmationsJson] = NULL\n")
                    .Append("OUTPUT [inserted].[Id], [inserted].[RequestedById]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[Status] = '{JobStatus.PendingConfirmation}' AND [j].[DueAt] < DATEADD(second, -{seconds}, {now});");

                // --- The reader: the stopped jobs and their requesters ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<StoppedJob> stopped = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        stopped.Add(new StoppedJob(set.GetInt32(0), set.IsDBNull(1) ? null : set.GetInt32(1)));
                    }

                    cursor.CountRows(stopped.Count);
                    cursor.Complete(result, stopped);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // The rows referencing a deleted job lose it through their keys' SET NULL: every job entity's
        // JobId and the schedules' LastJobId. The caller names those tables among the writes, so the
        // batch's change tracking and tags see them.
        /// <summary>
        ///     Appends one page of a retention band: up to <see cref="RetentionPageSize" /> jobs of the
        ///     band that ended longer than <paramref name="retention" /> ago are deleted.
        /// </summary>
        /// <param name="batch">The page's maintenance batch.</param>
        /// <param name="succeeded">True for the band of succeeded jobs; false for the band of failed and cancelled jobs.</param>
        /// <param name="retention">How long the band's jobs are kept after they ended.</param>
        /// <param name="writes">The tables the delete writes: the job table and every table whose key references it.</param>
        /// <returns>How many jobs the page deleted, once the batch executed.</returns>
        public static BatchResult<int> RetentionPage(DataBatch batch, bool succeeded, TimeSpan retention, IReadOnlySet<TableName> writes)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(writes);

            BatchResult<int> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The period in seconds; the band's statuses as the retention index's literals ---
                string seconds = draft.Scalar(SqlDbType.Int, Seconds(retention));
                string statuses = succeeded
                    ? $"'{JobStatus.Succeeded}'"
                    : $"'{JobStatus.Failed}', '{JobStatus.Cancelled}'";

                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DELETE TOP ({RetentionPageSize}) FROM {Jobs.Delimited}\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [Status] IN ({statuses}) AND [CompletedAt] < DATEADD(second, -{seconds}, SYSUTCDATETIME());\n")
                    .Append("SELECT @@ROWCOUNT AS [Deleted];");

                // --- The reader: the page's count ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), writes, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    int deleted = await set.ReadAsync(cancellationToken).ConfigureAwait(false) ? set.GetInt32(0) : 0;
                    cursor.CountRows(deleted);
                    cursor.Complete(result, deleted);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>A retention period in whole seconds, as the statements bind it.</summary>
        /// <param name="period">The period, which the options hold to between a day and the largest 32-bit count of seconds.</param>
        /// <returns>The seconds.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The period is negative or longer than the statements can bind.</exception>
        private static int Seconds(TimeSpan period)
        {
            double seconds = Math.Floor(period.TotalSeconds);
            ArgumentOutOfRangeException.ThrowIfNegative(seconds, nameof(period));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, int.MaxValue, nameof(period));
            return (int)seconds;
        }
    }

    /// <summary>A job the retention stop re-queued for cancellation.</summary>
    /// <param name="Id">The job.</param>
    /// <param name="RequestedById">Its requester; null for a job no user asked for.</param>
    internal sealed record StoppedJob(int Id, int? RequestedById);
}
