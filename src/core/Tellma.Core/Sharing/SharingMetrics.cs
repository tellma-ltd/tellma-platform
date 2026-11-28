// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Sharing
{
    // The instrument is spec 0033 §14's audience row.
    /// <summary>
    ///     The sharing model's instruments on the core meter: the audience users of each audience
    ///     resolved. No instrument carries a tenant, a user or a row; those are log properties.
    /// </summary>
    internal sealed class SharingMetrics
    {
        /// <summary>The audience users of each audience resolved, by resource.</summary>
        private readonly Histogram<long> _audienceMembers;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public SharingMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(SharingTelemetryNames.MeterName);
            _audienceMembers = meter.CreateHistogram<long>(
                SharingTelemetryNames.AudienceMembers,
                "{user}",
                "Audience users of each shareable row's audience resolved, by resource.");
        }

        /// <summary>Records one audience resolved.</summary>
        /// <param name="resource">The shareable resource.</param>
        /// <param name="total">Its audience users.</param>
        public void RecordAudience(string resource, int total)
        {
            _audienceMembers.Record(total, new KeyValuePair<string, object?>(SharingTelemetryNames.ResourceTag, resource));
        }
    }
}
