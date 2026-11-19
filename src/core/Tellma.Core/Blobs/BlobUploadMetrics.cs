// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The upload instruments on the <c>Tellma.Core</c> meter: <c>tellma.blobs.uploads</c>,
    ///     <c>tellma.blobs.upload.bytes</c> and <c>tellma.blobs.upload.duration</c>.
    /// </summary>
    /// <remarks>
    ///     Tagged by kind, a closed set per distribution, and never by tenant or user. An upload of a
    ///     kind no reference declares is never measured, since its name would open the kind tag to
    ///     whatever a client sends. The image instruments are the processor's own.
    /// </remarks>
    internal sealed class BlobUploadMetrics
    {
        private readonly Counter<long> _uploads;
        private readonly Counter<long> _bytes;
        private readonly Histogram<double> _duration;

        /// <summary>Creates the instruments on the platform meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public BlobUploadMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(BlobTelemetryNames.MeterName);

            _uploads = meter.CreateCounter<long>(
                BlobTelemetryNames.Uploads,
                BlobTelemetryNames.UploadUnit,
                "Blob uploads, by kind and outcome.");

            _bytes = meter.CreateCounter<long>(
                BlobTelemetryNames.UploadBytes,
                BlobTelemetryNames.ByteUnit,
                "Bytes received by staged blob uploads, by kind.");

            _duration = meter.CreateHistogram<double>(
                BlobTelemetryNames.UploadDuration,
                BlobTelemetryNames.SecondUnit,
                "Duration of one staged blob upload, from the first byte read to the objects written.");
        }

        /// <summary>Counts one upload's outcome.</summary>
        /// <param name="kind">The upload's kind, a declared one.</param>
        /// <param name="outcome">One of the upload outcome values of <see cref="BlobTelemetryTagValues" />.</param>
        public void Count(string kind, string outcome)
        {
            _uploads.Add(
                1,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.OutcomeTag, outcome));
        }

        /// <summary>Records one staged upload: its outcome, the bytes received and its duration.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="declaredLength">The upload's declared length, which classes its duration.</param>
        /// <param name="receivedBytes">The bytes received.</param>
        /// <param name="seconds">How long the upload took.</param>
        public void Staged(string kind, long declaredLength, long receivedBytes, double seconds)
        {
            Count(kind, BlobTelemetryTagValues.UploadStaged);
            _bytes.Add(receivedBytes, new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind));
            _duration.Record(
                seconds,
                new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind),
                new KeyValuePair<string, object?>(BlobTelemetryNames.SizeClassTag, BlobTelemetryTagValues.SizeClass(declaredLength)));
        }
    }
}
