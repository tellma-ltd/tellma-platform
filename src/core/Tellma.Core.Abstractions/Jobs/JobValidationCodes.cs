// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The codes the jobs' stack refuses an action with, each at the target row it judges. Each is
    ///     a resource key the client renders; none takes arguments.
    /// </summary>
    public static class JobValidationCodes
    {
        /// <summary>The job has not failed, so there is nothing to retry.</summary>
        public const string NotRetryable = "Jobs.NotRetryable";

        /// <summary>The job has already ended, so there is nothing to cancel.</summary>
        public const string NotCancellable = "Jobs.NotCancellable";

        /// <summary>The job is not paused, so there is nothing to resume.</summary>
        public const string NotHeld = "Jobs.NotHeld";

        /// <summary>
        ///     The job is not waiting for confirmation, or the confirmation leaves out a warning it
        ///     waits on.
        /// </summary>
        public const string NotPendingConfirmation = "Jobs.NotPendingConfirmation";

        /// <summary>Only the user who started the job confirms its warnings.</summary>
        public const string NotInitiator = "Jobs.NotInitiator";
    }
}
