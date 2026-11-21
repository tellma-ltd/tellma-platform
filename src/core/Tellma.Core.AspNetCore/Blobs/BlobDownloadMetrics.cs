// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.AspNetCore.Blobs
{
    /// <summary>The download instrument on the <c>Tellma.Core</c> meter: <c>tellma.blobs.downloads</c>.</summary>
    /// <remarks>
    ///     Tagged by kind and outcome, never by tenant or user. A download naming a kind no reference
    ///     declares is never measured, since its name would open the kind tag to whatever a client sends.
    /// </remarks>
    internal sealed class BlobDownloadMetrics
    {
        private readonly Counter<long> _downloads;

        /// <summary>Creates the instrument on the platform meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public BlobDownloadMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(BlobTelemetryNames.MeterName);
            _downloads = meter.CreateCounter<long>(
                BlobTelemetryNames.Downloads,
                BlobTelemetryNames.DownloadUnit,
                "Blob download requests, by kind and outcome.");
        }

        /// <summary>Counts one download's outcome.</summary>
        /// <param name="kind">The download's kind, a declared one.</param>
        /// <param name="outcome">One of the download outcome values of <see cref="BlobTelemetryTagValues" />.</param>
        public void Count(string kind, string outcome)
        {
            _downloads.Add(
                1,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.OutcomeTag, outcome));
        }
    }
}
