// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>One outgoing email, transport-agnostic.</summary>
    public sealed record EmailMessage
    {
        /// <summary>The sender; null uses the configured default of the registered transport.</summary>
        public EmailAddress? From { get; init; }

        /// <summary>The reply-to address, when replies should divert from <see cref="From" />.</summary>
        public EmailAddress? ReplyTo { get; init; }

        /// <summary>The primary recipients. At least one is required.</summary>
        public required IReadOnlyList<EmailAddress> To { get; init; }

        /// <summary>The carbon-copy recipients.</summary>
        public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

        /// <summary>The blind-carbon-copy recipients.</summary>
        public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

        /// <summary>The localized subject.</summary>
        public required string Subject { get; init; }

        /// <summary>
        ///     The plain-text body. Always present: text-only mail is fully valid, and HTML-only
        ///     mail hurts deliverability.
        /// </summary>
        public required string TextBody { get; init; }

        /// <summary>
        ///     The optional HTML body, sent as a multipart alternative to <see cref="TextBody" />.
        /// </summary>
        public string? HtmlBody { get; init; }

        /// <summary>The attachments, if any.</summary>
        public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];

        /// <summary>
        ///     Whose inbox this email targets. Drives the sandbox-tenant routing policy; required so
        ///     the classification is a deliberate act at every send site.
        /// </summary>
        public required EmailAudience Audience { get; init; }

        /// <summary>
        ///     The delivery-event correlation, minted by the owner of the email's state (the outbox
        ///     for outbox mail, identity for identity mail); null for fire-and-forget mail that
        ///     expects no delivery events.
        /// </summary>
        public EmailCorrelation? Correlation { get; init; }
    }
}
