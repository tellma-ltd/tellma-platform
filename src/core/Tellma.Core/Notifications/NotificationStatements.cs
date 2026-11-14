// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Composition;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The notifier's fixed statements, each appended on the batch's internal channel with the
    ///     tables it writes declared, so no caller authority check refuses it whichever stack the
    ///     caller's batch serves.
    /// </summary>
    /// <remarks>
    ///     Ids are reserved from the tables' sequences inside the statements, never from the batch's
    ///     allocator: an id a predicate filters out is simply unused.
    /// </remarks>
    internal static class NotificationStatements
    {
        /// <summary>The key of the job that carries a slice of a request to the channels other than the inbox.</summary>
        public const string DeliveryHandlerKey = "core.notification-delivery";

        /// <summary>The notification table's sequence, as <c>sp_sequence_get_range</c> names it.</summary>
        public const string NotificationsSequence = "core.sq_Notifications";

        /// <summary>The notification table.</summary>
        public static TableName Notifications { get; } = new("core", "Notifications");

        /// <summary>The preferences table.</summary>
        public static TableName Preferences { get; } = new("core", "NotificationPreferences");

        /// <summary>The users table, whose active rows alone receive notifications.</summary>
        private static readonly TableName Users = new("core", "Users");

        /// <summary>The first line of a statement that enqueues: the jobs commit with the rows they deliver, or not at all.</summary>
        private static readonly string OutsideTransactionGuard = string.Create(
            CultureInfo.InvariantCulture,
            $"IF @@TRANCOUNT = 0 THROW {TellmaSqlErrors.Invariant}, N'{JobEnqueueCodes.OutsideTransaction}', 1;\n");

        /// <summary>
        ///     Appends one request's statement: under <see cref="DuplicatePolicy.Replace" /> the refresh
        ///     of the recipients' unread duplicates, then the insert of the other recipients' rows, then
        ///     the delivery enqueue when the request has slices; for a channel-only type the delivery
        ///     enqueue alone.
        /// </summary>
        /// <remarks>
        ///     A statement that enqueues refuses to run outside a transaction: it throws
        ///     <see cref="TellmaSqlErrors.Invariant" /> with <see cref="JobEnqueueCodes.OutsideTransaction" />,
        ///     as every job enqueue does, so the jobs never commit apart from the rows they deliver.
        /// </remarks>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="request">The shaped request.</param>
        /// <returns>What the statement did, once the batch executed and the statement ran.</returns>
        public static BatchResult<NotifyOutcome> Notify(DataBatch batch, ShapedNotification request)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(request);

            BatchResult<NotifyOutcome> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                string text = request.Inbox ? InboxText(draft, request) : DeliveryOnlyText(draft, request);

                // --- The writes: the inbox rows, and the jobs when the statement enqueues ---
                bool enqueues = request.Slices is not null;
                HashSet<TableName> writes = (request.Inbox, enqueues) switch
                {
                    (true, true) => [Notifications, JobStatements.Jobs],
                    (true, false) => [Notifications],
                    _ => [JobStatements.Jobs],
                };

                // --- The reader: the refreshed, inserted and enqueued sets, in the text's order ---
                int ordinal = draft.Ordinal;
                bool replaces = request.Inbox && request.Policy == DuplicatePolicy.Replace;
                int requested = request.Recipients.Count;
                batch.Append(draft, text, writes, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    IReadOnlyList<int> refreshed = replaces ? await ReadIdsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false) : [];
                    IReadOnlyList<int> inserted = request.Inbox ? await ReadIdsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false) : [];
                    IReadOnlyList<int> jobs = enqueues ? await ReadIdsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false) : [];
                    cursor.Complete(result, new NotifyOutcome(requested, inserted, refreshed, jobs));
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        /// <summary>Appends one withdrawal: the deletion of every row of a key, or of the named recipients' rows of it.</summary>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="dedupKey">The key whose rows are deleted, read or not, whatever their type.</param>
        /// <param name="recipients">The distinct users whose rows are deleted; null for every user's.</param>
        /// <returns>The users and types of the deleted rows, once the batch executed and the statement ran.</returns>
        public static BatchResult<WithdrawOutcome> Withdraw(DataBatch batch, string dedupKey, IReadOnlyList<IdList>? recipients)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(dedupKey);

            BatchResult<WithdrawOutcome> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The key, then the named recipients when there are any ---
                string key = draft.Scalar(SqlDbType.NVarChar, dedupKey, Notification.DedupKeyLength);
                StringBuilder text = new();
                _ = text.Append("DELETE [n] OUTPUT [deleted].[UserId], [deleted].[Type]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Notifications.Delimited} AS [n] WHERE [n].[DedupKey] = {key}");
                if (recipients is not null)
                {
                    string users = draft.Table(recipients);
                    _ = text.Append(CultureInfo.InvariantCulture, $" AND [n].[UserId] IN (SELECT [Id] FROM {users})");
                }

                _ = text.Append(';');

                // --- The reader: the deleted rows' users and types ---
                int ordinal = draft.Ordinal;
                HashSet<TableName> writes = [Notifications];
                batch.Append(draft, text.ToString(), writes, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<WithdrawnRow> deleted = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(new WithdrawnRow(set.GetInt32(0), set.GetString(1)));
                    }

                    cursor.CountRows(deleted.Count);
                    cursor.Complete(result, new WithdrawOutcome(deleted));
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
        ///     The text of a request of a type with inbox rows: the replace under
        ///     <see cref="DuplicatePolicy.Replace" />, the insert, then the delivery enqueue when the
        ///     request has slices. Binds <c>p0</c> to <c>p7</c> and <c>t0</c>, then <c>t1</c> and <c>p8</c>
        ///     for the enqueue.
        /// </summary>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="request">The shaped request.</param>
        /// <returns>The text.</returns>
        private static string InboxText(StatementDraft draft, ShapedNotification request)
        {
            // --- The parameters, in the order the statement numbers them ---
            string type = draft.Scalar(SqlDbType.NVarChar, request.Type, Notification.TypeLength);
            string severity = draft.Scalar(SqlDbType.VarChar, request.Severity.ToString(), Notification.SeverityLength);
            string arguments = draft.Scalar(SqlDbType.NVarChar, request.ArgumentsJson, Notification.ArgumentsLength);
            string targetResource = draft.Scalar(SqlDbType.VarChar, request.TargetResource, Notification.TargetResourceLength);
            string targetId = draft.Scalar(SqlDbType.BigInt, request.TargetId);
            string actor = draft.Scalar(SqlDbType.Int, request.ActorUserId);
            string dedupKey = draft.Scalar(SqlDbType.NVarChar, request.DedupKey, Notification.DedupKeyLength);
            string mutable = draft.Scalar(SqlDbType.Bit, request.Mutable);
            string recipients = draft.Table(request.Recipients);
            string count = draft.Name(ReservedSqlNames.StatementNames.Count);
            string first = draft.Name(ReservedSqlNames.StatementNames.First);

            // The predicates every recipient passes: an active user, and the inbox not muted for a
            // mutable type (a non-mutable type short-circuits the preference seek).
            string active = $"EXISTS (SELECT 1 FROM {Users.Delimited} AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)";
            string unmuted = $"({mutable} = 0 OR NOT EXISTS (SELECT 1 FROM {Preferences.Delimited} AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {type} AND [p].[Channel] = '{CoreFeature.InboxChannel}' AND [p].[Enabled] = 0))";

            StringBuilder text = new();
            if (request.Slices is not null)
            {
                _ = text.Append(OutsideTransactionGuard);
            }

            // --- Replace only: the unread duplicates become this request's rows, ahead of the insert ---
            // The insert's duplicate predicate then skips every row refreshed here, so a recipient
            // gets a refreshed row or a new one, never both.
            if (request.Policy == DuplicatePolicy.Replace)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE [n] SET [Type] = {type}, [Severity] = {severity}, [ArgumentsJson] = {arguments}, [TargetResource] = {targetResource},\n")
                    .Append(CultureInfo.InvariantCulture, $"    [TargetId] = {targetId}, [ActorUserId] = {actor}, [CreatedAt] = SYSUTCDATETIME()\n")
                    .Append("OUTPUT [inserted].[UserId]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {Notifications.Delimited} AS [n] JOIN {recipients} AS [r] ON [r].[UserId] = [n].[UserId]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [n].[DedupKey] = {dedupKey} AND [n].[ReadAt] IS NULL\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND {active}\n")
                    .Append(CultureInfo.InvariantCulture, $"  AND {unmuted};\n");
            }

            // --- The insert: one id range for every recipient, taken even when every one is filtered ---
            // The recipients are never empty: an empty request appends nothing. Under Ongoing a read
            // row counts as a duplicate too, so the predicate drops its ReadAt term.
            string duplicateReadTerm = request.Policy == DuplicatePolicy.Ongoing ? string.Empty : " AND [n].[ReadAt] IS NULL";
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {count} int = (SELECT COUNT(*) FROM {recipients});\n")
                .Append(CultureInfo.InvariantCulture, $"DECLARE {first} sql_variant;\n")
                .Append(CultureInfo.InvariantCulture, $"EXEC sys.sp_sequence_get_range @sequence_name = N'{NotificationsSequence}', @range_size = {count}, @range_first_value = {first} OUTPUT;\n")
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Notifications.Delimited} ([Id], [UserId], [Type], [Severity], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])\n")
                .Append("OUTPUT [inserted].[UserId]\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT CAST({first} AS int) + [r].[Ordinal], [r].[UserId], {type}, {severity}, {arguments}, {targetResource}, {targetId}, {actor}, {dedupKey}, SYSUTCDATETIME()\n")
                .Append(CultureInfo.InvariantCulture, $"FROM {recipients} AS [r]\n")
                .Append(CultureInfo.InvariantCulture, $"WHERE {active}\n")
                .Append(CultureInfo.InvariantCulture, $"  AND {unmuted}\n")
                .Append(CultureInfo.InvariantCulture, $"  AND ({dedupKey} IS NULL OR NOT EXISTS (SELECT 1 FROM {Notifications.Delimited} AS [n] WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = {dedupKey}{duplicateReadTerm}));");

            // --- The delivery enqueue, after the insert ---
            if (request.Slices is not null)
            {
                string slices = draft.Table(request.Slices);
                string traceParent = draft.Scalar(SqlDbType.NVarChar, request.TraceParent, JobConfiguration.TraceParentLength);
                _ = text.Append('\n').Append(EnqueueText(draft, request, recipients, slices, type, traceParent));
            }

            return text.ToString();
        }

        /// <summary>
        ///     The text of a request of a channel-only type: the delivery enqueue alone. Binds
        ///     <c>t0</c>, <c>t1</c>, <c>p0</c> and <c>p1</c>.
        /// </summary>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="request">The shaped request, which has slices.</param>
        /// <returns>The text.</returns>
        private static string DeliveryOnlyText(StatementDraft draft, ShapedNotification request)
        {
            if (request.Slices is null)
            {
                throw new InvalidOperationException($"The channel-only notification '{request.Type}' has no slices to enqueue; such a request appends nothing.");
            }

            string recipients = draft.Table(request.Recipients);
            string slices = draft.Table(request.Slices);
            string type = draft.Scalar(SqlDbType.NVarChar, request.Type, Notification.TypeLength);
            string traceParent = draft.Scalar(SqlDbType.NVarChar, request.TraceParent, JobConfiguration.TraceParentLength);
            return OutsideTransactionGuard + EnqueueText(draft, request, recipients, slices, type, traceParent);
        }

        /// <summary>
        ///     The delivery enqueue: one <see cref="DeliveryHandlerKey" /> job for each slice holding an
        ///     active recipient with some channel other than the inbox enabled for the type, by the
        ///     user's preference row or else the channel's default. The job range is taken only when a
        ///     slice qualifies.
        /// </summary>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="request">The shaped request.</param>
        /// <param name="recipients">The recipients' table-valued parameter.</param>
        /// <param name="slices">The slices' table-valued parameter.</param>
        /// <param name="type">The type's parameter.</param>
        /// <param name="traceParent">The trace parent's parameter.</param>
        /// <returns>The text.</returns>
        private static string EnqueueText(StatementDraft draft, ShapedNotification request, string recipients, string slices, string type, string traceParent)
        {
            string qualified = draft.Name("qualified");
            string jobFirst = draft.Name("jobFirst");
            string jobCount = draft.Name("jobN");
            string condition = string.Format(CultureInfo.InvariantCulture, request.DeliveryCondition!, type);

            StringBuilder text = new();
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {qualified} TABLE ([SliceOrdinal] int NOT NULL PRIMARY KEY);\n")
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {qualified} ([SliceOrdinal])\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT [s].[SliceOrdinal] FROM {slices} AS [s]\n")
                .Append(CultureInfo.InvariantCulture, $"WHERE EXISTS (SELECT 1 FROM {recipients} AS [r] INNER JOIN {Users.Delimited} AS [u] ON [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1\n")
                .Append("    WHERE [r].[Ordinal] BETWEEN [s].[FirstOrdinal] AND [s].[LastOrdinal]\n")
                .Append(CultureInfo.InvariantCulture, $"      AND ({condition}));\n")
                .Append(CultureInfo.InvariantCulture, $"DECLARE {jobFirst} sql_variant, {jobCount} int = (SELECT COUNT(*) FROM {qualified});\n")
                .Append(CultureInfo.InvariantCulture, $"IF {jobCount} > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'{JobStatements.JobsSequence}', @range_size = {jobCount}, @range_first_value = {jobFirst} OUTPUT;\n")
                .Append(CultureInfo.InvariantCulture, $"INSERT INTO {JobStatements.Jobs.Delimited} ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [CreatedAt])\n")
                .Append("OUTPUT [inserted].[Id]\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT CAST({jobFirst} AS int) + ROW_NUMBER() OVER (ORDER BY [s].[SliceOrdinal]) - 1, N'{DeliveryHandlerKey}', {JobConfiguration.Quoted(JobStatus.Pending)},\n")
                .Append(CultureInfo.InvariantCulture, $"    SYSUTCDATETIME(), 0, [s].[ArgumentsJson], {traceParent}, {WellKnownIds.SystemUserId}, NULL, SYSUTCDATETIME()\n")
                .Append(CultureInfo.InvariantCulture, $"FROM {slices} AS [s] INNER JOIN {qualified} AS [q] ON [q].[SliceOrdinal] = [s].[SliceOrdinal];");
            return text.ToString();
        }

        /// <summary>Reads the statement's next result set as one <c>int</c> column.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The ids, in the order returned.</returns>
        private static async Task<IReadOnlyList<int>> ReadIdsAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            List<int> ids = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(set.GetInt32(0));
            }

            cursor.CountRows(ids.Count);
            return ids;
        }
    }
}
