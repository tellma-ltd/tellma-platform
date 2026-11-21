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
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Notifications
{
    // The statements are spec 0021 §4.5's load and §7's retention page, appended on the batch's
    // internal channel: the load reads core.Users and the preferences of one delivery slice, and the
    // retention page deletes from core.Notifications, which the notification stack owns, outside any
    // user's rows.
    /// <summary>The fixed statements of the notification delivery and retention handlers.</summary>
    internal static class NotificationDeliveryStatements
    {
        /// <summary>The table a retention page writes.</summary>
        private static readonly FrozenSet<TableName> RetentionWrites = new[] { NotificationStatements.Notifications }.ToFrozenSet();

        /// <summary>
        ///     Appends the load of one delivery slice: the slice's active users with their contact and
        ///     locale columns, then their preference rows for the type.
        /// </summary>
        /// <param name="batch">The read batch.</param>
        /// <param name="recipientIds">The slice's recipients; repeats are bound once.</param>
        /// <param name="type">The notification type's key.</param>
        /// <returns>The slice's users and preference rows, once the batch executed and the statement ran.</returns>
        public static BatchResult<DeliverySlice> Load(DataBatch batch, IReadOnlyList<int> recipientIds, string type)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(recipientIds);
            ArgumentNullException.ThrowIfNull(type);

            BatchResult<DeliverySlice> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters: the slice's distinct ids, then the type ---
                IdList[] ids = [.. recipientIds.Distinct().Select(static id => new IdList { Id = id })];
                string recipients = draft.Table(ids);
                string typeKey = draft.Scalar(SqlDbType.NVarChar, type, 64);

                // --- The two selects of spec 0021 §4.5 ---
                string text = new StringBuilder()
                    .Append("SELECT [u].[Id], [u].[Name], [u].[Email], [u].[ContactEmail], [u].[ContactMobile],\n")
                    .Append("       [u].[PreferredLanguage], [u].[PreferredCalendar], [u].[PreferredTimeZone], [u].[PreferredRegion]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM [core].[Users] AS [u] INNER JOIN {recipients} AS [r] ON [r].[Id] = [u].[Id]\n")
                    .Append("WHERE [u].[IsActive] = 1;\n")
                    .Append("SELECT [p].[UserId], [p].[Channel], [p].[Enabled]\n")
                    .Append(CultureInfo.InvariantCulture, $"FROM {NotificationStatements.Preferences.Delimited} AS [p] INNER JOIN {recipients} AS [r] ON [r].[Id] = [p].[UserId]\n")
                    .Append(CultureInfo.InvariantCulture, $"WHERE [p].[Type] = {typeKey};")
                    .ToString();

                // --- The reader: the users by name of column, then the preference rows ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: null, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader users = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    int id = users.GetOrdinal("Id");
                    int name = users.GetOrdinal("Name");
                    int email = users.GetOrdinal("Email");
                    int contactEmail = users.GetOrdinal("ContactEmail");
                    int contactMobile = users.GetOrdinal("ContactMobile");
                    int language = users.GetOrdinal("PreferredLanguage");
                    int calendar = users.GetOrdinal("PreferredCalendar");
                    int timeZone = users.GetOrdinal("PreferredTimeZone");
                    int region = users.GetOrdinal("PreferredRegion");
                    List<DeliveryUser> found = [];
                    while (await users.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        found.Add(new DeliveryUser(
                            users.GetInt32(id),
                            users.GetString(name),
                            Text(users, email),
                            Text(users, contactEmail),
                            Text(users, contactMobile),
                            Text(users, language),
                            Text(users, calendar),
                            Text(users, timeZone),
                            Text(users, region)));
                    }

                    cursor.CountRows(found.Count);

                    DbDataReader preferences = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<DeliveryPreference> rows = [];
                    while (await preferences.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        rows.Add(new DeliveryPreference(preferences.GetInt32(0), preferences.GetString(1), preferences.GetBoolean(2)));
                    }

                    cursor.CountRows(rows.Count);
                    cursor.Complete(result, new DeliverySlice(found, rows));
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
        ///     Appends one retention page: up to a page of a band's rows older than its cut-off, measured
        ///     on the database clock, deleted, and the count returned.
        /// </summary>
        /// <param name="batch">The maintenance batch.</param>
        /// <param name="read">True for the read band, aged from when each row was read; false for the unread band, aged from when each was created.</param>
        /// <param name="pageSize">The most rows the page deletes.</param>
        /// <param name="readRetention">How long a read row is kept.</param>
        /// <param name="unreadRetention">How long an unread row is kept.</param>
        /// <returns>The rows the page deleted, once the batch executed and the statement ran.</returns>
        public static BatchResult<int> RetentionPage(DataBatch batch, bool read, int pageSize, TimeSpan readRetention, TimeSpan unreadRetention)
        {
            ArgumentNullException.ThrowIfNull(batch);

            BatchResult<int> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters: the page size, then both bands' retention in seconds ---
                string size = draft.Scalar(SqlDbType.Int, pageSize);
                string readSeconds = draft.Scalar(SqlDbType.Int, Seconds(readRetention));
                string unreadSeconds = draft.Scalar(SqlDbType.Int, Seconds(unreadRetention));

                // --- The band's delete, its cut-off from the database clock ---
                string band = read
                    ? $"[ReadAt] IS NOT NULL AND [ReadAt] < DATEADD(second, -{readSeconds}, SYSUTCDATETIME())"
                    : $"[ReadAt] IS NULL AND [CreatedAt] < DATEADD(second, -{unreadSeconds}, SYSUTCDATETIME())";
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"DELETE TOP ({size}) FROM {NotificationStatements.Notifications.Delimited}\nWHERE {band};\nSELECT @@ROWCOUNT AS [Deleted];");

                // --- The reader: the one count ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: RetentionWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The retention page returned no count.");
                    }

                    int deleted = set.GetInt32(0);
                    cursor.CountRows(1);
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

        /// <summary>A retention as whole seconds, which the statement's <c>DATEADD</c> takes as an int.</summary>
        /// <param name="retention">The retention.</param>
        /// <returns>The seconds, at most <see cref="int.MaxValue" />.</returns>
        private static int Seconds(TimeSpan retention)
        {
            return (int)Math.Clamp((long)retention.TotalSeconds, 0, int.MaxValue);
        }

        /// <summary>A nullable text column.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="ordinal">The column.</param>
        /// <returns>The value; null for a null.</returns>
        private static string? Text(DbDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }

    /// <summary>What the load read of one delivery slice.</summary>
    /// <param name="Users">The slice's active users, in no order.</param>
    /// <param name="Preferences">The users' preference rows for the type, of any channel.</param>
    internal sealed record DeliverySlice(IReadOnlyList<DeliveryUser> Users, IReadOnlyList<DeliveryPreference> Preferences);

    /// <summary>One active recipient of a delivery slice, as the load read them.</summary>
    /// <param name="Id">The user's id.</param>
    /// <param name="Name">The user's name.</param>
    /// <param name="Email">The user's sign-in email; null when none.</param>
    /// <param name="ContactEmail">The user's contact email; null when none.</param>
    /// <param name="ContactMobile">The user's contact mobile; null when none.</param>
    /// <param name="PreferredLanguage">The user's preferred language; null for the tenant's.</param>
    /// <param name="PreferredCalendar">The user's preferred calendar; null for the tenant's.</param>
    /// <param name="PreferredTimeZone">The user's preferred time zone; null for the tenant's.</param>
    /// <param name="PreferredRegion">The user's preferred region; null for the tenant's.</param>
    internal sealed record DeliveryUser(
        int Id,
        string Name,
        string? Email,
        string? ContactEmail,
        string? ContactMobile,
        string? PreferredLanguage,
        string? PreferredCalendar,
        string? PreferredTimeZone,
        string? PreferredRegion);

    /// <summary>One stored preference of a delivery slice's recipient for the type.</summary>
    /// <param name="UserId">The recipient.</param>
    /// <param name="Channel">The channel's key.</param>
    /// <param name="Enabled">Whether the channel delivers the type to them.</param>
    internal sealed record DeliveryPreference(int UserId, string Channel, bool Enabled);
}
