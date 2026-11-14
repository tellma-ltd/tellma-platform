// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Notifications
{
    /// <summary>Source-generated log messages of notifications and client events.</summary>
    /// <remarks>Tenant and user ids reach these events through the log scope, never as parameters.</remarks>
    internal static partial class NotificationsLog
    {
        /// <summary>The category of the notifier's and the renderer's events.</summary>
        public const string Category = "Tellma.Core.Notifications";

        /// <summary>A committed request's outcome: how many recipients it named, how many rows it inserted, how many delivery jobs it enqueued.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="type">The request's type key.</param>
        /// <param name="requested">The distinct recipients it named.</param>
        /// <param name="inserted">The rows it inserted or refreshed.</param>
        /// <param name="deliveryJobs">The delivery jobs it enqueued.</param>
        [LoggerMessage(
            EventName = "NotificationsInserted",
            Level = LogLevel.Debug,
            Message = "The notification {Type} named {Requested} recipients, wrote {Inserted} rows and enqueued {DeliveryJobs} delivery jobs.")]
        public static partial void NotificationsInserted(ILogger logger, string type, int requested, int inserted, int deliveryJobs);

        /// <summary>A committed withdrawal's outcome.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="dedupKey">The withdrawn key.</param>
        /// <param name="deleted">The rows it deleted.</param>
        [LoggerMessage(
            EventName = "NotificationsWithdrawn",
            Level = LogLevel.Debug,
            Message = "The withdrawal of {DedupKey} deleted {Deleted} notifications.")]
        public static partial void NotificationsWithdrawn(ILogger logger, string dedupKey, int deleted);

        /// <summary>
        ///     The inbox event, or the poller nudge, after a committed batch failed; the rows are
        ///     committed, the client's next summary sees them, and the next poll claims the jobs.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "InboxEventFailed",
            Level = LogLevel.Warning,
            Message = "The inbox event after a committed batch of notifications failed; clients catch up on their next read.")]
        public static partial void InboxEventFailed(ILogger logger, Exception exception);

        /// <summary>A notification's stored arguments are not a JSON object, so its text renders without them.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The parse failure; null when the arguments parse as JSON that is not an object.</param>
        /// <param name="type">The notification's type key.</param>
        [LoggerMessage(
            EventName = "NotificationArgumentsInvalid",
            Level = LogLevel.Warning,
            Message = "The arguments of a notification of type {Type} are not a JSON object; its text renders without them.")]
        public static partial void NotificationArgumentsInvalid(ILogger logger, Exception? exception, string type);

        /// <summary>A client event could not be delivered; clients catch up on their next read, so it is never retried.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The delivery's exception.</param>
        /// <param name="eventName">The event's name.</param>
        [LoggerMessage(
            EventName = "ClientEventFailed",
            Level = LogLevel.Warning,
            Message = "The client event {EventName} could not be delivered; clients catch up on their next read.")]
        public static partial void ClientEventFailed(ILogger logger, Exception exception, string eventName);
    }
}
