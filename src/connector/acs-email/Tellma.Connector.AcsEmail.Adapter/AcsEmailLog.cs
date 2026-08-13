// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>Source-generated log messages for the ACS Email transport.</summary>
    internal static partial class AcsEmailLog
    {
        /// <summary>ACS refused a send request.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="status">The HTTP status it answered with.</param>
        /// <param name="errorCode">The Azure error code, when one was returned.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "ACS Email answered {Status} to a send request ({ErrorCode}).")]
        public static partial void RequestFailed(ILogger logger, int status, string? errorCode);

        /// <summary>
        ///     A correlation encoded to a message id longer than the budget, so the message went out
        ///     uncorrelated rather than risking a rejection over an oversized header.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="ownerKey">The correlation's owner key; the reference itself is not logged.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "A '{OwnerKey}' correlation encoded to a message id over the length budget; the message was sent uncorrelated and will produce no routable delivery events.")]
        public static partial void CorrelationTooLongToStamp(ILogger logger, string ownerKey);

        /// <summary>A send request exceeded the transport's own network timeout.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The timeout the Azure pipeline raised.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "An ACS Email send request timed out; the message may or may not have been accepted.")]
        public static partial void RequestTimedOut(ILogger logger, Exception exception);

        /// <summary>Acquiring an Azure token failed, so no request could be authenticated.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The credential failure.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Could not acquire an Azure token for ACS Email; the batch stopped issuing requests.")]
        public static partial void CredentialFailed(ILogger logger, Exception exception);

        /// <summary>Events belonging to no recognizable message were metered as uncorrelated.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">How many events were skipped as not concerning email.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Skipped {Count} Event Grid events that are not ACS Email delivery or engagement reports.")]
        public static partial void NonEmailEventsSkipped(ILogger logger, int count);
    }
}
