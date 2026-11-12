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
