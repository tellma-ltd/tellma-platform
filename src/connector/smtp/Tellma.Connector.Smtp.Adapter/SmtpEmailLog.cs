// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>Source-generated log messages for the SMTP transport.</summary>
    /// <remarks>
    ///     Transport-specific detail only; the pipeline logs batch outcomes centrally. Nothing here
    ///     carries a recipient address or message content.
    /// </remarks>
    internal static partial class SmtpEmailLog
    {
        /// <summary>The smarthost accepted a message, with its free-form response.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="messageId">The message id reported as the provider message id.</param>
        /// <param name="response">The server's acceptance line, which usually carries its queue id.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SMTP accepted email message {MessageId}: {Response}")]
        public static partial void MessageAccepted(ILogger logger, string messageId, string response);

        /// <summary>The server advertised SMTPUTF8, so non-ASCII addresses pass through unmangled.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="host">The connected host.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SMTP email host {Host} advertises SMTPUTF8; sending in international format.")]
        public static partial void InternationalFormatNegotiated(ILogger logger, string host);

        /// <summary>
        ///     The connection died while a batch was in flight. What happens next is said by whichever
        ///     of the three lines below follows, so this one deliberately promises nothing: on the
        ///     drop that exhausts the batch's reconnect budget there is no reconnect to promise.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="host">The host that was connected.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The SMTP email connection to {Host} was lost mid-batch; the message in flight is reported as a transient failure.")]
        public static partial void ConnectionLostMidBatch(ILogger logger, Exception exception, string host);

        /// <summary>
        ///     The connection was lost again after the batch had already spent its one reconnect, so
        ///     the remainder is abandoned rather than reconnected message by message.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="host">The host that keeps dropping the connection.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "The SMTP email connection to {Host} was lost again after this batch's one reconnect; the remainder of the batch is reported as transient failures without reconnecting again.")]
        public static partial void ReconnectBudgetSpent(ILogger logger, string host);

        /// <summary>The single reconnect attempt failed, so the rest of the batch is transient.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="host">The host that could not be reached.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Reconnecting to the SMTP email host {Host} failed; the remainder of the batch is reported as transient failures.")]
        public static partial void ReconnectFailed(ILogger logger, Exception exception, string host);
    }
}
