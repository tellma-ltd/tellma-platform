// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Security;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>
    ///     One channel's resolved settings — the flattened view the sender works from, with the
    ///     sandbox channel's fallbacks already applied so no fallback logic survives on the send path.
    /// </summary>
    /// <param name="Host">The host to submit through.</param>
    /// <param name="Port">The port to submit on.</param>
    /// <param name="SecureSocket">How TLS is negotiated.</param>
    /// <param name="Username">The submission user name, or null for an anonymous relay.</param>
    /// <param name="Password">The submission password.</param>
    /// <param name="From">The sender used when a message carries none.</param>
    /// <param name="TimeoutSeconds">The socket read/write timeout.</param>
    internal sealed record SmtpChannelSettings(
        string Host,
        int Port,
        SecureSocketOptions SecureSocket,
        string? Username,
        string? Password,
        EmailAddress From,
        int TimeoutSeconds);
}
