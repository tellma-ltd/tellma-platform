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
        /// <summary>
        ///     Told the due time of each sweep once the service waits for it, before the wait ends; lets a
        ///     test move a fake clock only once the wait is in place.
        /// </summary>
        internal Action<DateTimeOffset>? WaitingUntil { get; set; }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // --- A sweep every interval from the start ---
                // Each wait is for a due time on the schedule, so a wait that starts late does not
                // push its sweep back.
                TimeSpan interval = options.Value.SweepInterval;
                DateTimeOffset due = time.GetUtcNow() + interval;
                while (!stoppingToken.IsCancellationRequested)
                {
                    Task wait = HostedSchedule.DelayUntil(due, time, stoppingToken);
                    WaitingUntil?.Invoke(due);
                    await wait.ConfigureAwait(false);

                    await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                    due = HostedSchedule.Next(due, interval, time.GetUtcNow());
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
