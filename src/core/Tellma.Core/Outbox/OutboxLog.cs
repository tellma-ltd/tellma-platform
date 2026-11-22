// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Outbox
{
    /// <summary>Source-generated log messages of the email outbox.</summary>
    /// <remarks>
    ///     The sending job's events take the tenant's id from the job's log scope; the delivery
    ///     events' handler runs outside any tenant, so its events name the tenant themselves. No event
    ///     carries an address, a subject or a body: an email is named by its row's id.
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

        /// <summary>
        ///     A delivery event names an outgoing email that does not exist: it was deleted, or the
        ///     correlation is malformed or foreign. The event is counted unmatched and dropped.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant the correlation names; null when it names none.</param>
        /// <param name="reference">The correlation's reference, the email's id as text.</param>
        [LoggerMessage(
            EventName = "OutboxDeliveryEventUnmatched",
            Level = LogLevel.Warning,
            Message = "A delivery event names the outgoing email {Reference} of tenant {TenantId}, which does not exist; the event is dropped.")]
        public static partial void OutboxDeliveryEventUnmatched(ILogger logger, int? tenantId, string reference);

        /// <summary>
        ///     A tenant's delivery events were dropped because the tenant is missing, still being
        ///     provisioned, or retired, so it has no outgoing email to update.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">The tenant's state, or <c>Missing</c> when the registry does not hold it.</param>
        /// <param name="count">The events dropped.</param>
        [LoggerMessage(
            EventName = "OutboxDeliveryTenantSkipped",
            Level = LogLevel.Warning,
            Message = "{Count} delivery events of tenant {TenantId} were dropped: the tenant is {State}.")]
        public static partial void OutboxDeliveryTenantSkipped(ILogger logger, int tenantId, string state, int count);

        /// <summary>
        ///     Some of a tenant's delivery events changed nothing: each repeats an event already
        ///     applied, or is no stronger than what its email records.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="count">The events.</param>
        [LoggerMessage(
            EventName = "OutboxDeliveryEventsStale",
            Level = LogLevel.Debug,
            Message = "{Count} delivery events of tenant {TenantId} were repeats or no stronger than what their emails record.")]
        public static partial void OutboxDeliveryEventsStale(ILogger logger, int tenantId, int count);

        /// <summary>
        ///     A frozen tenant's delivery events were left for the provider to deliver again, since its
        ///     database refuses writes; the webhook call fails once every other tenant's were applied.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="count">The events left.</param>
        [LoggerMessage(
            EventName = "OutboxDeliveryTenantFrozen",
            Level = LogLevel.Information,
            Message = "{Count} delivery events of the frozen tenant {TenantId} wait for the provider to deliver them again.")]
        public static partial void OutboxDeliveryTenantFrozen(ILogger logger, int tenantId, int count);

        /// <summary>
        ///     A retention page's delete was refused because a row changed after the page was read,
        ///     such as an email an administrator retried, so the page is read again and retried once.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The refusal.</param>
        /// <param name="count">The ids the refused delete named.</param>
        [LoggerMessage(
            EventName = "OutboxRetentionPageReread",
            Level = LogLevel.Information,
            Message = "The retention's delete of {Count} outgoing emails was refused after the page was read; the page is read again.")]
        public static partial void OutboxRetentionPageReread(ILogger logger, Exception exception, int count);
    }
}
