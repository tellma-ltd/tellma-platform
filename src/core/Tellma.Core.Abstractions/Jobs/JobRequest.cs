// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     One job to enqueue through <see cref="IJobQueue" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Platform or distribution code composes a request, never a client, so a request that breaks
    ///         a rule below is a programming error: the queue refuses it with
    ///         <see cref="ArgumentException" /> or <see cref="InvalidOperationException" />, never with a
    ///         validation error.
    ///     </para>
    ///     <para>
    ///         The job also stores the enqueuing activity's trace parent, which its run links to, and the
    ///         warning keys the enqueuing scope confirmed, so the run passes the warnings the requester
    ///         already confirmed.
    ///     </para>
    /// </remarks>
    /// <param name="HandlerKey">The key of a handler registered on this instance.</param>
    /// <param name="Arguments">
    ///     The handler's arguments, serialised under the platform's JSON rules; at most 64 KB once
    ///     serialised, and <see langword="null" /> for none.
    /// </param>
    /// <param name="DueAt">When the job becomes claimable, stored at offset zero to the millisecond; null for now on the database's clock.</param>
    /// <param name="RequestedById">
    ///     The user who is told when the job fails, is held or waits for confirmation, and whose "My
    ///     jobs" list it joins; null to tell the administrators instead.
    /// </param>
    /// <param name="RunAsUserId">
    ///     The user the job runs as; null for the current scope's user, or the system user in a system
    ///     scope. A scope with neither must name one.
    /// </param>
    /// <param name="Entity">
    ///     The row the job processes, for an entity-backed handler: a saved row (a positive id), already
    ///     in the table or in the same batch's save, whose stored <see cref="IJobEntity.JobId" /> is null
    ///     or names a job that has ended; the in-memory member is not consulted. The enqueue sets its
    ///     <see cref="IJobEntity.JobId" /> in the same transaction. Null for an arguments-only job.
    /// </param>
    /// <param name="Label">
    ///     The job's display label; null for the handler's own label. Its key and arguments fit the job
    ///     table's columns (<see cref="JobLimits" />).
    /// </param>
    public sealed record JobRequest(
        string HandlerKey,
        object? Arguments,
        DateTimeOffset? DueAt,
        int? RequestedById,
        int? RunAsUserId,
        IJobEntity? Entity,
        JobMessage? Label = null);
}
