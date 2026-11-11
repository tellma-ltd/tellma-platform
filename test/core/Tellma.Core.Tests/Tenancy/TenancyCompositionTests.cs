// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The tenancy runtime as the composition root installs it, under a real generic host: the
    ///     registrations, the startup load after the gate, the refresh loop on the host's clock, and a
    ///     startup a behind catalog fails.
    /// </summary>
    public class TenancyCompositionTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Every_tenancy_service_resolves_as_one_singleton()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Same(provider.GetRequiredService<ITenantRegistry>(), provider.GetRequiredService<ITenantRegistry>());
            Assert.IsType<TenantRegistry>(provider.GetRequiredService<ITenantRegistry>());
            Assert.IsType<TenantConnectionFactory>(provider.GetRequiredService<ITenantConnectionFactory>());
            Assert.IsType<TenantCatalog>(provider.GetRequiredService<ITenantCatalog>());
            Assert.Same(provider.GetRequiredService<TenantMembershipDirectory>(), provider.GetRequiredService<ITenantMembershipDirectory>());
            Assert.Contains(provider.GetServices<IHostedService>(), static service => service is TenantRegistryRefreshService);
            Assert.IsType<TellmaStartupGate>(provider.GetServices<IHostedService>().First());
        }

        [Fact]
        public async Task The_host_loads_the_snapshot_at_start_and_the_loop_probes_every_interval()
        {
            FakeTimeProvider time = new();
            FakeCatalogReader catalog = new();
            catalog.Write(FakeCatalogReader.Row(1), FakeCatalogReader.Row(2));
            using IHost host = BuildHost(time, catalog);

            await host.StartAsync(Token);
            ITenantRegistry registry = host.Services.GetRequiredService<ITenantRegistry>();
            Assert.Equal([1, 2], registry.Tenants.Select(static tenant => tenant.Id));
            Assert.Equal(1, catalog.HistoryReads);

            // One interval: one probe, no reload.
            time.Advance(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(() => catalog.Probes == 1);
            Assert.Equal(1, catalog.Reloads);

            // A write elsewhere: the next interval reloads.
            catalog.Write(FakeCatalogReader.Row(1));
            time.Advance(TimeSpan.FromSeconds(15));
            // The reader counts a reload when it is asked, before the new snapshot is swapped in,
            // so the wait is on the snapshot the reload serves.
            await WaitUntilAsync(() => registry.Tenants.Count == 1);
            Assert.Equal([1], registry.Tenants.Select(static tenant => tenant.Id));
            Assert.Equal(2, catalog.Reloads);

            await host.StopAsync(Token);
        }

        [Fact]
        public async Task A_failing_refresh_never_ends_the_loop()
        {
            FakeTimeProvider time = new();
            FakeCatalogReader catalog = new();
            catalog.Write(FakeCatalogReader.Row(1));
            using IHost host = BuildHost(time, catalog);
            await host.StartAsync(Token);

            catalog.Failure = new InvalidOperationException("The catalog is unreachable.");
            time.Advance(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(() => catalog.Probes == 1);
            catalog.Failure = null;
            time.Advance(TimeSpan.FromSeconds(15));
            await WaitUntilAsync(() => catalog.Probes == 2);

            await host.StopAsync(Token);
        }

        [Fact]
        public async Task A_catalog_behind_the_platform_fails_the_host_start()
        {
            FakeCatalogReader catalog = new() { History = CatalogHistoryState.Behind };
            using IHost host = BuildHost(new FakeTimeProvider(), catalog);

            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => host.StartAsync(Token));

            Assert.Equal("core.catalog", Assert.Single(failure.Problems).Source);
        }

        [Fact]
        public async Task The_gate_runs_before_the_catalog_is_touched()
        {
            FakeCatalogReader catalog = new();
            using IHost host = BuildHost(new FakeTimeProvider(), catalog, TestComposition.Sound(("Tellma:Sql:Profiles:default", null)));

            _ = await Assert.ThrowsAsync<TellmaCompositionException>(() => host.StartAsync(Token));

            Assert.Equal(0, catalog.HistoryReads);
        }

        [Fact]
        public async Task A_tenant_scope_reaches_only_its_own_tenants_string()
        {
            FakeCatalogReader catalog = new();
            catalog.Write(FakeCatalogReader.Row(1, database: "tenant-one"), FakeCatalogReader.Row(2, database: "tenant-two"));
            using IHost host = BuildHost(new FakeTimeProvider(), catalog);
            await host.StartAsync(Token);
            ITenantConnectionFactory connections = host.Services.GetRequiredService<ITenantConnectionFactory>();

            Assert.Contains("Initial Catalog=tenant-one", connections.GetConnectionString(1), StringComparison.Ordinal);
            Assert.Contains("Initial Catalog=tenant-two", connections.GetConnectionString(2), StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-two", connections.GetConnectionString(1), StringComparison.Ordinal);
            _ = Assert.Throws<TenantNotFoundException>(() => connections.GetConnectionString(3));
            await host.StopAsync(Token);
        }

        /// <summary>A Production host composed over a fake catalog and clock.</summary>
        /// <param name="time">The clock.</param>
        /// <param name="catalog">The catalog.</param>
        /// <param name="configuration">The configuration; the sound one when null.</param>
        /// <returns>The host, not started.</returns>
        private static IHost BuildHost(FakeTimeProvider time, FakeCatalogReader catalog, IReadOnlyDictionary<string, string?>? configuration = null)
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
            });

            builder.Configuration.AddInMemoryCollection(configuration ?? TestComposition.SoundConfiguration);
            builder.Services.AddSingleton(SandboxContext.Never);
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.Services.AddTellma(TestComposition.Slug, builder.Configuration, builder.Environment, static _ => { }).UseFakeCatalog(catalog);
            return builder.Build();
        }

        /// <summary>Waits until a condition holds, for work the host runs on another thread.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }
}
