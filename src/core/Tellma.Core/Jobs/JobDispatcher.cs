// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.5–§6.7: each claim that leased rows runs on the thread pool in the slot reserved for
    // it, under the host token, and in_flight counts its rows while it runs. A claim the instance had
    // no slot for is released uncounted on the pool instead, as a drain releases a partition no
    // handler started, and the drain waits for those releases too. The drain cancels the
    // host token (each lease's items stop with HostStopping; partitions not started are released by
    // their runner), waits up to DrainTimeout for the batches, and leaves the handlers still running
    // leased: one final renewal per tenant binds their leases to the host's ShutdownTimeout, the
    // renewals have stopped by then, and the handlers are abandoned with a warning each.
    /// <summary>Runs the batches of this instance's claims, and drains them when the host stops.</summary>
    internal sealed class JobDispatcher : IJobDispatch, IDisposable
    {
        /// <summary>Runs each claim's batch.</summary>
        private readonly IJobClaimRunner _runner;

        /// <summary>Issues the final renewal.</summary>
        private readonly IJobRenewal _renewal;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>Counts the rows in flight.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>The clock the drain waits on.</summary>
        private readonly TimeProvider _time;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The host token's source: cancelled when the drain starts.</summary>
        private readonly CancellationTokenSource _host = new();

        /// <summary>Guards the running batches.</summary>
        private readonly Lock _gate = new();

        /// <summary>The batches running.</summary>
        private readonly HashSet<Task> _running = [];

        /// <summary>Creates the dispatcher.</summary>
        /// <param name="runner">Runs each claim's batch.</param>
        /// <param name="renewal">Issues the final renewal.</param>
        /// <param name="leases">The leases this instance holds.</param>
        /// <param name="metrics">Counts the rows in flight.</param>
        /// <param name="time">The clock the drain waits on.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobDispatcher(IJobClaimRunner runner, IJobRenewal renewal, JobLeases leases, JobsMetrics metrics, TimeProvider time, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(runner);
            ArgumentNullException.ThrowIfNull(renewal);
            ArgumentNullException.ThrowIfNull(leases);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(loggers);
            _runner = runner;
            _renewal = renewal;
            _leases = leases;
            _metrics = metrics;
            _time = time;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>The host's token, cancelled once the drain starts.</summary>
        public CancellationToken HostToken => _host.Token;

        /// <summary>The batches running now.</summary>
        public int Running
        {
            get
            {
                lock (_gate)
                {
                    return _running.Count;
                }
            }
        }

        /// <inheritdoc />
        public void Start(JobPolledClaim polled)
        {
            ArgumentNullException.ThrowIfNull(polled);
            Track(() => RunAsync(polled));
        }

        /// <inheritdoc />
        public void Release(JobPolledClaim polled)
        {
            ArgumentNullException.ThrowIfNull(polled);
            Track(() => ReleaseAsync(polled));
        }

        /// <summary>
        ///     Drains the batches: cancels the host token, waits up to <paramref name="drainTimeout" />
        ///     for them, then renews the leases still running one last time by
        ///     <paramref name="shutdownTimeout" /> and abandons their handlers.
        /// </summary>
        /// <param name="drainTimeout">How long to wait for the batches.</param>
        /// <param name="shutdownTimeout">The host's shutdown timeout, which the final renewal binds the leases to.</param>
        /// <param name="polls">The polls in flight, whose claims start batches the drain waits for as well; it never throws.</param>
        /// <param name="stopRenewals">Stops the renewals, before the final one.</param>
        /// <param name="cancellationToken">The host's own stop deadline: once cancelled, the wait ends and the final renewal is not attempted.</param>
        /// <returns>A task that completes once the drain ended; it never throws.</returns>
        public async Task DrainAsync(TimeSpan drainTimeout, TimeSpan shutdownTimeout, Task polls, Func<Task> stopRenewals, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(polls);
            ArgumentNullException.ThrowIfNull(stopRenewals);

            // --- The host stops: every lease's items stop with HostStopping ---
            await _host.CancelAsync().ConfigureAwait(false);

            // --- The wait, bounded by the drain timeout and by the host's own deadline: for the polls
            // in flight, then for every batch running, again while a poll that ended late started more ---
            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = Task.Delay(drainTimeout, _time, expiry.Token);
            bool drained = await Task.WhenAny(polls, timeout).ConfigureAwait(false) == polls;
            while (drained)
            {
                Task[] running;
                lock (_gate)
                {
                    running = [.. _running.Where(static batch => !batch.IsCompleted)];
                }

                if (running.Length == 0)
                {
                    break;
                }

                var all = Task.WhenAll(running);
                drained = await Task.WhenAny(all, timeout).ConfigureAwait(false) == all;
            }

            await expiry.CancelAsync().ConfigureAwait(false);

            // --- The renewals stop either way; the final renewal follows only for handlers still running ---
            await stopRenewals().ConfigureAwait(false);
            if (drained)
            {
                return;
            }

            await LeaveLeasedAsync(drainTimeout, shutdownTimeout, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _host.Dispose();
        }

        /// <summary>Renews the leases still running by the shutdown timeout, then abandons their handlers.</summary>
        /// <param name="drainTimeout">How long the drain waited.</param>
        /// <param name="shutdownTimeout">The host's shutdown timeout.</param>
        /// <param name="cancellationToken">The host's own stop deadline: once cancelled, the leases are left as their last renewal set them.</param>
        /// <returns>A task that completes once every lease was renewed or left, and every handler abandoned.</returns>
        private async Task LeaveLeasedAsync(TimeSpan drainTimeout, TimeSpan shutdownTimeout, CancellationToken cancellationToken)
        {
            // --- One final renewal per tenant, every lease bound to the shutdown timeout ---
            int leaseSeconds = Math.Max(1, (int)Math.Ceiling(shutdownTimeout.TotalSeconds));
            foreach (int tenantId in _leases.Tenants())
            {
                try
                {
                    _ = await _renewal.RenewFinalAsync(tenantId, leaseSeconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The host's deadline passed: the leases keep the length of their last renewal.
                    break;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // The leases keep the length of their last renewal.
                    using IDisposable? scope = _logger.BeginScope(new JobLogScope(tenantId, userId: null, [], originTraceParent: null));
                    JobsLog.JobRenewalFailed(_logger, failure);
                }
            }

            // --- The handlers still running are abandoned ---
            IReadOnlyList<JobLease> running = JobRenewal.Carried(_leases.All());
            foreach (JobLease lease in running)
            {
                if (lease.Abandon())
                {
                    using IDisposable? scope = _logger.BeginScope(new JobLogScope(lease.TenantId, userId: null, lease.Slots.Select(static slot => slot.JobId), originTraceParent: null));
                    JobsLog.JobHandlerAbandoned(_logger, lease.Handler.Key, "it was still running when the host stopped.");
                }
            }

            JobsLog.WorkerDrainTimedOut(_logger, drainTimeout, running.Count);
        }

        /// <summary>Runs one batch on the thread pool, under no ambient activity, among those the drain waits for.</summary>
        /// <param name="run">The batch; its task never throws.</param>
        private void Track(Func<Task> run)
        {
            // The batch runs under no ambient activity: each invocation starts its own root.
            Task batch;
            Activity? ambient = Activity.Current;
            Activity.Current = null;
            try
            {
                batch = Task.Run(run);
            }
            finally
            {
                Activity.Current = ambient;
            }

            lock (_gate)
            {
                if (!batch.IsCompleted)
                {
                    _ = _running.Add(batch);
                }
            }

            _ = batch.ContinueWith(
                ended =>
                {
                    lock (_gate)
                    {
                        _ = _running.Remove(ended);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>Runs one claim's batch; its rows are in in_flight while it runs, and its slot is released once it ended.</summary>
        /// <param name="polled">The claim and its slot.</param>
        /// <returns>A task that completes once the batch ended; it never throws.</returns>
        private async Task RunAsync(JobPolledClaim polled)
        {
            string key = polled.Claim.Handler.Key;
            int rows = polled.Claim.Rows.Value.Count;
            bool counted = false;
            try
            {
                _metrics.AddInFlight(key, rows);
                counted = true;
                _ = await _runner.RunAsync(polled.Claim, _host.Token).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                ReportBatchFailure(polled, failure);
            }
            finally
            {
                // The slot is released even when the instrument throws, so no key stays at its limit.
                try
                {
                    if (counted)
                    {
                        _metrics.AddInFlight(key, -rows);
                    }
                }
                finally
                {
                    polled.Reservation?.Dispose();
                }
            }
        }

        /// <summary>Releases one claim's rows uncounted; its slot is released once they were.</summary>
        /// <param name="polled">The claim and its slot.</param>
        /// <returns>A task that completes once the release ended; it never throws.</returns>
        private async Task ReleaseAsync(JobPolledClaim polled)
        {
            try
            {
                _ = await _runner.ReleaseAsync(polled.Claim).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                ReportBatchFailure(polled, failure);
            }
            finally
            {
                polled.Reservation?.Dispose();
            }
        }

        /// <summary>Logs a batch that failed unexpectedly; a log that fails too is dropped.</summary>
        /// <param name="polled">The batch's claim.</param>
        /// <param name="failure">What it threw.</param>
        private void ReportBatchFailure(JobPolledClaim polled, Exception failure)
        {
            try
            {
                using IDisposable? scope = _logger.BeginScope(new JobLogScope(polled.Claim.TenantId, userId: null, polled.Claim.Rows.Value.Select(static row => row.Job.Id), originTraceParent: null));
                JobsLog.JobBatchFailed(_logger, failure, polled.Claim.Handler.Key);
            }
            catch (Exception logFailure) when (logFailure is not OutOfMemoryException)
            {
                // The logger is what failed: the slot is released all the same.
            }
        }
    }
}
