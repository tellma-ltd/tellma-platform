// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Outbox;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The preconditions the outgoing emails' delete and retry judge each row by, over every pair
    ///     of the row's state and its job's status: a row is settled in a final state, its job live
    ///     while it may still run, and a row abandoned when it is unsettled with no live job.
    /// </summary>
    public sealed class OutgoingEmailPreconditionTests
    {
        /// <summary>Every job status, and no job at all.</summary>
        private static readonly JobStatus?[] Jobs = [null, .. Enum.GetValues<JobStatus>().Cast<JobStatus?>()];

        /// <summary>The states no retry sends again: refused, delivered or withdrawn.</summary>
        private static readonly OutgoingEmailState[] NeverRetryable =
            [OutgoingEmailState.Rejected, OutgoingEmailState.Sent, OutgoingEmailState.Sandboxed, OutgoingEmailState.Cancelled];

        [Fact]
        public void The_settled_states_are_the_final_ones()
        {
            Assert.Equal(
                [OutgoingEmailState.Sent, OutgoingEmailState.Sandboxed, OutgoingEmailState.Rejected, OutgoingEmailState.Failed, OutgoingEmailState.Cancelled],
                Enum.GetValues<OutgoingEmailState>().Where(OutgoingEmailService.IsSettled));
        }

        [Fact]
        public void A_job_is_live_while_it_may_still_run()
        {
            Assert.Equal(
                new[] { JobStatus.Pending, JobStatus.Running, JobStatus.Held, JobStatus.PendingConfirmation }.Order(),
                Enum.GetValues<JobStatus>().Where(OutgoingEmailService.IsLive).Order());
        }

        [Fact]
        public void A_row_is_deletable_when_settled_or_abandoned_and_never_while_a_live_job_may_send_it()
        {
            foreach (OutgoingEmailState state in Enum.GetValues<OutgoingEmailState>())
            {
                foreach (JobStatus? job in Jobs)
                {
                    bool mayStillSend = !OutgoingEmailService.IsSettled(state) && job is JobStatus status && OutgoingEmailService.IsLive(status);
                    Assert.Equal(!mayStillSend, OutgoingEmailService.IsDeletable(state, job));
                }
            }
        }

        [Fact]
        public void A_row_is_retryable_when_failed_or_abandoned_with_no_live_job()
        {
            foreach (OutgoingEmailState state in Enum.GetValues<OutgoingEmailState>())
            {
                foreach (JobStatus? job in Jobs)
                {
                    bool noLiveJob = job is not JobStatus status || !OutgoingEmailService.IsLive(status);
                    bool expected = noLiveJob && state is OutgoingEmailState.Failed or OutgoingEmailState.Pending or OutgoingEmailState.Sending;
                    Assert.Equal(expected, OutgoingEmailService.IsRetryable(state, job));
                }
            }
        }

        [Fact]
        public void A_rejected_sent_or_cancelled_row_is_never_retryable()
        {
            Assert.All(
                NeverRetryable,
                static state => Assert.DoesNotContain(Jobs, job => OutgoingEmailService.IsRetryable(state, job)));
        }
    }
}
