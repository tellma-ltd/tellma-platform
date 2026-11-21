// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     What a schedule does with the occurrences it missed while nothing ticked: a stopped worker,
    ///     a read-only tenant or an outage. Stored as the member's name, <c>varchar(9)</c>.
    /// </summary>
    public enum MissedPolicy
    {
        /// <summary>Fires once for the latest missed occurrence, however many were missed. The default.</summary>
        Coalesce,

        /// <summary>
        ///     Fires every missed occurrence inside the schedule's catch-up window, each as its own
        ///     job; requires <see cref="OverlapPolicy.Allow" />, since the jobs may run concurrently.
        /// </summary>
        ReplayAll,

        /// <summary>Fires none of the missed occurrences and waits for the next one.</summary>
        Skip,
    }
}
