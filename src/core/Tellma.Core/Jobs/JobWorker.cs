// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.1, §6.2 and §6.7: one worker per host process. It holds one poller per tenant
    // whose state is Active or ReadOnly, synchronised with the registry's snapshot on the catalog's
    // refresh cadence and at once on every state change the registry reports; a tenant leaving those
    // states stops its poller after the current poll, its orphan sample dropped once the poller ended.
    // StoppingAsync, which the host calls before it stops the web host's draining, stops the pollers,
    // cancels the host token, waits up to DrainTimeout for the polls in flight and the batches they
    // started, then stops the renewals, issues the final renewal bound to the host's ShutdownTimeout
    // and abandons the handlers still running. Nothing escapes ExecuteAsync: the host's default
    // BackgroundServiceExceptionBehavior would stop the process.
    /// <summary>Runs the tenants' job pollers on this instance, and drains them when the host stops.</summary>
    internal sealed class JobWorker : BackgroundService, IHostedLifecycleService, ITenantStateListener
    {
        /// <summary>The tenants' snapshot.</summary>
        private readonly ITenantRegistry _registry;

        /// <summary>Runs each poll's round trip.</summary>
        private readonly IJobPoll _poll;

        /// <summary>The instance's batch slots.</summary>
        private readonly JobConcurrency _concurrency;

        /// <summary>The handlers this instance runs.</summary>
        private readonly JobHandlerRegistry _handlers;

        /// <summary>The seam an enqueue nudges the pollers through.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>Runs the claims' batches and drains them.</summary>
        private readonly JobDispatcher _dispatcher;

        /// <summary>Renews the leases while the worker runs.</summary>
        private readonly JobRenewalScheduler _renewals;

        /// <summary>Drops a retired tenant's orphan sample.</summary>
        private readonly JobsMetrics _metrics;

        /// <summary>The job options, read once the worker runs.</summary>
        private readonly IOptions<JobsOptions> _options;

        /// <summary>The host's options, whose shutdown timeout the final renewal binds the leases to.</summary>
        private readonly IOptions<HostOptions> _host;

        /// <summary>The catalog options, whose refresh interval paces the registry's re-reads.</summary>
        private readonly IOptions<TellmaCatalogOptions> _catalog;

        /// <summary>The clock.</summary>
        private readonly TimeProvider _time;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Guards the pollers and the worker's state.</summary>
        private readonly Lock _gate = new();

        /// <summary>The pollers by tenant id.</summary>
        private readonly Dictionary<int, TenantPoller> _pollers = [];

        /// <summary>The retired pollers still finishing their poll.</summary>
        private readonly HashSet<Task> _retiring = [];

        /// <summary>What each poller polls with; null until the worker runs.</summary>
        private TenantPollerServices? _services;

        /// <summary>Whether the worker runs: set once its loop started, cleared once it stops.</summary>
        private bool _running;

        /// <summary>Whether the host is stopping, so no poller starts again.</summary>
        private bool _stopping;

        /// <summary>Creates the worker.</summary>
        /// <param name="registry">The tenants' snapshot.</param>
        /// <param name="poll">Runs each poll's round trip.</param>
        /// <param name="concurrency">The instance's batch slots.</param>
        /// <param name="handlers">The handlers this instance runs.</param>
        /// <param name="dispatcher">Runs the claims' batches and drains them.</param>
        /// <param name="renewals">Renews the leases while the worker runs.</param>
        /// <param name="wake">The seam an enqueue nudges the pollers through.</param>
        /// <param name="metrics">The job instruments.</param>
        /// <param name="options">The job options.</param>
        /// <param name="host">The host's options: its shutdown timeout.</param>
        /// <param name="catalog">The catalog options: the registry's refresh interval.</param>
        /// <param name="time">The clock.</param>
        /// <param name="loggers">Creates the job machinery's logger.</param>
        public JobWorker(
            ITenantRegistry registry,
            IJobPoll poll,
            JobConcurrency concurrency,
            JobHandlerRegistry handlers,
            JobDispatcher dispatcher,
            JobRenewalScheduler renewals,
            TenantPollerWake wake,
            JobsMetrics metrics,
            IOptions<JobsOptions> options,
            IOptions<HostOptions> host,
            IOptions<TellmaCatalogOptions> catalog,
            TimeProvider time,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(poll);
            ArgumentNullException.ThrowIfNull(concurrency);
            ArgumentNullException.ThrowIfNull(handlers);
            ArgumentNullException.ThrowIfNull(dispatcher);
            ArgumentNullException.ThrowIfNull(renewals);
            ArgumentNullException.ThrowIfNull(wake);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(time);
            ArgumentNullException.ThrowIfNull(loggers);
            // The host creates every hosted service before the startup gate runs, so no option is read
            // here: an invalid one is the gate's to report, with every other problem.
            _registry = registry;
            _poll = poll;
            _concurrency = concurrency;
            _handlers = handlers;
            _dispatcher = dispatcher;
            _renewals = renewals;
            _wake = wake;
            _metrics = metrics;
            _options = options;
            _host = host;
            _catalog = catalog;
            _time = time;
            _logger = loggers.CreateLogger(JobsLog.Category);
        }

        /// <summary>The tenants polled now, in id order.</summary>
        internal IReadOnlyList<int> Polled
        {
            get
            {
                lock (_gate)
                {
                    return [.. _pollers.Keys.Order()];
                }
            }
        }

        /// <summary>Whether a tenant state is one the worker polls.</summary>
        /// <param name="state">The state; null for a tenant that left the registry.</param>
        /// <returns>True for <see cref="TenantState.Active" /> and <see cref="TenantState.ReadOnly" />.</returns>
        internal static bool Serves(TenantState? state)
        {
            return state is TenantState.Active or TenantState.ReadOnly;
        }

        /// <inheritdoc />
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StartedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StoppingAsync(CancellationToken cancellationToken)
        {
            // --- Polling stops: no poller starts again, and each ends after its current poll ---
            List<Task> polls;
            lock (_gate)
            {
                if (!_running || _stopping)
                {
                    return;
                }

                _stopping = true;
                foreach (TenantPoller poller in _pollers.Values)
                {
                    poller.Stop();
                }

                polls = [.. _pollers.Values.Select(static poller => poller.Completion), .. _retiring];
            }

            // --- The drain: the host token, the wait, the final renewal and the abandoned handlers ---
            await _dispatcher.DrainAsync(_options.Value.DrainTimeout, _host.Value.ShutdownTimeout, Task.WhenAll(polls), _renewals.StopAsync, cancellationToken).ConfigureAwait(false);

            // --- A poll still in flight past the drain is cancelled ---
            lock (_gate)
            {
                foreach (TenantPoller poller in _pollers.Values)
                {
                    poller.Abort();
                }
            }
        }

        /// <inheritdoc />
        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            Apply(tenant.Id, tenant.State);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            Apply(tenant.Id, state: null);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            lock (_gate)
            {
                foreach (TenantPoller poller in _pollers.Values)
                {
                    poller.Abort();
                }
            }

            base.Dispose();
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            JobsOptions options = _options.Value;
            if (!options.Enabled)
            {
                return;
            }

            // --- The pollers' services, then the renewals, which run while the worker does ---
            lock (_gate)
            {
                _services = new TenantPollerServices(_poll, _concurrency, _handlers, _dispatcher, _wake, options, _time, _logger, JobLeaseOwner.Current);
                _running = true;
            }

            _renewals.Start();

            // --- The pollers follow the registry's snapshot, on the catalog's refresh cadence ---
            using PeriodicTimer timer = new(_catalog.Value.RefreshInterval, _time);
            try
            {
                do
                {
                    Synchronise();
                }
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host stopped; StoppingAsync drained the pollers.
            }
        }

        /// <summary>Starts a poller for each served tenant without one, updates each poller's read-only state, and retires the rest.</summary>
        internal void Synchronise()
        {
            lock (_gate)
            {
                // The snapshot is read under the gate: the registry publishes it before it tells the
                // listeners, so whichever of this and a listener's call takes the gate second applies
                // the newer state, and a stale snapshot never restarts a tenant a listener retired.
                var states = _registry.Tenants.ToDictionary(static tenant => tenant.Id, static tenant => tenant.Descriptor.State);
                foreach ((int tenantId, TenantState state) in states)
                {
                    ApplyLocked(tenantId, state);
                }

                foreach (int tenantId in _pollers.Keys.Where(tenantId => !states.ContainsKey(tenantId)).ToArray())
                {
                    ApplyLocked(tenantId, state: null);
                }
            }
        }

        /// <summary>Brings one tenant's poller in line with its state.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">Its state now; null once it left the registry.</param>
        private void Apply(int tenantId, TenantState? state)
        {
            lock (_gate)
            {
                ApplyLocked(tenantId, state);
            }
        }

        /// <summary>Brings one tenant's poller in line with its state; the caller holds the gate.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="state">Its state now; null once it left the registry.</param>
        private void ApplyLocked(int tenantId, TenantState? state)
        {
            if (!_running || _stopping || _services is not TenantPollerServices services)
            {
                return;
            }

            bool polled = _pollers.TryGetValue(tenantId, out TenantPoller? poller);
            if (Serves(state))
            {
                // --- A served tenant: polled, its read-only state current ---
                bool readOnly = state == TenantState.ReadOnly;
                if (polled)
                {
                    poller!.ReadOnly = readOnly;
                    return;
                }

                TenantPoller started = new(tenantId, readOnly, services);
                _pollers.Add(tenantId, started);
                started.Start();
                return;
            }

            if (!polled)
            {
                return;
            }

            // --- A tenant that left the served states: its poller stops after the current poll ---
            _ = _pollers.Remove(tenantId);
            poller!.Stop();
            string now = state is TenantState left ? left.ToString() : "removed from the catalog";
            using (_logger.BeginScope(new JobLogScope(tenantId, userId: null, [], originTraceParent: null)))
            {
                JobsLog.JobPollerRetired(_logger, now);
            }

            Task retiring = RetireAsync(poller);
            _ = _retiring.Add(retiring);
        }

        /// <summary>Waits for a retired poller to end, then drops its tenant's orphan sample unless a new poller took the tenant.</summary>
        /// <param name="poller">The poller.</param>
        /// <returns>A task that completes once the poller ended; it never throws.</returns>
        private async Task RetireAsync(TenantPoller poller)
        {
            // Off the caller's thread, which holds the gate.
            await Task.Yield();
            await poller.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            lock (_gate)
            {
                if (!_pollers.ContainsKey(poller.TenantId))
                {
                    _metrics.DropOrphaned(poller.TenantId);
                }

                _ = _retiring.RemoveWhere(static task => task.IsCompleted);
            }

            poller.Dispose();
        }
    }
}
