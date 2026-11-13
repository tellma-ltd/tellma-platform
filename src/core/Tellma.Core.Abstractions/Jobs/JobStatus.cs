// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Where a job is in its life, stored as the member's name, <c>varchar(19)</c>. A job is queued
    ///     <see cref="Pending" />, leased <see cref="Running" /> by a claim, and ends
    ///     <see cref="Succeeded" />, <see cref="Failed" /> or <see cref="Cancelled" />; a stale queued
    ///     job waits <see cref="Held" /> for an administrator after a service interruption, and a job
    ///     that raised a warning waits <see cref="PendingConfirmation" /> for its initiator.
    /// </summary>
    /// <remarks>
    ///     A job is available to a claim when it is <see cref="Pending" />, or <see cref="Running" />
    ///     under a lease that has lapsed, and its due time has come. The three terminal states are the
    ///     only ones that carry a completion time.
    /// </remarks>
    public enum JobStatus
    {
        /// <summary>Queued: claimable once due. A failed attempt that will be retried returns here.</summary>
        Pending,

        /// <summary>Leased by one instance, whose handler is running it; claimable again once the lease lapses.</summary>
        Running,

        /// <summary>Ended: the handler completed the job. Terminal.</summary>
        Succeeded,

        /// <summary>Ended: the handler failed the job, or it ran out of attempts. Terminal until an administrator retries it.</summary>
        Failed,

        /// <summary>Ended: stopped on request, or its handler or owning row is gone. Terminal.</summary>
        Cancelled,

        /// <summary>
        ///     Quarantined after a service interruption: it was overdue by more than the gap threshold
        ///     when the worker came back, and waits for an administrator to resume or cancel it.
        /// </summary>
        Held,

        /// <summary>
        ///     Waiting for its initiator to confirm the warnings it raised; it then continues from its
        ///     last checkpoint.
        /// </summary>
        PendingConfirmation,
    }
}
