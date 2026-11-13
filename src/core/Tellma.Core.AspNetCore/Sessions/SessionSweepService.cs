// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     A hosted timer that sweeps the sessions that expired more than a day ago, every sweep interval.
    ///     The sweep is set-based and idempotent, so every instance runs it; it is not a job, because jobs
    ///     are per tenant and the session table is the catalog's.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="options">The session options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class SessionSweepService(
        ICatalogSessionStore store,
        IOptions<TellmaSessionOptions> options,
        TimeProvider time,
        ILogger<SessionSweepService> logger) : BackgroundService
    {
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(options.Value.SweepInterval, time);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is stopping.
            }
        }

        /// <summary>Runs one sweep; a failure is logged and the next interval retries.</summary>
        /// <param name="cancellationToken">Cancels the sweep.</param>
        /// <returns>A task that completes when the sweep ran or its failure was logged.</returns>
        internal async Task SweepOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                _ = await store.SweepAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                HostLog.SessionSweepFailed(logger, exception);
            }
        }
    }
}
