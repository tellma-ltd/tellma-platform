// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The owner of a unit of work's counters: created when its scope first resolves
    ///     <see cref="DataAccessScope" />, whether the opener naming the operation or a batch does so,
    ///     it records the unit of work's round trips when the scope ends, zero included, and stamps
    ///     them on the current trace span, so a request, a job or a provisioning step is observed
    ///     through one instrument.
    /// </summary>
    /// <param name="metrics">The executor's instruments.</param>
    internal sealed class DataAccessRecorder(DataMetrics metrics) : IDisposable
    {
        /// <summary>The executor's instruments.</summary>
        private readonly DataMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        /// <summary>Whether the scope's round trips were recorded.</summary>
        private bool _recorded;

        /// <summary>The unit of work's counters, which the scope resolves as its <see cref="DataAccessScope" />.</summary>
        public DataAccessScope Scope { get; } = new();

        /// <summary>Records the unit of work's round trips, once, as its scope ends.</summary>
        public void Dispose()
        {
            if (_recorded)
            {
                return;
            }

            _recorded = true;
            _metrics.RecordRoundTrips(Scope.Operation, Scope.RoundTrips);
            _ = Activity.Current?.SetTag(DataTelemetryNames.RoundTrips, Scope.RoundTrips);
        }
    }
}
