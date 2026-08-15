// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The Development transport: writes every message to <see cref="ILogger" /> instead of
    ///     sending it. Registered as the <c>log-sink</c> transport and used by default in
    ///     Development, so a fresh clone sends mail without any configuration, and end-to-end suites
    ///     can read codes and links out of the log.
    /// </summary>
    /// <remarks>
    ///     This is the one place in the platform where full message content is logged — that is the
    ///     entire point of the sink — which is exactly why it may only run in Development. Two guards
    ///     enforce that: the pipeline fails startup when <c>log-sink</c> is the active provider
    ///     outside Development, and this constructor refuses to build. The second exists for a host
    ///     that hand-registers the sink and thereby leaves the pipeline; it is the last tripwire.
    ///     Staging is deliberately included in the ban — it exists to be a faithful replica of
    ///     production, which a log sink is not; a staging deployment points a real transport at a
    ///     mail trap instead.
    /// </remarks>
    public sealed class LogSinkEmailSender : IEmailSender
    {
        private readonly ILogger<LogSinkEmailSender> _logger;
        private readonly EmailChannel _channel;

        /// <summary>Creates the sink, refusing to run outside Development.</summary>
        /// <param name="environment">The host environment, consulted by the guard.</param>
        /// <param name="logger">Where messages are written.</param>
        /// <param name="channel">Which channel this instance stands in for; named in every log line
        ///     so a developer can tell sandbox-tenant mail apart from ordinary mail.</param>
        /// <exception cref="InvalidOperationException">The host environment is not Development.</exception>
        public LogSinkEmailSender(IHostEnvironment environment, ILogger<LogSinkEmailSender> logger, EmailChannel channel)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(logger);

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"The development log-sink email transport cannot run in the '{environment.EnvironmentName}' environment, where it would silently discard real mail. Configure Email:Provider to a real transport.");
            }

            _logger = logger;
            _channel = channel;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();

            List<EmailSendResult> results = new(messages.Count);
            string channel = EmailDiagnostics.ToTagValue(_channel);

            foreach (EmailMessage message in messages)
            {
                // The sink is bound by the same batch contract as every other transport, so a
                // structurally invalid message is rejected on its own rather than logged as sent.
                if (!EmailMessageValidation.TryValidate(message, out string? error))
                {
                    results.Add(new EmailSendResult(EmailSendOutcome.Rejected, Error: error));
                    continue;
                }

                // Guarded because formatting the recipient, attachment, and body detail is the
                // expensive part of the sink, and there is no point paying it when nobody listens.
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    string to = FormatAddresses(message.To);
                    string cc = FormatAddresses(message.Cc);
                    string bcc = FormatAddresses(message.Bcc);
                    string audience = EmailDiagnostics.ToTagValue(message.Audience);
                    string correlation = message.Correlation?.ToString() ?? EmailTelemetryNames.NoOwner;
                    string attachments = FormatAttachments(message.Attachments);
                    int htmlLength = message.HtmlBody?.Length ?? 0;

                    LogSinkEmailSenderLog.MessageSunk(
                        _logger, channel, to, cc, bcc, message.Subject, audience, correlation,
                        message.TextBody, htmlLength, attachments);
                }

                results.Add(new EmailSendResult(EmailSendOutcome.Sent));
            }

            return Task.FromResult<IReadOnlyList<EmailSendResult>>(results);
        }

        private static string FormatAddresses(IReadOnlyList<EmailAddress> addresses)
        {
            return addresses.Count == 0
                ? "-"
                : string.Join(", ", addresses.Select(static a =>
                    string.IsNullOrWhiteSpace(a.DisplayName) ? a.Address : $"{a.DisplayName} <{a.Address}>"));
        }

        private static string FormatAttachments(IReadOnlyList<EmailAttachment> attachments)
        {
            if (attachments.Count == 0)
            {
                return "-";
            }

            // Names and sizes only. Attachment bytes are never logged, at any level.
            StringBuilder builder = new();
            foreach (EmailAttachment attachment in attachments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(CultureInfo.InvariantCulture, $"{attachment.FileName} ({attachment.ContentType}, {attachment.Content.Length} bytes)");
            }

            return builder.ToString();
        }
    }

    /// <summary>Source-generated log messages for <see cref="LogSinkEmailSender" />.</summary>
    internal static partial class LogSinkEmailSenderLog
    {
        /// <summary>A message was written to the sink instead of being sent.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="channel">The channel this sink instance stood in for.</param>
        /// <param name="to">The primary recipients.</param>
        /// <param name="cc">The carbon-copy recipients.</param>
        /// <param name="bcc">The blind-carbon-copy recipients.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="audience">Whose inbox the message targeted.</param>
        /// <param name="correlation">The delivery-event correlation, or "none".</param>
        /// <param name="textBody">The full plain-text body — the sink's entire purpose.</param>
        /// <param name="htmlBodyLength">The length of the HTML body, which is not itself logged.</param>
        /// <param name="attachments">Attachment names, content types, and sizes; never their bytes.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "EMAIL SINK channel={Channel} to={To} cc={Cc} bcc={Bcc} audience={Audience} correlation={Correlation} attachments={Attachments} htmlLength={HtmlBodyLength} subject={Subject} body={TextBody}")]
        public static partial void MessageSunk(
            ILogger logger,
            string channel,
            string to,
            string cc,
            string bcc,
            string subject,
            string audience,
            string correlation,
            string textBody,
            int htmlBodyLength,
            string attachments);
    }
}
