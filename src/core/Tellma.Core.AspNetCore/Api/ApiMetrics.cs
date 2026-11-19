// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>The web API's instruments, on the host's meter.</summary>
    internal sealed class ApiMetrics
    {
        /// <summary>The rejection reason of a request whose contract fingerprint is not the endpoint's.</summary>
        public const string ClientOutdatedReason = "client_outdated";

        private readonly Counter<long> _requestsRejected;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public ApiMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(ApiTelemetryNames.MeterName);
            _requestsRejected = meter.CreateCounter<long>(
                ApiTelemetryNames.RequestsRejected,
                description: "Web API requests refused before their endpoint ran, by reason.");
        }

        /// <summary>Counts a request refused before its endpoint ran.</summary>
        /// <param name="reason">Why it was refused.</param>
        public void RecordRejection(string reason)
        {
            _requestsRejected.Add(1, new KeyValuePair<string, object?>(ApiTelemetryNames.ReasonTag, reason));
        }
    }
}
