// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     A handler of arguments-only jobs: each item is the job itself, and the handler reads its
    ///     arguments and whatever rows it needs. The class carries <see cref="JobHandlerAttribute" />,
    ///     implements this interface or <see cref="IEntityJobHandler{TEntity}" /> and not both, and is
    ///     registered by its feature.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The worker resolves the handler from the job's tenant scope, one instance per invocation,
    ///         running as the job's run-as user with that user's permissions as they are at run time.
    ///         Every round trip the handler starts observes the scope's cancellation, which a lost lease
    ///         or a host stop cancels; a requested stop cancels only the item's own token.
    ///     </para>
    ///     <para>
    ///         Execution is at least once: a lost lease, a crash, an ambiguous completion or a retry runs
    ///         an item again, possibly elsewhere. A handler is correct when each of its steps is
    ///         idempotent or it checkpoints its progress and resumes from the checkpoint; a side effect
    ///         outside the database is recorded where a re-run can detect it.
    ///     </para>
    /// </remarks>
    public interface IJobHandler
    {
        /// <summary>
        ///     Runs one batch of claimed jobs. Marks each item with its outcome, or leaves it to follow
        ///     how this method ends.
        /// </summary>
        /// <param name="batch">The claimed jobs, which all run as one user, and the completion batch.</param>
        /// <returns>A task that completes when the handler is done with every item.</returns>
        Task ExecuteAsync(JobBatch<Job> batch);
    }
}
