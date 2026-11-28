// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Core.Outbox
{
    // The statements are spec 0022 §5's dispatch, §6's delivery update and §7.3's retention read,
    // appended on the batch's internal channel (spec 0022 §1.4): the load reads the attachments of
    // the claim's pending rows joined to core.Blobs, whose storage key Queryex never exposes; the
    // pre-mark, the settle and the delivery update write core.OutgoingEmails, which the outbox's
    // stack owns, from outside its pipeline, and leave the audit columns alone. The writes are
    // idempotent: the pre-mark answers the end state, the settle matches only rows still sending,
    // and the delivery update skips an event already applied or no stronger than the row's, so the
    // executor's re-run of any of them returns or changes what the first run did.
    /// <summary>The fixed statements of the outgoing emails' sending job, delivery events and retention.</summary>
    internal static class OutboxStatements
    {
        /// <summary>The outgoing emails' table.</summary>
        public static TableName OutgoingEmails { get; } = new("core", "OutgoingEmails");

        /// <summary>The outgoing emails' attachments' table.</summary>
        public static TableName OutgoingEmailAttachments { get; } = new("core", "OutgoingEmailAttachments");

        /// <summary>The table the pre-mark and the settle write.</summary>
        private static readonly FrozenSet<TableName> EmailWrites = new[] { OutgoingEmails }.ToFrozenSet();

        /// <summary>The settled states, as an <c>IN</c> list.</summary>
        private static readonly string SettledStates = Literals(
            nameof(OutgoingEmailState.Sent), nameof(OutgoingEmailState.Sandboxed), nameof(OutgoingEmailState.Rejected), nameof(OutgoingEmailState.Failed), nameof(OutgoingEmailState.Cancelled));

        /// <summary>The unsettled states, as an <c>IN</c> list.</summary>
        private static readonly string UnsettledStates = Literals(nameof(OutgoingEmailState.Pending), nameof(OutgoingEmailState.Sending));

        /// <summary>The statuses of a job that may still run, as an <c>IN</c> list.</summary>
        private static readonly string LiveJobStatuses = Literals(
            nameof(JobStatus.Pending), nameof(JobStatus.Running), nameof(JobStatus.Held), nameof(JobStatus.PendingConfirmation));

        /// <summary>
        ///     Appends the load of the rows' attachments, in each row's order, each with its blob's
        ///     storage key, content type and size; an attachment whose blob row is gone has none.
        /// </summary>
        /// <param name="batch">The read batch.</param>
        /// <param name="ids">The rows' ids; at least one.</param>
        /// <returns>The attachments, once the batch executed and the statement ran.</returns>
        public static BatchResult<IReadOnlyList<DispatchAttachment>> LoadAttachments(DataBatch batch, IReadOnlyCollection<int> ids)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0)
            {
                throw new ArgumentException("Load the attachments of at least one email.", nameof(ids));
            }

            BatchResult<IReadOnlyList<DispatchAttachment>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameter: the rows' distinct ids ---
                string rows = draft.Table([.. ids.Distinct().Select(static id => new IdList { Id = id })]);

                // --- The attachments in each row's order, the blob joined for what the store needs ---
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    SELECT [a].[OutgoingEmailId], [a].[FileName], [a].[ContentId], [b].[{BlobConfiguration.StorageKey}], [b].[ContentType], [b].[Size]
                    FROM {OutgoingEmailAttachments.Delimited} AS [a]
                    INNER JOIN {rows} AS [r] ON [r].[Id] = [a].[OutgoingEmailId]
                    LEFT JOIN {BlobStatements.Blobs.Delimited} AS [b] ON [b].[Id] = [a].[FileId]
                    ORDER BY [a].[OutgoingEmailId], [a].[Id];
                    """);

                // --- The reader: one attachment per row of the set ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: null, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<DispatchAttachment> attachments = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        attachments.Add(new DispatchAttachment(
                            set.GetInt32(0),
                            set.GetString(1),
                            set.IsDBNull(2) ? null : set.GetString(2),
                            set.IsDBNull(3) ? null : set.GetString(3),
                            set.IsDBNull(4) ? null : set.GetString(4),
                            set.IsDBNull(5) ? null : set.GetInt64(5)));
                    }

                    cursor.CountRows(attachments.Count);
                    cursor.Complete(result, attachments);
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
        ///     Appends the pre-mark: every row still pending is marked sending and counts one more
        ///     attempt, then the ids of the rows now sending are read back. The read answers the end
        ///     state rather than what changed, so a re-run returns the same rows.
        /// </summary>
        /// <param name="batch">The maintenance batch.</param>
        /// <param name="ids">The rows' ids; at least one.</param>
        /// <returns>The ids of the rows sending once the statement ran; a row left out was settled meanwhile.</returns>
        public static BatchResult<IReadOnlySet<int>> PreMark(DataBatch batch, IReadOnlyCollection<int> ids)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0)
            {
                throw new ArgumentException("Pre-mark at least one email.", nameof(ids));
            }

            BatchResult<IReadOnlySet<int>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameter: the rows' distinct ids ---
                string rows = draft.Table([.. ids.Distinct().Select(static id => new IdList { Id = id })]);

                // --- The mark of the pending rows, then the end state ---
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    UPDATE [e] SET [State] = '{nameof(OutgoingEmailState.Sending)}', [AttemptCount] = [e].[AttemptCount] + 1
                    FROM {OutgoingEmails.Delimited} AS [e] INNER JOIN {rows} AS [r] ON [r].[Id] = [e].[Id]
                    WHERE [e].[State] = '{nameof(OutgoingEmailState.Pending)}';
                    SELECT [e].[Id]
                    FROM {OutgoingEmails.Delimited} AS [e] INNER JOIN {rows} AS [r] ON [r].[Id] = [e].[Id]
                    WHERE [e].[State] = '{nameof(OutgoingEmailState.Sending)}';
                    """);

                // --- The reader: the ids now sending ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: EmailWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    HashSet<int> sending = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        _ = sending.Add(set.GetInt32(0));
                    }

                    cursor.CountRows(sending.Count);
                    cursor.Complete(result, sending);
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
        ///     Appends the settle: each outcome's row, matched on its id while it is still sending, takes
        ///     the outcome's state, provider id, delivery-events flag and error, and a sent or sandboxed
        ///     row its send time from the database clock. A row no longer sending is left as it is, so a
        ///     re-run changes nothing.
        /// </summary>
        /// <param name="batch">The maintenance batch.</param>
        /// <param name="outcomes">The outcomes, one per row; at least one.</param>
        /// <returns>The rows the statement settled, once it ran.</returns>
        public static BatchResult<IReadOnlyList<SettledEmail>> Settle(DataBatch batch, IReadOnlyList<OutgoingEmailOutcomeList> outcomes)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(outcomes);
            if (outcomes.Count == 0)
            {
                throw new ArgumentException("Settle at least one email.", nameof(outcomes));
            }

            BatchResult<IReadOnlyList<SettledEmail>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameter: one outcome per row ---
                string values = draft.Table(outcomes);

                // --- The update of the rows still sending, answering what it settled ---
                // The wait of a sent row is measured on the database clock, from its enqueue to its send.
                string sent = $"'{nameof(OutgoingEmailState.Sent)}', '{nameof(OutgoingEmailState.Sandboxed)}'";
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    UPDATE [e] SET
                        [State]                 = [v].[State],
                        [SentAt]                = CASE WHEN [v].[State] IN ({sent}) THEN SYSUTCDATETIME() ELSE [e].[SentAt] END,
                        [ProviderMessageId]     = [v].[ProviderMessageId],
                        [ExpectsDeliveryEvents] = [v].[ExpectsDeliveryEvents],
                        [ErrorCode]             = [v].[ErrorCode],
                        [ErrorDetail]           = [v].[ErrorDetail]
                    OUTPUT [inserted].[Id], [inserted].[State],
                        CASE WHEN [inserted].[State] IN ({sent}) THEN DATEDIFF_BIG(millisecond, [inserted].[CreatedAt], [inserted].[SentAt]) END
                    FROM {OutgoingEmails.Delimited} AS [e] INNER JOIN {values} AS [v] ON [v].[Id] = [e].[Id]
                    WHERE [e].[State] = '{nameof(OutgoingEmailState.Sending)}';
                    """);

                // --- The reader: each settled row's state and wait ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: EmailWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<SettledEmail> settled = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        settled.Add(new SettledEmail(
                            set.GetInt32(0),
                            Enum.Parse<OutgoingEmailState>(set.GetString(1)),
                            set.IsDBNull(2) ? null : TimeSpan.FromMilliseconds(set.GetInt64(2))));
                    }

                    cursor.CountRows(settled.Count);
                    cursor.Complete(result, settled);
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
        ///     Appends the delivery update: each event's row moves to the event's status only when the
        ///     event is not the one last applied and is stronger than the row's status, or a later event
        ///     of the same status; then the count of rows updated, and the events whose row does not
        ///     exist.
        /// </summary>
        /// <remarks>
        ///     The update applies in any state, so an interrupted row whose message did go out still
        ///     records its delivery. A stored status is ranked in the statement by the order of
        ///     <see cref="EmailDeliveryStatus" />'s values, which each event carries as its rank.
        /// </remarks>
        /// <param name="batch">The maintenance batch, of the tenant's database outside any tenant scope.</param>
        /// <param name="events">The events, at most one per row; at least one.</param>
        /// <returns>The rows updated and the unmatched rows' ids, once the statement ran.</returns>
        public static BatchResult<DeliveryUpdateResult> DeliveryUpdate(DataBatch batch, IReadOnlyList<EmailDeliveryEventList> events)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(events);
            if (events.Count == 0)
            {
                throw new ArgumentException("Apply at least one delivery event.", nameof(events));
            }

            BatchResult<DeliveryUpdateResult> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameter: one event per row ---
                string values = draft.Table(events);

                // --- The monotonic update, its count, and the references that match no row ---
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    UPDATE [e]
                    SET [DeliveryStatus] = [v].[Status], [DeliveryAt] = COALESCE([v].[Timestamp], SYSUTCDATETIME()),
                        [DeliveryRecipient] = [v].[Recipient], [DeliveryReason] = [v].[Reason],
                        [LastProviderEventId] = [v].[ProviderEventId]
                    FROM {OutgoingEmails.Delimited} AS [e] INNER JOIN {values} AS [v] ON [v].[OutgoingEmailId] = [e].[Id]
                    WHERE ([e].[LastProviderEventId] IS NULL OR [e].[LastProviderEventId] <> [v].[ProviderEventId])
                      AND ([e].[DeliveryStatus] IS NULL
                           OR {RankOf("[e].[DeliveryStatus]")} < [v].[Rank]
                           OR ([e].[DeliveryStatus] = [v].[Status] AND [e].[DeliveryAt] < [v].[Timestamp]));
                    SELECT @@ROWCOUNT AS [Applied];
                    SELECT [v].[OutgoingEmailId] FROM {values} AS [v]
                    WHERE NOT EXISTS (SELECT 1 FROM {OutgoingEmails.Delimited} AS [e] WHERE [e].[Id] = [v].[OutgoingEmailId]);
                    """);

                // --- The reader: the count, then the unmatched ids ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: EmailWrites, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader counted = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await counted.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("The delivery update returned no count.");
                    }

                    int applied = counted.GetInt32(0);
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<int> unmatched = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        unmatched.Add(set.GetInt32(0));
                    }

                    cursor.CountRows(1 + unmatched.Count);
                    cursor.Complete(result, new DeliveryUpdateResult(applied, unmatched));
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
        ///     Appends one retention page's read: the ids of up to a page of rows created longer ago
        ///     than the retention, measured on the database clock, that are settled, or unsettled with
        ///     no job that may still run them, in id order.
        /// </summary>
        /// <param name="batch">The read batch.</param>
        /// <param name="pageSize">The most ids the page reads.</param>
        /// <param name="retention">How long a row is kept.</param>
        /// <returns>The ids, once the statement ran.</returns>
        public static BatchResult<IReadOnlyList<int>> RetentionPage(DataBatch batch, int pageSize, TimeSpan retention)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

            BatchResult<IReadOnlyList<int>> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters: the page size, then the retention in seconds ---
                string size = draft.Scalar(SqlDbType.Int, pageSize);
                string seconds = draft.Scalar(SqlDbType.Int, (int)Math.Clamp((long)retention.TotalSeconds, 0, int.MaxValue));

                // --- The page: settled rows, and unsettled rows whose job is gone or no longer live ---
                // The settled states seek IX_OutgoingEmails_State on (State, CreatedAt); the unsettled
                // rows are few, and each is judged by its job's status, as the delete's precondition is.
                string text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    SELECT TOP ({size}) [e].[Id]
                    FROM {OutgoingEmails.Delimited} AS [e]
                    WHERE [e].[CreatedAt] < DATEADD(second, -{seconds}, SYSUTCDATETIME())
                      AND ([e].[State] IN ({SettledStates})
                           OR ([e].[State] IN ({UnsettledStates})
                               AND NOT EXISTS (SELECT 1 FROM {JobStatements.Jobs.Delimited} AS [j] WHERE [j].[Id] = [e].[JobId] AND [j].[Status] IN ({LiveJobStatuses}))))
                    ORDER BY [e].[Id];
                    """);

                // --- The reader: the ids ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, writes: null, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    List<int> ids = [];
                    while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ids.Add(set.GetInt32(0));
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

        /// <summary>The rank of a stored delivery status, in the order of <see cref="EmailDeliveryStatus" />'s values.</summary>
        /// <param name="column">The status column.</param>
        /// <returns>The expression.</returns>
        private static string RankOf(string column)
        {
            string cases = string.Join(" ", Enum.GetValues<EmailDeliveryStatus>().Select(static status => string.Create(
                CultureInfo.InvariantCulture,
                $"WHEN '{status}' THEN {(int)status}")));
            return $"CASE {column} {cases} END";
        }

        /// <summary>The literals of a set of names, for an <c>IN</c> list.</summary>
        /// <param name="names">The names.</param>
        /// <returns>The list's text.</returns>
        private static string Literals(params string[] names)
        {
            return string.Join(", ", names.Select(static name => $"'{name}'"));
        }
    }

    /// <summary>One attachment of a row the sending job loads, with what the store needs to read it.</summary>
    /// <param name="OutgoingEmailId">The row it belongs to.</param>
    /// <param name="FileName">The name the recipient sees.</param>
    /// <param name="ContentId">The inline content id; null for an ordinary attachment.</param>
    /// <param name="StorageKey">The blob's object name in the store; null when its blob row is gone.</param>
    /// <param name="ContentType">The blob's content type; null when its blob row is gone.</param>
    /// <param name="Size">The blob's size in bytes; null when its blob row is gone.</param>
    internal sealed record DispatchAttachment(int OutgoingEmailId, string FileName, string? ContentId, string? StorageKey, string? ContentType, long? Size);

    /// <summary>One row the settle changed.</summary>
    /// <param name="Id">The row's id.</param>
    /// <param name="State">The state it settled in.</param>
    /// <param name="Latency">For a sent or sandboxed row, how long it waited from its enqueue to its send; else null.</param>
    internal sealed record SettledEmail(int Id, OutgoingEmailState State, TimeSpan? Latency);

    /// <summary>What one delivery update did.</summary>
    /// <param name="Applied">The rows the update moved to a stronger or later event.</param>
    /// <param name="Unmatched">The ids of the events' rows that do not exist.</param>
    internal sealed record DeliveryUpdateResult(int Applied, IReadOnlyList<int> Unmatched);
}
