// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using System.Globalization;

namespace Tellma.Testing.WebHost
{
    /// <summary>
    ///     The host's fake clock, which also keeps the timers armed on it: when each next comes due, and
    ///     in what order they were armed. A test moves the clock to an instant once the host has armed
    ///     the timers it expects there, so a move never lands before the wait it is meant to end, and the
    ///     work a timer releases runs before the clock passes the next one, however slowly the host runs.
    /// </summary>
    /// <param name="start">The clock's first reading.</param>
    internal sealed class TestClock(DateTimeOffset start) : FakeTimeProvider(start)
    {
        /// <summary>Guards the timers and the arm count.</summary>
        private readonly Lock _gate = new();

        /// <summary>The timers created on the clock and not yet disposed.</summary>
        private readonly List<ArmedTimer> _timers = [];

        /// <summary>How many times a timer was armed, by its creation or by a change of its due time.</summary>
        private long _arms;

        /// <summary>
        ///     A mark in the clock's history of armed timers: the waits below count only the timers armed
        ///     after it, so a timer that was waiting before a scenario began does not stand in for one the
        ///     scenario arms.
        /// </summary>
        /// <returns>The mark.</returns>
        public long Mark()
        {
            lock (_gate)
            {
                return _arms;
            }
        }

        /// <summary>The number of timers armed after a mark that still wait, each next due at an instant.</summary>
        /// <param name="mark">The mark, from <see cref="Mark" />.</param>
        /// <param name="due">The instant.</param>
        /// <returns>The number.</returns>
        public int Waiting(long mark, DateTimeOffset due)
        {
            DateTimeOffset now = GetUtcNow();
            lock (_gate)
            {
                return _timers.Count(timer => timer.Sequence > mark && timer.NextDue(now) == due);
            }
        }

        /// <summary>
        ///     Waits until timers armed after a mark wait for an instant, then moves the clock to it,
        ///     which fires them and every other timer due by then.
        /// </summary>
        /// <param name="mark">The mark, from <see cref="Mark" /> or an earlier move.</param>
        /// <param name="due">The instant, after the clock's reading.</param>
        /// <param name="count">How many such timers to wait for.</param>
        /// <returns>A mark taken just before the move, for the timers the work it releases arms next.</returns>
        /// <exception cref="TimeoutException">The timers were not armed within the test's patience.</exception>
        public async Task<long> AdvanceToAsync(long mark, DateTimeOffset due, int count = 1)
        {
            await Patience.UntilAsync(
                () => Waiting(mark, due) >= count,
                string.Create(CultureInfo.InvariantCulture, $"{count} timer(s) armed to come due at {due:O}"));

            // The mark comes first: the move runs the timers' callbacks on this thread, and they may
            // arm the next timers before it returns.
            long moved = Mark();
            Advance(due - GetUtcNow());
            return moved;
        }

        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // The timer is recorded before the clock arms it: a timer due at once fires inside the
            // clock's own creation, and is then simply no longer waiting.
            ArmedTimer timer = new(this);
            timer.Arm(dueTime, period);
            timer.Inner = base.CreateTimer(callback, state, dueTime, period);
            return timer;
        }

        /// <summary>Records an arming of a timer, adding it to the timers the clock keeps.</summary>
        /// <param name="timer">The timer.</param>
        /// <param name="dueTime">How long from now it comes due; infinite when disarmed.</param>
        /// <param name="period">How often it comes due again after that; infinite or zero for once.</param>
        private void Record(ArmedTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            DateTimeOffset now = GetUtcNow();
            lock (_gate)
            {
                timer.Sequence = ++_arms;
                timer.Due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
                timer.Period = period;
                if (!_timers.Contains(timer))
                {
                    _timers.Add(timer);
                }
            }
        }

        /// <summary>Forgets a disposed timer.</summary>
        /// <param name="timer">The timer.</param>
        private void Forget(ArmedTimer timer)
        {
            lock (_gate)
            {
                _ = _timers.Remove(timer);
            }
        }

        /// <summary>A timer of the clock, recorded each time it is armed and forgotten once disposed.</summary>
        /// <param name="clock">The clock that keeps it.</param>
        private sealed class ArmedTimer(TestClock clock) : ITimer
        {
            /// <summary>The fake clock's own timer, which fires the callback.</summary>
            public ITimer? Inner { get; set; }

            /// <summary>The arm count when the timer was last armed.</summary>
            public long Sequence { get; set; }

            /// <summary>When the timer first comes due after it was last armed; null when disarmed.</summary>
            public DateTimeOffset? Due { get; set; }

            /// <summary>How often it comes due again after that; infinite or zero for once.</summary>
            public TimeSpan Period { get; set; }

            /// <summary>When the timer next comes due after a reading of the clock.</summary>
            /// <param name="now">The reading.</param>
            /// <returns>The instant, or null when it no longer waits: disarmed, or fired once and not periodic.</returns>
            public DateTimeOffset? NextDue(DateTimeOffset now)
            {
                if (Due is not { } due)
                {
                    return null;
                }

                if (due > now)
                {
                    return due;
                }

                if (Period <= TimeSpan.Zero || Period == Timeout.InfiniteTimeSpan)
                {
                    return null;
                }

                // A periodic timer has fired every period since it first came due.
                long elapsed = ((now - due).Ticks / Period.Ticks) + 1;
                return due + TimeSpan.FromTicks(Period.Ticks * elapsed);
            }

            /// <summary>Records the arming, then lets the clock's own timer arm.</summary>
            /// <param name="dueTime">How long from now it comes due.</param>
            /// <param name="period">How often it comes due again.</param>
            public void Arm(TimeSpan dueTime, TimeSpan period)
            {
                clock.Record(this, dueTime, period);
            }

            /// <inheritdoc />
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Arm(dueTime, period);
                return Inner!.Change(dueTime, period);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                clock.Forget(this);
                Inner?.Dispose();
            }

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                clock.Forget(this);
                return Inner?.DisposeAsync() ?? ValueTask.CompletedTask;
            }
        }
    }
}
