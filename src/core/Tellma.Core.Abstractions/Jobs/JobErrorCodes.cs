// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The error codes the job machinery records on a job, and the code its fenced statements throw
    ///     when a lease no longer holds. A job's error is one of these, a handler's own code or a
    ///     validation code; each is a resource key the job pages render with its arguments.
    /// </summary>
    public static class JobErrorCodes
    {
        /// <summary>
        ///     The handler threw an exception the outcome rules do not name, or its completion failed for
        ///     a reason other than a validation error; the job is retried. The error carries no
        ///     arguments, and its trace id finds the logged exception.
        /// </summary>
        public const string Internal = "Jobs.Internal";

        /// <summary>The job was stopped: a cancel was requested, or retention stopped a job that waited too long.</summary>
        public const string Cancelled = "Jobs.Cancelled";

        /// <summary>The row an entity-backed job works on no longer exists, so the job was stopped without running its handler.</summary>
        public const string EntityMissing = "Jobs.EntityMissing";

        /// <summary>The job used up its attempts without succeeding.</summary>
        public const string AttemptsExhausted = "Jobs.AttemptsExhausted";

        /// <summary>The user the job runs as is no longer an active member of the tenant, so its handler was not run.</summary>
        public const string UserInactive = "Jobs.UserInactive";

        /// <summary>
        ///     The job raised more distinct warnings than one confirmation can cover; its argument
        ///     <c>count</c> is the number of distinct warning keys.
        /// </summary>
        public const string TooManyConfirmations = "Jobs.TooManyConfirmations";

        /// <summary>The job was overdue when the worker came back after a long interruption, and waits for an administrator.</summary>
        public const string HeldAfterGap = "Jobs.HeldAfterGap";

        /// <summary>The job's handler was retired, so the job was stopped.</summary>
        public const string HandlerRetired = "Jobs.HandlerRetired";

        /// <summary>
        ///     The code a fenced job statement throws, as <see cref="TellmaSqlErrors.Invariant" />, when a
        ///     job it addresses is no longer leased under the statement's token: another instance owns
        ///     it now, and nothing the statement's transaction wrote survives.
        /// </summary>
        public const string LeaseLost = "Job.LeaseLost";

        /// <summary>
        ///     The code the scheduler's second tick statement throws, as <see cref="TellmaSqlErrors.Invariant" />,
        ///     when a schedule it advances is no longer leased under the tick's token: the lease lapsed,
        ///     or a save of the schedule cleared it. Nothing the statement's transaction wrote survives,
        ///     and the next tick recomputes the schedules from their stored state.
        /// </summary>
        public const string TickLeaseLost = "Schedule.TickLeaseLost";
    }
}
