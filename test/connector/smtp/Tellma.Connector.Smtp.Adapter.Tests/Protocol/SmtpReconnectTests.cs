// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using Tellma.Connector.Smtp.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter.Tests.Protocol
{
    /// <summary>
    ///     A connection that dies mid-batch, driven at the client seam rather than at the socket:
    ///     socket-level teardown timing is platform-sensitive, while the behaviour that matters — one
    ///     reconnect, the failed message reported transient and never re-sent — is exact here.
    /// </summary>
    public class SmtpReconnectTests
    {
        [Fact]
        public async Task Reconnects_once_and_finishes_the_batch()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            CountingClientFactory factory = new(failOnSend: 2);

            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port), factory);

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [
                    SmtpSenderFactory.Message("first"),
                    SmtpSenderFactory.Message("dies"),
                    SmtpSenderFactory.Message("third"),
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);

            // The message in flight when the connection died is genuinely ambiguous.
            Assert.Equal(EmailSendOutcome.TransientFailure, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[2].Outcome);

            // Exactly one reconnect, and the failed message is never re-sent — that would be the
            // adapter-level retry the contract forbids.
            Assert.Equal(2, factory.CreatedClients);
            Assert.Equal(["first", "third"], server.Store.Messages.Select(static m => m.Subject));
        }

        [Fact]
        public async Task Reports_the_remainder_transient_when_the_reconnect_fails()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();

            // The replacement client refuses to connect, which is what a smarthost that has gone
            // away looks like from here.
            CountingClientFactory factory = new(failOnSend: 2) { SecondClientCannotConnect = true };
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port), factory);

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [
                    SmtpSenderFactory.Message("first"),
                    SmtpSenderFactory.Message("dies"),
                    SmtpSenderFactory.Message("never-attempted"),
                ],
                TestContext.Current.CancellationToken);

            // Never an exception once the batch has started: the caller gets one result per message.
            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[2].Outcome);
        }

        /// <summary>Hands out clients and counts them, so "reconnected once" is a settled number.</summary>
        private sealed class CountingClientFactory(int failOnSend) : ISmtpClientFactory
        {
            /// <summary>How many clients the sender asked for.</summary>
            public int CreatedClients { get; private set; }

            /// <summary>When set, the replacement client refuses to connect.</summary>
            public bool SecondClientCannotConnect { get; init; }

            public ISmtpClient Create()
            {
                CreatedClients++;
                if (CreatedClients == 1)
                {
                    return new FaultingSmtpClient(failOnSend);
                }

                // The replacement behaves normally unless the test wants the reconnect to fail.
                return SecondClientCannotConnect ? new UnreachableSmtpClient() : new SmtpClient();
            }
        }

        /// <summary>A client whose connection attempt always fails.</summary>
        private sealed class UnreachableSmtpClient : SmtpClient
        {
            public override Task ConnectAsync(
                string host,
                int port = 0,
                MailKit.Security.SecureSocketOptions options = MailKit.Security.SecureSocketOptions.Auto,
                CancellationToken cancellationToken = default)
            {
                throw new System.Net.Sockets.SocketException(10061);
            }
        }

        /// <summary>A real client that drops the connection on its nth send.</summary>
        private sealed class FaultingSmtpClient(int failOnSend) : SmtpClient
        {
            private int _sends;

            public override Task<string> SendAsync(
                FormatOptions options,
                MimeMessage message,
                CancellationToken cancellationToken = default,
                ITransferProgress? progress = null)
            {
                _sends++;
                return _sends == failOnSend
                    ? throw new SmtpProtocolException("The connection was closed by the remote host.")
                    : base.SendAsync(options, message, cancellationToken, progress);
            }
        }
    }
}
