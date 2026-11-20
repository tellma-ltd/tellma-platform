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
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Core.Notifications
{
    // The statements are spec 0021 §5.3, appended on the batch's internal channel: the verbs write
    // core.Notifications, which the notification stack owns, on the caller's behalf, and the
    // summary declares the actors' table the actors' entity query restricts on. Every statement
    // names the caller as the connect prologue declared them (@tm_UserId), so its own predicate is
    // the authorization.
    /// <summary>The inbox's fixed statements: the summary, and the five verbs with their version tail.</summary>
    internal static class InboxStatements
    {
        /// <summary>The word the summary's actors table is named by, within the statement's namespace.</summary>
        private const string ActorsWord = "actors";

        /// <summary>The job statuses the summary lists: every one that has not ended.</summary>
        private static readonly string LiveStatuses = JobConfiguration.In(JobStatus.Pending, JobStatus.Running, JobStatus.Held, JobStatus.PendingConfirmation);

        /// <summary>
        ///     Appends the summary statement: the caller's version and seen stamp first, then the capped
        ///     counts and the most severe unseen row, the newest rows, the actors' table, and the
        ///     caller's own jobs that have not ended.
        /// </summary>
        /// <remarks>
        ///     The version is read in the first statement: under read-committed snapshot every later
        ///     statement sees at least what that version covers, so a copy at a verb's version contains
        ///     the verb.
        /// </remarks>
        /// <param name="batch">The batch, which carries the caller's connect prologue.</param>
        /// <param name="countCap">The cap of the unseen and unread counts.</param>
        /// <param name="latestCount">How many rows and jobs are listed at most.</param>
        /// <param name="actors">The batch-local table of the distinct actors of the newest rows, for the actors' entity query.</param>
        /// <returns>The summary's rows, once the batch executed and the statement ran.</returns>
        public static BatchResult<InboxSummaryRows> Summary(DataBatch batch, int countCap, int latestCount, out string actors)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchResult<InboxSummaryRows> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters and the statement's own names ---
                string cap = draft.Scalar(SqlDbType.Int, countCap);
                string latestTop = draft.Scalar(SqlDbType.Int, latestCount);
                string seenAt = draft.Name("seenAt");
                string version = draft.Name("version");
                string latest = draft.Name("latest");
                actors = draft.Name(ActorsWord);
                string user = ReservedSqlNames.UserId;
                string notifications = NotificationStatements.Notifications.Delimited;

                StringBuilder text = new();

                // --- First: the version and the seen stamp ---
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {seenAt} datetimeoffset(3), {version} bigint;\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT {seenAt} = [InboxSeenAt], {version} = [InboxVersion] FROM {UserStampConfiguration.Table.Delimited} WHERE [UserId] = {user};\n");

                // --- The capped counts and the most severe of the capped newest unseen rows ---
                _ = text.Append("SELECT [a].[Unseen], [a].[MaxUnseenSeverity],\n")
                    .Append(CultureInfo.InvariantCulture, $"  (SELECT COUNT(*) FROM (SELECT TOP ({cap}) 1 AS [x] FROM {notifications}\n")
                    .Append(CultureInfo.InvariantCulture, $"                         WHERE [UserId] = {user} AND [ReadAt] IS NULL) AS [b]) AS [Unread],\n")
                    .Append(CultureInfo.InvariantCulture, $"  {version} AS [Version], {seenAt} AS [SeenAt]\n")
                    .Append("FROM (SELECT COUNT(*) AS [Unseen],\n")
                    .Append("             MAX(CASE [Severity] WHEN 'Error' THEN 3 WHEN 'Warning' THEN 2 WHEN 'Success' THEN 1 ELSE 0 END) AS [MaxUnseenSeverity]\n")
                    .Append(CultureInfo.InvariantCulture, $"      FROM (SELECT TOP ({cap}) [Severity] FROM {notifications}\n")
                    .Append(CultureInfo.InvariantCulture, $"            WHERE [UserId] = {user} AND [CreatedAt] > COALESCE({seenAt}, '0001-01-01')\n")
                    .Append("            ORDER BY [CreatedAt] DESC, [Id] DESC) AS [u]) AS [a];\n");

                // --- The newest rows, and the distinct actors among them ---
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {latest} TABLE ([Id] int NOT NULL PRIMARY KEY, [ActorUserId] int NULL);\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {latest} ([Id], [ActorUserId])\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT TOP ({latestTop}) [Id], [ActorUserId] FROM {notifications} WHERE [UserId] = {user} ORDER BY [CreatedAt] DESC, [Id] DESC;\n")
                    .Append("SELECT [n].[Id], [n].[UserId], [n].[Type], [n].[Severity], [n].[ArgumentsJson], [n].[TargetResource], [n].[TargetId], [n].[ActorUserId], [n].[DedupKey], [n].[CreatedAt], [n].[ReadAt]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {notifications} AS [n] INNER JOIN {latest} AS [l] ON [l].[Id] = [n].[Id]\n")
                    .Append("ORDER BY [n].[CreatedAt] DESC, [n].[Id] DESC;\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {actors} TABLE ([Id] int NOT NULL PRIMARY KEY);\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {actors} ([Id]) SELECT DISTINCT [ActorUserId] FROM {latest} WHERE [ActorUserId] IS NOT NULL;\n");

                // --- The caller's own jobs that have not ended, cron occurrences aside ---
                _ = text.Append(CultureInfo.InvariantCulture, $"SELECT TOP ({latestTop}) [Id], [HandlerKey], [Status], [ProgressPercent], [ProgressKey], [ProgressArgumentsJson], [CancelRequestedAt], [StartedAt], [CreatedAt],\n")
                    .Append("       [LabelKey], [LabelArgumentsJson]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {JobStatements.Jobs.Delimited}\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [RequestedById] = {user} AND [RequestedById] IS NOT NULL AND [Status] {LiveStatuses} AND [ScheduledFor] IS NULL\n")
                    .Append("ORDER BY [CreatedAt] DESC, [Id] DESC;");

                // --- The reader: the counts, the newest rows, the jobs ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), writes: null, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader counts = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await counts.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The inbox summary's counts returned no row.");
                    }

                    int unseen = counts.GetInt32(counts.GetOrdinal("Unseen"));
                    int rankOrdinal = counts.GetOrdinal("MaxUnseenSeverity");
                    NotificationSeverity? severity = counts.IsDBNull(rankOrdinal) ? null : Severity(counts.GetInt32(rankOrdinal));
                    int unread = counts.GetInt32(counts.GetOrdinal("Unread"));
                    int versionOrdinal = counts.GetOrdinal("Version");
                    long stamped = counts.IsDBNull(versionOrdinal)
                        ? throw new InvalidOperationException("The caller has no stamp row, so the inbox has no version.")
                        : counts.GetInt64(versionOrdinal);
                    int seenOrdinal = counts.GetOrdinal("SeenAt");
                    DateTimeOffset? seen = counts.IsDBNull(seenOrdinal) ? null : counts.GetFieldValue<DateTimeOffset>(seenOrdinal);
                    cursor.CountRows(1);

                    IReadOnlyList<Notification> rows = await ReadNotificationsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false);
                    IReadOnlyList<InboxRunningJob> jobs = await ReadJobsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false);
                    cursor.Complete(result, new InboxSummaryRows(unseen, severity, unread, rows, jobs, stamped, seen));
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
        ///     Appends one verb's statement: its change to the caller's rows, then the version tail that
        ///     increments the caller's inbox version and returns it with the count.
        /// </summary>
        /// <param name="batch">The persist batch, which carries the caller's connect prologue.</param>
        /// <param name="verb">The verb.</param>
        /// <param name="ids">The distinct ids <see cref="InboxVerb.Read" /> and <see cref="InboxVerb.Clear" /> name; ignored by the others.</param>
        /// <returns>The count and the version, once the batch executed and the statement ran.</returns>
        public static BatchResult<InboxVerbResult> Verb(DataBatch batch, InboxVerb verb, IReadOnlyList<IdList> ids)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(ids);

            BatchResult<InboxVerbResult> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                string user = ReservedSqlNames.UserId;
                string notifications = NotificationStatements.Notifications.Delimited;
                string stamps = UserStampConfiguration.Table.Delimited;
                string version = draft.Name("v");
                StringBuilder text = new();
                HashSet<TableName> writes = [NotificationStatements.Notifications, UserStampConfiguration.Table];

                switch (verb)
                {
                    // --- Seen: the stamp and the version in one update; the count is always one ---
                    case InboxVerb.Seen:
                        writes = [UserStampConfiguration.Table];
                        _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {version} bigint;\n")
                            .Append(CultureInfo.InvariantCulture, $"UPDATE {stamps} SET [InboxSeenAt] = SYSUTCDATETIME(), {version} = [InboxVersion] = [InboxVersion] + 1 WHERE [UserId] = {user};\n")
                            .Append(CultureInfo.InvariantCulture, $"SELECT 1 AS [Count], {version} AS [Version];");
                        break;

                    // --- The others: the rows changed, then the version tail counting them ---
                    case InboxVerb.Read:
                        string read = draft.Table(ids);
                        _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE {notifications} SET [ReadAt] = SYSUTCDATETIME()\n")
                            .Append(CultureInfo.InvariantCulture, $"WHERE [UserId] = {user} AND [ReadAt] IS NULL AND [Id] IN (SELECT [Id] FROM {read});\n");
                        AppendTail(text, draft, version, stamps, user);
                        break;

                    case InboxVerb.ReadAll:
                        _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE {notifications} SET [ReadAt] = SYSUTCDATETIME() WHERE [UserId] = {user} AND [ReadAt] IS NULL;\n");
                        AppendTail(text, draft, version, stamps, user);
                        break;

                    case InboxVerb.Clear:
                        string cleared = draft.Table(ids);
                        _ = text.Append(CultureInfo.InvariantCulture, $"DELETE FROM {notifications} WHERE [UserId] = {user} AND [Id] IN (SELECT [Id] FROM {cleared});\n");
                        AppendTail(text, draft, version, stamps, user);
                        break;

                    case InboxVerb.ClearRead:
                        _ = text.Append(CultureInfo.InvariantCulture, $"DELETE FROM {notifications} WHERE [UserId] = {user} AND [ReadAt] IS NOT NULL;\n");
                        AppendTail(text, draft, version, stamps, user);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(verb), verb, "The inbox verb is not defined.");
                }

                // --- The reader: the one row of the count and the version ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), writes, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The inbox verb returned no row.");
                    }

                    int count = set.GetInt32(0);
                    long after = set.IsDBNull(1)
                        ? throw new InvalidOperationException("The caller has no stamp row, so the inbox verb has no version to increment.")
                        : set.GetInt64(1);
                    cursor.CountRows(1);
                    cursor.Complete(result, new InboxVerbResult(count, after));
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
        ///     Appends the version tail every verb but <see cref="InboxVerb.Seen" /> shares: the rows the
        ///     change counted, then the version incremented and returned. It directly follows the change,
        ///     so <c>@@ROWCOUNT</c> is the change's.
        /// </summary>
        /// <param name="text">The statement's text so far.</param>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="version">The version variable's name.</param>
        /// <param name="stamps">The stamps table, delimited.</param>
        /// <param name="user">The caller's variable.</param>
        private static void AppendTail(StringBuilder text, StatementDraft draft, string version, string stamps, string user)
        {
            string count = draft.Name("n");
            _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {count} int = @@ROWCOUNT, {version} bigint;\n")
                .Append(CultureInfo.InvariantCulture, $"UPDATE {stamps} SET {version} = [InboxVersion] = [InboxVersion] + 1 WHERE [UserId] = {user};\n")
                .Append(CultureInfo.InvariantCulture, $"SELECT {count} AS [Count], {version} AS [Version];");
        }

        /// <summary>The severity a rank of the summary's <c>CASE</c> maps back to.</summary>
        /// <param name="rank">The rank: 3 for an error down to 0 for information.</param>
        /// <returns>The severity.</returns>
        private static NotificationSeverity Severity(int rank)
        {
            return rank switch
            {
                3 => NotificationSeverity.Error,
                2 => NotificationSeverity.Warning,
                1 => NotificationSeverity.Success,
                _ => NotificationSeverity.Info,
            };
        }

        /// <summary>Reads the newest rows, every column by name.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows, newest first.</returns>
        private static async Task<IReadOnlyList<Notification>> ReadNotificationsAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            int id = set.GetOrdinal(nameof(Notification.Id));
            int userId = set.GetOrdinal(nameof(Notification.UserId));
            int type = set.GetOrdinal(nameof(Notification.Type));
            int severity = set.GetOrdinal(nameof(Notification.Severity));
            int arguments = set.GetOrdinal(nameof(Notification.ArgumentsJson));
            int targetResource = set.GetOrdinal(nameof(Notification.TargetResource));
            int targetId = set.GetOrdinal(nameof(Notification.TargetId));
            int actor = set.GetOrdinal(nameof(Notification.ActorUserId));
            int dedupKey = set.GetOrdinal(nameof(Notification.DedupKey));
            int createdAt = set.GetOrdinal(nameof(Notification.CreatedAt));
            int readAt = set.GetOrdinal(nameof(Notification.ReadAt));

            List<Notification> rows = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new Notification
                {
                    Id = set.GetInt32(id),
                    UserId = set.GetInt32(userId),
                    Type = set.GetString(type),
                    Severity = Enum.Parse<NotificationSeverity>(set.GetString(severity)),
                    ArgumentsJson = set.IsDBNull(arguments) ? null : set.GetString(arguments),
                    TargetResource = set.IsDBNull(targetResource) ? null : set.GetString(targetResource),
                    TargetId = set.IsDBNull(targetId) ? null : set.GetInt64(targetId),
                    ActorUserId = set.IsDBNull(actor) ? null : set.GetInt32(actor),
                    DedupKey = set.IsDBNull(dedupKey) ? null : set.GetString(dedupKey),
                    CreatedAt = set.GetFieldValue<DateTimeOffset>(createdAt),
                    ReadAt = set.IsDBNull(readAt) ? null : set.GetFieldValue<DateTimeOffset>(readAt),
                });
            }

            cursor.CountRows(rows.Count);
            return rows;
        }

        /// <summary>Reads the caller's jobs that have not ended, every column by name.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The jobs, newest first.</returns>
        private static async Task<IReadOnlyList<InboxRunningJob>> ReadJobsAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            int id = set.GetOrdinal(nameof(InboxRunningJob.Id));
            int handlerKey = set.GetOrdinal(nameof(InboxRunningJob.HandlerKey));
            int status = set.GetOrdinal(nameof(InboxRunningJob.Status));
            int percent = set.GetOrdinal(nameof(InboxRunningJob.ProgressPercent));
            int progressKey = set.GetOrdinal(nameof(InboxRunningJob.ProgressKey));
            int progressArguments = set.GetOrdinal(nameof(InboxRunningJob.ProgressArgumentsJson));
            int cancelRequestedAt = set.GetOrdinal(nameof(InboxRunningJob.CancelRequestedAt));
            int startedAt = set.GetOrdinal(nameof(InboxRunningJob.StartedAt));
            int createdAt = set.GetOrdinal(nameof(InboxRunningJob.CreatedAt));
            int labelKey = set.GetOrdinal(nameof(InboxRunningJob.LabelKey));
            int labelArguments = set.GetOrdinal(nameof(InboxRunningJob.LabelArgumentsJson));

            List<InboxRunningJob> jobs = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                jobs.Add(new InboxRunningJob(
                    set.GetInt32(id),
                    set.GetString(handlerKey),
                    Enum.Parse<JobStatus>(set.GetString(status)),
                    set.IsDBNull(percent) ? null : set.GetByte(percent),
                    set.IsDBNull(progressKey) ? null : set.GetString(progressKey),
                    set.IsDBNull(progressArguments) ? null : set.GetString(progressArguments),
                    set.IsDBNull(cancelRequestedAt) ? null : set.GetFieldValue<DateTimeOffset>(cancelRequestedAt),
                    set.IsDBNull(startedAt) ? null : set.GetFieldValue<DateTimeOffset>(startedAt),
                    set.GetFieldValue<DateTimeOffset>(createdAt),
                    set.IsDBNull(labelKey) ? null : set.GetString(labelKey),
                    set.IsDBNull(labelArguments) ? null : set.GetString(labelArguments)));
            }

            cursor.CountRows(jobs.Count);
            return jobs;
        }
    }

    /// <summary>The inbox verbs, each one statement with the version tail.</summary>
    internal enum InboxVerb
    {
        /// <summary>Marks the inbox seen: stamps when the bell was last opened.</summary>
        Seen,

        /// <summary>Marks the named rows read.</summary>
        Read,

        /// <summary>Marks every row read.</summary>
        ReadAll,

        /// <summary>Deletes the named rows.</summary>
        Clear,

        /// <summary>Deletes every read row.</summary>
        ClearRead,
    }

    /// <summary>What the summary statement read, before the actors' query fills the related entities.</summary>
    /// <param name="Unseen">The capped count of rows newer than the seen stamp.</param>
    /// <param name="MaxUnseenSeverity">The most severe of the capped newest unseen rows; null when none is unseen.</param>
    /// <param name="Unread">The capped count of unread rows.</param>
    /// <param name="Latest">The newest rows, newest first.</param>
    /// <param name="Running">The caller's jobs that have not ended, newest first.</param>
    /// <param name="Version">The caller's inbox version, read first.</param>
    /// <param name="SeenAt">The caller's seen stamp; null when never.</param>
    internal sealed record InboxSummaryRows(
        int Unseen,
        NotificationSeverity? MaxUnseenSeverity,
        int Unread,
        IReadOnlyList<Notification> Latest,
        IReadOnlyList<InboxRunningJob> Running,
        long Version,
        DateTimeOffset? SeenAt);
}
