// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     A tenant registry over a fake catalog, a fake clock, a capturing logger and the real meter
    ///     factory, with listeners the test registers.
    /// </summary>
    internal sealed class RegistryHarness : IDisposable
    {
        /// <summary>The deployment every composed string names.</summary>
        public static readonly DeploymentIdentity Deployment = new("acme", "Production");

        private readonly ServiceProvider _services;

        /// <summary>Creates the harness.</summary>
        /// <param name="configure">Changes the SQL options before the registry is created.</param>
        /// <param name="isDevelopment">Whether the composition runs in Development.</param>
        /// <param name="verifyCatalog">Whether startup verifies the catalog's migrations.</param>
        /// <param name="configureServices">Adds services, such as scoped listeners, beside the given ones.</param>
        /// <param name="listenerTimeout">The bound on one listener call; 5 seconds when null.</param>
        /// <param name="listeners">The tenant-state listeners.</param>
        public RegistryHarness(
            Action<TellmaSqlOptions>? configure = null,
            bool isDevelopment = false,
            bool verifyCatalog = true,
            Action<IServiceCollection>? configureServices = null,
            TimeSpan? listenerTimeout = null,
            params ITenantStateListener[] listeners)
        {
            TellmaSqlOptions sql = new();
            sql.Profiles["default"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20";
            sql.AllowedServers.Add(".database.windows.net");
            configure?.Invoke(sql);
            Sql = new ChangeableOptionsMonitor<TellmaSqlOptions>(sql);

            ServiceCollection services = new();
            _ = services.AddMetrics();
            foreach (ITenantStateListener listener in listeners)
            {
                _ = services.AddSingleton(listener);
            }

            configureServices?.Invoke(services);

            _services = services.BuildServiceProvider();
            MeterFactory = _services.GetRequiredService<IMeterFactory>();

            Registry = new TenantRegistry(
                Catalog,
                Options.Create(CatalogOptions),
                Sql,
                new TenancyEnvironment("acme", isDevelopment),
                Deployment,
                Time,
                _services.GetRequiredService<IServiceScopeFactory>(),
                new TenancyMetrics(MeterFactory),
                Logger,
                Options.Create(new TenantRegistryStartupOptions
                {
                    VerifyCatalog = verifyCatalog,
                    ListenerTimeout = listenerTimeout ?? TimeSpan.FromSeconds(5),
                }));
        }

        /// <summary>The catalog.</summary>
        public FakeCatalogReader Catalog { get; } = new();

        /// <summary>The catalog options: the default interval and staleness bound.</summary>
        public TellmaCatalogOptions CatalogOptions { get; } = new() { ConnectionString = "Data Source=catalog.database.windows.net;Encrypt=True" };

        /// <summary>The SQL options, changeable.</summary>
        public ChangeableOptionsMonitor<TellmaSqlOptions> Sql { get; }

        /// <summary>The clock.</summary>
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

        /// <summary>The registry's log.</summary>
        public FakeLogger<TenantRegistry> Logger { get; } = new();

        /// <summary>The meter factory the instruments are created from.</summary>
        public IMeterFactory MeterFactory { get; }

        /// <summary>The registry.</summary>
        public TenantRegistry Registry { get; }

        /// <summary>Every log record's message and exception text, for leak checks.</summary>
        public string AllLogText => string.Join(
            Environment.NewLine,
            Logger.Collector.GetSnapshot().Select(static record => $"{record.Message} {record.Exception}"));

        /// <summary>The ids of the snapshot's tenants.</summary>
        public int[] TenantIds => [.. Registry.Tenants.Select(static tenant => tenant.Id)];

        /// <summary>Waits until every queued listener delivery has ended.</summary>
        /// <returns>A task that completes when they have.</returns>
        public Task DeliveredAsync()
        {
            return Registry.NotificationsDelivered.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        /// <summary>Writes rows and loads them as the first snapshot.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>A task that completes when the snapshot is loaded.</returns>
        public async Task LoadAsync(params TenantRow[] rows)
        {
            Catalog.Write(rows);
            await Registry.InitializeAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>The log records of one event.</summary>
        /// <param name="eventName">The event name.</param>
        /// <returns>The records.</returns>
        public List<FakeLogRecord> Events(string eventName)
        {
            return [.. Logger.Collector.GetSnapshot().Where(record => record.Id.Name == eventName)];
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Registry.Dispose();
            _services.Dispose();
        }
    }

    /// <summary>An options monitor whose value the test replaces, notifying its listeners as a configuration reload does.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="value">The first value.</param>
    internal sealed class ChangeableOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        private readonly List<Action<TOptions, string?>> _listeners = [];

        /// <inheritdoc />
        public TOptions CurrentValue { get; private set; } = value;

        /// <inheritdoc />
        public TOptions Get(string? name)
        {
            return CurrentValue;
        }

        /// <inheritdoc />
        public IDisposable? OnChange(Action<TOptions, string?> listener)
        {
            _listeners.Add(listener);
            return null;
        }

        /// <summary>Replaces the value and notifies every listener.</summary>
        /// <param name="next">The new value.</param>
        public void Change(TOptions next)
        {
            CurrentValue = next;
            foreach (Action<TOptions, string?> listener in _listeners)
            {
                listener(next, Options.DefaultName);
            }
        }
    }

    /// <summary>A listener that records every call, and optionally throws.</summary>
    /// <param name="throws">Whether every call throws after recording.</param>
    internal sealed class RecordingListener(bool throws = false) : ITenantStateListener
    {
        private readonly Lock _sync = new();
        private readonly List<(TenantDescriptor Tenant, TenantState? Previous)> _calls = [];
        private readonly List<TenantDescriptor> _removals = [];

        /// <summary>Every removal, in order.</summary>
        public IReadOnlyList<TenantDescriptor> Removals
        {
            get
            {
                lock (_sync)
                {
                    return [.. _removals];
                }
            }
        }

        /// <summary>Every call, in order.</summary>
        public IReadOnlyList<(TenantDescriptor Tenant, TenantState? Previous)> Calls
        {
            get
            {
                lock (_sync)
                {
                    return [.. _calls];
                }
            }
        }

        /// <inheritdoc />
        public Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            lock (_sync)
            {
                _calls.Add((tenant, previous));
            }

            return throws ? throw new InvalidOperationException("The listener failed.") : Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            lock (_sync)
            {
                _removals.Add(tenant);
            }

            return throws ? throw new InvalidOperationException("The listener failed.") : Task.CompletedTask;
        }
    }

    /// <summary>A listener whose every call waits for a signal the test may never give.</summary>
    internal sealed class HangingListener : ITenantStateListener
    {
        /// <summary>Released to let every call end.</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed once a call has started.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            _ = Entered.TrySetResult();
            return Release.Task;
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            _ = Entered.TrySetResult();
            return Release.Task;
        }
    }

    /// <summary>A listener that calls back into the registry, forcing a refresh, and waits for it.</summary>
    /// <param name="registry">Resolves the registry once the harness has built it.</param>
    internal sealed class ReentrantListener(Func<ITenantRegistry> registry) : ITenantStateListener
    {
        /// <summary>Completed when the listener's own refresh has finished.</summary>
        public TaskCompletionSource Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public async Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            await registry().RefreshAsync(force: true);
            _ = Refreshed.TrySetResult();
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A scoped listener that records which instance, and so which scope, took each call.</summary>
    /// <param name="seen">The instances, shared across scopes.</param>
    internal sealed class ScopedListener(List<ScopedListener> seen) : ITenantStateListener
    {
        /// <inheritdoc />
        public Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            lock (seen)
            {
                seen.Add(this);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            return OnStateChangedAsync(tenant, null);
        }
    }
}
