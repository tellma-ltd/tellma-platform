// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>Source-generated log messages of the realtime hub.</summary>
    /// <remarks>
    ///     The tenant and the user ride the logger scope, never the message. A session is named by its
    ///     handle, never by its key, and no message carries a claim's raw value other than an id.
    /// </remarks>
    internal static partial class RealtimeLog
    {
        /// <summary>The hub accepted a connection.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="mode">The hosting mode.</param>
        [LoggerMessage(
            EventName = "HubConnected",
            Level = LogLevel.Debug,
            Message = "A hub connection opened ({Mode}).")]
        public static partial void HubConnected(ILogger logger, string mode);

        /// <summary>An accepted connection ended.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="mode">The hosting mode.</param>
        [LoggerMessage(
            EventName = "HubDisconnected",
            Level = LogLevel.Debug,
            Message = "A hub connection closed ({Mode}).")]
        public static partial void HubDisconnected(ILogger logger, string mode);

        /// <summary>The hub's front door, or the hub itself, refused a connection attempt.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reason">Why: the refusal's problem code, or what the hub found wrong.</param>
        [LoggerMessage(
            EventName = "HubConnectionRefused",
            Level = LogLevel.Information,
            Message = "A hub connection attempt was refused: {Reason}.")]
        public static partial void HubConnectionRefused(ILogger logger, string reason);

        /// <summary>The server closed a group's connections on this instance, after telling them why.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="group">The group told and closed.</param>
        /// <param name="count">How many of its connections this instance closed.</param>
        /// <param name="reason">Why: the <c>session.ended</c> reason sent.</param>
        [LoggerMessage(
            EventName = "HubConnectionsClosed",
            Level = LogLevel.Information,
            Message = "The hub closed {Count} connection(s) of group {Group} on this instance: {Reason}.")]
        public static partial void HubConnectionsClosed(ILogger logger, string group, int count, string reason);

        /// <summary>The hosting mode may not reach every instance's connections.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="message">What was found and what to do.</param>
        [LoggerMessage(
            EventName = "RealtimeHostingWarning",
            Level = LogLevel.Warning,
            Message = "{Message}")]
        public static partial void RealtimeHostingWarning(ILogger logger, string message);

        /// <summary>
        ///     The tenants a subject belongs to could not be read, so the end of its sessions was sent
        ///     only to the tenants this instance holds its connections in.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "HubSessionTenantsUnread",
            Level = LogLevel.Warning,
            Message = "The tenants of a subject whose sessions ended could not be read; session.ended was sent to the tenants this instance holds its connections in, and its other connections close when their tickets expire.")]
        public static partial void HubSessionTenantsUnread(ILogger logger, Exception exception);
    }
}
