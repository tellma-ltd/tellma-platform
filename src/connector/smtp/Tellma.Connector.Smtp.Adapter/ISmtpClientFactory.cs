// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Net.Smtp;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>
    ///     Creates the MailKit client the sender submits through — one per send call, since the
    ///     client is not thread-safe and holds a single connection.
    /// </summary>
    /// <remarks>
    ///     The seam exists so the protocol suite can drive the sender against a client it controls:
    ///     a certificate-validation override for the STARTTLS profile, and a scripted client for the
    ///     mid-batch reconnect path, which is otherwise timing-sensitive to reproduce.
    /// </remarks>
    internal interface ISmtpClientFactory
    {
        /// <summary>Creates a disconnected client.</summary>
        /// <returns>A client the caller owns and disposes.</returns>
        ISmtpClient Create();
    }

    /// <summary>The production factory: a plain MailKit client.</summary>
    internal sealed class SmtpClientFactory : ISmtpClientFactory
    {
        /// <inheritdoc />
        public ISmtpClient Create()
        {
            return new SmtpClient();
        }
    }
}
