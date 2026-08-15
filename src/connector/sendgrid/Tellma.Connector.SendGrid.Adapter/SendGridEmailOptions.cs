// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>The SendGrid transport's configuration, bound from the <c>Email:SendGrid</c> section.</summary>
    /// <remarks>
    ///     There is no sandbox subsection by design, not by omission: SendGrid's sandbox channel is a
    ///     per-request mode on the same credentials, so it needs no configuration of its own.
    /// </remarks>
    public sealed class SendGridEmailOptions
    {
        /// <summary>The configuration section these options bind from.</summary>
        public const string SectionName = "Email:SendGrid";

        /// <summary>The API key, supplied by the host's secret store. Required.</summary>
        public string? ApiKey { get; set; }

        /// <summary>The sender used when a message carries none. Required.</summary>
        public EmailAddressOptions From { get; } = new EmailAddressOptions();

        /// <summary>
        ///     How many mail-send requests a batch issues at once. Eight by default, well inside the
        ///     endpoint's documented ceiling.
        /// </summary>
        public int MaxConcurrency { get; set; } = 8;

        /// <summary>The per-request timeout, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>The event-webhook settings.</summary>
        public SendGridWebhookOptions Webhook { get; } = new SendGridWebhookOptions();
    }

    /// <summary>The <c>Email:SendGrid:Webhook</c> subsection.</summary>
    public sealed class SendGridWebhookOptions
    {
        /// <summary>
        ///     The public keys inbound event deliveries are verified against. More than one is
        ///     accepted so a key can be rotated without dropping events; the receiver is registered
        ///     only when this list is non-empty, and adding the first key takes a restart.
        /// </summary>
        public IList<string> VerificationKeys { get; } = [];
    }
}
