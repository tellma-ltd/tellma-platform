// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;

namespace Tellma.Core.Webhooks
{
    /// <summary>The webhook fronting's instruments.</summary>
    /// <remarks>
    ///     Request bodies and query strings never reach these tags — the query string can carry a
    ///     webhook credential — and the receiver key is replaced by a literal when it matches no
    ///     receiver, so scanner traffic cannot inflate the dimension.
    /// </remarks>
    internal sealed class WebhookMetrics
    {
        private readonly Counter<long> _requests;
        private readonly Histogram<double> _requestDuration;

        /// <summary>Creates the instruments on the shared webhook meter.</summary>
        /// <param name="meterFactory">The host's meter factory.</param>
        public WebhookMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // A local, not a field: the factory owns the meter's lifetime.
            Meter meter = meterFactory.Create(WebhookTelemetryNames.MeterName);

            _requests = meter.CreateCounter<long>(
                WebhookTelemetryNames.RequestsInstrument,
                WebhookTelemetryNames.RequestUnit,
                "Inbound webhook requests, by receiver key and outcome.");

            _requestDuration = meter.CreateHistogram<double>(
                WebhookTelemetryNames.RequestDurationInstrument,
                WebhookTelemetryNames.SecondUnit,
                "Wall-clock duration the fronting spent on one webhook request.");
        }

        /// <summary>Records one handled request.</summary>
        /// <param name="key">The receiver key, or the unknown-key literal.</param>
        /// <param name="outcome">What the fronting did with the request.</param>
        /// <param name="seconds">How long it took.</param>
        public void RecordRequest(string key, string outcome, double seconds)
        {
            KeyValuePair<string, object?> keyTag = new(WebhookTelemetryNames.KeyTag, key);
            KeyValuePair<string, object?> outcomeTag = new(WebhookTelemetryNames.OutcomeTag, outcome);

            _requests.Add(1, keyTag, outcomeTag);
            _requestDuration.Record(seconds, keyTag, outcomeTag);
        }
    }
}
