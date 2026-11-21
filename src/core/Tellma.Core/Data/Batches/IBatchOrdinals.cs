// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The ordinals a batch's statements take, for a composer that attributes an executor failure's
    ///     statement ordinal to the part of the batch it composed.
    /// </summary>
    internal interface IBatchOrdinals
    {
        /// <summary>
        ///     The ordinal the next statement appended to the batch takes. The batch's prologue stage
        ///     runs first when it has not, so its statements never take an ordinal a caller marked.
        /// </summary>
        /// <returns>The ordinal.</returns>
        /// <exception cref="InvalidOperationException">The batch executed, or a statement is being composed.</exception>
        int NextOrdinal();
    }
}
