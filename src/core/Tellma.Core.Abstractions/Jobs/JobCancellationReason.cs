// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Why a job item's cancellation token was cancelled, which decides what becomes of an item the
    ///     handler leaves unmarked.
    /// </summary>
    /// <remarks>
    ///     A handler stopping for <see cref="HostStopping" /> or <see cref="LeaseLost" /> throws
    ///     <see cref="OperationCanceledException" /> and never returns normally, since a normal return
    ///     completes its unmarked items as succeeded with the work half done. A handler stopping for
    ///     <see cref="CancelRequested" /> may still record the stop on its owning row: the job's round
    ///     trips are not cancelled for a requested stop.
    /// </remarks>
    public enum JobCancellationReason
    {
        /// <summary>The token has not been cancelled.</summary>
        None,

        /// <summary>
        ///     Another instance may own the job now: a renewal no longer found it leased under this
        ///     instance's token. Nothing the handler wrote will be kept. Once set, the reason never
        ///     changes again.
        /// </summary>
        LeaseLost,

        /// <summary>Someone asked to stop the job; an item the handler leaves unmarked ends cancelled.</summary>
        CancelRequested,

        /// <summary>The host is shutting down; an item the handler never touched is released for another instance.</summary>
        HostStopping,
    }
}
