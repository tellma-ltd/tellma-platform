// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Security;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>The SMTP transport's configuration, bound from the <c>Email:Smtp</c> section.</summary>
    public sealed class SmtpEmailOptions
    {
        /// <summary>The configuration section these options bind from.</summary>
        public const string SectionName = "Email:Smtp";

        /// <summary>The smarthost to submit through. Required.</summary>
        public string? Host { get; set; }

        /// <summary>The submission port; 587 by default.</summary>
        public int Port { get; set; } = 587;

        /// <summary>
        ///     How TLS is negotiated. Defaults to <see cref="SecureSocketOptions.StartTls" />:
        ///     mandatory TLS that fails the connection when the server cannot upgrade, which is the
        ///     fail-closed default for credentialed submission. The opportunistic modes are an
        ///     explicit choice for legacy relays.
        /// </summary>
        public SecureSocketOptions SecureSocket { get; set; } = SecureSocketOptions.StartTls;

        /// <summary>The submission user name; absent for the anonymous relays common on-prem.</summary>
        public string? Username { get; set; }

        /// <summary>The submission password, supplied by the host's secret store.</summary>
        public string? Password { get; set; }

        /// <summary>
        ///     The sender used when a message carries none — the overwhelmingly common case, since
        ///     the sending identity is a deployment concern rather than a business one. Required.
        /// </summary>
        public EmailAddressOptions From { get; } = new EmailAddressOptions();

        /// <summary>The socket read/write timeout, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        ///     An optional mail trap that receives a sandbox tenant's external mail — a staging
        ///     Mailpit, an internal catch-all relay — so it is inspectable instead of delivered.
        ///     When absent, the transport declares no sandbox channel and the pipeline withholds
        ///     that mail entirely.
        /// </summary>
        public SmtpChannelOptions? Sandbox { get; set; }
    }
}
