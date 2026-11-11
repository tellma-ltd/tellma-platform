// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The executor's companion in a unit of work's scope: created with the scope's first batch,
    ///     it records the unit of work's round trips when the scope ends and stamps them on the
    ///     current trace span, so a request, a job or a provisioning step is observed through one
    ///     instrument.
    /// </summary>
    /// <param name="scope">The unit of work's counters.</param>
    /// <param name="metrics">The executor's instruments.</param>
    internal sealed class DataAccessRecorder(DataAccessScope scope, DataMetrics metrics) : IDisposable
    {
        /// <summary>The executor's instruments.</summary>
        private readonly DataMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        /// <summary>Whether the scope's round trips were recorded.</summary>
        private bool _recorded;

        /// <summary>The unit of work's counters.</summary>
        public DataAccessScope Scope { get; } = scope ?? throw new ArgumentNullException(nameof(scope));

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
