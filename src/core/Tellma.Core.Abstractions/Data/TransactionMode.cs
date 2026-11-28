// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>How a batch's statements are grouped into a transaction.</summary>
    public enum TransactionMode
    {
        /// <summary>
        ///     The default: the batch text is wrapped in one in-text transaction when any statement
        ///     declares a write, and runs untransacted otherwise.
        /// </summary>
        Auto,

        /// <summary>
        ///     Autocommit: every statement commits on its own. For bookkeeping whose composer
        ///     tolerates a partial re-run, such as lease renewal, sweeps and the job poll.
        /// </summary>
        None,

        /// <summary>
        ///     A transaction opened and committed from C# around the round trip: the escape hatch for
        ///     a decision that must be taken in C# before the commit. It costs two more round trips,
        ///     is never re-run by the executor, and is never used by the platform's own paths.
        /// </summary>
        Explicit,
    }
}
