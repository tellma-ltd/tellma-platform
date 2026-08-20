// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;

namespace Tellma.Identity.Services.RateLimiting
{
    /// <summary>An in-memory counter store for unit tests and single-instance development.</summary>
    /// <param name="timeProvider">The clock.</param>
    public sealed class InMemoryRateLimitCounterStore(TimeProvider timeProvider) : IRateLimitCounterStore
    {
        private readonly ConcurrentDictionary<(string Key, long WindowStart), int> _counters = new();

        /// <inheritdoc />
        public Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

            long windowStart = timeProvider.GetUtcNow().UtcTicks / window.Ticks * window.Ticks;
            int count = _counters.AddOrUpdate((key, windowStart), 1, static (_, current) => current + 1);
            return Task.FromResult(count);
        }
    }
}
