// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Tenancy;
using Tellma.Testing.WebHost;
using Tellma.Testing.WebHost.Realtime;

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
            // Another instance may hold its connections in a tenant this one holds none in, which a
            // mode that reaches every instance reaches through the group.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(static options => RecordingLifetimeManager.Compose(options));
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
        public async Task In_the_in_process_mode_an_ended_session_reads_no_memberships()
        {
            // A group send would reach this instance's connections alone, which the tracker holds.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            TestSession session = host.Session("ann");
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);
            int before = host.Memberships.Reads;

            await host.Services.GetRequiredService<HubConnectionTracker>().SessionsTerminatedAsync("ann", [session.Handle]);

            Assert.Equal("session-terminated", Envelope.Payload(await client.NextAsync(), "reason"));
            _ = await client.ClosedAsync();
            Assert.Equal(before, host.Memberships.Reads);
        }

        [Fact]
        public async Task When_the_memberships_cannot_be_read_the_session_s_own_connections_are_still_told_and_closed()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options =>
            {
                HubConnectTests.CaptureDebug(options, logs);
                _ = RecordingLifetimeManager.Compose(options);
            });
            TestSession session = host.Session("ann");
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);
            host.Memberships.Failure = new InvalidOperationException("The catalog is unreachable.");

            await host.Services.GetRequiredService<HubConnectionTracker>().SessionsTerminatedAsync("ann", [session.Handle]);

            Assert.Equal("session-terminated", Envelope.Payload(await client.NextAsync(), "reason"));
            _ = await client.ClosedAsync();
            CapturedEvent unread = Assert.Single(logs.Events, static e => e.EventName == "HubSessionTenantsUnread");
            Assert.Equal(LogLevel.Warning, unread.Level);
        }

        [Fact]
        public async Task With_the_hub_off_a_session_s_end_reads_no_memberships()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.ConfigureServices += static services => services.Replace(ServiceDescriptor.Singleton(RecordingLifetimeManager.ReachingEveryInstance())));
            int before = host.Memberships.Reads;

            await host.Services.GetRequiredService<HubConnectionTracker>().SessionsTerminatedAsync("ann", ["handle-1"]);

            Assert.Equal(before, host.Memberships.Reads);
        }

        [Fact]
        public async Task A_connection_joining_under_an_ended_session_is_told_why_and_refused()
        {
            // The negotiate passed before the close was recorded; the connect comes after it.
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => HubConnectTests.CaptureDebug(options, logs));
            using MetricCollector<long> connects = HubConnectTests.Collect(host, RealtimeTelemetryNames.Connects);
            TestSession session = host.Session("ann");
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            await tracker.SessionsTerminatedAsync("ann", [session.Handle]);

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);

            JsonElement envelope = await client.NextAsync();
            Assert.Equal("session.ended", Envelope.Name(envelope));
            Assert.Equal(RealtimeTestHost.Active, Envelope.TenantId(envelope));
            Assert.Equal("session-terminated", Envelope.Payload(envelope, "reason"));
            _ = await client.ClosedAsync();
            Assert.Equal(0, tracker.Count);
            _ = Assert.Single(connects.GetMeasurementSnapshot(), static m => Equals(m.Tags[RealtimeTelemetryNames.OutcomeTag], RealtimeMetrics.RefusedOutcome));
            Assert.Contains(logs.Events, static e => e.EventName == "HubConnectionRefused" && e.Members["Reason"] == "session-terminated");
        }

        [Fact]
        public async Task A_connection_joining_under_a_revoked_access_is_told_why_and_refused_in_that_tenant_alone()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            string ann = host.Cookie("ann");
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            await tracker.TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");

            await using HubClient refused = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient elsewhere = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, ann);
            await using HubClient colleague = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));

            Assert.Equal("access-revoked", Envelope.Payload(await refused.NextAsync(), "reason"));
            _ = await refused.ClosedAsync();
            Assert.Equal(HubConnectionState.Connected, elsewhere.Connection.State);
            Assert.Equal(HubConnectionState.Connected, colleague.Connection.State);
            Assert.Equal(2, tracker.Count);
        }

        [Theory]
        [InlineData(TenantState.ReadOnly)]
        [InlineData(TenantState.Frozen)]
        [InlineData(TenantState.Active)]
        public async Task A_connection_joining_an_unavailable_tenant_is_refused_until_the_tenant_reads_again(TenantState state)
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;
            await tracker.OnStateChangedAsync(tenant with { State = TenantState.Suspended }, TenantState.Active);

            // --- Refused while remembered, and told why ---
            await using (HubClient refused = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie()))
            {
                Assert.Equal("tenant-unavailable", Envelope.Payload(await refused.NextAsync(), "reason"));
                _ = await refused.ClosedAsync();
            }

            // --- A state that allows reads forgets the close ---
            await tracker.OnStateChangedAsync(tenant with { State = state }, TenantState.Suspended);
            await using HubClient admitted = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());
            Assert.Equal(HubConnectionState.Connected, admitted.Connection.State);
            Assert.Equal(1, tracker.Count);
        }

        [Fact]
        public async Task Self_hosted_a_remembered_close_expires_after_ten_seconds()
        {
            // The connect itself passes the front door, so the memo covers only the door's moment.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            Assert.Equal(TimeSpan.FromSeconds(10), HubConnectionTracker.SelfHostedMemoLifetime);
            await AssertMemosExpireAsync(host, HubConnectionTracker.SelfHostedMemoLifetime);
        }

        [Fact]
        public async Task On_Azure_SignalR_Service_a_remembered_close_lasts_the_service_token_s_five_minutes()
        {
            // A negotiate's service token opens a connection without passing the door again. The
            // hosting check would refuse an Azure mode over the in-process manager; the tracker reads
            // the mode alone.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(static options =>
                options.ConfigureServices += static services =>
                {
                    _ = services.Replace(ServiceDescriptor.Singleton(new RealtimeHosting(RealtimeMode.Azure, typeof(DefaultHubLifetimeManager<TellmaHub>), azureOptionsType: null)));
                    foreach (ServiceDescriptor check in services.Where(static d => d.ImplementationType == typeof(RealtimeHostingCheck)).ToList())
                    {
                        _ = services.Remove(check);
                    }
                });
            Assert.Equal(TimeSpan.FromMinutes(5), HubConnectionTracker.AzureMemoLifetime);
            await AssertMemosExpireAsync(host, HubConnectionTracker.AzureMemoLifetime);
        }

        [Fact]
        public async Task A_stalled_send_delays_no_other_connection_s_close_and_is_counted_untold()
        {
            CapturingLoggerProvider logs = new();
            RecordingLifetimeManager? manager = null;
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options =>
            {
                HubConnectTests.CaptureDebug(options, logs);
                manager = RecordingLifetimeManager.Compose(options);
            });
            using MetricCollector<long> closes = HubConnectTests.Collect(host, RealtimeTelemetryNames.Closes);
            using MetricCollector<long> events = HubConnectTests.Collect(host, ApiTelemetryNames.RealtimeEvents);
            string ann = host.Cookie("ann");
            await using HubClient live = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient first = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient second = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            manager!.StalledConnections[first.Connection.ConnectionId!] = true;
            manager.StalledConnections[second.Connection.ConnectionId!] = true;

            // --- Two sends that never complete wait out their bounds side by side, not one after the other ---
            var elapsed = Stopwatch.StartNew();
            await host.Services.GetRequiredService<HubConnectionTracker>().TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");
            elapsed.Stop();
            Assert.True(elapsed.Elapsed < HubConnectionTracker.SendBound * 2, $"The close took {elapsed.Elapsed}.");

            // --- The live connection told, every connection closed, the stalled ones counted untold ---
            Assert.Equal("access-revoked", Envelope.Payload(await live.NextAsync(), "reason"));
            foreach (HubClient client in new[] { live, first, second })
            {
                _ = await client.ClosedAsync();
            }

            Assert.Equal(1, Closed(closes, "access-revoked", RealtimeMetrics.ToldValue));
            Assert.Equal(2, Closed(closes, "access-revoked", RealtimeMetrics.UntoldValue));
            Assert.Empty(events.GetMeasurementSnapshot());
            Assert.Equal(2, logs.Events.Count(static e => e.EventName == "ClientEventFailed"));
        }

        [Fact]
        public async Task A_failed_group_send_leaves_this_instance_s_connections_told()
        {
            CapturingLoggerProvider logs = new();
            RecordingLifetimeManager? manager = null;
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options =>
            {
                HubConnectTests.CaptureDebug(options, logs);
                manager = RecordingLifetimeManager.Compose(options);
            });
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("ann"));
            manager!.FailGroupSends = true;

            await host.Services.GetRequiredService<HubConnectionTracker>().TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");

            // --- The group send was tried and failed; the connection's own send still went first ---
            Assert.Equal([HubGroups.Subject(RealtimeTestHost.Active, "ann")], manager.GroupSends);
            Assert.Equal("access-revoked", Envelope.Payload(await client.NextAsync(), "reason"));
            _ = await client.ClosedAsync();
            Assert.Contains(logs.Events, static e => e.EventName == "ClientEventFailed");
        }

        [Fact]
        public async Task A_tenant_s_state_change_tells_this_instance_s_connections_and_sends_to_no_group()
        {
            // Every instance runs the change, so a group send would reach each connection once per instance.
            RecordingLifetimeManager? manager = null;
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => manager = RecordingLifetimeManager.Compose(options));
            await using HubClient reading = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());
            await using HubClient closing = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, host.Cookie());
            ITenantRegistry registry = host.Services.GetRequiredService<ITenantRegistry>();
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();

            await tracker.OnStateChangedAsync(registry.Find(RealtimeTestHost.Active)!.Descriptor with { State = TenantState.ReadOnly }, TenantState.Active);
            await tracker.OnStateChangedAsync(registry.Find(RealtimeTestHost.Other)!.Descriptor with { State = TenantState.Suspended }, TenantState.Active);

            Assert.Equal("cache.changed", Envelope.Name(await reading.NextAsync()));
            Assert.Equal("tenant-unavailable", Envelope.Payload(await closing.NextAsync(), "reason"));
            _ = await closing.ClosedAsync();
            Assert.Empty(manager!.GroupSends);
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
            Assert.Equal(1, Closed(closes, "access-revoked", RealtimeMetrics.ToldValue));
            Assert.Equal(0, Closed(closes, "access-revoked", RealtimeMetrics.UntoldValue));
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

        /// <summary>Records a close of each kind, then connects just inside and just past a memo lifetime.</summary>
        /// <param name="host">The host.</param>
        /// <param name="lifetime">The memo lifetime the host's mode keeps.</param>
        /// <returns>A task that completes when both connects were checked.</returns>
        private static async Task AssertMemosExpireAsync(TellmaTestHost host, TimeSpan lifetime)
        {
            TestSession session = host.Session("ann");
            HubConnectionTracker tracker = host.Services.GetRequiredService<HubConnectionTracker>();
            await tracker.SessionsTerminatedAsync("ann", [session.Handle]);
            await tracker.TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;
            await tracker.OnStateChangedAsync(tenant with { State = TenantState.Suspended }, TenantState.Active);

            // --- Just inside the lifetime: still refused ---
            host.Time.Advance(lifetime - TimeSpan.FromSeconds(1));
            await using (HubClient refused = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie))
            {
                Assert.Equal("session-terminated", Envelope.Payload(await refused.NextAsync(), "reason"));
                _ = await refused.ClosedAsync();
            }

            // --- Past it: every memo has expired, and the connection is admitted ---
            host.Time.Advance(TimeSpan.FromSeconds(2));
            await using HubClient admitted = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);
            Assert.Equal(HubConnectionState.Connected, admitted.Connection.State);
            Assert.False(admitted.HasUnread());
            Assert.Equal(1, tracker.Count);
        }

        /// <summary>The connections counted closed for a reason, told or untold.</summary>
        /// <param name="closes">The closes collected.</param>
        /// <param name="reason">The reason.</param>
        /// <param name="told">The told tag.</param>
        /// <returns>How many.</returns>
        private static long Closed(MetricCollector<long> closes, string reason, string told)
        {
            return closes.GetMeasurementSnapshot()
                .Where(m => Equals(m.Tags[RealtimeTelemetryNames.ReasonTag], reason) && Equals(m.Tags[RealtimeTelemetryNames.ToldTag], told))
                .Sum(static m => m.Value);
        }
    }
}
