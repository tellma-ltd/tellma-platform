// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.RateLimiting
{
    /// <summary>
    ///     Fixed-window rate-limit counters behind a swappable seam (SQL now, a distributed
    ///     cache later without touching callers).
    /// </summary>
    public interface IRateLimitCounterStore
    {
        /// <summary>Increments a counter and returns its value within the current window.</summary>
        /// <param name="key">The counter key, for example <c>emailcode:user:&lt;id&gt;</c>.</param>
        /// <param name="window">The fixed window length.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The count within the current window, including this increment.</returns>
        Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken);
    }
}
