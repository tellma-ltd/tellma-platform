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
    /// <summary>
    ///     The job machinery's fixed statements, each appended on the batch's internal channel with the
    ///     tables it writes declared, so no caller authority check refuses it whichever stack owns the
    ///     table it writes.
    /// </summary>
    internal static partial class JobStatements
    {
        /// <summary>The job table.</summary>
        public static TableName Jobs { get; } = new("core", "Jobs");

        /// <summary>The writes of a statement that writes the job table alone.</summary>
        private static readonly FrozenSet<TableName> JobWrites = new[] { Jobs }.ToFrozenSet();

        /// <summary>The job table's sequence, as <c>sp_sequence_get_range</c> names it.</summary>
        public const string JobsSequence = "core.sq_Jobs";

        /// <summary>
        ///     The progress columns a renewal and a checkpoint write from a <see cref="JobProgressList" />
        ///     row aliased <c>[p]</c>: a null column keeps the stored value, and a message's arguments are
        ///     written with its key or not at all.
        /// </summary>
        private const string ProgressAssignments =
            "    [ProgressPercent] = COALESCE([p].[ProgressPercent], [j].[ProgressPercent]),\n"
            + "    [ProgressKey] = COALESCE([p].[ProgressKey], [j].[ProgressKey]),\n"
            + "    [ProgressArgumentsJson] = CASE WHEN [p].[ProgressKey] IS NULL THEN [j].[ProgressArgumentsJson] ELSE [p].[ProgressArgumentsJson] END,\n"
            + "    [StateJson] = COALESCE([p].[StateJson], [j].[StateJson])\n";

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

        // Spec 0020 §5.1: one claim per handler key, in the poll's Maintenance batch, whose statements
        // autocommit. The first statement collects what an earlier attempt of the same round trip
        // leased under the same token, so the executor's re-run returns those rows with the ones it
        // leases now and leaves none leased and untracked. The availability predicate repeats the
        // filter literals of IX_Jobs_Available verbatim, so the optimizer proves it a subset of the
        // index's filter and seeks it.
        /// <summary>
        ///     Appends the claim of one handler key: it leases up to <paramref name="size" /> available
        ///     jobs of the key, oldest due first, under <paramref name="token" />, counts the attempt,
        ///     and returns every job leased under the token, in due order.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="size">The most jobs to lease: the handler's batch size.</param>
        /// <param name="token">The claim's fresh fencing token.</param>
        /// <param name="leaseSeconds">The lease's length, in seconds.</param>
        /// <param name="owner">The instance and process that hold the lease, for diagnostics.</param>
        /// <returns>The statement's result and the table variable holding the claimed ids, which an entity load restricts its rows to.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The size or the lease is outside its range.</exception>
        public static ClaimStatement Claim(DataBatch batch, string handlerKey, int size, Guid token, int leaseSeconds, string owner)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentException.ThrowIfNullOrWhiteSpace(handlerKey);
            ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(size, JobLimits.MaxBatchSize);
            ArgumentOutOfRangeException.ThrowIfLessThan(leaseSeconds, JobLimits.MinLeaseSeconds);
            ArgumentNullException.ThrowIfNull(owner);

            BatchResult<IReadOnlyList<ClaimedJob>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, then the parameters in the order the statement numbers them ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string claimed = draft.Name(ReservedSqlNames.StatementNames.Claimed);
                string room = draft.Name(ReservedSqlNames.StatementNames.Room);
                string key = draft.Scalar(SqlDbType.NVarChar, handlerKey, JobLimits.MaxKeyLength);
                string count = draft.Scalar(SqlDbType.Int, size);
                string lease = draft.Scalar(SqlDbType.UniqueIdentifier, token);
                string seconds = draft.Scalar(SqlDbType.Int, leaseSeconds);
                string holder = draft.Scalar(SqlDbType.NVarChar, owner, JobConfiguration.LeaseOwnerLength);

                // --- The text: the earlier attempt's rows, the room left, the lease, the rows ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {claimed} TABLE ([Id] int NOT NULL PRIMARY KEY);\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {claimed} ([Id])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [j].[Id] FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[LeaseToken] = {lease} AND [j].[LeaseExpiresAt] >= {now};\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {room} int = {count} - (SELECT COUNT(*) FROM {claimed});\n")
                    .Append("WITH [due] AS (\n")
                    .Append(CultureInfo.InvariantCulture, $"    SELECT TOP ({room}) [j].*\n")
                    .Append(CultureInfo.InvariantCulture, $"    FROM {Jobs.Delimited} AS [j] WITH (READPAST, UPDLOCK, ROWLOCK)\n")
                    .Append(CultureInfo.InvariantCulture, $"    WHERE [j].[Status] IN ('{JobStatus.Pending}', '{JobStatus.Running}')\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND [j].[HandlerKey] = {key}\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND [j].[DueAt] <= {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND ([j].[LeaseExpiresAt] IS NULL OR [j].[LeaseExpiresAt] < {now})\n")
                    .Append("    ORDER BY [j].[DueAt], [j].[Id])\n")
                    .Append("UPDATE [due]\n")
                    .Append(CultureInfo.InvariantCulture, $"SET [Status] = '{JobStatus.Running}', [LeaseToken] = {lease}, [LeaseExpiresAt] = DATEADD(second, {seconds}, {now}),\n")
                    .Append(CultureInfo.InvariantCulture, $"    [LeaseOwner] = {holder}, [Attempts] = [Attempts] + 1, [StartedAt] = COALESCE([StartedAt], {now})\n")
                    .Append(CultureInfo.InvariantCulture, $"OUTPUT [inserted].[Id] INTO {claimed};\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [j].* FROM {Jobs.Delimited} AS [j] INNER JOIN {claimed} AS [c] ON [c].[Id] = [j].[Id] ORDER BY [j].[DueAt], [j].[Id];");

                // --- The reader: every row leased under the token, in due order ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    JobRowReader rows = new(set);
                    List<ClaimedJob> jobs = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        jobs.Add(rows.Read(set));
                    }

                    cursor.CountRows(jobs.Count);
                    cursor.Complete(result, jobs);
                });
                return new ClaimStatement(result, claimed);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §5.2: one renewal per tenant per interval, joining the leases by token, so it is
        // idempotent and a re-run after an ambiguous failure extends the same leases. Progress columns
        // the buffered row leaves null keep their stored values; a message's arguments go with its key.
        // A kept token's leases keep their stored end: the end the final renewal of §6.7 wrote, which
        // whole seconds from this statement's clock would overrun. They are still judged and carry
        // progress.
        /// <summary>
        ///     Appends the renewal: it extends every running job leased under one of the tokens by that
        ///     token's lease length, or keeps its lease's end when the token is kept, writes the
        ///     buffered progress of the jobs it carries, and returns every job it renewed with its
        ///     cancel request.
        /// </summary>
        /// <param name="batch">The renewal's batch.</param>
        /// <param name="leases">The tokens and their lease lengths.</param>
        /// <param name="progress">The buffered progress, one row per job.</param>
        /// <param name="kept">The tokens, among <paramref name="leases" />, whose leases keep their stored end.</param>
        /// <returns>The jobs still leased, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<RenewedJob>> Renew(DataBatch batch, IReadOnlyList<JobLeaseList> leases, IReadOnlyList<JobProgressList> progress, IReadOnlyList<GuidList> kept)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(progress);
            ArgumentNullException.ThrowIfNull(kept);

            BatchResult<IReadOnlyList<RenewedJob>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names and the three table-valued parameters ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string tokens = draft.Table(leases);
                string rows = draft.Table(progress);
                string keeping = draft.Table(kept);

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append("UPDATE [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"SET [LeaseExpiresAt] = CASE WHEN [k].[Id] IS NULL THEN DATEADD(second, [t].[LeaseSeconds], {now}) ELSE [j].[LeaseExpiresAt] END,\n")
                    .Append(ProgressAssignments)
                    .Append("OUTPUT [inserted].[Id], [inserted].[LeaseToken], [inserted].[CancelRequestedAt]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {tokens} AS [t] ON [t].[Id] = [j].[LeaseToken]\n")
                    .Append(CultureInfo.InvariantCulture, $"LEFT JOIN {rows} AS [p] ON [p].[Id] = [j].[Id]\n")
                    .Append(CultureInfo.InvariantCulture, $"LEFT JOIN {keeping} AS [k] ON [k].[Id] = [j].[LeaseToken]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[Status] = '{JobStatus.Running}';");

                // --- The reader: the jobs still leased ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<RenewedJob> renewed = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        renewed.Add(new RenewedJob(set.GetInt32(0), set.GetGuid(1), set.IsDBNull(2) ? null : set.GetFieldValue<DateTimeOffset>(2)));
                    }

                    cursor.CountRows(renewed.Count);
                    cursor.Complete(result, renewed);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §5.3: a checkpoint inside the caller's own transaction, fenced by the token. A
        // job no longer leased under it throws Job.LeaseLost, which rolls the caller's statements back
        // with it under XACT_ABORT.
        /// <summary>
        ///     Appends a fenced checkpoint: it writes the progress rows of jobs leased under
        ///     <paramref name="token" />, and throws <see cref="JobErrorCodes.LeaseLost" /> when any of
        ///     them is no longer leased under it.
        /// </summary>
        /// <param name="batch">The caller's batch.</param>
        /// <param name="token">The lease's token.</param>
        /// <param name="progress">The progress, one row per job.</param>
        public static void AppendProgress(DataBatch batch, Guid token, IReadOnlyList<JobProgressList> progress)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(progress);

            StatementDraft draft = batch.Begin();
            try
            {
                // --- The token, then the rows ---
                string lease = draft.Scalar(SqlDbType.UniqueIdentifier, token);
                string rows = draft.Table(progress);

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append("UPDATE [j] SET ")
                    .Append(ProgressAssignments.TrimStart())
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j] INNER JOIN {rows} AS [p] ON [p].[Id] = [j].[Id]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[LeaseToken] = {lease} AND [j].[Status] = '{JobStatus.Running}';\n")
                    .Append(CultureInfo.InvariantCulture, $"IF @@ROWCOUNT <> (SELECT COUNT(*) FROM {rows}) THROW {TellmaSqlErrors.Invariant}, N'{JobErrorCodes.LeaseLost}', 1;");

                batch.Append(draft, text.ToString(), JobWrites, idempotent: false, reader: null);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §5.4: the first statement of the completion transaction after the platform's
        // guards, so nothing the handler wrote survives a lost lease. The outcome list is bound by
        // reference and filled once the handler returned: its rows are read when the round trip is
        // sent, not when the statement is appended.
        /// <summary>
        ///     Appends the completion: it records each outcome on its job, leased under
        ///     <paramref name="token" />, releases the leases, and throws
        ///     <see cref="JobErrorCodes.LeaseLost" /> when any job is no longer leased under it.
        /// </summary>
        /// <param name="batch">The completion's batch.</param>
        /// <param name="token">The lease's token.</param>
        /// <param name="outcomes">The outcomes, one row per job; filled before the batch executes.</param>
        /// <returns>The completed jobs, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<CompletedJob>> Complete(DataBatch batch, Guid token, IReadOnlyList<JobOutcomeList> outcomes)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(outcomes);

            BatchResult<IReadOnlyList<CompletedJob>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, the token and the outcomes ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string lease = draft.Scalar(SqlDbType.UniqueIdentifier, token);
                string rows = draft.Table(outcomes);

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append("UPDATE [j]\n")
                    .Append("SET [Status] = [o].[Status],\n")
                    .Append(CultureInfo.InvariantCulture, $"    [DueAt] = CASE WHEN [o].[RetryAfterSeconds] IS NULL THEN [j].[DueAt] ELSE DATEADD(second, [o].[RetryAfterSeconds], {now}) END,\n")
                    .Append("    [Attempts] = CASE WHEN [o].[Released] = 1 THEN [j].[Attempts] - 1 ELSE [j].[Attempts] END,\n")
                    .Append("    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL, [LeaseOwner] = NULL,\n")
                    .Append(CultureInfo.InvariantCulture, $"    [CompletedAt] = CASE WHEN [o].[Status] IN ('{JobStatus.Succeeded}', '{JobStatus.Failed}', '{JobStatus.Cancelled}') THEN {now} ELSE NULL END,\n")
                    .Append("    [ErrorCode] = [o].[ErrorCode], [ErrorArgumentsJson] = [o].[ErrorArgumentsJson], [ErrorTraceId] = [o].[ErrorTraceId],\n")
                    .Append("    [StateJson] = COALESCE([o].[StateJson], [j].[StateJson]), [PendingConfirmationsJson] = [o].[PendingConfirmationsJson],\n")
                    .Append(CultureInfo.InvariantCulture, $"    [ProgressPercent] = CASE WHEN [o].[Status] = '{JobStatus.Succeeded}' THEN {JobLimits.MaxProgressPercent} ELSE [j].[ProgressPercent] END\n")
                    .Append("OUTPUT [inserted].[Id], [inserted].[Status], [inserted].[RequestedById], [inserted].[Attempts]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Jobs.Delimited} AS [j] INNER JOIN {rows} AS [o] ON [o].[Id] = [j].[Id]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [j].[LeaseToken] = {lease} AND [j].[Status] = '{JobStatus.Running}';\n")
                    .Append(CultureInfo.InvariantCulture, $"IF @@ROWCOUNT <> (SELECT COUNT(*) FROM {rows}) THROW {TellmaSqlErrors.Invariant}, N'{JobErrorCodes.LeaseLost}', 1;");

                // --- The reader: the jobs as the completion left them ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<CompletedJob> completed = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        completed.Add(new CompletedJob(
                            set.GetInt32(0),
                            Enum.Parse<JobStatus>(set.GetString(1), ignoreCase: false),
                            set.IsDBNull(2) ? null : set.GetInt32(2),
                            set.GetInt32(3)));
                    }

                    cursor.CountRows(completed.Count);
                    cursor.Complete(result, completed);
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

    /// <summary>A claim statement appended to a poll's batch.</summary>
    /// <param name="Rows">The jobs leased under the claim's token, once the batch executed.</param>
    /// <param name="ClaimedTable">
    ///     The statement's table variable of the claimed ids, which later statements of the same round
    ///     trip may read, such as the entity load of an entity-backed handler.
    /// </param>
    internal sealed record ClaimStatement(BatchResult<IReadOnlyList<ClaimedJob>> Rows, string ClaimedTable);
}
