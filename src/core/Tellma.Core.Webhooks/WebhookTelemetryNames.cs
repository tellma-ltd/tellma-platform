// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Webhooks
{
    /// <summary>The wire names of the webhook fronting's telemetry: its meter, instruments, and tags.</summary>
    /// <remarks>
    ///     Public because dashboards and alert queries are written against these exact strings, and a
    ///     test cross-checks the checked-in queries against these constants — so a rename here turns
    ///     a stale alert into a failing build rather than a silently dead alert.
    /// </remarks>
    public static class WebhookTelemetryNames
    {
        /// <summary>The meter every webhook instrument is created on.</summary>
        public const string MeterName = "Tellma.Webhooks";

        /// <summary>Counter of inbound webhook requests, one measurement per request.</summary>
        public const string RequestsInstrument = "tellma.webhook.requests";

        /// <summary>Histogram of how long the fronting spent on one request, in seconds.</summary>
        public const string RequestDurationInstrument = "tellma.webhook.request.duration";

        /// <summary>The unit of the request-counting instrument.</summary>
        public const string RequestUnit = "{request}";

        /// <summary>The unit of the duration instrument.</summary>
        public const string SecondUnit = "s";

        /// <summary>Tag: the receiver key the request was routed to.</summary>
        public const string KeyTag = "webhook.key";

        /// <summary>Tag: what the fronting did with the request.</summary>
        public const string OutcomeTag = "webhook.outcome";

        /// <summary>
        ///     <see cref="KeyTag" />: the request named no registered receiver. A literal is used
        ///     rather than the requested key, because probes and scanners would otherwise make this
        ///     dimension unbounded.
        /// </summary>
        public const string UnknownKeyTagValue = "unknown";

        /// <summary><see cref="OutcomeTag" />: verified and accepted.</summary>
        public const string AcceptedOutcome = "accepted";

        /// <summary><see cref="OutcomeTag" />: signature or credential verification failed.</summary>
        public const string UnauthorizedOutcome = "unauthorized";

        /// <summary><see cref="OutcomeTag" />: the payload was malformed or unprocessable.</summary>
        public const string InvalidOutcome = "invalid";

        /// <summary><see cref="OutcomeTag" />: a transient internal failure; the caller redelivers.</summary>
        public const string TransientFailureOutcome = "transient_failure";

        /// <summary><see cref="OutcomeTag" />: the receiver threw.</summary>
        public const string ErrorOutcome = "error";

        /// <summary><see cref="OutcomeTag" />: the body exceeded the configured cap.</summary>
        public const string TooLargeOutcome = "too_large";

        /// <summary><see cref="OutcomeTag" />: no receiver is registered under the requested key.</summary>
        public const string UnknownKeyOutcome = "unknown_key";

        /// <summary>
        ///     <see cref="OutcomeTag" />: the request ended before the fronting could classify it,
        ///     which in practice means the connection dropped mid-body.
        /// </summary>
        public const string AbortedOutcome = "aborted";
    }
}
