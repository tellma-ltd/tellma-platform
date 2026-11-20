// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §5.2: one renewal per tenant, each time the earliest of its carried leases is due, at
    // a fifth of that lease after its last renewal; a renewal that fails carries no verdict, and the
    // tenant's leases wait for the next interval. A handler that ignores a requested cancel for one
    // further lease length is abandoned: its task keeps running with a logged warning, no renewal
    // carries its lease again, and its rows are claimed again once the lease lapsed. The scheduler
    // checks once a second; the worker stops it before the final renewal of §6.7.
    /// <summary>Renews this instance's leases when they are due, and abandons handlers that ignore a cancel.</summary>
    internal sealed class JobRenewalScheduler : IDisposable
    {
        /// <summary>How often the scheduler checks the leases.</summary>
        internal static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

        /// <summary>Runs the renewals.</summary>
        private readonly IJobRenewal _renewal;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>The clock the checks run on.</summary>
        private readonly TimeProvider _time;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Cancelled when the scheduler stops.</summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>Guards the renewals in flight, the back-offs and the loop.</summary>
        private readonly Lock _gate = new();

        /// <summary>The renewal in flight per tenant.</summary>
        private readonly Dictionary<int, Task> _renewing = [];

        /// <summary>Until when a tenant whose renewal failed waits.</summary>
        private readonly Dictionary<int, DateTimeOffset> _backOff = [];

        /// <summary>The checking loop; null until started.</summary>
        private Task? _run;

        /// <summary>1 once disposed.</summary>
        private int _disposed;

        /// <summary>Creates the scheduler; it checks once started.</summary>
        /// <param name="renewal">Runs the renewals.</param>
        /// <param name="leases">The leases this instance holds.</param>
        /// <param name="time">The clock.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobRenewalScheduler(IJobRenewal renewal, JobLeases leases, TimeProvider time, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(renewal);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(loggers);
            _renewal = renewal;
            _leases = leases;
            _time = time;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>Starts checking; starting twice, or after the stop, does nothing.</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (!_stop.IsCancellationRequested)
                {
                    _run ??= Task.Run(RunAsync);
                }
            }
        }

        /// <summary>Stops checking and waits for the renewals in flight, which are cancelled.</summary>
        /// <returns>A task that completes once nothing the scheduler started runs.</returns>
        public async Task StopAsync()
        {
            // A disposed scheduler cancelled everything it started already.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            // Cancelled outside the lock, since a cancelled renewal may end inline; no check starts a
            // renewal once the token is cancelled, so the snapshot after it holds every one.
            _stop.Cancel();
            Task[] running;
            lock (_gate)
            {
                running = [.. _renewing.Values, _run ?? Task.CompletedTask];
            }

            // Each task answers its own failure; the wait never throws.
            await Task.WhenAll(running).ConfigureAwait(false);
        }

        /// <summary>Stops checking; the renewals in flight are cancelled and end on their own. Disposing twice does nothing.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _stop.Cancel();
            _stop.Dispose();
        }

        /// <summary>Abandons the handlers that ignored a cancel, and starts the renewals due; one check.</summary>
        /// <returns>A task that completes once the renewals this check started ended.</returns>
        internal Task CheckAsync()
        {
            DateTimeOffset now = _time.GetUtcNow();

            // --- The handlers still running a further lease length after a requested cancel ---
            foreach (JobLease lease in JobRenewal.Carried(_leases.All()))
            {
                if (lease.CancelledAt is DateTimeOffset cancelledAt && cancelledAt + TimeSpan.FromSeconds(lease.LeaseSeconds) <= now && lease.Abandon())
                {
                    using IDisposable? scope = _logger.BeginScope(new JobLogScope(lease.TenantId, userId: null, lease.Slots.Select(static slot => slot.JobId), originTraceParent: null));
                    JobsLog.JobHandlerAbandoned(_logger, lease.Handler.Key, "it kept running a lease length after its cancel was requested.");
                }
            }

            // --- One renewal per tenant whose earliest carried lease is due, unless one runs or the tenant backs off ---
            List<Task> started = [];
            lock (_gate)
            {
                if (_stop.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                foreach (int tenantId in _leases.Tenants())
                {
                    IReadOnlyList<JobLease> carried = JobRenewal.Carried(_leases.InFlight(tenantId));
                    bool due = carried.Count > 0 && carried.Min(static lease => lease.RenewalDueAt) <= now;
                    bool backingOff = _backOff.TryGetValue(tenantId, out DateTimeOffset until) && until > now;
                    if (due && !backingOff && !_renewing.ContainsKey(tenantId))
                    {
                        TimeSpan retryAfter = carried.Min(static lease => lease.RenewalInterval);
                        Task renewal = RenewAsync(tenantId, retryAfter);
                        _renewing[tenantId] = renewal;
                        started.Add(renewal);
                    }
                }
            }

            return Task.WhenAll(started);
        }

        /// <summary>The checking loop.</summary>
        /// <returns>A task that completes once the scheduler stopped.</returns>
        private async Task RunAsync()
        {
            using PeriodicTimer timer = new(CheckEvery, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    // The renewals run on; the next check skips a tenant whose renewal is in flight.
                    _ = CheckAsync();
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Stopped.
            }
        }

        /// <summary>Runs one tenant's renewal; a failure is logged and backs the tenant off by its shortest renewal interval.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="retryAfter">How long the tenant waits after a failure.</param>
        /// <returns>A task that completes once the renewal ended; it never throws.</returns>
        private async Task RenewAsync(int tenantId, TimeSpan retryAfter)
        {
            await Task.Yield();
            try
            {
                await _renewal.RenewAsync(tenantId, _stop.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    _ = _backOff.Remove(tenantId);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Stopped: the final renewal follows.
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                lock (_gate)
                {
                    _backOff[tenantId] = _time.GetUtcNow() + retryAfter;
                }

                using IDisposable? scope = _logger.BeginScope(new JobLogScope(tenantId, userId: null, [], originTraceParent: null));
                JobsLog.JobRenewalFailed(_logger, failure);
            }
            finally
            {
                lock (_gate)
                {
                    _ = _renewing.Remove(tenantId);
                }
            }
        }
    }
}
