// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Enqueues background jobs in the current tenant scope. A scoped service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every job id is assigned inside the enqueue statement, in request order. When the
    ///         enqueuing batch commits, the queue wakes this instance's worker for the tenant; other
    ///         instances see the jobs on their next poll. A request that names an entity sets that row's
    ///         <see cref="Entities.IJobEntity.JobId" /> in the same transaction, without touching the
    ///         row's modification stamp.
    ///     </para>
    ///     <para>
    ///         A request that breaks a rule of <see cref="JobRequest" />, names a key no handler on this
    ///         instance registers, names an entity with a temporary id or a row another request names,
    ///         or names rows of two entity types in one call, and a batch that does not write or whose
    ///         statements autocommit, are programming errors: the call throws
    ///         <see cref="ArgumentException" /> or <see cref="InvalidOperationException" /> and appends
    ///         nothing.
    ///     </para>
    ///     <para>
    ///         A named row's stored <see cref="Entities.IJobEntity.JobId" /> must be null or name a job
    ///         that has ended; the in-memory member is not consulted. The statement checks the stored
    ///         row under its update's lock, so two enqueues of one row never both succeed, and refuses
    ///         the whole batch with <see cref="JobEnqueueCodes.OwnerUnavailable" /> when a row is
    ///         missing or its job is still active.
    ///     </para>
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "The service is the job queue; it enqueues jobs and is not a collection.")]
    public interface IJobQueue
    {
        /// <summary>
        ///     Appends the enqueue of <paramref name="requests" /> to <paramref name="batch" />, so the
        ///     jobs commit or roll back with the batch's other work: the way a service hands work off
        ///     from inside its save.
        /// </summary>
        /// <param name="batch">A persist batch that is still composing, in a transaction mode other than autocommit.</param>
        /// <param name="requests">The jobs to enqueue; every one naming an entity names a row of one entity type.</param>
        /// <returns>A result whose value, once the batch has executed, is the new jobs' ids in request order.</returns>
        BatchResult<IReadOnlyList<int>> Enqueue(IDataBatch batch, IReadOnlyList<JobRequest> requests);

        /// <summary>
        ///     Enqueues <paramref name="requests" /> in a persist batch of its own, for a caller that holds
        ///     no batch, and wakes this instance's worker once it commits.
        /// </summary>
        /// <param name="requests">The jobs to enqueue; every one naming an entity names a row of one entity type.</param>
        /// <param name="cancellationToken">Cancels the enqueue before its round trip is sent.</param>
        /// <returns>The new jobs' ids in request order.</returns>
        Task<IReadOnlyList<int>> EnqueueAsync(IReadOnlyList<JobRequest> requests, CancellationToken cancellationToken = default);
    }
}
