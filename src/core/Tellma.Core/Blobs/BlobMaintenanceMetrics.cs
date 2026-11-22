// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The sweep and reconcile instruments on the <c>Tellma.Core</c> meter:
    ///     <c>tellma.blobs.sweep.deleted</c> by the state a row was claimed from,
    ///     <c>tellma.blobs.sweep.failures</c> untagged, and <c>tellma.blobs.reconcile.released</c> by
    ///     kind.
    /// </summary>
    /// <remarks>
    ///     The state is one of the three state values of <see cref="BlobTelemetryTagValues" /> and the
    ///     kind a registered one; no tenant or user tag is carried.
    /// </remarks>
    internal sealed class BlobMaintenanceMetrics
    {
        private readonly Counter<long> _deleted;
        private readonly Counter<long> _failures;
        private readonly Counter<long> _released;

        /// <summary>Creates the instruments on the platform meter.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public BlobMaintenanceMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: the factory owns and disposes it.
            Meter meter = meterFactory.Create(BlobTelemetryNames.MeterName);
            _deleted = meter.CreateCounter<long>(
                BlobTelemetryNames.SweepDeleted,
                BlobTelemetryNames.BlobUnit,
                "Blobs the sweep deleted from storage and the database, by the state they were claimed from.");
            _failures = meter.CreateCounter<long>(
                BlobTelemetryNames.SweepFailures,
                BlobTelemetryNames.FailureUnit,
                "Sweep failures: blobs whose objects or row the sweep could not delete, and failed purges.");
            _released = meter.CreateCounter<long>(
                BlobTelemetryNames.ReconcileReleased,
                BlobTelemetryNames.BlobUnit,
                "Committed blobs the reconcile released because no owner row referenced them, by kind.");
        }

        /// <summary>Counts blobs the sweep deleted.</summary>
        /// <param name="state">The state they were claimed from.</param>
        /// <param name="count">How many; nothing is counted for zero.</param>
        public void Deleted(BlobState state, int count)
        {
            if (count > 0)
            {
                _deleted.Add(count, new KeyValuePair<string, object?>(BlobTelemetryNames.StateTag, StateValue(state)));
            }
        }

        /// <summary>Counts sweep failures.</summary>
        /// <param name="count">How many; nothing is counted for zero.</param>
        public void Failed(int count)
        {
            if (count > 0)
            {
                _failures.Add(count);
            }
        }

        /// <summary>Counts blobs the reconcile released.</summary>
        /// <param name="kind">Their kind.</param>
        /// <param name="count">How many; nothing is counted for zero.</param>
        public void Released(string kind, int count)
        {
            if (count > 0)
            {
                _released.Add(count, new KeyValuePair<string, object?>(BlobTelemetryNames.KindTag, kind));
            }
        }

        /// <summary>The state tag's value for a state the sweep claims from.</summary>
        /// <param name="state">Staged, released or deleting.</param>
        /// <returns>The spelled-out tag value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The sweep never claims from <paramref name="state" />.</exception>
        internal static string StateValue(BlobState state)
        {
            return state switch
            {
                BlobState.Staged => BlobTelemetryTagValues.StateStaged,
                BlobState.Released => BlobTelemetryTagValues.StateReleased,
                BlobState.Deleting => BlobTelemetryTagValues.StateDeleting,
                BlobState.Committed or _ => throw new ArgumentOutOfRangeException(nameof(state), state, "The sweep claims only staged, released and deleting blobs."),
            };
        }
    }
}
