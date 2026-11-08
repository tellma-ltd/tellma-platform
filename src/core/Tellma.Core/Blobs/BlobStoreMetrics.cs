// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The store instruments, <c>tellma.blobs.store.operations</c> and
    ///     <c>tellma.blobs.store.duration</c>, on the shared store meter that every store
    ///     implementation emits on.
    /// </summary>
    /// <remarks>
    ///     One measurement per store call, never per object, and never a tenant: the operation,
    ///     the outcome and a write's size class are each a small closed set.
    /// </remarks>
    internal sealed class BlobStoreMetrics
    {
        private readonly Counter<long> _operations;
        private readonly Histogram<double> _duration;

        /// <summary>Creates the instruments on the store meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public BlobStoreMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(BlobTelemetryNames.StoreMeterName);

            _operations = meter.CreateCounter<long>(
                BlobTelemetryNames.StoreOperations,
                BlobTelemetryNames.OperationUnit,
                "Blob store calls, by operation and outcome.");

            _duration = meter.CreateHistogram<double>(
                BlobTelemetryNames.StoreDuration,
                BlobTelemetryNames.SecondUnit,
                "Duration of one blob store call; for a read, the time to open the stream.");
        }

        /// <summary>Records one store call.</summary>
        /// <param name="operation">One of the store operation values of <see cref="BlobTelemetryTagValues" />.</param>
        /// <param name="outcome">One of the store outcome values of <see cref="BlobTelemetryTagValues" />.</param>
        /// <param name="seconds">How long the call took.</param>
        /// <param name="writtenBytes">For a write, the call's total declared bytes; otherwise null.</param>
        public void Record(string operation, string outcome, double seconds, long? writtenBytes = null)
        {
            _operations.Add(
                1,
                new KeyValuePair<string, object?>(BlobTelemetryNames.OperationTag, operation),
                new KeyValuePair<string, object?>(BlobTelemetryNames.OutcomeTag, outcome));

            // The size class rides the write's duration only: it is what separates a slow disk from
            // a large file.
            TagList tags = new() { { BlobTelemetryNames.OperationTag, operation } };
            if (writtenBytes is long bytes)
            {
                tags.Add(BlobTelemetryNames.SizeClassTag, BlobTelemetryTagValues.SizeClass(bytes));
            }

            _duration.Record(seconds, tags);
        }
    }
}
