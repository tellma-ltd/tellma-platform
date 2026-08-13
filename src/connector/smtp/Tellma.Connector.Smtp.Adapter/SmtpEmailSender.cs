// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Globalization;
using System.Net.Sockets;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>
    ///     Submits a batch over one SMTP connection: connect, authenticate, send every message, quit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One client per call, because MailKit's client is not thread-safe and holds a single
    ///         connection — and the batch contract already amortizes the setup. There is no
    ///         connection pooling; a future throughput need can add it behind the same contract.
    ///     </para>
    ///     <para>
    ///         Delivery events are structurally impossible here: SMTP's only feedback is the
    ///         synchronous accept, which means "the smarthost took responsibility", not "delivered".
    ///         Bounces arrive out of band as messages to the return path, which nothing in this
    ///         adapter reads.
    ///     </para>
    /// </remarks>
    internal sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpChannel _channel;
        private readonly IOptionsMonitor<SmtpEmailOptions> _options;
        private readonly ISmtpClientFactory _clientFactory;
        private readonly ILogger<SmtpEmailSender> _logger;

        /// <summary>Creates a sender bound to one channel.</summary>
        /// <param name="channel">Which endpoint to submit to.</param>
        /// <param name="options">The transport's options, read fresh per batch so a credential
        ///     rotation takes effect without a restart.</param>
        /// <param name="clientFactory">Creates the MailKit client.</param>
        /// <param name="logger">Where connection detail is logged.</param>
        public SmtpEmailSender(
            SmtpChannel channel,
            IOptionsMonitor<SmtpEmailOptions> options,
            ISmtpClientFactory clientFactory,
            ILogger<SmtpEmailSender> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(clientFactory);
            ArgumentNullException.ThrowIfNull(logger);

            _channel = channel;
            _options = options;
            _clientFactory = clientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            var results = new EmailSendResult?[messages.Count];
            List<int> pending = [];

            // Structurally invalid messages are rejected on their own and never touch the wire, so
            // one malformed row cannot poison a bulk dispatch.
            for (int i = 0; i < messages.Count; i++)
            {
                if (TryDescribeInvalid(messages[i], out string? error))
                {
                    results[i] = new EmailSendResult(EmailSendOutcome.Rejected, Error: error);
                }
                else
                {
                    pending.Add(i);
                }
            }

            if (pending.Count == 0)
            {
                return Complete(results);
            }

            SmtpChannelSettings settings = SmtpEmailOptionsValidator.Resolve(_options.CurrentValue, _channel);

            // Nothing has been attempted yet, so a connect or authentication failure is exactly the
            // case where the contract permits — and prefers — an exception.
            ISmtpClient client = await ConnectAsync(settings, cancellationToken).ConfigureAwait(false);
            bool clientOwned = true;

            try
            {
                FormatOptions format = FormatOptions.Default.Clone();
                format.International = client.Capabilities.HasFlag(SmtpCapabilities.UTF8);
                if (format.International)
                {
                    SmtpEmailLog.InternationalFormatNegotiated(_logger, settings.Host);
                }

                for (int p = 0; p < pending.Count; p++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int index = pending[p];
                    MimeMessage mime = SmtpMessageBuilder.Build(messages[index], settings.From);

                    try
                    {
                        string response = await client.SendAsync(format, mime, cancellationToken).ConfigureAwait(false);
                        string messageId = SmtpMessageBuilder.GetMessageId(mime);
                        SmtpEmailLog.MessageAccepted(_logger, messageId, response);
                        results[index] = new EmailSendResult(EmailSendOutcome.Sent, messageId);
                    }
                    catch (SmtpCommandException exception)
                    {
                        results[index] = FromCommandException(exception);
                    }
                    catch (Exception exception) when (exception is SmtpProtocolException or IOException or SocketException)
                    {
                        // The connection died mid-batch. This message is genuinely ambiguous, so it
                        // is transient; the rest of the batch deserves one reconnect rather than a
                        // blanket failure.
                        results[index] = new EmailSendResult(
                            EmailSendOutcome.TransientFailure, Error: exception.Message);

                        SmtpEmailLog.ConnectionLostMidBatch(_logger, exception, settings.Host);
                        Disconnect(client);
                        clientOwned = false;

                        ISmtpClient? reconnected = await TryReconnectAsync(settings, cancellationToken)
                            .ConfigureAwait(false);
                        if (reconnected is null)
                        {
                            // Never an exception once the batch has started: the caller gets one
                            // result per message and decides what to retry.
                            for (int rest = p + 1; rest < pending.Count; rest++)
                            {
                                results[pending[rest]] = new EmailSendResult(
                                    EmailSendOutcome.TransientFailure,
                                    Error: "The SMTP connection was lost and could not be re-established.");
                            }

                            return Complete(results);
                        }

                        client = reconnected;
                        clientOwned = true;
                        format = FormatOptions.Default.Clone();
                        format.International = client.Capabilities.HasFlag(SmtpCapabilities.UTF8);
                    }
                }
            }
            finally
            {
                if (clientOwned)
                {
                    Disconnect(client);
                }
            }

            return Complete(results);
        }

        private static EmailSendResult[] Complete(EmailSendResult?[] results)
        {
            var completed = new EmailSendResult[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                completed[i] = results[i]
                    ?? new EmailSendResult(
                        EmailSendOutcome.TransientFailure, Error: "The message was not attempted.");
            }

            return completed;
        }

        private static bool TryDescribeInvalid(EmailMessage message, out string? error)
        {
            if (!EmailMessageValidation.TryValidate(message, out error))
            {
                return true;
            }

            // MailKit would throw on a syntactically invalid mailbox while building the MIME
            // message, which would take the whole batch down; rejecting the one message is the
            // behaviour the contract asks for.
            foreach (EmailAddress address in message.To.Concat(message.Cc).Concat(message.Bcc))
            {
                if (!MailboxAddress.TryParse(address.Address, out _))
                {
                    error = $"'{address.Address}' is not a valid mailbox address.";
                    return true;
                }
            }

            if (message.From is not null && !MailboxAddress.TryParse(message.From.Address, out _))
            {
                error = $"The sender '{message.From.Address}' is not a valid mailbox address.";
                return true;
            }

            if (message.ReplyTo is not null && !MailboxAddress.TryParse(message.ReplyTo.Address, out _))
            {
                error = $"The reply-to address '{message.ReplyTo.Address}' is not a valid mailbox address.";
                return true;
            }

            error = null;
            return false;
        }

        private static EmailSendResult FromCommandException(SmtpCommandException exception)
        {
            int status = (int)exception.StatusCode;

            // The SMTP reply classes map directly onto the contract: 4xx is transient-negative
            // (greylisting, throttling, mailbox busy), 5xx is permanent-negative (unknown user,
            // policy refusal, message too large).
            EmailSendOutcome outcome = status is >= 500 and < 600
                ? EmailSendOutcome.Rejected
                : EmailSendOutcome.TransientFailure;

            string mailbox = exception.Mailbox is null ? string.Empty : $" <{exception.Mailbox.Address}>";
            string error = string.Create(
                CultureInfo.InvariantCulture,
                $"{status} {exception.ErrorCode}{mailbox}: {exception.Message}");

            return new EmailSendResult(outcome, Error: error);
        }

        private async Task<ISmtpClient> ConnectAsync(
            SmtpChannelSettings settings, CancellationToken cancellationToken)
        {
            ISmtpClient client = _clientFactory.Create();
            try
            {
                // MailKit's Timeout is a socket read/write timeout in milliseconds and does not
                // cancel the awaited task, so the token is passed as well.
                client.Timeout = settings.TimeoutSeconds * 1000;
                await client.ConnectAsync(settings.Host, settings.Port, settings.SecureSocket, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(settings.Username))
                {
                    await client
                        .AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                }

                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private async Task<ISmtpClient?> TryReconnectAsync(
            SmtpChannelSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                return await ConnectAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                SmtpEmailLog.ReconnectFailed(_logger, exception, settings.Host);
                return null;
            }
        }

        private static void Disconnect(ISmtpClient client)
        {
            try
            {
                if (client.IsConnected)
                {
                    // Best effort, and never with the caller's token: a QUIT that fails or is
                    // cancelled must not change what the batch already reported.
                    client.Disconnect(quit: true, CancellationToken.None);
                }
            }
#pragma warning disable CA1031 // A failed QUIT must not change what the batch already reported.
            catch (Exception)
            {
                // Best effort only: the socket is going away either way.
            }
#pragma warning restore CA1031
            finally
            {
                client.Dispose();
            }
        }
    }
}
