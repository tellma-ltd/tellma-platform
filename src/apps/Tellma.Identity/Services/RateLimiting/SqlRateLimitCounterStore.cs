// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.RateLimiting
{
    /// <summary>The SQL-backed counter store: one row per key and fixed window.</summary>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class SqlRateLimitCounterStore(TellmaIdentityDbContext context, TimeProvider timeProvider) : IRateLimitCounterStore
    {
        /// <inheritdoc />
        public async Task<int> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

            DateTimeOffset now = timeProvider.GetUtcNow();
            DateTimeOffset windowStart = new(now.UtcTicks / window.Ticks * window.Ticks, TimeSpan.Zero);

            // Fast path: bump the existing window row atomically.
            int updated = await context.Set<RateLimitCounter>()
                .Where(counter => counter.Key == key && counter.WindowStartUtc == windowStart)
                .ExecuteUpdateAsync(
                    static setters => setters.SetProperty(static c => c.Count, static c => c.Count + 1),
                    cancellationToken);
            if (updated > 0)
            {
                RateLimitCounter row = await context.Set<RateLimitCounter>()
                    .AsNoTracking()
                    .FirstAsync(counter => counter.Key == key && counter.WindowStartUtc == windowStart, cancellationToken);
                return row.Count;
            }

            // First event in the window: insert, tolerating a concurrent winner.
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<RateLimitCounter> entry =
                context.Set<RateLimitCounter>().Add(new RateLimitCounter { Key = key, WindowStartUtc = windowStart, Count = 1 });
            try
            {
                await context.SaveChangesAsync(cancellationToken);

                // Opportunistic cleanup of the key's expired windows.
                await context.Set<RateLimitCounter>()
                    .Where(counter => counter.Key == key && counter.WindowStartUtc < windowStart)
                    .ExecuteDeleteAsync(cancellationToken);

                return 1;
            }
            catch (DbUpdateException)
            {
                // A concurrent request created the row first. Detach only this failed insert — not
                // the whole tracker — so a caller's other pending changes are never discarded, then
                // retry the atomic bump.
                entry.State = EntityState.Detached;
                await context.Set<RateLimitCounter>()
                    .Where(counter => counter.Key == key && counter.WindowStartUtc == windowStart)
                    .ExecuteUpdateAsync(
                        static setters => setters.SetProperty(static c => c.Count, static c => c.Count + 1),
                        cancellationToken);
                RateLimitCounter row = await context.Set<RateLimitCounter>()
                    .AsNoTracking()
                    .FirstAsync(counter => counter.Key == key && counter.WindowStartUtc == windowStart, cancellationToken);
                return row.Count;
            }
        }
    }
}
