// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The names of the job worker's and the scheduler's instruments, their activity source and their
    ///     tags. Dashboards and alerts key on these strings, so they never change.
    /// </summary>
    /// <remarks>
    ///     Every tag takes a bounded set of values: <see cref="HandlerTag" /> a registered handler key,
    ///     <see cref="OutcomeTag" /> one of <c>succeeded</c>, <c>retry</c>, <c>failed</c>,
    ///     <c>released</c>, <c>cancelled</c> and <c>pending_confirmation</c>, and
    ///     <see cref="PolicyTag" /> one of <c>coalesce</c>, <c>replay_all</c> and <c>skip</c>. The
    ///     tenant, the job and the user are never tags; they go to the log scope.
    /// </remarks>
    public static class JobsTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>The activity source of the worker's spans: one poll, one claim, one handler invocation, one completion.</summary>
        public const string ActivitySourceName = "Tellma.Core";

        /// <summary>Counter (<c>{job}</c>): jobs claimed, the throughput. Tagged <see cref="HandlerTag" />.</summary>
        public const string Claimed = "tellma.jobs.claimed";

        /// <summary>Counter (<c>{job}</c>): jobs completed, by outcome; the failure and dead-letter rate. Tagged <see cref="HandlerTag" /> and <see cref="OutcomeTag" />.</summary>
        public const string Completed = "tellma.jobs.completed";

        /// <summary>Histogram (<c>s</c>): how long a handler invocation ran, for sizing a handler's lease. Tagged <see cref="HandlerTag" />.</summary>
        public const string Duration = "tellma.jobs.duration";

        /// <summary>
        ///     Histogram (<c>s</c>): how long a job waited past its due time before its first claim; a
        ///     backed-up queue or a poll interval too long. Tagged <see cref="HandlerTag" />.
        /// </summary>
        public const string QueueLatency = "tellma.jobs.queue.latency";

        /// <summary>Histogram (<c>1</c>): the jobs a claim leased over the handler's batch size. Tagged <see cref="HandlerTag" />.</summary>
        public const string BatchFill = "tellma.jobs.batch_fill";

        /// <summary>Up-down counter (<c>{job}</c>): jobs running on this instance. Tagged <see cref="HandlerTag" />.</summary>
        public const string InFlight = "tellma.jobs.in_flight";

        /// <summary>Counter (<c>{batch}</c>): batches whose lease was lost, handlers slower than their lease. Tagged <see cref="HandlerTag" />.</summary>
        public const string LeaseLost = "tellma.jobs.lease_lost";

        /// <summary>Histogram (<c>s</c>): the age of the oldest queued job, sampled per minute; starvation. Tagged <see cref="HandlerTag" />.</summary>
        public const string BacklogAge = "tellma.jobs.backlog.age";

        /// <summary>Histogram (<c>s</c>): how long one tenant's poll round trip took. Untagged.</summary>
        public const string PollDuration = "tellma.jobs.poll.duration";

        /// <summary>
        ///     Observable gauge (<c>{job}</c>): queued jobs overdue by an hour whose key no handler on this
        ///     instance registers and no feature retires, summed over this instance's latest sample of
        ///     each tenant. Untagged.
        /// </summary>
        public const string Orphaned = "tellma.jobs.orphaned";

        /// <summary>Counter (<c>{job}</c>): jobs schedules fired. Tagged <see cref="PolicyTag" />.</summary>
        public const string SchedulesFired = "tellma.schedules.fired";

        /// <summary>Counter (<c>{occurrence}</c>): occurrences dropped or coalesced within the gap threshold. Tagged <see cref="PolicyTag" />.</summary>
        public const string SchedulesMissed = "tellma.schedules.missed";

        /// <summary>Counter (<c>{firing}</c>): firings skipped because the schedule's previous job was still active. Untagged.</summary>
        public const string SchedulesOverlapSkipped = "tellma.schedules.overlap_skipped";

        /// <summary>Counter (<c>{tick}</c>): ticks that found the scheduler had been away past the gap threshold, a restore or a long outage. Untagged.</summary>
        public const string SchedulesGapDetected = "tellma.schedules.gap_detected";

        /// <summary>The handler tag: the job's registered handler key.</summary>
        public const string HandlerTag = "handler";

        /// <summary>The outcome tag: how a completed job ended.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>The policy tag: the missed policy a schedule fired under.</summary>
        public const string PolicyTag = "policy";
    }
}
