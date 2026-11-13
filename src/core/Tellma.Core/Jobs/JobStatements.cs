// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The job machinery's fixed statements, each appended on the batch's internal channel with the
    ///     tables it writes declared, so no caller authority check refuses it whichever stack owns the
    ///     table it writes.
    /// </summary>
    internal static class JobStatements
    {
        /// <summary>The job table.</summary>
        public static TableName Jobs { get; } = new("core", "Jobs");

        /// <summary>The job table's sequence, as <c>sp_sequence_get_range</c> names it.</summary>
        public const string JobsSequence = "core.sq_Jobs";

        /// <summary>The statuses of a job that has not ended, which hold its owning row, as SQL literals.</summary>
        private static readonly string ActiveStatuses = string.Join(
            ", ",
            new[] { JobStatus.Pending, JobStatus.Running, JobStatus.Held, JobStatus.PendingConfirmation }.Select(static status => $"'{status}'"));

        /// <summary>
        ///     Appends the enqueue statement: it reserves one id per request from the job table's
        ///     sequence inside itself, inserts the jobs <c>Pending</c>, sets the <c>JobId</c> of every
        ///     owning row in one update when the requests name owners, and returns the new ids in
        ///     request order.
        /// </summary>
        /// <remarks>
        ///     The statement refuses to run outside a transaction, and refuses the whole batch when an
        ///     owning row is missing or still holds a job that has not ended: it throws
        ///     <see cref="TellmaSqlErrors.Invariant" /> with <see cref="JobEnqueueCodes.OutsideTransaction" />
        ///     or <see cref="JobEnqueueCodes.OwnerUnavailable" />. The row's stored job decides, read under
        ///     the update's lock, never the in-memory member.
        /// </remarks>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="requests">The requests, each <see cref="JobRequestList.Ordinal" /> its 0-based position.</param>
        /// <param name="owners">
        ///     The table of the rows whose <c>JobId</c> the statement sets, through each request's
        ///     <see cref="JobRequestList.OwnerId" />; null when no request names an owner.
        /// </param>
        /// <returns>The new job ids in request order, once the batch executed and the statement ran.</returns>
        public static BatchResult<IReadOnlyList<int>> Enqueue(DataBatch batch, IReadOnlyList<JobRequestList> requests, TableName? owners)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);

            BatchResult<IReadOnlyList<int>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The statement's names and its one table-valued parameter ---
                string first = draft.Name(ReservedSqlNames.StatementNames.First);
                string count = draft.Name(ReservedSqlNames.StatementNames.Count);
                string jobIds = draft.Name(ReservedSqlNames.StatementNames.JobIds);
                string affected = draft.Name(ReservedSqlNames.StatementNames.Affected);
                string rows = draft.Table(requests);

                // --- The text: ids from the sequence inside the statement, never from the allocator's buffer ---
                // The insert and the owners' update commit together or not at all, so a batch whose
                // statements autocommit is refused before either runs.
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"IF @@TRANCOUNT = 0 THROW {TellmaSqlErrors.Invariant}, N'{JobEnqueueCodes.OutsideTransaction}', 1;\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {first} sql_variant, {count} int = (SELECT COUNT(*) FROM {rows}), {affected} int;\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {jobIds} TABLE ([Ordinal] int NOT NULL PRIMARY KEY, [JobId] int NOT NULL);\n")
                    .Append(CultureInfo.InvariantCulture, $"IF {count} > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'{JobsSequence}', @range_size = {count}, @range_first_value = {first} OUTPUT;\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {jobIds} ([Ordinal], [JobId]) SELECT [r].[Ordinal], CAST({first} AS int) + [r].[Ordinal] FROM {rows} AS [r];\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Jobs.Delimited} ([Id], [HandlerKey], [LabelKey], [LabelArgumentsJson], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [ConfirmedKeys], [RunAsUserId], [RequestedById], [CreatedAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [i].[JobId], [r].[HandlerKey], [r].[LabelKey], [r].[LabelArgumentsJson], '{JobStatus.Pending}', COALESCE([r].[DueAt], SYSUTCDATETIME()), 0, [r].[ArgumentsJson], [r].[TraceParent], [r].[ConfirmedKeys], [r].[RunAsUserId], [r].[RequestedById], SYSUTCDATETIME()\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {rows} AS [r] INNER JOIN {jobIds} AS [i] ON [i].[Ordinal] = [r].[Ordinal];\n");

                // --- Every owner's JobId in one update, inside the same transaction; ModifiedAt is not touched ---
                // spec 0020 §4.1: a row's JobId is null or names an ended job. The update skips a row
                // whose job is still active, and the count check refuses the batch when it skipped one
                // or a row is missing. The update's lock serialises two enqueues of one row, and the
                // job is read with READCOMMITTEDLOCK so the second sees the first's committed job
                // rather than the statement's row-versioned snapshot.
                if (owners is not null)
                {
                    _ = text.Append("UPDATE [o] SET [JobId] = [i].[JobId]\n")
                        .Append(CultureInfo.InvariantCulture, $"FROM {owners.Delimited} AS [o]\n")
                        .Append(CultureInfo.InvariantCulture, $"INNER JOIN {rows} AS [r] ON [r].[OwnerId] = [o].[Id]\n")
                        .Append(CultureInfo.InvariantCulture, $"INNER JOIN {jobIds} AS [i] ON [i].[Ordinal] = [r].[Ordinal]\n")
                        .Append(CultureInfo.InvariantCulture, $"WHERE [o].[JobId] IS NULL OR NOT EXISTS (SELECT 1 FROM {Jobs.Delimited} AS [a] WITH (READCOMMITTEDLOCK) WHERE [a].[Id] = [o].[JobId] AND [a].[Status] IN ({ActiveStatuses}));\n")
                        .Append(CultureInfo.InvariantCulture, $"SET {affected} = @@ROWCOUNT;\n")
                        .Append(CultureInfo.InvariantCulture, $"IF {affected} <> (SELECT COUNT(*) FROM {rows} WHERE [OwnerId] IS NOT NULL) THROW {TellmaSqlErrors.Invariant}, N'{JobEnqueueCodes.OwnerUnavailable}', 1;\n");
                }

                _ = text.Append(CultureInfo.InvariantCulture, $"SELECT [Ordinal], [JobId] FROM {jobIds} ORDER BY [Ordinal];");

                // --- The writes: the job table, and the owners' table when there is one ---
                HashSet<TableName> writes = owners is null ? [Jobs] : [Jobs, owners];

                // --- The reader: the ids in request order ---
                int ordinal = draft.Ordinal;
                int expected = requests.Count;
                batch.Append(draft, text.ToString(), writes, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<int> ids = new(expected);
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ids.Add(set.GetInt32(1));
                    }

                    if (ids.Count != expected)
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The enqueue statement at {ordinal} returned {ids.Count} job ids for {expected} requests."));
                    }

                    cursor.CountRows(ids.Count);
                    cursor.Complete(result, ids);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }
    }
}
