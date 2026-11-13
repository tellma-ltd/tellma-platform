// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Sessions
{
    /// <summary>
    ///     The host's timers on a clock the suite moves, which it moves only once a timer waits: the
    ///     session sweep every sweep interval, and the membership reconciliation five minutes after the
    ///     first snapshot and then every interval, on the instance that holds the lock alone.
    /// </summary>
    public sealed class TimerTests
    {
        [Fact]
        public async Task The_sweep_deletes_the_rows_expired_for_a_day_on_each_interval()
        {
            // The host's own sweep is taken out, so the test drives one whose waits it can see.
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.ConfigureServices = static services => _ = services.Remove(services.Single(static descriptor => descriptor.ImplementationType == typeof(SessionSweepService))));
            DateTimeOffset now = host.Time.GetUtcNow();
            host.Rows.Seed("long-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddDays(-2));
            host.Rows.Seed("just-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddHours(-1));
            host.Rows.Seed("live-row-key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "sub-1", now.AddDays(1));
            var waits = Channel.CreateUnbounded<DateTimeOffset>();
            using SessionSweepService sweep = new(
                host.Services.GetRequiredService<ICatalogSessionStore>(),
                host.Services.GetRequiredService<IOptions<TellmaSessionOptions>>(),
                host.Time,
                NullLogger<SessionSweepService>.Instance)
            {
                WaitingUntil = due => _ = waits.Writer.TryWrite(due),
            };
            await sweep.StartAsync(TestContext.Current.CancellationToken);

            // --- Nothing before the interval ---
            Assert.Equal(now.AddMinutes(15), await NextWaitAsync(waits));
            host.Time.Advance(TimeSpan.FromMinutes(14));
            Assert.Equal(3, host.Rows.Count);

            // --- On it, the rows expired for a day go, and the next sweep is an interval on ---
            host.Time.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(now.AddMinutes(30), await NextWaitAsync(waits));
            Assert.Equal(["just-expired-row-key-aaaaaaaaaaaaaaaaaaaaaaaa", "live-row-key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"], [.. host.Rows.Keys().Order(StringComparer.Ordinal)]);

            await sweep.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task The_reconciliation_runs_five_minutes_after_the_first_snapshot_and_then_every_interval()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            DateTimeOffset firstRun = host.Services.GetRequiredService<ITenantRegistry>().SnapshotLoadedAt!.Value + MembershipReconcileService.FirstRunDelay;
            FakeReconcileStore store = new();
            store.Users[1] = [new TenantMembershipRecord { TenantId = 1, Subject = "sub-1", IsActive = true }];
            var waits = Channel.CreateUnbounded<DateTimeOffset>();
            using MembershipReconcileService service = Service(host, store);
            service.WaitingUntil = due => _ = waits.Writer.TryWrite(due);
            await service.StartAsync(TestContext.Current.CancellationToken);

            // --- Nothing before five minutes past the snapshot ---
            Assert.Equal(firstRun, await NextWaitAsync(waits));
            host.Time.Advance(firstRun - host.Time.GetUtcNow() - TimeSpan.FromMinutes(1));
            Assert.Empty(store.Reconciled);

            // --- The first run: every active tenant's users; the next is due a day after it was ---
            host.Time.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(firstRun.AddDays(1), await NextWaitAsync(waits));
            (int tenantId, IReadOnlyList<TenantMembershipRecord> records) = Assert.Single(store.Reconciled);
            Assert.Equal(1, tenantId);
            Assert.Equal("sub-1", Assert.Single(records).Subject);

            // --- The next, a day later ---
            host.Time.Advance(TimeSpan.FromHours(23));
            _ = Assert.Single(store.Reconciled);
            host.Time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(firstRun.AddDays(2), await NextWaitAsync(waits));
            Assert.Equal(2, store.Reconciled.Count);

            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public void A_run_is_due_an_interval_after_the_last_was_and_the_intervals_an_overrun_spans_collapse_into_one_due_at_once()
        {
            DateTimeOffset due = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var interval = TimeSpan.FromMinutes(15);

            // --- A run inside its interval: the next is an interval after this one was due ---
            Assert.Equal(due.AddMinutes(15), HostedSchedule.Next(due, interval, due.AddMinutes(3)));

            // --- A run that overran: the last slot it overran is due at once, and the schedule keeps its grid ---
            Assert.Equal(due.AddMinutes(15), HostedSchedule.Next(due, interval, due.AddMinutes(15)));
            Assert.Equal(due.AddMinutes(45), HostedSchedule.Next(due, interval, due.AddMinutes(47)));

            // --- A clock that went back: still an interval on ---
            Assert.Equal(due.AddMinutes(15), HostedSchedule.Next(due, interval, due.AddMinutes(-1)));
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

        /// <summary>
        ///     The due time of a timer's next wait, once the wait is in place: from then the fake clock can
        ///     be moved without the move landing before the timer exists, and everything the timer did
        ///     before the wait has happened.
        /// </summary>
        /// <param name="waits">The due times the timer reports as it starts each wait.</param>
        /// <returns>The due time.</returns>
        private static Task<DateTimeOffset> NextWaitAsync(Channel<DateTimeOffset> waits)
        {
            return waits.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }
}
