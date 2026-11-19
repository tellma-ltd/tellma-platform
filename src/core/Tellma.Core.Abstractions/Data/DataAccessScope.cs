// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The data-access counters of one unit of work: a scoped value whose operation its opener
    ///     names and the executor counts round trips, retries and database time against, and on
    ///     which it leaves each executed batch's outcome. A job scope receives a fresh one.
    /// </summary>
    public sealed class DataAccessScope
    {
        /// <summary>
        ///     The unit of work's name: <c>&lt;Resource&gt;:&lt;operation&gt;</c> for a request
        ///     (<c>gl.Center:save</c>), <c>job:&lt;key&gt;</c> for a job; empty until its opener names
        ///     it. Round-trip spans are named after it.
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>The round trips the unit of work has taken so far, retries included.</summary>
        public int RoundTrips { get; set; }

        /// <summary>The round trips the unit of work has re-run after a failure.</summary>
        public int Retries { get; set; }

        /// <summary>
        ///     The time the unit of work's batch attempts have spent so far, from acquiring the
        ///     connection to releasing it, retries included.
        /// </summary>
        public TimeSpan DatabaseTime { get; set; }

        /// <summary>
        ///     The outcome of the unit of work's last executed batch: the version tags a response
        ///     reports to its client; null until a batch completes.
        /// </summary>
        public BatchOutcome? LastOutcome { get; set; }
    }
}
