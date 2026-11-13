// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>
    ///     A hosted timer that rebuilds the membership hints of every active tenant from its users, so a
    ///     failed post-commit write is repaired without a deployment. It first runs five minutes after
    ///     the registry's first snapshot, then every reconcile interval. Each tenant is one read of its
    ///     users and one catalog transaction; a failed tenant is logged and the run goes on.
    /// </summary>
    /// <remarks>
    ///     The catalog's application lock keeps runs from overlapping: an instance whose tick finds
    ///     another instance's run in progress skips its own. It does not make one run per interval for
    ///     the deployment: each instance's timer runs from its own first snapshot, and the lock is
    ///     released when a run ends, so with several instances each may run once per interval, each
    ///     reading every active tenant's users. That is safe because a run is idempotent: it rebuilds
    ///     each tenant's hints from the tenant's users, so a second run over unchanged users changes no
    ///     row.
    /// </remarks>
    /// <param name="registry">The tenant registry, whose snapshot lists the tenants.</param>
    /// <param name="store">The reconciliation's reads and writes.</param>
    /// <param name="options">The tenancy options.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class MembershipReconcileService(
        ITenantRegistry registry,
        IMembershipReconcileStore store,
        IOptions<TenancyHostOptions> options,
        TimeProvider time,
        ILogger<MembershipReconcileService> logger) : BackgroundService
    {
        /// <summary>How long after the first snapshot the first run starts.</summary>
        public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);

        /// <summary>How often the service looks for the registry's first snapshot.</summary>
        public static readonly TimeSpan SnapshotPollInterval = TimeSpan.FromSeconds(5);

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // --- The first run: five minutes after the first snapshot ---
                DateTimeOffset? loadedAt;
                while ((loadedAt = registry.SnapshotLoadedAt) is null)
                {
                    await Task.Delay(SnapshotPollInterval, time, stoppingToken).ConfigureAwait(false);
                }

                TimeSpan wait = loadedAt.Value + FirstRunDelay - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, stoppingToken).ConfigureAwait(false);
                }

                _ = await RunOnceAsync(stoppingToken).ConfigureAwait(false);

                // --- Then every interval ---
                using PeriodicTimer timer = new(options.Value.MembershipReconcileInterval, time);
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    _ = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is stopping.
            }
        }

        /// <summary>One run: the lock, then every active tenant.</summary>
        /// <param name="cancellationToken">Cancels the run.</param>
        /// <returns>True when this instance led the run; false when another held the lock or the run failed to start.</returns>
        internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                IAsyncDisposable? lease = await store.TryLeadAsync(cancellationToken).ConfigureAwait(false);
                if (lease is null)
                {
                    return false;
                }

                await using (lease.ConfigureAwait(false))
                {
                    int tenants = 0;
                    int rows = 0;
                    int failed = 0;
                    foreach (TenantInfo tenant in registry.Tenants.Where(static tenant => tenant.Descriptor.State == TenantState.Active))
                    {
                        try
                        {
                            IReadOnlyList<TenantMembershipRecord> records = await store.ReadUsersAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                            rows += await store.ReconcileAsync(tenant.Id, records, cancellationToken).ConfigureAwait(false);
                            tenants++;
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        {
                            failed++;
                            HostLog.MembershipReconcileTenantFailed(logger, tenant.Id, exception);
                        }
                    }

                    HostLog.MembershipReconcileCompleted(logger, tenants, rows, failed);
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                HostLog.MembershipReconcileFailed(logger, exception);
                return false;
            }
        }
    }
}
