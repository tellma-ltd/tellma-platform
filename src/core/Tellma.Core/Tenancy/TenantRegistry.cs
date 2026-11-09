// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The tenant registry: an immutable snapshot of the catalog's tenants, swapped whole on every
    ///     reload, confirmed by a version probe every refresh interval, and failing closed beyond its
    ///     staleness bound.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Refreshes are single-flight. At most one runs at a time; a refresh asked for while one
    ///         runs joins it, unless it is forced, in which case one more refresh is queued to run
    ///         right after it (and every caller asking meanwhile joins that one). So a forced refresh
    ///         always starts after the write that asked for it committed.
    ///     </para>
    ///     <para>
    ///         Listeners hear of the tenants whose descriptor a reload changed or that left the
    ///         snapshot, in tenant order, from an ordered queue delivered after the refresh has ended,
    ///         each call in a scope of its own and within a bound. The first snapshot a process loads
    ///         tells no one.
    ///     </para>
    /// </remarks>
    internal sealed class TenantRegistry : ITenantRegistry, IDisposable
    {
        private const string StartupSource = "core.catalog";

        private readonly ICatalogReader _reader;
        private readonly IOptions<TellmaCatalogOptions> _catalogOptions;
        private readonly IOptionsMonitor<TellmaSqlOptions> _sqlOptions;
        private readonly TenancyEnvironment _environment;
        private readonly DeploymentIdentity _deployment;
        private readonly TimeProvider _time;
        private readonly IServiceScopeFactory _scopes;
        private readonly TenancyMetrics _metrics;
        private readonly ILogger<TenantRegistry> _logger;
        private readonly TenantRegistryStartupOptions _startup;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly CancellationToken _stopping;
        private readonly Lock _sync = new();
        private readonly IDisposable? _profilesChanged;

        private volatile Published? _published;
        private volatile bool _catalogVerified;
        private Run? _running;
        private Run? _pending;
        private Task _notifications = Task.CompletedTask;

        /// <summary>Creates the registry; the hosted refresh service loads it.</summary>
        /// <param name="reader">The catalog reads.</param>
        /// <param name="catalogOptions">The catalog options: the refresh interval and the staleness bound.</param>
        /// <param name="sqlOptions">The SQL options: the profiles and the allowed servers.</param>
        /// <param name="environment">The composition's slug and environment.</param>
        /// <param name="deployment">The running deployment, every connection's application name.</param>
        /// <param name="time">The clock.</param>
        /// <param name="scopes">Creates the scope listeners are resolved in.</param>
        /// <param name="metrics">The tenancy instruments.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="startup">How the first load behaves.</param>
        public TenantRegistry(
            ICatalogReader reader,
            IOptions<TellmaCatalogOptions> catalogOptions,
            IOptionsMonitor<TellmaSqlOptions> sqlOptions,
            TenancyEnvironment environment,
            DeploymentIdentity deployment,
            TimeProvider time,
            IServiceScopeFactory scopes,
            TenancyMetrics metrics,
            ILogger<TenantRegistry> logger,
            IOptions<TenantRegistryStartupOptions> startup)
        {
            ArgumentNullException.ThrowIfNull(startup);

            _reader = reader;
            _catalogOptions = catalogOptions;
            _sqlOptions = sqlOptions;
            _environment = environment;
            _deployment = deployment;
            _time = time;
            _scopes = scopes;
            _metrics = metrics;
            _logger = logger;
            _startup = startup.Value;
            _stopping = _shutdown.Token;

            _metrics.ObserveSnapshot(ObserveAge, ObserveTenants);

            // A changed profile (a rotated password, a new pool bound) recomposes every string at once
            // instead of waiting for the next catalog write.
            _profilesChanged = _sqlOptions.OnChange(_ => OnProfilesChanged());
        }

        /// <inheritdoc />
        public IReadOnlyList<TenantInfo> Tenants => _published?.Snapshot.Tenants ?? [];

        /// <inheritdoc />
        public Guid SnapshotVersion => _published?.Snapshot.Version ?? Guid.Empty;

        /// <inheritdoc />
        public DateTimeOffset? SnapshotLoadedAt => _published?.ConfirmedAt;

        /// <inheritdoc />
        public bool IsAvailable => IsFresh(_published);

        /// <summary>The listener deliveries queued so far; completes when every one has ended.</summary>
        public Task NotificationsDelivered
        {
            get
            {
                lock (_sync)
                {
                    return _notifications;
                }
            }
        }

        /// <summary>The rows the current snapshot refused; empty before the first load.</summary>
        public IReadOnlyList<RefusedTenantRow> Refused => _published?.Snapshot.Refused ?? [];

        /// <inheritdoc />
        public TenantInfo? Find(int tenantId)
        {
            return Available(tenantId).Find(tenantId);
        }

        /// <inheritdoc />
        public TenantInfo Get(int tenantId)
        {
            return Available(tenantId).Find(tenantId) ?? throw new TenantNotFoundException(tenantId);
        }

        /// <inheritdoc />
        public Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_stopping.IsCancellationRequested, this);

            Task task;
            lock (_sync)
            {
                if (_pending is Run pending)
                {
                    // A refresh is already queued behind the running one: every caller joins it.
                    pending.Forced |= force;
                    task = pending.Task;
                }
                else if (_running is Run running && !force)
                {
                    task = running.Task;
                }
                else if (_running is Run current)
                {
                    // Forced while one runs: queue one that starts only after it, so it reads what was
                    // committed before this call.
                    Run next = new(forced: true);
                    next.Task = RunAfterAsync(current.Task, next);
                    _pending = next;
                    task = next.Task;
                }
                else
                {
                    Run run = new(force);
                    _running = run;
                    run.Task = RunAsync(run);
                    task = run.Task;
                }
            }

            return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
        }

        /// <summary>
        ///     The startup load: verifies the catalog's migrations, then loads the first snapshot,
        ///     within the first-load bound. A catalog that is missing in Development, or that lacks the
        ///     platform's latest migration, fails startup with the migrator command that fixes it; an
        ///     unreachable one lets the host start not ready while the loop keeps trying.
        /// </summary>
        /// <param name="cancellationToken">The host's start token.</param>
        /// <returns>A task that completes when the load succeeded or its failure was logged.</returns>
        /// <exception cref="TellmaCompositionException">The catalog is missing or behind.</exception>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = new(_startup.FirstLoadTimeout, _time);
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                // --- The catalog's migrations, unless the migrator owns them ---
                if (_startup.VerifyCatalog)
                {
                    CatalogHistoryState state = await _reader.ReadHistoryAsync(bound.Token).ConfigureAwait(false);
                    switch (state)
                    {
                        case CatalogHistoryState.Current:
                            _catalogVerified = true;
                            break;

                        case CatalogHistoryState.MissingDatabase when !_environment.IsDevelopment:
                            // Outside Development a database that cannot be opened may be a transient
                            // outage, which must not crash-loop every instance: start not ready.
                            TenancyLog.CatalogDatabaseUnavailable(_logger, _environment.MigrateCommand);
                            return;

                        case CatalogHistoryState.MissingDatabase:
                        case CatalogHistoryState.MissingSchema:
                        case CatalogHistoryState.Behind:
                        default:
                            throw StartupFailure(state);
                    }
                }

                // --- The first snapshot ---
                await RefreshAsync(force: true, bound.Token).ConfigureAwait(false);
            }
            catch (TellmaCompositionException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Unreachable, or slower than the bound: the host starts and the loop retries.
                TenancyLog.FirstLoadFailed(_logger, exception);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The token stays readable after the source is disposed, which is all a run still in
            // flight touches.
            if (!_stopping.IsCancellationRequested)
            {
                _shutdown.Cancel();
            }

            _profilesChanged?.Dispose();
            _shutdown.Dispose();
        }

        /// <summary>The snapshot, when it may be served.</summary>
        /// <param name="tenantId">The tenant asked for, named by the refusal.</param>
        /// <returns>The snapshot.</returns>
        /// <exception cref="TenantUnavailableException">The snapshot was never loaded or is beyond its staleness bound.</exception>
        private TenantSnapshot Available(int tenantId)
        {
            Published? published = _published;
            return IsFresh(published)
                ? published!.Snapshot
                : throw new TenantUnavailableException(tenantId, null, TenantProblemCodes.CatalogUnavailable, RetryAfter());
        }

        /// <summary>Whether a published snapshot exists and was confirmed within the staleness bound.</summary>
        /// <param name="published">The published snapshot, or null.</param>
        /// <returns>True when it may be served.</returns>
        private bool IsFresh(Published? published)
        {
            return published is not null && _time.GetUtcNow() - published.ConfirmedAt <= _catalogOptions.Value.MaxStaleness;
        }

        /// <summary>How long a refused caller should wait: one refresh interval, in whole seconds.</summary>
        /// <returns>The wait.</returns>
        private TimeSpan RetryAfter()
        {
            return TimeSpan.FromSeconds(Math.Ceiling(_catalogOptions.Value.RefreshInterval.TotalSeconds));
        }

        /// <summary>Runs a refresh that started with nothing ahead of it.</summary>
        /// <param name="run">The run.</param>
        /// <returns>The refresh.</returns>
        private async Task RunAsync(Run run)
        {
            // Off the caller's thread at once: the caller holds the lock while this starts.
            await Task.Yield();
            await ExecuteAsync(run).ConfigureAwait(false);
        }

        /// <summary>Runs a queued refresh once the one ahead of it completes, whatever its outcome.</summary>
        /// <param name="previous">The refresh ahead.</param>
        /// <param name="run">The queued run.</param>
        /// <returns>The refresh.</returns>
        private async Task RunAfterAsync(Task previous, Run run)
        {
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
            lock (_sync)
            {
                _pending = null;
                _running = run;
            }

            await ExecuteAsync(run).ConfigureAwait(false);
        }

        /// <summary>Runs a refresh and clears it as the running one when it ends.</summary>
        /// <param name="run">The run.</param>
        /// <returns>The refresh.</returns>
        private async Task ExecuteAsync(Run run)
        {
            try
            {
                bool forced;
                lock (_sync)
                {
                    forced = run.Forced;
                }

                await RefreshCoreAsync(forced, _stopping).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    if (_running == run)
                    {
                        _running = null;
                    }
                }
            }
        }

        /// <summary>One refresh: the probe, and the reload when the version moved or the refresh is forced.</summary>
        /// <param name="force">Reload regardless of the version.</param>
        /// <param name="cancellationToken">The registry's shutdown.</param>
        /// <returns>A task that completes when the refresh did.</returns>
        private async Task RefreshCoreAsync(bool force, CancellationToken cancellationToken)
        {
            long started = _time.GetTimestamp();
            string outcome = TenancyMetrics.Failed;
            try
            {
                // --- A catalog not yet verified, because startup could not reach it ---
                if (_startup.VerifyCatalog && !_catalogVerified)
                {
                    await VerifyCatalogAsync(cancellationToken).ConfigureAwait(false);
                }

                // --- The probe: an unchanged version only confirms the snapshot ---
                Published? previous = _published;
                if (!force && previous is not null)
                {
                    Guid version = await _reader.ReadVersionAsync(cancellationToken).ConfigureAwait(false);
                    if (version == previous.Snapshot.Version)
                    {
                        _published = new Published(previous.Snapshot, _time.GetUtcNow());
                        outcome = TenancyMetrics.Unchanged;
                        return;
                    }
                }

                // --- The reload: a whole new snapshot, swapped in at once ---
                CatalogReload reload = await _reader.ReadTenantsAsync(cancellationToken).ConfigureAwait(false);
                TellmaSqlOptions sql = _sqlOptions.CurrentValue;
                var snapshot = TenantSnapshot.Build(
                    reload.Version,
                    reload.Rows,
                    new TenantRowComposer(sql, _environment.IsDevelopment, _deployment.DeploymentId));

                _published = new Published(snapshot, _time.GetUtcNow());
                outcome = TenancyMetrics.Reloaded;

                // --- What the reload says about the rows, and who must hear of it ---
                ReportRefusals(snapshot);
                if (snapshot.WarmTenants > sql.MaxWarmTenants)
                {
                    TenancyLog.WarmTenantsExceeded(_logger, snapshot.WarmTenants, sql.MaxWarmTenants);
                }

                // The first snapshot tells no one; later ones queue their changes, delivered after
                // this refresh has ended so no listener can hold the single flight.
                List<TenantChange> changes = previous is null ? [] : Changes(previous.Snapshot, snapshot);
                TenancyLog.SnapshotReloaded(_logger, previous is null ? snapshot.Tenants.Count : changes.Count, snapshot.Tenants.Count);
                Enqueue(changes);
            }
            catch (CatalogNotMigratedException)
            {
                // Logged where it was found, with the command that fixes it.
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (IsFresh(_published))
                {
                    TenancyLog.RefreshFailed(_logger, exception);
                }
                else
                {
                    TenancyLog.RefreshFailedStale(_logger, exception);
                }

                throw;
            }
            finally
            {
                _metrics.RecordRefresh(outcome, _time.GetElapsedTime(started).TotalSeconds);
            }
        }

        /// <summary>Verifies the catalog's migrations on a refresh after a startup that could not reach the catalog.</summary>
        /// <param name="cancellationToken">The registry's shutdown.</param>
        /// <returns>A task that completes when the catalog is verified.</returns>
        /// <exception cref="CatalogNotMigratedException">The catalog lacks the platform's latest migration.</exception>
        /// <exception cref="InvalidOperationException">The catalog database cannot be opened.</exception>
        private async Task VerifyCatalogAsync(CancellationToken cancellationToken)
        {
            CatalogHistoryState state = await _reader.ReadHistoryAsync(cancellationToken).ConfigureAwait(false);
            switch (state)
            {
                case CatalogHistoryState.Current:
                    _catalogVerified = true;
                    return;

                case CatalogHistoryState.MissingDatabase:
                    throw new InvalidOperationException("The catalog database could not be opened: it is missing, or this host's principal has no access to it.");

                case CatalogHistoryState.MissingSchema:
                case CatalogHistoryState.Behind:
                default:
                    // The host is already serving, so this cannot fail startup any more; it fails
                    // closed instead, serving no tenant until the catalog is migrated.
                    TenancyLog.CatalogBehind(_logger, _environment.MigrateCommand);
                    throw new CatalogNotMigratedException(_environment.MigrateCommand);
            }
        }

        /// <summary>The startup failure of a catalog that cannot be served.</summary>
        /// <param name="state">The catalog's state.</param>
        /// <returns>The exception to throw.</returns>
        private TellmaCompositionException StartupFailure(CatalogHistoryState state)
        {
            string problem = state switch
            {
                CatalogHistoryState.MissingDatabase => "The catalog database does not exist, or this host's principal cannot open it.",
                CatalogHistoryState.MissingSchema => "The catalog database has no migration history: it was never migrated.",
                CatalogHistoryState.Behind or CatalogHistoryState.Current or _ => $"The catalog lacks the platform's latest catalog migration, '{CatalogMigrations.Latest}': it is behind this host.",
            };

            return new TellmaCompositionException([new CompositionProblem(StartupSource, problem, _environment.MigrateCommand)]);
        }

        /// <summary>Logs every row the snapshot refused.</summary>
        /// <param name="snapshot">The snapshot.</param>
        private void ReportRefusals(TenantSnapshot snapshot)
        {
            foreach (RefusedTenantRow refused in snapshot.Refused)
            {
                switch (refused.Reason)
                {
                    case TenantRowRefusal.ServerRefused:
                        TenancyLog.TenantServerRefused(_logger, refused.Row.Id, refused.Row.Server);
                        break;

                    case TenantRowRefusal.DatabaseMalformed:
                        TenancyLog.TenantDatabaseRefused(_logger, refused.Row.Id);
                        break;

                    case TenantRowRefusal.ProfileUnknown:
                    case TenantRowRefusal.ProfileMalformed:
                        TenancyLog.TenantProfileRefused(_logger, refused.Row.Id, refused.Row.CredentialProfile);
                        break;

                    case TenantRowRefusal.UnknownCategoryOrState:
                    default:
                        TenancyLog.TenantRowUnknown(_logger, refused.Row.Id);
                        break;
                }
            }
        }

        /// <summary>
        ///     The tenants whose descriptor differs between two snapshots, new ones included, and the
        ///     tenants the new one no longer serves, in id order. A row whose version moved without a
        ///     descriptor change (a relocation) is not one.
        /// </summary>
        /// <param name="previous">The snapshot replaced.</param>
        /// <param name="next">The new snapshot.</param>
        /// <returns>Each change.</returns>
        private static List<TenantChange> Changes(TenantSnapshot previous, TenantSnapshot next)
        {
            List<TenantChange> changes = [];
            foreach (TenantInfo tenant in next.Tenants)
            {
                TenantInfo? before = previous.Find(tenant.Id);
                if (before is null || (before.Version != tenant.Version && before.Descriptor != tenant.Descriptor))
                {
                    changes.Add(new TenantChange(tenant.Descriptor, before?.Descriptor.State, Removed: false));
                }
            }

            foreach (TenantInfo tenant in previous.Tenants)
            {
                if (next.Find(tenant.Id) is null)
                {
                    changes.Add(new TenantChange(tenant.Descriptor, tenant.Descriptor.State, Removed: true));
                }
            }

            changes.Sort(static (left, right) => left.Tenant.Id.CompareTo(right.Tenant.Id));
            return changes;
        }

        /// <summary>
        ///     Queues the changes of one reload for the listeners, behind every earlier reload's. The
        ///     refresh does not wait: a listener that is slow, hung or re-entrant never holds the
        ///     registry's single flight.
        /// </summary>
        /// <param name="changes">The changes.</param>
        private void Enqueue(List<TenantChange> changes)
        {
            if (changes.Count == 0)
            {
                return;
            }

            lock (_sync)
            {
                _notifications = DispatchAfterAsync(_notifications, changes);
            }
        }

        /// <summary>Delivers one reload's changes once the deliveries ahead of it are done.</summary>
        /// <param name="previous">The deliveries ahead.</param>
        /// <param name="changes">The changes.</param>
        /// <returns>The delivery.</returns>
        private async Task DispatchAfterAsync(Task previous, List<TenantChange> changes)
        {
            // Off the refresh's thread at once, in order behind the earlier deliveries.
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);

            int count;
            try
            {
                AsyncServiceScope probe = _scopes.CreateAsyncScope();
                await using (probe.ConfigureAwait(false))
                {
                    count = probe.ServiceProvider.GetServices<ITenantStateListener>().Count();
                }
            }
            catch (Exception exception)
            {
                TenancyLog.ListenerFailed(_logger, nameof(ITenantStateListener), changes[0].Tenant.Id, exception);
                return;
            }

            foreach (TenantChange change in changes)
            {
                for (int index = 0; index < count; index++)
                {
                    await CallAsync(change, index).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///     Calls one listener about one change, in a scope of its own and within the listener bound.
        ///     A call that outlasts the bound is logged and left to finish on its own; its scope is
        ///     disposed when it does.
        /// </summary>
        /// <param name="change">The change.</param>
        /// <param name="index">The listener's position among the registrations.</param>
        /// <returns>A task that completes when the call ended, failed or timed out.</returns>
        private async Task CallAsync(TenantChange change, int index)
        {
            AsyncServiceScope scope = _scopes.CreateAsyncScope();
            string name = nameof(ITenantStateListener);
            Task call;
            try
            {
                ITenantStateListener listener = scope.ServiceProvider.GetServices<ITenantStateListener>().ElementAt(index);
                name = listener.GetType().FullName ?? listener.GetType().Name;
                call = change.Removed
                    ? listener.OnRemovedAsync(change.Tenant)
                    : listener.OnStateChangedAsync(change.Tenant, change.Previous);
            }
            catch (Exception exception)
            {
                TenancyLog.ListenerFailed(_logger, name, change.Tenant.Id, exception);
                await scope.DisposeAsync().ConfigureAwait(false);
                return;
            }

            try
            {
                await call.WaitAsync(_startup.ListenerTimeout, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The call runs on; its scope goes when it ends, never under it.
                TenancyLog.ListenerTimedOut(_logger, name, change.Tenant.Id, _startup.ListenerTimeout);
                _ = call.ContinueWith(
                    _ => scope.DisposeAsync().AsTask(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return;
            }
            catch (Exception exception)
            {
                TenancyLog.ListenerFailed(_logger, name, change.Tenant.Id, exception);
            }

            await scope.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>Refreshes with a reload after the profiles changed, once a snapshot exists.</summary>
        private void OnProfilesChanged()
        {
            if (_published is null || _stopping.IsCancellationRequested)
            {
                return;
            }

            // Failures are logged by the refresh itself; observing the task keeps them from going unobserved.
            _ = RefreshAsync(force: true).ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>The snapshot-age gauge's measurement; none before the first load.</summary>
        /// <returns>The measurement.</returns>
        private IEnumerable<Measurement<double>> ObserveAge()
        {
            Published? published = _published;
            return published is null ? [] : [new Measurement<double>((_time.GetUtcNow() - published.ConfirmedAt).TotalSeconds)];
        }

        /// <summary>The tenants gauge's measurements: the served tenants counted by category and state.</summary>
        /// <returns>The measurements.</returns>
        private IEnumerable<Measurement<int>> ObserveTenants()
        {
            Published? published = _published;
            return published is null
                ? []
                : [.. published.Snapshot.Tenants
                    .GroupBy(static tenant => (tenant.Descriptor.Category, tenant.Descriptor.State))
                    .OrderBy(static group => group.Key)
                    .Select(static group => new Measurement<int>(
                        group.Count(),
                        new KeyValuePair<string, object?>(TenancyTelemetryNames.CategoryTag, group.Key.Category.ToString()),
                        new KeyValuePair<string, object?>(TenancyTelemetryNames.StateTag, group.Key.State.ToString())))];
        }

        /// <summary>A published snapshot and when it was last confirmed against the catalog.</summary>
        /// <param name="Snapshot">The snapshot.</param>
        /// <param name="ConfirmedAt">When it was last loaded or found current.</param>
        private sealed record Published(TenantSnapshot Snapshot, DateTimeOffset ConfirmedAt);

        /// <summary>One change a listener hears of.</summary>
        /// <param name="Tenant">The tenant as it is now, or as the snapshot last held it when removed.</param>
        /// <param name="Previous">The state before; null for a new tenant.</param>
        /// <param name="Removed">Whether the tenant left the snapshot.</param>
        private sealed record TenantChange(TenantDescriptor Tenant, TenantState? Previous, bool Removed);

        /// <summary>One refresh, running or queued.</summary>
        /// <param name="forced">Whether it reloads regardless of the version.</param>
        private sealed class Run(bool forced)
        {
            /// <summary>Whether it reloads regardless of the version; a queued run can be upgraded until it starts.</summary>
            public bool Forced { get; set; } = forced;

            /// <summary>The refresh's task.</summary>
            public Task Task { get; set; } = Task.CompletedTask;
        }
    }

    /// <summary>The catalog lacks the platform's latest catalog migration, found after startup.</summary>
    /// <param name="command">The migrator command that migrates the catalog.</param>
    internal sealed class CatalogNotMigratedException(string command)
        : InvalidOperationException($"The catalog lacks the platform's latest catalog migration, '{CatalogMigrations.Latest}'. {command}");
}
