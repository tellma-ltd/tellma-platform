// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Whether a schedule fires while a job it fired earlier has not ended. Stored as the member's
    ///     name, <c>varchar(8)</c>.
    /// </summary>
    public enum OverlapPolicy
    {
        /// <summary>Skips the occurrence while the schedule's previous job is still active. The default.</summary>
        Skip,

        /// <summary>Fires regardless, so the schedule's jobs may run concurrently.</summary>
        Allow,
    }
}
