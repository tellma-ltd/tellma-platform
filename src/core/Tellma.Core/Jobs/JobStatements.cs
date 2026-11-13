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

        /// <summary>
        ///     Appends the enqueue statement: it reserves one id per request from the job table's
        ///     sequence inside itself, inserts the jobs <c>Pending</c>, sets the <c>JobId</c> of every
        ///     owning row in one update when the requests name owners, and returns the new ids in
        ///     request order.
        /// </summary>
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
                string rows = draft.Table(requests);

                // --- The text: ids from the sequence inside the statement, never from the allocator's buffer ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {first} sql_variant, {count} int = (SELECT COUNT(*) FROM {rows});\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {jobIds} TABLE ([Ordinal] int NOT NULL PRIMARY KEY, [JobId] int NOT NULL);\n")
                    .Append(CultureInfo.InvariantCulture, $"IF {count} > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'{JobsSequence}', @range_size = {count}, @range_first_value = {first} OUTPUT;\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {jobIds} ([Ordinal], [JobId]) SELECT [r].[Ordinal], CAST({first} AS int) + [r].[Ordinal] FROM {rows} AS [r];\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Jobs.Delimited} ([Id], [HandlerKey], [LabelKey], [LabelArgumentsJson], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [ConfirmedKeys], [RunAsUserId], [RequestedById], [CreatedAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [i].[JobId], [r].[HandlerKey], [r].[LabelKey], [r].[LabelArgumentsJson], '{JobStatus.Pending}', COALESCE([r].[DueAt], SYSUTCDATETIME()), 0, [r].[ArgumentsJson], [r].[TraceParent], [r].[ConfirmedKeys], [r].[RunAsUserId], [r].[RequestedById], SYSUTCDATETIME()\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {rows} AS [r] INNER JOIN {jobIds} AS [i] ON [i].[Ordinal] = [r].[Ordinal];\n");

                // --- Every owner's JobId in one update, inside the same transaction; ModifiedAt is not touched ---
                if (owners is not null)
                {
                    _ = text.Append("UPDATE [o] SET [JobId] = [i].[JobId]\n")
                        .Append(CultureInfo.InvariantCulture, $"FROM {owners.Delimited} AS [o]\n")
                        .Append(CultureInfo.InvariantCulture, $"INNER JOIN {rows} AS [r] ON [r].[OwnerId] = [o].[Id]\n")
                        .Append(CultureInfo.InvariantCulture, $"INNER JOIN {jobIds} AS [i] ON [i].[Ordinal] = [r].[Ordinal];\n");
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
