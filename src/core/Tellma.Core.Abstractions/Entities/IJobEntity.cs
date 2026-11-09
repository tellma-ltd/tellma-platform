// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     The gate of an entity whose rows are processed by background jobs: the row's link to the job
    ///     processing it.
    /// </summary>
    /// <remarks>
    ///     <see cref="JobId" /> is server-owned by derivation. The enqueue statement sets it in the same
    ///     transaction without touching the row's modification stamp, and a handler that inserts its
    ///     own row supplies it explicitly. The column is a foreign key to <c>core.Jobs</c> that is set
    ///     to null when the job is deleted, and it is unique wherever it is not null: one job processes
    ///     at most one row.
    /// </remarks>
    public interface IJobEntity
    {
        /// <summary>The job processing the row, if any; server-owned, a foreign key to <c>core.Jobs</c>.</summary>
        int? JobId { get; set; }
    }
}
