// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Sessions
{
    /// <summary>
    ///     The host's timers on a clock the suite moves: the session sweep every sweep interval, and the
    ///     membership reconciliation five minutes after the first snapshot and then every interval, on
    ///     the instance that holds the lock alone.
    /// </summary>
    public sealed class TimerTests
    {
        [Fact]
        public async Task The_sweep_deletes_the_rows_expired_for_a_day_on_each_interval()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            DateTimeOffset now = host.Time.GetUtcNow();
            host.Rows.Seed("long-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddDays(-2));
            host.Rows.Seed("just-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddHours(-1));
            host.Rows.Seed("live-row-key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddDays(1));

            // Nothing before the interval.
            host.Time.Advance(TimeSpan.FromMinutes(14));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Equal(3, host.Rows.Count);

            host.Time.Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(() => host.Rows.Count == 2);

            Assert.Equal(["just-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "live-row-key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"], [.. host.Rows.Keys().Order(StringComparer.Ordinal)]);
        }

        [Fact]
        public async Task The_reconciliation_runs_five_minutes_after_the_first_snapshot_and_then_every_interval()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            FakeReconcileStore store = new();
            store.Users[1] = [new TenantMembershipRecord { TenantId = 1, Subject = "sub-1", IsActive = true }];
            using MembershipReconcileService service = Service(host, store);
            await service.StartAsync(TestContext.Current.CancellationToken);

            // --- Nothing before five minutes past the snapshot ---
            host.Time.Advance(TimeSpan.FromMinutes(4));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Empty(store.Reconciled);

            // --- The first run: every active tenant's users ---
            host.Time.Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(() => !store.Reconciled.IsEmpty);
            (int tenantId, IReadOnlyList<TenantMembershipRecord> records) = Assert.Single(store.Reconciled);
            Assert.Equal(1, tenantId);
            Assert.Equal("sub-1", Assert.Single(records).Subject);

            // The interval's timer starts once the first run ends.
            await Task.Delay(100, TestContext.Current.CancellationToken);

            // --- The next, a day later ---
            host.Time.Advance(TimeSpan.FromHours(23));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            _ = Assert.Single(store.Reconciled);
            host.Time.Advance(TimeSpan.FromHours(1));
            await WaitUntilAsync(() => store.Reconciled.Count == 2);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Only_the_instance_holding_the_lock_reconciles()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            FakeReconcileStore.LockHolder holder = new();
            FakeReconcileStore leader = new(holder);
            FakeReconcileStore follower = new(holder);
            using MembershipReconcileService leading = Service(host, leader);
            using MembershipReconcileService following = Service(host, follower);

            // --- While one instance holds the lock, another runs nothing ---
            Assert.True(holder.TryTake());
            Assert.False(await following.RunOnceAsync(TestContext.Current.CancellationToken));
            Assert.Empty(follower.Reconciled);
            holder.Release();

            // --- Free, the lock is taken, the run done, and the lock released ---
            Assert.True(await leading.RunOnceAsync(TestContext.Current.CancellationToken));
            _ = Assert.Single(leader.Reconciled);
            Assert.False(holder.IsHeld);
        }

        [Fact]
        public async Task A_failed_tenant_is_skipped_and_the_run_goes_on()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.Rows.Add(FakeCatalogReader.Row(2)));
            FakeReconcileStore store = new();
            store.Failing[1] = true;
            using MembershipReconcileService service = Service(host, store);

            Assert.True(await service.RunOnceAsync(TestContext.Current.CancellationToken));

            (int tenantId, _) = Assert.Single(store.Reconciled);
            Assert.Equal(2, tenantId);
        }

        /// <summary>A reconciliation service over the host's registry and clock.</summary>
        /// <param name="host">The host.</param>
        /// <param name="store">The store.</param>
        /// <returns>The service.</returns>
        private static MembershipReconcileService Service(TellmaTestHost host, IMembershipReconcileStore store)
        {
            return new MembershipReconcileService(
                host.Services.GetRequiredService<ITenantRegistry>(),
                store,
                Options.Create(new TenancyHostOptions()),
                host.Time,
                NullLogger<MembershipReconcileService>.Instance);
        }

        /// <summary>Waits for a condition a background timer brings about.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when the condition holds.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
    }
}
