// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §5: the handler's registration and policy. The job queue refuses a key no handler on
    // the instance registers, so the enqueue of spec 0022 §3.2 step 5 needs the handler composed
    // before the dispatch itself is built. Until then every claim fails closed: nothing is sent, the
    // row stays Pending, and the worker retries the job with backoff until its attempts run out,
    // which the administrators learn of from the job's failure notice in their inbox.
    /// <summary>
    ///     The handler of the jobs that send outgoing emails: one job per row, in batches of 25, one
    ///     claim at a time per tenant, twelve attempts with backoff from one minute to two hours, run
    ///     on read-only tenants too, with its failure notices kept to the inbox.
    /// </summary>
    /// <remarks>
    ///     The dispatch is not built yet: every claim fails without sending, so each email stays
    ///     pending until the dispatch replaces this body.
    /// </remarks>
    [JobHandler(
        OutgoingEmailService.SendHandlerKey,
        BatchSize = 25,
        LeaseSeconds = 120,
        MaxConcurrency = 1,
        MaxAttempts = 12,
        RetryBaseSeconds = 60,
        RetryMaxSeconds = 7200,
        InboxOnlyFailureNotices = true,
        RunsWhenReadOnly = true)]
    internal sealed class OutgoingEmailHandler : IEntityJobHandler<OutgoingEmail>
    {
        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always: the dispatch is not built yet.</exception>
        public Task ExecuteAsync(JobBatch<OutgoingEmail> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            throw new NotSupportedException("Outgoing emails are not dispatched yet: the job is retried with backoff and its email stays pending.");
        }
    }
}
