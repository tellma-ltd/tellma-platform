// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Text.Json;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The tracker's closes: by session, by (tenant, subject) and by tenant, each telling the
    ///     group's connections <c>session.ended</c> before aborting them, and closing nothing else;
    ///     and the tenant-state change that only tells open tabs to re-read the tenant.
    /// </summary>
    public sealed class HubConnectionTrackerTests
    {
        [Fact]
        public async Task The_tracker_is_the_hub_s_session_and_tenant_state_listener()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();

            Assert.Contains(tracker, host.Services.GetServices<ISessionTerminationListener>());
            Assert.Contains(tracker, host.Services.GetServices<ITenantStateListener>());
        }

        [Fact]
        public async Task A_signed_out_session_is_told_in_each_of_its_tenants_then_closed_and_no_other_session_is()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => HubConnectTests.CaptureDebug(options, logs));
            using MetricCollector<long> closes = HubConnectTests.Collect(host, RealtimeTelemetryNames.Closes);

            // --- One session open in two tenants, and the same subject's other session ---
            TestSession ending = host.Session("ann", sessionId: "sid-a");
            TestSession staying = host.Session("ann", sessionId: "sid-b");
            await using HubClient first = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ending.Cookie);
            await using HubClient second = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, ending.Cookie);
            await using HubClient other = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, staying.Cookie);

            // --- The sign-out, through the store, which tells the listeners ---
            await host.Services.GetRequiredService<CatalogSessionStore>()
                .EndAsync(new EndedSession(ending.Key, ending.Handle, "ann"), TestContext.Current.CancellationToken);

            // --- Told with each connection's own tenant, then closed ---
            foreach ((HubClient client, int tenantId) in new[] { (first, RealtimeTestHost.Active), (second, RealtimeTestHost.Other) })
            {
                JsonElement envelope = await client.NextAsync();
                Assert.Equal("session.ended", Envelope.Name(envelope));
                Assert.Equal(tenantId, Envelope.TenantId(envelope));
                Assert.Equal("session-terminated", Envelope.Payload(envelope, "reason"));
                _ = await client.ClosedAsync();
            }

            // --- The other session is untouched ---
            Assert.Equal(HubConnectionState.Connected, other.Connection.State);
            Assert.False(other.HasUnread());
            Assert.Equal(2, closes.GetMeasurementSnapshot()
                .Where(static m => Equals(m.Tags[RealtimeTelemetryNames.ReasonTag], "session-terminated"))
                .Sum(static m => m.Value));
            Assert.Equal(2, logs.Events.Count(static e => e.EventName == "HubConnectionsClosed" && e.Members["Reason"] == "session-terminated"));
        }

        [Fact]
        public async Task An_ended_session_is_also_told_in_the_tenants_its_subject_belongs_to()
        {
            // Another instance may hold its connections in a tenant this one holds none in.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            TestSession session = host.Session("ann");
            TenantDescriptor other = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Other)!.Descriptor;
            host.Memberships.Memberships["ann"] = [new TenantMembership(other, IsActive: true, DateTimeOffset.UtcNow)];
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, session.Cookie);

            // Forget the connection, as if another instance hosted it: only the membership can find it.
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            await HubConnectTests.WaitUntilAsync(() => tracker.Count == 1);
            string id = Assert.Single(tracker.ConnectionIds);
            _ = tracker.Remove(id);

            await tracker.SessionsTerminatedAsync("ann", [session.Handle]);

            JsonElement envelope = await client.NextAsync();
            Assert.Equal("session.ended", Envelope.Name(envelope));
            Assert.Equal(RealtimeTestHost.Other, Envelope.TenantId(envelope));
        }

        [Fact]
        public async Task A_connection_joining_under_an_ended_session_is_refused()
        {
            // The negotiate passed before the close was recorded; the connect comes after it.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            TestSession session = host.Session("ann");
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            await tracker.SessionsTerminatedAsync("ann", [session.Handle]);

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);

            _ = await client.ClosedAsync();
            Assert.Equal(0, tracker.Count);
        }

        [Fact]
        public async Task A_revoked_access_closes_the_subject_s_connections_in_that_tenant_alone()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using MetricCollector<long> closes = HubConnectTests.Collect(host, RealtimeTelemetryNames.Closes);
            string ann = host.Cookie("ann");
            await using HubClient revoked = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient elsewhere = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, ann);
            await using HubClient colleague = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));

            await host.Services.GetRequiredService<HubConnectionTracker>().TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");

            JsonElement envelope = await revoked.NextAsync();
            Assert.Equal("session.ended", Envelope.Name(envelope));
            Assert.Equal("access-revoked", Envelope.Payload(envelope, "reason"));
            _ = await revoked.ClosedAsync();

            Assert.Equal(HubConnectionState.Connected, elsewhere.Connection.State);
            Assert.Equal(HubConnectionState.Connected, colleague.Connection.State);
            Assert.False(elsewhere.HasUnread());
            Assert.False(colleague.HasUnread());
            Assert.Contains(closes.GetMeasurementSnapshot(), static m => m.Value == 1 && Equals(m.Tags[RealtimeTelemetryNames.ReasonTag], "access-revoked"));
        }

        [Theory]
        [InlineData(TenantState.Suspended)]
        [InlineData(TenantState.Retired)]
        public async Task A_tenant_that_becomes_unavailable_closes_its_connections_alone(TenantState state)
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            await using HubClient closing = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("ann"));
            await using HubClient staying = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, host.Cookie("ann"));
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;

            await host.Services.GetRequiredService<HubConnectionTracker>().OnStateChangedAsync(tenant with { State = state }, TenantState.Active);

            JsonElement envelope = await closing.NextAsync();
            Assert.Equal("session.ended", Envelope.Name(envelope));
            Assert.Equal("tenant-unavailable", Envelope.Payload(envelope, "reason"));
            _ = await closing.ClosedAsync();
            Assert.Equal(HubConnectionState.Connected, staying.Connection.State);
            Assert.False(staying.HasUnread());
        }

        [Fact]
        public async Task A_tenant_that_leaves_the_registry_closes_its_connections()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;

            await host.Services.GetRequiredService<HubConnectionTracker>().OnRemovedAsync(tenant);

            Assert.Equal("tenant-unavailable", Envelope.Payload(await client.NextAsync(), "reason"));
            _ = await client.ClosedAsync();
        }

        [Theory]
        [InlineData(TenantState.ReadOnly)]
        [InlineData(TenantState.Frozen)]
        [InlineData(TenantState.Active)]
        public async Task Any_other_state_change_tells_the_tenant_to_re_read_itself_and_closes_nothing(TenantState state)
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using MetricCollector<long> closes = HubConnectTests.Collect(host, RealtimeTelemetryNames.Closes);
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());
            await using HubClient other = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, host.Cookie());
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;

            await host.Services.GetRequiredService<HubConnectionTracker>().OnStateChangedAsync(tenant with { State = state }, TenantState.Active);

            JsonElement envelope = await client.NextAsync();
            Assert.Equal("cache.changed", Envelope.Name(envelope));
            Assert.Equal(RealtimeTestHost.Active, Envelope.TenantId(envelope));
            Assert.Equal("tenant", Envelope.Payload(envelope, "tag"));
            Assert.Equal(HubConnectionState.Connected, client.Connection.State);
            Assert.False(other.HasUnread());
            Assert.Empty(closes.GetMeasurementSnapshot());
        }
    }
}
