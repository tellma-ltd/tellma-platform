// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Blobs;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Outbox
{
    // The statements are spec 0022 §5's dispatch, appended on the batch's internal channel (spec 0022
    // §1.4): the load reads the attachments of the claim's pending rows joined to core.Blobs, whose
    // storage key Queryex never exposes; the pre-mark and the settle write core.OutgoingEmails, which
    // the outbox's stack owns, from outside its pipeline, and leave the audit columns alone. Both
    // writes are idempotent: the pre-mark answers the end state and the settle matches only rows
    // still sending, so the executor's re-run of either returns or changes what the first run did.
    /// <summary>The fixed statements of the outgoing emails' sending job.</summary>
    internal static class OutboxStatements
    {
        /// <summary>The outgoing emails' table.</summary>
        public static TableName OutgoingEmails { get; } = new("core", "OutgoingEmails");

        /// <summary>The outgoing emails' attachments' table.</summary>
        public static TableName OutgoingEmailAttachments { get; } = new("core", "OutgoingEmailAttachments");

        /// <summary>The table the pre-mark and the settle write.</summary>
        private static readonly FrozenSet<TableName> EmailWrites = new[] { OutgoingEmails }.ToFrozenSet();

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
}
