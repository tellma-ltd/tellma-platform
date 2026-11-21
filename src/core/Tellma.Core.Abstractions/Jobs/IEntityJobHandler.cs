// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     A handler of entity-backed jobs: each job processes the one row of
    ///     <typeparamref name="TEntity" /> whose <see cref="IJobEntity.JobId" /> names it, and the claim
    ///     loads that row, every mapped property included, together with the job. The class carries
    ///     <see cref="JobHandlerAttribute" />, implements this interface or <see cref="IJobHandler" /> and
    ///     not both, and is registered by its feature.
    /// </summary>
    /// <remarks>
    ///     <typeparamref name="TEntity" /> is a mapped entity: the leaf the distribution registered, which
    ///     the composition gate resolves. A claimed job whose row is gone is cancelled without reaching
    ///     the handler, so every item's <see cref="JobItem{TItem}.Item" /> is a row. The rest of
    ///     <see cref="IJobHandler" />'s contract applies unchanged.
    /// </remarks>
    /// <typeparam name="TEntity">The entity whose rows the jobs process.</typeparam>
    public interface IEntityJobHandler<TEntity>
        where TEntity : class, IJobEntity
    {
        /// <summary>
        ///     Runs one batch of claimed jobs. Marks each item with its outcome, or leaves it to follow
        ///     how this method ends.
        /// </summary>
        /// <param name="batch">The claimed jobs with their rows, which all run as one user, and the completion batch.</param>
        /// <returns>A task that completes when the handler is done with every item.</returns>
        Task ExecuteAsync(JobBatch<TEntity> batch);
    }
}
