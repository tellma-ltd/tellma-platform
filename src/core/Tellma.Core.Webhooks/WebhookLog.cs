// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Webhooks
{
    /// <summary>Source-generated log messages for the webhook fronting.</summary>
    /// <remarks>
    ///     No message here takes a request body or a query string, at any level. Bodies carry
    ///     recipient addresses and provider junk; query strings carry webhook credentials. What is
    ///     left — the receiver key, the outcome, and the receiver's own diagnostic detail — is enough
    ///     to investigate any of these events.
    /// </remarks>
    internal static partial class WebhookLog
    {
        /// <summary>A receiver refused a call.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The receiver key.</param>
        /// <param name="outcome">The refusing outcome.</param>
        /// <param name="detail">The receiver's diagnostic detail, never sent to the caller.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Webhook '{Key}' rejected a call: {Outcome}. {Detail}")]
        public static partial void WebhookRejected(ILogger logger, string key, string outcome, string? detail);

        /// <summary>A receiver threw instead of returning an outcome.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="key">The receiver key.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "The webhook receiver '{Key}' threw; the caller is told to redeliver.")]
        public static partial void WebhookReceiverThrew(ILogger logger, Exception exception, string key);

        /// <summary>A request named no registered receiver.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The requested key, logged at Debug only — probes and scanners land here.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "No webhook receiver is registered under the key '{Key}'.")]
        public static partial void UnknownWebhookKey(ILogger logger, string key);

        /// <summary>A request body exceeded the configured cap and was never dispatched.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The receiver key the request was addressed to.</param>
        /// <param name="maxRequestBodyBytes">The configured cap.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "A webhook call to '{Key}' exceeded the {MaxRequestBodyBytes}-byte body cap and was not dispatched.")]
        public static partial void WebhookBodyTooLarge(ILogger logger, string key, long maxRequestBodyBytes);

        /// <summary>
        ///     A receiver returned a response body with no content type. Providers are strict about
        ///     the content type, so the body is dropped rather than sent unlabeled.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The receiver key.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "The webhook receiver '{Key}' returned a response body with no content type; the body was dropped.")]
        public static partial void WebhookResponseBodyWithoutContentType(ILogger logger, string key);
    }
}
