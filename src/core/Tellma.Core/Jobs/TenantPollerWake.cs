// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The seam through which an enqueue on this instance wakes this instance's poller for the
    ///     tenant, so a job enqueued here is claimed without waiting for the next poll. Pollers
    ///     subscribe per tenant; a wake reaches every subscriber of that tenant and nothing else.
    ///     Other instances see the jobs on their next poll. A singleton.
    /// </summary>
    /// <remarks>
    ///     A wake never throws: a subscriber that throws is logged and the remaining subscribers are
    ///     still woken. A wake for a tenant with no subscriber does nothing, as on an instance that
    ///     runs no worker.
    /// </remarks>
    internal sealed class TenantPollerWake
    {
        /// <summary>The logger a failing subscriber is reported to.</summary>
        private readonly ILogger _logger;

        /// <summary>Guards <see cref="_subscribers" />.</summary>
        private readonly Lock _gate = new();

        /// <summary>The subscribers by tenant id; each list is replaced, never mutated, so a wake reads a stable copy.</summary>
        private readonly Dictionary<int, Subscription[]> _subscribers = [];

        /// <summary>Creates the seam.</summary>
        /// <param name="loggers">Creates the logger of the job machinery's category.</param>
        public TenantPollerWake(ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(loggers);
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>Subscribes a poller to the wakes of one tenant.</summary>
        /// <param name="tenantId">The tenant whose enqueues wake the poller.</param>
        /// <param name="wake">
        ///     Called on every wake of the tenant, on the enqueuing thread after its batch committed;
        ///     it signals the poller and returns at once.
        /// </param>
        /// <returns>A handle whose disposal ends the subscription.</returns>
        public IDisposable Subscribe(int tenantId, Action wake)
        {
            ArgumentNullException.ThrowIfNull(wake);

            Subscription subscription = new(this, tenantId, wake);
            lock (_gate)
            {
                // --- Copy on write: a wake in flight keeps the array it read ---
                Subscription[] current = _subscribers.GetValueOrDefault(tenantId, []);
                _subscribers[tenantId] = [.. current, subscription];
            }

            return subscription;
        }

        /// <summary>Wakes every poller subscribed to the tenant; a failing one is logged, never thrown.</summary>
        /// <param name="tenantId">The tenant whose jobs were enqueued.</param>
        public void Wake(int tenantId)
        {
            Subscription[] subscribers;
            lock (_gate)
            {
                subscribers = _subscribers.GetValueOrDefault(tenantId, []);
            }

            foreach (Subscription subscriber in subscribers)
            {
                try
                {
                    subscriber.Wake();
                }
                catch (Exception exception)
                {
                    // The jobs are committed; the poller claims them on its next poll anyway.
                    JobsLog.JobNudgeFailed(_logger, exception);
                }
            }
        }

        /// <summary>Removes one subscription; removing it twice does nothing.</summary>
        /// <param name="subscription">The subscription.</param>
        private void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                if (!_subscribers.TryGetValue(subscription.TenantId, out Subscription[]? current))
                {
                    return;
                }

                Subscription[] remaining = [.. current.Where(existing => !ReferenceEquals(existing, subscription))];
                if (remaining.Length == 0)
                {
                    _ = _subscribers.Remove(subscription.TenantId);
                }
                else
                {
                    _subscribers[subscription.TenantId] = remaining;
                }
            }
        }

        /// <summary>One poller's subscription to one tenant's wakes.</summary>
        /// <param name="owner">The seam it is registered with.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="wake">The poller's signal.</param>
        private sealed class Subscription(TenantPollerWake owner, int tenantId, Action wake) : IDisposable
        {
            /// <summary>The tenant.</summary>
            public int TenantId { get; } = tenantId;

            /// <summary>Signals the poller.</summary>
            public void Wake()
            {
                wake();
            }

            /// <inheritdoc />
            public void Dispose()
            {
                owner.Remove(this);
            }
        }
    }
}
