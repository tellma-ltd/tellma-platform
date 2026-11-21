// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     One email a producer hands the outbox: the rendered message, the blobs it attaches by
    ///     reference, and what the row records about it.
    /// </summary>
    /// <remarks>
    ///     The outbox stages the message's own <see cref="EmailMessage.Attachments" /> as blobs and
    ///     ignores its <see cref="EmailMessage.Correlation" />: the sending job correlates every message
    ///     with its row.
    /// </remarks>
    /// <param name="Message">The rendered message.</param>
    /// <param name="BlobAttachments">The blobs the email attaches by reference, after the message's own attachments; null for none.</param>
    /// <param name="RecipientUserId">The member the email is addressed to, when there is one.</param>
    /// <param name="RegardingEntity">The resource of the business record the email concerns, such as <c>acme.SalesInvoice</c>; null for none.</param>
    /// <param name="RegardingId">The id of the business record the email concerns; null for none.</param>
    /// <param name="SendAfter">The earliest instant the email may be sent; null for as soon as the worker runs.</param>
    /// <param name="Source">Who produced the email, the source the outbox's enqueue counter is tagged with: <c>api</c> by default, <c>notification</c> for the email notification channel.</param>
    public sealed record EmailEnqueueRequest(
        EmailMessage Message,
        IReadOnlyList<EmailBlobAttachment>? BlobAttachments = null,
        int? RecipientUserId = null,
        string? RegardingEntity = null,
        long? RegardingId = null,
        DateTimeOffset? SendAfter = null,
        string Source = EmailEnqueueRequest.ApiSource)
    {
        /// <summary>The source of an email a feature's own code produced.</summary>
        public const string ApiSource = "api";

        /// <summary>The source of an email the email notification channel produced.</summary>
        public const string NotificationSource = "notification";
    }
}
