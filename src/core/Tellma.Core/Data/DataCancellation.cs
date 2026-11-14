// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The token that cancels the data access of one unit of work: a scoped value the scope's
    ///     opener sets, such as a web request's aborted token or a job's stopping token. Unset, it
    ///     never cancels.
    /// </summary>
    /// <remarks>
    ///     Read and validation round trips, and the connection opening of every round trip, are
    ///     cancelled by it. A persist round trip, once sent, runs to completion regardless.
    /// </remarks>
    public sealed class DataCancellation
    {
        /// <summary>The unit of work's token; <see cref="CancellationToken.None" /> until set.</summary>
        public CancellationToken Token { get; set; }
    }
}
