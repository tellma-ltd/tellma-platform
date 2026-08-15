// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Security;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>
    ///     The <c>Email:Smtp:Sandbox</c> subsection: an independent SMTP endpoint for mail the
    ///     sandbox policy must not really deliver.
    /// </summary>
    /// <remarks>
    ///     Everything except the sender is configured separately from the live channel, because the
    ///     trap is a different host with its own credentials. The sender falls back to the live
    ///     <c>From</c>, which is almost always what a trap should show.
    /// </remarks>
    public sealed class SmtpChannelOptions
    {
        /// <summary>The mail trap's host. Required when this section is present.</summary>
        public string? Host { get; set; }

        /// <summary>The trap's port; 587 by default.</summary>
        public int Port { get; set; } = 587;

        /// <summary>
        ///     How TLS is negotiated with the trap. Keeps the fail-closed default; a local Mailpit
        ///     needs an explicit <c>None</c>.
        /// </summary>
        public SecureSocketOptions SecureSocket { get; set; } = SecureSocketOptions.StartTls;

        /// <summary>The trap's user name, when it authenticates.</summary>
        public string? Username { get; set; }

        /// <summary>The trap's password, supplied by the host's secret store.</summary>
        public string? Password { get; set; }

        /// <summary>An override for the sender; falls back to the live channel's when absent.</summary>
        public EmailAddressOptions? From { get; set; }

        /// <summary>The socket read/write timeout, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;
    }
}
