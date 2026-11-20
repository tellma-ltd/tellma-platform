// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §7.4 to §7.8: the scheduler's statements. Tick step 1 rides the poll's round trip and
    // leases up to fifty schedule states for thirty seconds, uninitialised built-ins first, then due
    // rows; tick step 2 fires the jobs the C# step computed, fenced on that lease, and advances the
    // states; the pause rides step 2's batch. The schedules' stack appends the state upsert and the
    // paused reason's clear to its saves and activations, and the run-now insert to its action.
    /// <content>The scheduler's statements.</content>
    internal static partial class JobStatements
    {
        /// <summary>The most schedule states one tick leases.</summary>
        public const int TickLeaseRows = 50;

        /// <summary>How long a tick's lease of a schedule state lasts, in seconds.</summary>
        public const int TickLeaseSeconds = 30;

        /// <summary>The schedule table.</summary>
        public static TableName Schedules { get; } = new("core", "Schedules");

        /// <summary>The schedule states' table.</summary>
        public static TableName ScheduleStates { get; } = new("core", ScheduleStateConfiguration.Table);

        /// <summary>The writes of a statement that writes the schedule states alone.</summary>
        private static readonly FrozenSet<TableName> ScheduleStateWrites = new[] { ScheduleStates }.ToFrozenSet();

        /// <summary>The writes of a statement that writes the schedule table alone.</summary>
        private static readonly FrozenSet<TableName> ScheduleWrites = new[] { Schedules }.ToFrozenSet();

        /// <summary>The writes of tick step 2: the jobs it fires and the states it advances.</summary>
        private static readonly FrozenSet<TableName> TickWrites = new[] { Jobs, ScheduleStates }.ToFrozenSet();

        // Spec 0020 §7.4: the first statement collects what an earlier attempt of the same round trip
        // leased under the token (§6.8), so a re-run returns those rows with the ones it leases now,
        // within the same fifty. The uninitialised branch is a range seek on the clustered key over
        // the reserved band, active schedules only; the due branch seeks IX_ScheduleStates_Due. On a
        // read-only tenant both branches also keep to the keys whose handlers run then; the schedules
        // they skip stay due. The spec's text names the key list's column [Value]; StringList's one
        // column is [Id].
        /// <summary>
        ///     Appends tick step 1: it leases up to <see cref="TickLeaseRows" /> schedule states under
        ///     <paramref name="token" />, uninitialised built-ins first and then due ones, and returns
        ///     every schedule leased under the token with what the tick computes its firing from.
        /// </summary>
        /// <param name="batch">The poll's batch.</param>
        /// <param name="token">The tick's fresh token.</param>
        /// <param name="readOnlyKeys">On a read-only tenant, the keys whose handlers run then; null on an active tenant.</param>
        /// <returns>The leased schedules, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<ScheduleTickRow>> TickStep1(DataBatch batch, Guid token, IReadOnlyList<string>? readOnlyKeys)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchResult<IReadOnlyList<ScheduleTickRow>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, the token and, on a read-only tenant, the keys that run then ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string due = draft.Name(ReservedSqlNames.StatementNames.Due);
                string room = draft.Name(ReservedSqlNames.StatementNames.Room);
                string lease = draft.Scalar(SqlDbType.UniqueIdentifier, token);
                string? keys = readOnlyKeys is null ? null : draft.Table(Keys(readOnlyKeys));
                string readOnly = keys is null
                    ? string.Empty
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"AND EXISTS (SELECT 1 FROM {Schedules.Delimited} AS [k] WHERE [k].[Id] = [s].[ScheduleId] AND [k].[HandlerKey] IN (SELECT [Id] FROM {keys}))");
                string expiry = string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, {TickLeaseSeconds}, {now})");

                // --- The text: the earlier attempt's rows, the uninitialised built-ins, the due rows, the schedules ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {due} TABLE ([ScheduleId] int NOT NULL PRIMARY KEY);\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {due} ([ScheduleId])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [s].[ScheduleId] FROM {ScheduleStates.Delimited} AS [s]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [s].[LeaseToken] = {lease} AND [s].[LeaseExpiresAt] >= {now};\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {room} int = {TickLeaseRows} - (SELECT COUNT(*) FROM {due});\n")
                    .Append(CultureInfo.InvariantCulture, $"UPDATE TOP ({room}) [s] SET [LeaseToken] = {lease}, [LeaseExpiresAt] = {expiry}\n")
                    .Append(CultureInfo.InvariantCulture, $"OUTPUT [inserted].[ScheduleId] INTO {due}\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {ScheduleStates.Delimited} AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [s].[ScheduleId] <= {WellKnownIds.ReservedIdBandEnd} AND [s].[NextDueAt] IS NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < {now})\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND EXISTS (SELECT 1 FROM {Schedules.Delimited} AS [x] WHERE [x].[Id] = [s].[ScheduleId] AND [x].[IsActive] = 1)");
                if (keys is not null)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"\n  {readOnly}");
                }

                _ = text.Append(";\n")
                    .Append(CultureInfo.InvariantCulture, $"SET {room} = {TickLeaseRows} - (SELECT COUNT(*) FROM {due});\n")
                    .Append("WITH [d] AS (\n")
                    .Append(CultureInfo.InvariantCulture, $"    SELECT TOP ({room}) [s].*\n")
                    .Append(CultureInfo.InvariantCulture, $"    FROM {ScheduleStates.Delimited} AS [s] WITH (READPAST, UPDLOCK, ROWLOCK)\n")
                    .Append(CultureInfo.InvariantCulture, $"    WHERE [s].[NextDueAt] IS NOT NULL AND [s].[NextDueAt] <= {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"      AND ([s].[LeaseExpiresAt] IS NULL OR [s].[LeaseExpiresAt] < {now})\n");
                if (keys is not null)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"      {readOnly}\n");
                }

                _ = text.Append("    ORDER BY [s].[NextDueAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"UPDATE [d] SET [LeaseToken] = {lease}, [LeaseExpiresAt] = {expiry}\n")
                    .Append(CultureInfo.InvariantCulture, $"OUTPUT [inserted].[ScheduleId] INTO {due};\n")
                    .Append("SELECT [s].[Id], [s].[HandlerKey], [s].[ArgumentsJson], [s].[CronExpression], [s].[TimeZoneId], [s].[MissedPolicy],\n")
                    .Append("       [s].[OverlapPolicy], [s].[CatchUpWindowMinutes], [s].[RunAsUserId], [s].[Name], [s].[Name2], [s].[Name3],\n")
                    .Append(CultureInfo.InvariantCulture, $"       [st].[NextDueAt], [st].[LastScheduledFor], [u].[IsActive] AS [OwnerIsActive], {now} AS [Now]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {due} AS [d]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {Schedules.Delimited} AS [s] ON [s].[Id] = [d].[ScheduleId]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {ScheduleStates.Delimited} AS [st] ON [st].[ScheduleId] = [d].[ScheduleId]\n")
                    .Append("INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [s].[RunAsUserId];");

                // --- The reader: every schedule leased under the token ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), ScheduleStateWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<ScheduleTickRow> rows = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        rows.Add(new ScheduleTickRow(
                            Id: set.GetInt32(0),
                            HandlerKey: set.GetString(1),
                            ArgumentsJson: set.IsDBNull(2) ? null : set.GetString(2),
                            CronExpression: set.GetString(3),
                            TimeZoneId: set.IsDBNull(4) ? null : set.GetString(4),
                            MissedPolicy: Enum.Parse<MissedPolicy>(set.GetString(5), ignoreCase: false),
                            OverlapPolicy: Enum.Parse<OverlapPolicy>(set.GetString(6), ignoreCase: false),
                            CatchUpWindowMinutes: set.GetInt32(7),
                            RunAsUserId: set.GetInt32(8),
                            Name: new LocalizedText(set.GetString(9), set.IsDBNull(10) ? null : set.GetString(10), set.IsDBNull(11) ? null : set.GetString(11)),
                            NextDueAt: set.IsDBNull(12) ? null : set.GetFieldValue<DateTimeOffset>(12),
                            LastScheduledFor: set.IsDBNull(13) ? null : set.GetFieldValue<DateTimeOffset>(13),
                            OwnerIsActive: set.GetBoolean(14),
                            Now: set.GetFieldValue<DateTimeOffset>(15)));
                    }

                    cursor.CountRows(rows.Count);
                    cursor.Complete(result, rows);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §7.6: one transaction, the fence first, so a schedule another ticker owns now, or
        // whose save cleared the lease, rolls back the whole step. The ids come from the job table's
        // sequence inside the statement, one range for every row; a Skip schedule with a job that has
        // not ended fires nothing and has its skip stamped. Every state the step carries is advanced
        // and released, the paused ones' next due time null.
        /// <summary>
        ///     Appends tick step 2: it checks every schedule is still leased under the token, inserts
        ///     the jobs the overlap rule admits with ids reserved from the sequence, advances every
        ///     schedule's state and releases its lease, and returns the jobs it fired.
        /// </summary>
        /// <param name="batch">The step's persist batch.</param>
        /// <param name="token">The tick's token.</param>
        /// <param name="jobs">The jobs to fire, each one's <c>Id</c> its 1-based ordinal; the statement ignores it.</param>
        /// <param name="next">One row per schedule the tick leased and advances.</param>
        /// <returns>The fired jobs by schedule, in id order, once the batch executed.</returns>
        public static BatchResult<IReadOnlyList<ScheduleFired>> TickStep2(DataBatch batch, Guid token, IReadOnlyList<Job> jobs, IReadOnlyList<ScheduleNextList> next)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(jobs);
            ArgumentNullException.ThrowIfNull(next);

            BatchResult<IReadOnlyList<ScheduleFired>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, then the parameters in the order the statement numbers them ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string fired = draft.Name(ReservedSqlNames.StatementNames.Fired);
                string first = draft.Name(ReservedSqlNames.StatementNames.First);
                string count = draft.Name(ReservedSqlNames.StatementNames.Count);
                string lease = draft.Scalar(SqlDbType.UniqueIdentifier, token);
                string rows = draft.Table(jobs);
                string states = draft.Table(next);

                // --- The text: the fence, the range, the jobs, the states, the fired jobs ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {fired} TABLE ([ScheduleId] int NOT NULL, [JobId] int NOT NULL);\n")
                    .Append(CultureInfo.InvariantCulture, $"IF (SELECT COUNT(*) FROM {ScheduleStates.Delimited} AS [st] WITH (UPDLOCK, ROWLOCK) INNER JOIN {states} AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]\n")
                    .Append(CultureInfo.InvariantCulture, $"    WHERE [st].[LeaseToken] = {lease} AND [st].[LeaseExpiresAt] >= {now}) <> (SELECT COUNT(*) FROM {states})\n")
                    .Append(CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.Invariant}, N'{JobErrorCodes.TickLeaseLost}', 1;\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {first} sql_variant, {count} int = (SELECT COUNT(*) FROM {rows});\n")
                    .Append(CultureInfo.InvariantCulture, $"IF {count} > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'{JobsSequence}', @range_size = {count}, @range_first_value = {first} OUTPUT;\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Jobs.Delimited} ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"OUTPUT [inserted].[ScheduleId], [inserted].[Id] INTO {fired}\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT CAST({first} AS int) + ROW_NUMBER() OVER (ORDER BY [r].[ScheduleId], [r].[ScheduledFor]) - 1,\n")
                    .Append(CultureInfo.InvariantCulture, $"       [r].[HandlerKey], '{JobStatus.Pending}', [r].[DueAt], 0, [r].[ArgumentsJson], [r].[RunAsUserId], [r].[RequestedById], [r].[ScheduleId], [r].[ScheduledFor], {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {rows} AS [r]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {Schedules.Delimited} AS [s] ON [s].[Id] = [r].[ScheduleId]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {ScheduleStates.Delimited} AS [st] ON [st].[ScheduleId] = [r].[ScheduleId] AND [st].[LeaseToken] = {lease} AND [st].[LeaseExpiresAt] >= {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [s].[OverlapPolicy] = '{OverlapPolicy.Allow}'\n")
                    .Append(CultureInfo.InvariantCulture, $"   OR NOT EXISTS (SELECT 1 FROM {Jobs.Delimited} AS [x] WHERE [x].[ScheduleId] = [r].[ScheduleId] AND [x].[Status] IN ({ActiveStatuses}));\n")
                    .Append("UPDATE [st]\n")
                    .Append("SET [NextDueAt] = [n].[NextDueAt],\n")
                    .Append("    [LastScheduledFor] = COALESCE([n].[LastScheduledFor], [st].[LastScheduledFor]),\n")
                    .Append(CultureInfo.InvariantCulture, $"    [LastFiredAt] = CASE WHEN [f].[ScheduleId] IS NOT NULL THEN {now} ELSE [st].[LastFiredAt] END,\n")
                    .Append("    [LastJobId] = COALESCE([f].[JobId], [st].[LastJobId]),\n")
                    .Append(CultureInfo.InvariantCulture, $"    [LastSkippedAt] = CASE WHEN [f].[ScheduleId] IS NULL AND EXISTS (SELECT 1 FROM {rows} AS [r] WHERE [r].[ScheduleId] = [st].[ScheduleId]) THEN {now} ELSE [st].[LastSkippedAt] END,\n")
                    .Append("    [LeaseToken] = NULL, [LeaseExpiresAt] = NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {ScheduleStates.Delimited} AS [st]\n")
                    .Append(CultureInfo.InvariantCulture, $"INNER JOIN {states} AS [n] ON [n].[ScheduleId] = [st].[ScheduleId]\n")
                    .Append(CultureInfo.InvariantCulture, $"LEFT JOIN (SELECT [ScheduleId], MAX([JobId]) AS [JobId] FROM {fired} GROUP BY [ScheduleId]) AS [f] ON [f].[ScheduleId] = [st].[ScheduleId]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [st].[LeaseToken] = {lease};\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [ScheduleId], [JobId] FROM {fired} ORDER BY [JobId];");

                // --- The reader: the fired jobs, in id order ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), TickWrites, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<ScheduleFired> jobsFired = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        jobsFired.Add(new ScheduleFired(set.GetInt32(0), set.GetInt32(1)));
                    }

                    cursor.CountRows(jobsFired.Count);
                    cursor.Complete(result, jobsFired);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §7.7: the pause is a visible change of the schedule, so it stamps the row with the
        // system user and writes a history row; a schedule already inactive keeps its stamp. Its state
        // takes no second update: tick step 2 already carries its next due time as null.
        /// <summary>
        ///     Appends the pause of schedules for one reason: each still active is deactivated with the
        ///     reason, stamped by the system user.
        /// </summary>
        /// <param name="batch">Tick step 2's batch.</param>
        /// <param name="reason">Why the schedules pause.</param>
        /// <param name="scheduleIds">The schedules.</param>
        public static void Pause(DataBatch batch, SchedulePausedReason reason, IReadOnlyList<int> scheduleIds)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(scheduleIds);
            if (!Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "The pause reason is not defined.");
            }

            StatementDraft draft = batch.Begin();
            try
            {
                // --- The clock and the schedules ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string ids = draft.Table(Ids(scheduleIds));

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(7) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"UPDATE [s] SET [IsActive] = 0, [PausedReason] = '{reason}', [ModifiedAt] = {now}, [ModifiedById] = {WellKnownIds.SystemUserId}\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Schedules.Delimited} AS [s] INNER JOIN {ids} AS [p] ON [p].[Id] = [s].[Id]\n")
                    .Append("WHERE [s].[IsActive] = 1;");
                batch.Append(draft, text.ToString(), ScheduleWrites, idempotent: false);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §7.8: a save or an activation recomputes each schedule's next due time from the
        // application clock; the next tick corrects it against the database's. The upsert also
        // releases a tick's lease of the row, so a tick that leased the schedule before the change
        // fails its fence rather than advancing the schedule from what it read before.
        /// <summary>
        ///     Appends the upsert of schedule states: each schedule's next due time, its row inserted when
        ///     it has none yet, and any tick's lease of it released.
        /// </summary>
        /// <param name="batch">The save's or the action's persist batch.</param>
        /// <param name="next">One row per schedule; a null next due time for one that fires nothing.</param>
        public static void UpsertScheduleStates(DataBatch batch, IReadOnlyList<ScheduleNextList> next)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(next);

            StatementDraft draft = batch.Begin();
            try
            {
                // --- The rows ---
                string rows = draft.Table(next);

                // --- The text: the rows that exist, then the ones that do not ---
                StringBuilder text = new();
                _ = text.Append("UPDATE [st] SET [NextDueAt] = [n].[NextDueAt], [LeaseToken] = NULL, [LeaseExpiresAt] = NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {ScheduleStates.Delimited} AS [st] INNER JOIN {rows} AS [n] ON [n].[ScheduleId] = [st].[ScheduleId];\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {ScheduleStates.Delimited} ([ScheduleId], [NextDueAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [n].[ScheduleId], [n].[NextDueAt] FROM {rows} AS [n]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE NOT EXISTS (SELECT 1 FROM {ScheduleStates.Delimited} AS [st] WHERE [st].[ScheduleId] = [n].[ScheduleId]);");
                batch.Append(draft, text.ToString(), ScheduleStateWrites, idempotent: false);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>Appends the clear of the paused reason of schedules an activation brings back.</summary>
        /// <param name="batch">The activation's persist batch.</param>
        /// <param name="scheduleIds">The schedules whose reason was set.</param>
        public static void ClearPausedReason(DataBatch batch, IReadOnlyList<int> scheduleIds)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(scheduleIds);

            StatementDraft draft = batch.Begin();
            try
            {
                string ids = draft.Table(Ids(scheduleIds));
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"UPDATE [s] SET [PausedReason] = NULL FROM {Schedules.Delimited} AS [s] INNER JOIN {ids} AS [p] ON [p].[Id] = [s].[Id] WHERE [s].[PausedReason] IS NOT NULL;");
                batch.Append(draft, text, ScheduleWrites, idempotent: false);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // Spec 0020 §7.8: one job of the schedule, now, its id from the sequence; the cron's own state
        // is left alone, so the next due time and the last fired job stay the cron's.
        /// <summary>Appends the run-now insert: one job of the schedule, due now, with no scheduled occurrence.</summary>
        /// <param name="batch">The action's persist batch.</param>
        /// <param name="scheduleId">The schedule.</param>
        /// <param name="runAsUserId">The user the job runs as.</param>
        /// <param name="requestedById">The user who asked for the run.</param>
        /// <param name="traceParent">The trace the job's run links to; null for none.</param>
        /// <returns>The job's id, once the batch executed.</returns>
        public static BatchResult<int> RunNow(DataBatch batch, int scheduleId, int runAsUserId, int requestedById, string? traceParent)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchResult<int> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The names, then the parameters in the order the statement numbers them ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string jobIds = draft.Name(ReservedSqlNames.StatementNames.JobIds);
                string schedule = draft.Scalar(SqlDbType.Int, scheduleId);
                string runAs = draft.Scalar(SqlDbType.Int, runAsUserId);
                string requester = draft.Scalar(SqlDbType.Int, requestedById);
                string trace = draft.Scalar(SqlDbType.VarChar, traceParent, JobConfiguration.TraceParentLength);

                // --- The text ---
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {jobIds} TABLE ([JobId] int NOT NULL);\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Jobs.Delimited} ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [ScheduleId], [ScheduledFor], [CreatedAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"OUTPUT [inserted].[Id] INTO {jobIds}\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT NEXT VALUE FOR [core].[sq_Jobs], [s].[HandlerKey], '{JobStatus.Pending}', {now}, 0, [s].[ArgumentsJson], {trace}, {runAs}, {requester}, [s].[Id], NULL, {now}\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Schedules.Delimited} AS [s] WHERE [s].[Id] = {schedule};\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT [JobId] FROM {jobIds};");

                // --- The reader: the one job's id ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), JobWrites, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The run-now statement at {ordinal} inserted no job: the schedule {scheduleId} is gone."));
                    }

                    int id = set.GetInt32(0);
                    cursor.CountRows(1);
                    cursor.Complete(result, id);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>Ids as the rows of an <c>IdList</c> parameter.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The rows, each id once.</returns>
        private static List<IdList> Ids(IReadOnlyList<int> ids)
        {
            return [.. ids.Distinct().Select(static id => new IdList { Id = id })];
        }
    }

    /// <summary>One schedule tick step 1 leased, with what step 2 computes its firing from.</summary>
    /// <param name="Id">The schedule.</param>
    /// <param name="HandlerKey">The key of the handler its jobs run.</param>
    /// <param name="ArgumentsJson">The arguments its jobs carry; null for none.</param>
    /// <param name="CronExpression">Its cron expression.</param>
    /// <param name="TimeZoneId">The zone the expression is evaluated in; null for the tenant's.</param>
    /// <param name="MissedPolicy">What it does with occurrences that came due while no tick ran.</param>
    /// <param name="OverlapPolicy">Whether a run may start while another has not ended.</param>
    /// <param name="CatchUpWindowMinutes">How old a replayed occurrence may be.</param>
    /// <param name="RunAsUserId">The user its jobs run as.</param>
    /// <param name="Name">Its name in each content language.</param>
    /// <param name="NextDueAt">When it is next due; null for a built-in the scheduler has not initialised.</param>
    /// <param name="LastScheduledFor">The latest occurrence it fired; null before its first.</param>
    /// <param name="OwnerIsActive">Whether the run-as user is active.</param>
    /// <param name="Now">The database's clock when step 1 ran.</param>
    internal sealed record ScheduleTickRow(
        int Id,
        string HandlerKey,
        string? ArgumentsJson,
        string CronExpression,
        string? TimeZoneId,
        MissedPolicy MissedPolicy,
        OverlapPolicy OverlapPolicy,
        int CatchUpWindowMinutes,
        int RunAsUserId,
        LocalizedText Name,
        DateTimeOffset? NextDueAt,
        DateTimeOffset? LastScheduledFor,
        bool OwnerIsActive,
        DateTimeOffset Now)
    {
        /// <summary>Whether the schedule is built in: its id lies in the reserved band.</summary>
        public bool IsBuiltIn => Id <= WellKnownIds.ReservedIdBandEnd;
    }

    /// <summary>A job tick step 2 fired.</summary>
    /// <param name="ScheduleId">The schedule.</param>
    /// <param name="JobId">The job.</param>
    internal sealed record ScheduleFired(int ScheduleId, int JobId);
}
