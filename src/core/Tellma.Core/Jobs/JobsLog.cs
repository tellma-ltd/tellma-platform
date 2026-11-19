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
    }
}
