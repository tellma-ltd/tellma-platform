// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>The web host's authentication instruments, on the host's meter.</summary>
    internal sealed class HostMetrics
    {
        private readonly Counter<long> _csrfRejections;
        private readonly Counter<long> _stepUpChallenges;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The meter factory.</param>
        public HostMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);
            Meter meter = meterFactory.Create(HostTelemetryNames.MeterName);
            _csrfRejections = meter.CreateCounter<long>(
                HostTelemetryNames.CsrfRejections,
                description: "Requests the CSRF control refused, by the rule they broke.");
            _stepUpChallenges = meter.CreateCounter<long>(
                HostTelemetryNames.StepUpChallenges,
                description: "Step-up challenges written to callers whose session fell short of an operation's assurance bar.");
        }

        /// <summary>Counts a CSRF refusal.</summary>
        /// <param name="rule">The rule the request broke.</param>
        public void RecordCsrfRejection(string rule)
        {
            _csrfRejections.Add(1, new KeyValuePair<string, object?>(HostTelemetryNames.RuleTag, rule));
        }

        /// <summary>Counts a step-up challenge.</summary>
        public void RecordStepUpChallenge()
        {
            _stepUpChallenges.Add(1);
        }
    }
}
