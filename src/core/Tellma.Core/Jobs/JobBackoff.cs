// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Jobs
{
    // Spec 0020 §5.5: full jitter, U(0, min(RetryMaxSeconds, RetryBaseSeconds × 2^(Attempts − 1))),
    // applied to the database clock as a duration.
    /// <summary>The delay before a job retried without a delay of its own is due again.</summary>
    internal static class JobBackoff
    {
        /// <summary>The exponent past which the doubling exceeds every ceiling a handler can declare.</summary>
        private const int MaxShift = 32;

        /// <summary>The ceiling of the delay after an attempt: the base doubled per earlier attempt, at most the maximum.</summary>
        /// <param name="attempts">The job's attempts, the current one included: at least 1.</param>
        /// <param name="baseSeconds">The handler's base delay, in seconds: at least 1.</param>
        /// <param name="maxSeconds">The handler's longest delay, in seconds: at least the base.</param>
        /// <returns>The ceiling, in seconds.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An argument is outside its range.</exception>
        public static int CeilingSeconds(int attempts, int baseSeconds, int maxSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(baseSeconds, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxSeconds, baseSeconds);

            // The base is below 2^31 and the shift at most 31, so the product fits a long.
            int shift = attempts - 1;
            return shift >= MaxShift ? maxSeconds : (int)Math.Min(maxSeconds, (long)baseSeconds << shift);
        }

        /// <summary>A delay drawn uniformly from zero to the ceiling, both included.</summary>
        /// <param name="attempts">The job's attempts, the current one included: at least 1.</param>
        /// <param name="baseSeconds">The handler's base delay, in seconds.</param>
        /// <param name="maxSeconds">The handler's longest delay, in seconds.</param>
        /// <param name="random">The source of the jitter.</param>
        /// <returns>The delay, in seconds.</returns>
        public static int Seconds(int attempts, int baseSeconds, int maxSeconds, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);
            int ceiling = CeilingSeconds(attempts, baseSeconds, maxSeconds);
            return (int)random.NextInt64(0, (long)ceiling + 1);
        }

        /// <summary>A delay a handler supplied, in whole seconds, rounded up so a short delay never runs at once.</summary>
        /// <param name="delay">The delay, already within the longest an explicit retry may ask for.</param>
        /// <returns>The delay, in seconds.</returns>
        public static int SuppliedSeconds(TimeSpan delay)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
            return (int)Math.Ceiling(delay.TotalSeconds);
        }
    }
}
