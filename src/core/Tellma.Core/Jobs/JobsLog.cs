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
    }
}
