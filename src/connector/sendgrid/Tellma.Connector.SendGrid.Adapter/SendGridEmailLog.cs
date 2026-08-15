// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>Source-generated log messages for the SendGrid transport.</summary>
    /// <remarks>
    ///     Transport-specific detail only; the pipeline logs batch outcomes centrally. Nothing here
    ///     carries a recipient address or message content, though a provider error text may quote one
    ///     back — that is accepted, being failure-path-only and operationally essential.
    /// </remarks>
    internal static partial class SendGridEmailLog
    {
        /// <summary>SendGrid refused a mail-send request.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="statusCode">The HTTP status it answered with.</param>
        /// <param name="error">The parsed error text, or the raw body when it was not JSON.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "SendGrid answered {StatusCode} to an email mail-send request: {Error}")]
        public static partial void RequestFailed(ILogger logger, int statusCode, string? error);

        /// <summary>
        ///     Events belonging to another deployment were dropped before dispatch. Routine background
        ///     under shared provider credentials, and a misrouted-dashboard signal under dedicated
        ///     ones.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">How many events were dropped.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Dropped {Count} SendGrid email delivery events belonging to another deployment.")]
        public static partial void ForeignEventsDropped(ILogger logger, int count);
    }
}
