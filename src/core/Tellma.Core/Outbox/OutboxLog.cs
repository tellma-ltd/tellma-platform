// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Outbox
{
    /// <summary>Source-generated log messages of the email outbox.</summary>
    /// <remarks>
    ///     The tenant's id reaches these events through the job's log scope, never as a parameter, and
    ///     no event carries an address, a subject or a body: an email is named by its row's id.
    /// </remarks>
    internal static partial class OutboxLog
    {
        /// <summary>The category of the outbox's events.</summary>
        public const string Category = "Tellma.Core.Outbox";

        /// <summary>
        ///     A claim found an email still sending: an earlier attempt was interrupted after it marked
        ///     the email, so the message may have reached the provider. The email fails rather than
        ///     risk a duplicate, and an administrator decides whether to retry it.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="rowId">The outgoing email's id.</param>
        [LoggerMessage(
            EventName = "OutboxSendInterrupted",
            Level = LogLevel.Warning,
            Message = "The outgoing email {RowId} was found still sending: an earlier attempt was interrupted, so it is failed rather than sent again.")]
        public static partial void OutboxSendInterrupted(ILogger logger, int rowId);

        /// <summary>
        ///     The email sender threw before it sent anything, so every email of the batch failed
        ///     transiently and is retried with backoff while it has attempts left. The exception's
        ///     type alone is logged: the email pipeline logs the exception itself, and its message,
        ///     which the rows keep as their error detail, may name an address.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exceptionType">The type of what the sender threw.</param>
        /// <param name="count">The emails of the batch.</param>
        [LoggerMessage(
            EventName = "OutboxSendFailed",
            Level = LogLevel.Warning,
            Message = "The email sender threw {ExceptionType} before it sent any of {Count} outgoing emails; each is retried while it has attempts left.")]
        public static partial void OutboxSendFailed(ILogger logger, string exceptionType, int count);

        /// <summary>
        ///     The administrators could not be read after emails settled rejected or failed, so the
        ///     notice that tells them is not raised; the emails' states stand, and the outgoing emails
        ///     list shows them.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="count">The emails that settled rejected or failed.</param>
        [LoggerMessage(
            EventName = "OutboxNoticeRecipientsFailed",
            Level = LogLevel.Warning,
            Message = "The administrators could not be read, so {Count} outgoing emails that settled rejected or failed raise no notice.")]
        public static partial void OutboxNoticeRecipientsFailed(ILogger logger, Exception exception, int count);
    }
}
