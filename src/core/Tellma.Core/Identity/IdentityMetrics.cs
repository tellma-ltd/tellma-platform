// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Identity
{
    // The instruments are spec 0018 §2.2's and §8.2's identity rows.
    /// <summary>
    ///     The identity-server client's instruments on the core meter: one count and one duration per
    ///     call, by endpoint and outcome. No instrument carries a tenant or a user.
    /// </summary>
    internal sealed class IdentityMetrics
    {
        /// <summary>The calls made.</summary>
        private readonly Counter<long> _calls;

        /// <summary>How long each call took.</summary>
        private readonly Histogram<double> _duration;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public IdentityMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(UsersTelemetryNames.MeterName);

            _calls = meter.CreateCounter<long>(
                UsersTelemetryNames.IdentityCalls,
                "{call}",
                "Calls of the identity server's management API, by endpoint and outcome.");

            _duration = meter.CreateHistogram<double>(
                UsersTelemetryNames.IdentityCallDuration,
                "s",
                "Duration of each call of the identity server's management API, its chunks and token fetches included, by endpoint and outcome.");
        }

        /// <summary>Records one call.</summary>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="outcome">The outcome's tag value.</param>
        /// <param name="elapsed">How long the call took.</param>
        public void RecordCall(string endpoint, string outcome, TimeSpan elapsed)
        {
            KeyValuePair<string, object?> endpointTag = new(UsersTelemetryNames.EndpointTag, endpoint);
            KeyValuePair<string, object?> outcomeTag = new(UsersTelemetryNames.OutcomeTag, outcome);
            _calls.Add(1, endpointTag, outcomeTag);
            _duration.Record(elapsed.TotalSeconds, endpointTag, outcomeTag);
        }
    }
}
