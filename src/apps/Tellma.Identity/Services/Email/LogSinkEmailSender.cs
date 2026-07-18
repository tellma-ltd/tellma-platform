// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     The Development email sink: writes each message — including sign-in codes and
    ///     invitation links — to the log, where developers and end-to-end tests read them. Never
    ///     registered outside the sink opt-in.
    /// </summary>
    /// <param name="logger">The sink target.</param>
    public sealed class LogSinkEmailSender(ILogger<LogSinkEmailSender> logger) : IEmailSender
    {
        /// <inheritdoc />
        public Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            foreach (EmailMessage message in messages)
            {
                LogSinkEmailSenderLog.MessageSunk(logger, message.ToEmail, message.Subject, message.TextBody);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Source-generated log messages for <see cref="LogSinkEmailSender" />.</summary>
    internal static partial class LogSinkEmailSenderLog
    {
        /// <summary>A message was written to the sink instead of being sent.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="to">The recipient.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="body">The full text body (contains the code or link).</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "EMAIL SINK to={To} subject={Subject} body={Body}")]
        public static partial void MessageSunk(ILogger logger, string to, string subject, string body);
    }
}
