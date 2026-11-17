// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The registry's refresh loop on a fake clock: a host that loads at startup refreshes on every
    ///     tick, even after its first load failed; a host that leaves the first load to its command,
    ///     as the migrator does, reads nothing until that load, so it never reads a catalog the
    ///     command has yet to create.
    /// </summary>
    public sealed class TenantRegistryRefreshServiceTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     Without a startup load, ticks pass without a read until the host's own first load;
        ///     every tick after it refreshes.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Without_a_startup_load_the_loop_reads_nothing_until_the_hosts_own_first_load()
        {
            using RegistryHarness harness = new(loadOnStart: false);
            harness.Catalog.Write(FakeCatalogReader.Row(1));
            using TenantRegistryRefreshService service = Service(harness, loadOnStart: false);
            await service.StartAsync(Token);
            try
            {
                // --- Ticks before the first load: no read reaches the gate ---
                harness.Catalog.Gate = NewGate();
                Task reached = harness.Catalog.AtGate.Task;
                for (int tick = 0; tick < 3; tick++)
                {
                    harness.Time.Advance(harness.CatalogOptions.RefreshInterval);
                }

                // A read that must not come cannot be awaited, so the wait is bounded; the read
                // after the load below shows that a tick's read reaches the gate at once.
                Assert.NotSame(reached, await Task.WhenAny(reached, Task.Delay(TimeSpan.FromSeconds(1), Token)));
                Assert.Equal(0, harness.Catalog.Reloads + harness.Catalog.Probes);

                // --- The host's own first load, then a tick probes the catalog ---
                harness.Catalog.Gate = null;
                await harness.Registry.RefreshAsync(force: true, Token);
                TaskCompletionSource gate = NewGate();
                harness.Catalog.Gate = gate;
                harness.Time.Advance(harness.CatalogOptions.RefreshInterval);
                await harness.Catalog.AtGate.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
                Assert.Equal(1, harness.Catalog.Reloads);
                Assert.Equal(1, harness.Catalog.Probes);
                gate.SetResult();
            }
            finally
            {
                await service.StopAsync(Token);
            }
        }

        /// <summary>
        ///     With a startup load, a tick refreshes even when that load failed, so a host that
        ///     started while its catalog was unreachable becomes ready once the catalog answers.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task With_a_startup_load_a_tick_refreshes_even_after_the_first_load_failed()
        {
            // The migration check is left out, so every read the loop makes is a reload.
            using RegistryHarness harness = new(verifyCatalog: false);
            harness.Catalog.Write(FakeCatalogReader.Row(1));
            harness.Catalog.Failure = new TimeoutException("The catalog did not answer.");
            using TenantRegistryRefreshService service = Service(harness, loadOnStart: true);
            await service.StartAsync(Token);
            try
            {
                Assert.False(harness.Registry.IsAvailable);
                Assert.Equal(1, harness.Catalog.Reloads);

                // --- The catalog answers again; the next tick reloads ---
                harness.Catalog.Failure = null;
                TaskCompletionSource gate = NewGate();
                harness.Catalog.Gate = gate;
                harness.Time.Advance(harness.CatalogOptions.RefreshInterval);
                await harness.Catalog.AtGate.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
                Assert.Equal(2, harness.Catalog.Reloads);
                gate.SetResult();
            }
            finally
            {
                await service.StopAsync(Token);
            }
        }

        /// <summary>The loop over a harness's registry, catalog options and clock.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="loadOnStart">Whether the host loads at startup.</param>
        /// <returns>The loop, not started.</returns>
        private static TenantRegistryRefreshService Service(RegistryHarness harness, bool loadOnStart)
        {
            return new TenantRegistryRefreshService(
                harness.Registry,
                Options.Create(harness.CatalogOptions),
                Options.Create(new TenantRegistryStartupOptions { LoadOnStart = loadOnStart }),
                harness.Time);
        }

        /// <summary>A gate that holds a read until the test opens it.</summary>
        /// <returns>The gate.</returns>
        private static TaskCompletionSource NewGate()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
