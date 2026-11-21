// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §9.2: tellma.outbox.enqueued counts the rows that were enqueued, so it is recorded
    // once the frame the outbox enlisted them with committed, never for a frame that rolled back.
    /// <summary>
    ///     Counts the outgoing emails a committed save wrote on <c>tellma.outbox.enqueued</c>, each by
    ///     the source of the request the outbox built it from. The effect appends no statement.
    /// </summary>
    /// <param name="metrics">The outbox's instruments, which hold each enlisted row's source.</param>
    internal sealed class OutboxEnqueuedEffect(OutboxMetrics metrics) : IPersistEffect<OutgoingEmail>
    {
        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<OutgoingEmail> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task AfterCommitAsync(PersistOutcome<OutgoingEmail> outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome is SavePersistOutcome<OutgoingEmail> save)
            {
                metrics.RecordEnqueued(save.Entities);
            }

            return Task.CompletedTask;
        }
    }
}
