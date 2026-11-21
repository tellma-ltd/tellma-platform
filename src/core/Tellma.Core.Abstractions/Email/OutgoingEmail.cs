// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     A row of <c>core.OutgoingEmails</c>: one rendered message to one or more addresses, written in
    ///     the transaction of the change that produced it and sent afterwards by its own background job
    ///     as the system user. The row records exactly what was sent and where the send stands.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rows arrive only through <see cref="IEmailOutbox" />, which enlists them with the
    ///         producer's frame; the stack serves reads and deletes, never a save. The envelope, the
    ///         bodies and the regarding record are written once, on insert; the state, the send's
    ///         result and the delivery columns are server-owned, written only by the sending job and
    ///         the delivery events.
    ///     </para>
    ///     <para>
    ///         Addresses are stored as RFC 5322 address lists (<c>"Name" &lt;address&gt;</c>, comma
    ///         separated), readable in a grid and filterable with <c>contains</c>. The bodies, the
    ///         copies and the provider's identifiers are left out of the default projection and stay
    ///         selectable.
    ///     </para>
    /// </remarks>
    [Table("OutgoingEmails", Schema = "core")]
    [TableType]
    [ApiResource(Mcp = McpExposure.ReadOnly)]
    [Stack(Operations = StackOperations.Query | StackOperations.Details | StackOperations.Delete | StackOperations.Enlist, DefaultOrderBy = "CreatedAt desc")]
    [DisplayNameSource(nameof(Subject))]
    [DefaultSelect(DefaultSelectText)]
    public sealed class OutgoingEmail : TopLevelEntity, IJobEntity
    {
        /// <summary>The width of a single address, <c>nvarchar(512)</c>: <see cref="From" /> and <see cref="ReplyTo" />.</summary>
        public const int AddressLength = 512;

        /// <summary>The width of <see cref="Subject" />, <c>nvarchar(500)</c>.</summary>
        public const int SubjectLength = 500;

        /// <summary>The width of <see cref="RegardingEntity" />, <c>varchar(128)</c>: a securable resource's.</summary>
        public const int RegardingEntityLength = 128;

        /// <summary>The width of <see cref="ProviderMessageId" />, <c>nvarchar(255)</c>.</summary>
        public const int ProviderMessageIdLength = 255;

        /// <summary>The width of <see cref="ErrorCode" />, <c>nvarchar(64)</c>.</summary>
        public const int ErrorCodeLength = 64;

        /// <summary>The width of <see cref="ErrorDetail" />, <c>nvarchar(1024)</c>.</summary>
        public const int ErrorDetailLength = 1024;

        /// <summary>The width of <see cref="DeliveryRecipient" />, <c>nvarchar(255)</c>.</summary>
        public const int DeliveryRecipientLength = 255;

        /// <summary>The width of <see cref="DeliveryReason" />, <c>nvarchar(512)</c>.</summary>
        public const int DeliveryReasonLength = 512;

        /// <summary>The width of <see cref="LastProviderEventId" />, <c>nvarchar(100)</c>.</summary>
        public const int ProviderEventIdLength = 100;

        /// <summary>The most files one email attaches.</summary>
        public const int MaxAttachments = 20;

        /// <summary>The default projection: every scalar but the bodies, the copies and the provider's identifiers.</summary>
        private const string DefaultSelectText =
            "Id,JobId,State,Audience,From,ReplyTo,To,Subject,RecipientUserId,RegardingEntity,RegardingId,SendAfter,"
            + "AttemptCount,SentAt,ExpectsDeliveryEvents,ErrorCode,ErrorDetail,"
            + "DeliveryStatus,DeliveryAt,DeliveryRecipient,DeliveryReason,"
            + "CreatedAt,CreatedById,ModifiedAt,ModifiedById";

        /// <inheritdoc />
        /// <remarks>Set when the row's sending job is enqueued, in the same transaction as the row.</remarks>
        public int? JobId { get; set; }

        /// <summary>Where the email stands; server-owned, <c>varchar(9)</c>. Every row is inserted <see cref="OutgoingEmailState.Pending" />.</summary>
        [ServerOwned]
        public OutgoingEmailState State { get; set; }

        // --- The envelope and the bodies, written once ---

        /// <summary>Whose inbox the email targets, which the sandbox routing reads; write-once, <c>varchar(8)</c>.</summary>
        [WriteOnce]
        public EmailAudience Audience { get; set; }

        /// <summary>The sender as <c>"Name" &lt;address&gt;</c>, <c>nvarchar(512)</c>; null for the transport's configured default. Write-once.</summary>
        [WriteOnce]
        [MaxLength(AddressLength)]
        public string? From { get; set; }

        /// <summary>The reply-to address as <c>"Name" &lt;address&gt;</c>, <c>nvarchar(512)</c>; null for none. Write-once.</summary>
        [WriteOnce]
        [MaxLength(AddressLength)]
        public string? ReplyTo { get; set; }

        /// <summary>The primary recipients as an address list, <c>nvarchar(max)</c>; never empty. Write-once.</summary>
        [WriteOnce]
        public string To { get; set; } = null!;

        /// <summary>The carbon-copy recipients as an address list, <c>nvarchar(max)</c>; null for none. Write-once.</summary>
        [WriteOnce]
        public string? Cc { get; set; }

        /// <summary>The blind-carbon-copy recipients as an address list, <c>nvarchar(max)</c>; null for none. Write-once.</summary>
        [WriteOnce]
        public string? Bcc { get; set; }

        /// <summary>The rendered subject, <c>nvarchar(500)</c>. Write-once.</summary>
        [WriteOnce]
        [MaxLength(SubjectLength)]
        public string Subject { get; set; } = null!;

        /// <summary>The rendered plain-text body, <c>nvarchar(max)</c>. Write-once.</summary>
        [WriteOnce]
        public string TextBody { get; set; } = null!;

        /// <summary>The rendered HTML body, <c>nvarchar(max)</c>; null for a text-only email. Write-once.</summary>
        [WriteOnce]
        public string? HtmlBody { get; set; }

        /// <summary>The member the email is addressed to, when there is one; navigation <c>RecipientUser</c>. Write-once.</summary>
        [WriteOnce]
        public int? RecipientUserId { get; set; }

        /// <summary>The resource of the business record the email concerns, such as <c>acme.SalesInvoice</c>, <c>varchar(128)</c>; null for none. Write-once.</summary>
        [WriteOnce]
        [Column(TypeName = "varchar(128)")]
        public string? RegardingEntity { get; set; }

        /// <summary>The id of the business record the email concerns; null for none. Write-once.</summary>
        [WriteOnce]
        public long? RegardingId { get; set; }

        /// <summary>The earliest instant the email may be sent, <c>datetimeoffset(3)</c>; null for as soon as the worker runs. Write-once.</summary>
        [WriteOnce]
        public DateTimeOffset? SendAfter { get; set; }

        // --- The send's result, server-owned ---

        /// <summary>How many claims of the row began sending it; server-owned, 0 on insert. It survives the job's retention.</summary>
        [ServerOwned]
        public int AttemptCount { get; set; }

        /// <summary>When the provider accepted the message, or the sandbox took it, <c>datetimeoffset(3)</c>; server-owned.</summary>
        [ServerOwned]
        public DateTimeOffset? SentAt { get; set; }

        /// <summary>The provider's id of the accepted message, <c>nvarchar(255)</c>; server-owned.</summary>
        [ServerOwned]
        [MaxLength(ProviderMessageIdLength)]
        public string? ProviderMessageId { get; set; }

        /// <summary>Whether the provider reports delivery events for the message; server-owned, false on insert.</summary>
        [ServerOwned]
        public bool ExpectsDeliveryEvents { get; set; }

        /// <summary>Why the row was refused or failed, an <c>Email.*</c> code, <c>nvarchar(64)</c>; server-owned.</summary>
        [ServerOwned]
        [MaxLength(ErrorCodeLength)]
        public string? ErrorCode { get; set; }

        /// <summary>The provider's text about the refusal or failure, <c>nvarchar(1024)</c>; server-owned.</summary>
        [ServerOwned]
        [MaxLength(ErrorDetailLength)]
        public string? ErrorDetail { get; set; }

        // --- The delivery events, server-owned ---

        /// <summary>The strongest delivery status reported so far, <c>varchar(12)</c>; server-owned.</summary>
        [ServerOwned]
        public EmailDeliveryStatus? DeliveryStatus { get; set; }

        /// <summary>When the event behind <see cref="DeliveryStatus" /> happened, <c>datetimeoffset(3)</c>; server-owned.</summary>
        [ServerOwned]
        public DateTimeOffset? DeliveryAt { get; set; }

        /// <summary>The address that event concerned, <c>nvarchar(255)</c>; server-owned.</summary>
        [ServerOwned]
        [MaxLength(DeliveryRecipientLength)]
        public string? DeliveryRecipient { get; set; }

        /// <summary>The provider's reason given with that event, <c>nvarchar(512)</c>; server-owned.</summary>
        [ServerOwned]
        [MaxLength(DeliveryReasonLength)]
        public string? DeliveryReason { get; set; }

        /// <summary>The provider's id of the last applied event, <c>nvarchar(100)</c>, which skips its redelivery; server-owned.</summary>
        [ServerOwned]
        [MaxLength(ProviderEventIdLength)]
        public string? LastProviderEventId { get; set; }

        /// <summary>The files attached to the email, at most 20, each a staged blob confirmed with the row; null on a row loaded without them.</summary>
        [NotMapped]
        [MaxChildren(MaxAttachments)]
        public IReadOnlyList<OutgoingEmailAttachment>? OutgoingEmailAttachments { get; set; }
    }
}
