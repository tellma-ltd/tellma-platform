// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     Why the scheduler paused a schedule, which then fires nothing until a user acts on it.
    ///     Stored as the member's name, <c>varchar(14)</c>; null on a schedule that is not paused.
    /// </summary>
    public enum SchedulePausedReason
    {
        /// <summary>The user the schedule runs as is no longer active.</summary>
        OwnerInactive,

        /// <summary>The schedule's expression has no further occurrence.</summary>
        Exhausted,

        /// <summary>A feature retired the handler key the schedule names.</summary>
        HandlerRetired,
    }
}
