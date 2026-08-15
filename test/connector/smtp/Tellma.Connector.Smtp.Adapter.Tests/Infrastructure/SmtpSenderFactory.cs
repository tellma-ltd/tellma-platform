// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Email;
using Tellma.Testing.Support.Options;

namespace Tellma.Connector.Smtp.Adapter.Tests.Infrastructure
{
    /// <summary>Builds SMTP senders pointed at an in-process server.</summary>
    internal static class SmtpSenderFactory
    {
        /// <summary>Builds options for a plain, unauthenticated loopback endpoint.</summary>
        /// <param name="port">The server's port.</param>
        /// <param name="username">A user name, when the test wants AUTH exercised.</param>
        /// <param name="secureSocket">The TLS mode; none by default, matching a local trap.</param>
        /// <returns>The options.</returns>
        public static SmtpEmailOptions Options(
            int port,
            string? username = null,
            SecureSocketOptions secureSocket = SecureSocketOptions.None)
        {
            SmtpEmailOptions options = new()
            {
                Host = "localhost",
                Port = port,
                SecureSocket = secureSocket,
                Username = username,
                Password = username is null ? null : "secret",
                TimeoutSeconds = 10,
            };

            options.From.Address = "no-reply@tellma.com";
            options.From.DisplayName = "Tellma";
            return options;
        }

        /// <summary>Builds a sender over the given options and client factory.</summary>
        /// <param name="options">The transport options.</param>
        /// <param name="clientFactory">The client seam; the default MailKit factory when null.</param>
        /// <param name="channel">Which channel the sender serves.</param>
        /// <returns>The sender.</returns>
        public static IEmailSender Sender(
            SmtpEmailOptions options,
            ISmtpClientFactory? clientFactory = null,
            SmtpChannel channel = SmtpChannel.Live)
        {
            return new SmtpEmailSender(
                channel,
                new StaticOptionsMonitor<SmtpEmailOptions>(options),
                clientFactory ?? new SmtpClientFactory(),
                NullLogger<SmtpEmailSender>.Instance);
        }

        /// <summary>Builds a message with a given subject.</summary>
        /// <param name="subject">The subject, which the scripted store keys on.</param>
        /// <returns>The message.</returns>
        public static EmailMessage Message(string subject)
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = subject,
                TextBody = "Body",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
