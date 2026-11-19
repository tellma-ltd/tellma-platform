// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The attach instrument on the <c>Tellma.Core</c> meter: <c>tellma.blobs.attach.rejected</c>,
    ///     one count per refused attach, tagged by the column's kind and the refusal's reason.
    /// </summary>
    /// <remarks>
    ///     The kind is always a declared one, read from the column's metadata, and the reason one of the
    ///     attach reasons of <see cref="BlobTelemetryTagValues" />; no tenant or user tag is carried.
    /// </remarks>
    internal sealed class BlobAttachMetrics
    {
        private readonly Counter<long> _rejected;

        /// <summary>Creates the instrument on the platform meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public BlobAttachMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(BlobTelemetryNames.MeterName);
            _rejected = meter.CreateCounter<long>(
                BlobTelemetryNames.AttachRejected,
                BlobTelemetryNames.FailureUnit,
                "Blob attaches a save refused, by kind and reason.");
        }

        /// <summary>Counts one refused attach.</summary>
        /// <param name="kind">The kind of the column the save tried to attach to.</param>
        /// <param name="reason">One of the attach reasons of <see cref="BlobTelemetryTagValues" />.</param>
        public void Rejected(string kind, string reason)
        {
            _rejected.Add(
                1,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.ReasonTag, reason));
        }
    }
}
