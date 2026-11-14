// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The data-access counters of one unit of work: a scoped value whose operation its opener
    ///     names and the executor counts round trips and retries against. A job scope receives a
    ///     fresh one.
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
    }
}
