// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The codes the enqueue statement throws, as <see cref="TellmaSqlErrors.Invariant" />, when the
    ///     database refuses what <see cref="IJobQueue.Enqueue" /> appended. The batch fails with an
    ///     assertion carrying the error number and the code, and rolls back: no job is stored, no row
    ///     is stamped and no worker is woken.
    /// </summary>
    public static class JobEnqueueCodes
    {
        /// <summary>
        ///     A row a request names is missing, or its <see cref="Entities.IJobEntity.JobId" /> names a job
        ///     that has not ended (pending, running, held or waiting for confirmation). A row has one
        ///     active job at a time; one whose job ended may be enqueued again.
        /// </summary>
        public const string OwnerUnavailable = "Jobs.OwnerUnavailable";

        /// <summary>
        ///     The enqueue ran outside a transaction: its batch was switched to autocommit after the
        ///     enqueue was appended, so the jobs and their rows' stamps could not commit together.
        /// </summary>
        public const string OutsideTransaction = "Jobs.EnqueueOutsideTransaction";
    }
}
