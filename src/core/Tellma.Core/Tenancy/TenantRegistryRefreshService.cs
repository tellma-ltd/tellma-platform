// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Loads the tenant registry at startup and refreshes it every refresh interval: a
    ///     <see cref="PeriodicTimer" /> over the host's <see cref="TimeProvider" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Start awaits the first load, bounded: a catalog that is behind fails startup, and one
    ///         that cannot be reached lets the host start not ready. The loop then keeps refreshing;
    ///         a failed refresh is logged by the registry and the loop goes on, so a transient outage
    ///         ends as soon as the catalog answers.
    ///     </para>
    ///     <para>Stopping cancels the loop's wait; a refresh in flight finishes on its own.</para>
    /// </remarks>
    /// <param name="registry">The registry.</param>
    /// <param name="catalogOptions">The catalog options: the refresh interval.</param>
    /// <param name="time">The clock the timer runs on.</param>
    internal sealed class TenantRegistryRefreshService(TenantRegistry registry, IOptions<TellmaCatalogOptions> catalogOptions, TimeProvider time)
        : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private Task? _loop;

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _loop = LoopAsync(_stopping.Token);
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_loop is null)
            {
                return;
            }

            await _stopping.CancelAsync().ConfigureAwait(false);
            await _loop.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _stopping.Dispose();
        }

        /// <summary>Refreshes every interval until stopped.</summary>
        /// <param name="stoppingToken">Stops the loop.</param>
        /// <returns>The loop.</returns>
        private async Task LoopAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(catalogOptions.Value.RefreshInterval, time);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        await registry.RefreshAsync(force: false, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Logged by the registry with its staleness; the next tick tries again, so
                        // no failure ever ends the loop.
                        continue;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopped while waiting for a tick.
            }
        }
    }
}
