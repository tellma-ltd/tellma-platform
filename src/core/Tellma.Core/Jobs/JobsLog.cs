// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Jobs
{
    /// <summary>Source-generated log messages of background jobs and the scheduler.</summary>
    /// <remarks>Tenant, job and user ids reach these events through the log scope, never as parameters.</remarks>
    internal static partial class JobsLog
    {
        /// <summary>The category every job machinery event is logged under.</summary>
        public const string Category = "Tellma.Core.Jobs";

        /// <summary>
        ///     A built-in schedule names a key no handler on this host registers; its jobs wait for a
        ///     host that runs the handler.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="scheduleId">The built-in schedule's id.</param>
        /// <param name="handlerKey">The key it names.</param>
        [LoggerMessage(
            EventName = "SchedulerHandlerUnknown",
            Level = LogLevel.Warning,
            Message = "The built-in schedule {ScheduleId} names the handler key {HandlerKey}, which no handler on this host registers; its jobs wait for a host that runs the handler.")]
        public static partial void SchedulerHandlerUnknown(ILogger logger, int scheduleId, string handlerKey);

        /// <summary>Waking this host's poller after an enqueue failed; the poller claims the jobs on its next poll.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The wake's exception.</param>
        [LoggerMessage(
            EventName = "JobNudgeFailed",
            Level = LogLevel.Warning,
            Message = "Waking the job poller after an enqueue failed; the jobs are claimed on its next poll.")]
        public static partial void JobNudgeFailed(ILogger logger, Exception exception);

        /// <summary>A claim leased jobs of a handler; the jobs' ids ride the log scope.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="count">The jobs leased.</param>
        [LoggerMessage(
            EventName = "JobClaimed",
            Level = LogLevel.Debug,
            Message = "Claimed {Count} job(s) of {HandlerKey}.")]
        public static partial void JobClaimed(ILogger logger, string handlerKey, int count);

        /// <summary>A handler reported progress for a job.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="percent">The percent reported; null when unchanged.</param>
        /// <param name="message">The progress message rendered in English; null when unchanged.</param>
        [LoggerMessage(
            EventName = "JobProgress",
            Level = LogLevel.Debug,
            Message = "A job of {HandlerKey} reported progress {Percent}%: {Message}")]
        public static partial void JobProgress(ILogger logger, string handlerKey, byte? percent, string? message);

        /// <summary>A completion recorded a job's outcome; the error is rendered in English for a failure or a retry that records one.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="outcome">The outcome.</param>
        /// <param name="durationMs">How long the job's attempt took, from its claim to its completion, in milliseconds.</param>
        /// <param name="error">The recorded error rendered in English; null when none.</param>
        [LoggerMessage(
            EventName = "JobCompleted",
            Level = LogLevel.Information,
            Message = "A job of {HandlerKey} completed {Outcome} after {DurationMs} ms. {Error}")]
        public static partial void JobCompleted(ILogger logger, string handlerKey, string outcome, double durationMs, string? error);

        /// <summary>
        ///     A handler threw an exception the outcome rules do not expect; its unmarked jobs retry with
        ///     the internal error, whose recorded trace id is this event's.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">What the handler threw.</param>
        /// <param name="handlerKey">The handler key.</param>
        [LoggerMessage(
            EventName = "JobHandlerFailed",
            Level = LogLevel.Error,
            Message = "The handler of {HandlerKey} threw; its unmarked jobs retry with the internal error.")]
        public static partial void JobHandlerFailed(ILogger logger, Exception exception, string handlerKey);

        /// <summary>
        ///     A completion failed: its handler statements and enlisted writes are discarded and a
        ///     completion carrying the outcomes alone follows, or its lease is left to lapse.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The completion's failure.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="next">What follows the failure.</param>
        [LoggerMessage(
            EventName = "JobCompletionFailed",
            Level = LogLevel.Error,
            Message = "The completion of jobs of {HandlerKey} failed; {Next}")]
        public static partial void JobCompletionFailed(ILogger logger, Exception exception, string handlerKey, string next);

        /// <summary>The lease of a claim was lost: another instance owns its rows, and the batch's outcomes are discarded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        [LoggerMessage(
            EventName = "JobLeaseLost",
            Level = LogLevel.Warning,
            Message = "The lease of jobs of {HandlerKey} was lost; their outcomes are discarded and another claim runs them again.")]
        public static partial void JobLeaseLost(ILogger logger, string handlerKey);

        /// <summary>The scope a handler runs in could not be opened; the lease is left to lapse and the jobs run again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The refusal.</param>
        /// <param name="handlerKey">The handler key.</param>
        [LoggerMessage(
            EventName = "JobScopeUnavailable",
            Level = LogLevel.Warning,
            Message = "The scope of jobs of {HandlerKey} could not be opened; their lease is left to lapse and they run again once it does.")]
        public static partial void JobScopeUnavailable(ILogger logger, Exception exception, string handlerKey);

        /// <summary>The administrators a notice of a job without a requester goes to could not be read; the notice is not raised.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="handlerKey">The handler key.</param>
        [LoggerMessage(
            EventName = "JobNoticeRecipientsFailed",
            Level = LogLevel.Warning,
            Message = "The administrators to notify about jobs of {HandlerKey} could not be read; the notices are not raised.")]
        public static partial void JobNoticeRecipientsFailed(ILogger logger, Exception exception, string handlerKey);

        /// <summary>A poll of a tenant failed; the poller polls again after the longest interval.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="retryAfter">How long the poller waits before it polls again.</param>
        [LoggerMessage(
            EventName = "JobPollFailed",
            Level = LogLevel.Error,
            Message = "A poll of the tenant's jobs failed; it is polled again in {RetryAfter}.")]
        public static partial void JobPollFailed(ILogger logger, Exception exception, TimeSpan retryAfter);

        /// <summary>
        ///     A poll found the worker had been away from the tenant past the gap threshold; the jobs
        ///     overdue by more than it are held for an administrator.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="previousTickAt">When a poll last stamped the tenant.</param>
        /// <param name="now">The database's clock.</param>
        /// <param name="heldCount">The jobs held.</param>
        [LoggerMessage(
            EventName = "SchedulerGapDetected",
            Level = LogLevel.Warning,
            Message = "The tenant was last polled at {PreviousTickAt:O}, more than the gap threshold before {Now:O}; {HeldCount} overdue job(s) are held for an administrator to resume or cancel.")]
        public static partial void SchedulerGapDetected(ILogger logger, DateTimeOffset previousTickAt, DateTimeOffset now, int heldCount);

        /// <summary>
        ///     A handler ignored a cancel for a further lease length, or was still running when the host
        ///     stopped: its task runs on, its lease is no longer renewed, and its jobs are claimed again
        ///     once the lease lapses.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="why">Why the handler was abandoned.</param>
        [LoggerMessage(
            EventName = "JobHandlerAbandoned",
            Level = LogLevel.Warning,
            Message = "The handler of {HandlerKey} was abandoned: {Why} Its lease is no longer renewed, and its jobs are claimed again once it lapses.")]
        public static partial void JobHandlerAbandoned(ILogger logger, string handlerKey, string why);

        /// <summary>The host stopped before every running handler returned; those still running keep their lease, renewed one last time.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="drainTimeout">How long the worker waited.</param>
        /// <param name="running">The claims whose handlers are still running.</param>
        [LoggerMessage(
            EventName = "WorkerDrainTimedOut",
            Level = LogLevel.Warning,
            Message = "The job worker stopped after waiting {DrainTimeout} for its handlers; {Running} claim(s) still running keep their lease until it lapses after the process exits.")]
        public static partial void WorkerDrainTimedOut(ILogger logger, TimeSpan drainTimeout, int running);

        /// <summary>A renewal of the tenant's leases failed; the leases wait for the next renewal.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "JobRenewalFailed",
            Level = LogLevel.Warning,
            Message = "Renewing the tenant's job leases failed; they wait for the next renewal.")]
        public static partial void JobRenewalFailed(ILogger logger, Exception exception);

        /// <summary>A poll stamped the tenant's heartbeat, at most once a minute; the log-based staleness alert reads it.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="stampedAt">The stamp, the database's clock.</param>
        [LoggerMessage(
            EventName = "JobWorkerHeartbeat",
            Level = LogLevel.Information,
            Message = "The job worker stamped the tenant's heartbeat at {StampedAt:O}.")]
        public static partial void JobWorkerHeartbeat(ILogger logger, DateTimeOffset stampedAt);

        /// <summary>The worker stopped polling a tenant that left the states it serves; its heartbeat stops by design.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="state">The tenant's state now; null for a tenant that left the registry.</param>
        [LoggerMessage(
            EventName = "JobPollerRetired",
            Level = LogLevel.Information,
            Message = "The job worker stopped polling the tenant, now {State}.")]
        public static partial void JobPollerRetired(ILogger logger, string state);

        /// <summary>The retired-key cancel ended jobs of keys no feature offers any longer.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">The jobs cancelled.</param>
        [LoggerMessage(
            EventName = "JobRetiredKeysCancelled",
            Level = LogLevel.Information,
            Message = "Cancelled {Count} job(s) of retired handler keys.")]
        public static partial void JobRetiredKeysCancelled(ILogger logger, int count);

        /// <summary>Work that follows a poll's round trip failed: the notices it raises or withdraws, or its instruments and log lines; the poll's own work stands.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="what">Which work.</param>
        [LoggerMessage(
            EventName = "JobPollNoticesFailed",
            Level = LogLevel.Warning,
            Message = "The {What} that follow a poll of the tenant failed; the poll's own work stands.")]
        public static partial void JobPollNoticesFailed(ILogger logger, Exception exception, string what);

        /// <summary>Bringing the pollers in line with the tenants' snapshot failed; the next attempt follows on the catalog's refresh cadence.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="retryAfter">When the next attempt follows.</param>
        [LoggerMessage(
            EventName = "JobSynchroniseFailed",
            Level = LogLevel.Error,
            Message = "Bringing the job pollers in line with the tenants failed; they are brought in line again in {RetryAfter}.")]
        public static partial void JobSynchroniseFailed(ILogger logger, Exception exception, TimeSpan retryAfter);

        /// <summary>A claim's rows were released uncounted, the instance having no free slot to run them in; its tenant polls again once a slot is freed.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="handlerKey">The handler key.</param>
        /// <param name="count">The jobs released.</param>
        [LoggerMessage(
            EventName = "JobClaimReleased",
            Level = LogLevel.Debug,
            Message = "Released {Count} claimed job(s) of {HandlerKey} uncounted: the instance had no free slot to run them.")]
        public static partial void JobClaimReleased(ILogger logger, string handlerKey, int count);

        /// <summary>A claim's batch ended in a failure its runner did not answer; its leases are left to lapse.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="handlerKey">The handler key.</param>
        [LoggerMessage(
            EventName = "JobBatchFailed",
            Level = LogLevel.Error,
            Message = "A batch of {HandlerKey} ended in an unexpected failure; its leases are left to lapse and its jobs run again once they do.")]
        public static partial void JobBatchFailed(ILogger logger, Exception exception, string handlerKey);
    }
}
