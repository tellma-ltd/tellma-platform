// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     What a batch is for, fixed when it is created. The purpose decides the frame the batch is
    ///     wrapped in, the command timeout and whether a sent round trip may be cancelled; the
    ///     batch's contributors consult it too.
    /// </summary>
    public enum BatchPurpose
    {
        /// <summary>
        ///     A read on a caller's behalf: untransacted, and cancelled when the caller goes away.
        /// </summary>
        Read,

        /// <summary>
        ///     The reads a validation round makes before a write: untransacted, and cancelled when
        ///     the caller goes away.
        /// </summary>
        Validate,

        /// <summary>
        ///     A write on a caller's behalf, wrapped in the persist frame's transaction. Once sent, it
        ///     runs to completion whether or not the caller is still there.
        /// </summary>
        Persist,

        /// <summary>
        ///     Platform bookkeeping outside any caller: jobs, sweeps and the migrator. It carries the
        ///     schema guard and the version-tag prelude alone, no connect prologue and no guard
        ///     wrapper, and runs under the longer maintenance timeout.
        /// </summary>
        Maintenance,
    }
}
