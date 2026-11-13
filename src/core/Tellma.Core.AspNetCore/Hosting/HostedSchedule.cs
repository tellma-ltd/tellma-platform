// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The schedule of a hosted timer, kept as due times rather than as a period: each wait is for a
    ///     moment on the schedule, so a wait that starts late still ends when the schedule says, and the
    ///     time a run takes does not push the later runs back.
    /// </summary>
    internal static class HostedSchedule
    {
        /// <summary>Waits until a due time.</summary>
        /// <param name="due">When the wait ends.</param>
        /// <param name="time">The clock.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes at the due time; already complete when the time has come.</returns>
        internal static Task DelayUntil(DateTimeOffset due, TimeProvider time, CancellationToken cancellationToken)
        {
            TimeSpan wait = due - time.GetUtcNow();
            return wait > TimeSpan.Zero ? Task.Delay(wait, time, cancellationToken) : Task.CompletedTask;
        }

        /// <summary>
        ///     The due time of the run after one that was due at <paramref name="due" />: an interval on.
        ///     When the run overran whole intervals, the slots it overran collapse into the last of them,
        ///     which is due at once, and the schedule goes on from there.
        /// </summary>
        /// <param name="due">When the run that just ended was due.</param>
        /// <param name="interval">The schedule's interval.</param>
        /// <param name="now">The time now.</param>
        /// <returns>When the next run is due.</returns>
        internal static DateTimeOffset Next(DateTimeOffset due, TimeSpan interval, DateTimeOffset now)
        {
            // Whole intervals since the last due time; none (or a clock that went back) means one on.
            long passed = (now - due).Ticks / interval.Ticks;
            return due + TimeSpan.FromTicks(interval.Ticks * Math.Max(1, passed));
        }
    }
}
