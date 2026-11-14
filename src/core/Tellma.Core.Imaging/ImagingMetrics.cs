// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The image instruments, <c>tellma.blobs.image.duration</c> and
    ///     <c>tellma.blobs.image.failures</c>, on the runtime's blob meter.
    /// </summary>
    /// <remarks>
    ///     The processor records them itself, because only it can tell a render that ran out of time
    ///     from a file it cannot render. Every tag is a closed set: the kind is bounded by the kinds a
    ///     distribution registers, and no tenant or user is ever a dimension.
    /// </remarks>
    internal sealed class ImagingMetrics
    {
        private readonly Histogram<double> _duration;
        private readonly Counter<long> _failures;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public ImagingMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it, and hands every assembly
            // that asks for this name the same meter.
            Meter meter = meterFactory.Create(BlobTelemetryNames.MeterName);

            _duration = meter.CreateHistogram<double>(
                BlobTelemetryNames.ImageDuration,
                BlobTelemetryNames.SecondUnit,
                "Duration of one image normalisation or thumbnail render, by kind and operation.");

            _failures = meter.CreateCounter<long>(
                BlobTelemetryNames.ImageFailures,
                BlobTelemetryNames.FailureUnit,
                "Image normalisations refused and thumbnails not rendered, by kind, operation and reason.");
        }

        /// <summary>Records how long one operation took.</summary>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation"><see cref="BlobTelemetryTagValues.ImageProcess" /> or <see cref="BlobTelemetryTagValues.ImageThumbnail" />.</param>
        /// <param name="seconds">The duration.</param>
        public void RecordDuration(string kind, string operation, double seconds)
        {
            _duration.Record(
                seconds,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.OperationTag, operation));
        }

        /// <summary>Counts one failure.</summary>
        /// <param name="kind">The blob kind.</param>
        /// <param name="operation"><see cref="BlobTelemetryTagValues.ImageProcess" /> or <see cref="BlobTelemetryTagValues.ImageThumbnail" />.</param>
        /// <param name="reason">One of the image reason values of <see cref="BlobTelemetryTagValues" />.</param>
        public void RecordFailure(string kind, string operation, string reason)
        {
            _failures.Add(
                1,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(BlobTelemetryNames.ReasonTag, reason));
        }
    }
}
