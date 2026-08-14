// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>
    ///     The ACS Email transport's configuration, bound from the <c>Email:AcsEmail</c> section.
    /// </summary>
    /// <remarks>
    ///     There is deliberately no credential here. The client authenticates with a
    ///     <c>TokenCredential</c> — the App Service's managed identity in Azure, the developer's own
    ///     credential locally — so ACS's access keys are left unused and there is no mail secret to
    ///     store or rotate. There is also no sandbox subsection: ACS offers no validate-only mode, so
    ///     the transport declares no sandbox channel and the pipeline withholds a sandbox tenant's
    ///     external mail itself.
    /// </remarks>
    public sealed class AcsEmailOptions
    {
        /// <summary>The configuration section these options bind from.</summary>
        public const string SectionName = "Email:AcsEmail";

        /// <summary>The Communication Services resource endpoint. Required.</summary>
        public Uri? Endpoint { get; set; }

        /// <summary>
        ///     The sender used when a message carries none. Must be an address on a domain the
        ///     resource is provisioned for. Required.
        /// </summary>
        /// <remarks>
        ///     Address only: a configured <see cref="EmailAddressOptions.DisplayName" /> fails
        ///     startup validation, because ACS validates the sender against a MailFrom address on the
        ///     domain resource and refuses anything carrying a display name. The sender's display
        ///     name belongs on that MailFrom address instead.
        /// </remarks>
        public EmailAddressOptions From { get; } = new EmailAddressOptions();

        /// <summary>How many send requests a batch issues at once.</summary>
        public int MaxConcurrency { get; set; } = 8;

        /// <summary>The per-request timeout, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>The Event Grid receiver's settings.</summary>
        public AcsEmailWebhookOptions Webhook { get; } = new AcsEmailWebhookOptions();
    }

    /// <summary>The <c>Email:AcsEmail:Webhook</c> subsection.</summary>
    public sealed class AcsEmailWebhookOptions
    {
        /// <summary>
        ///     The <c>?token=</c> values accepted on the Event Grid subscription URL. Event Grid does
        ///     not sign its deliveries, so this shared secret is what authenticates them; more than
        ///     one is accepted so a token can be rotated without dropping events. The receiver is
        ///     registered only when this list is non-empty, and adding the first token takes a
        ///     restart.
        /// </summary>
        public IList<string> Tokens { get; } = [];
    }
}
