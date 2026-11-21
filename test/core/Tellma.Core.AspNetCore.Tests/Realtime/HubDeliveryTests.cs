// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Testing.WebHost;
using Tellma.Testing.WebHost.Realtime;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The SignalR publisher: an event reaches the connections of the users it names in the
    ///     scope's tenant, or every connection of the tenant, and never another user's or another
    ///     tenant's; and the refusals it shares with every publisher.
    /// </summary>
    public sealed class HubDeliveryTests
    {
        /// <summary>A registered event without a payload.</summary>
        private const string InboxChanged = "inbox.changed";

        /// <summary>A registered event the barrier sends, so a client that missed an event knows it did.</summary>
        private const string Barrier = "cache.changed";

        [Fact]
        public async Task An_event_for_a_user_reaches_that_user_s_connections_in_the_tenant_alone()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using MetricCollector<long> events = new(
                host.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), RealtimeTelemetryNames.MeterName, ApiTelemetryNames.RealtimeEvents);

            // --- Ann is user 7 in tenant 1 and in tenant 2, on two tabs in tenant 1; Bob is user 8 in tenant 1 ---
            host.Users.UserIds[(RealtimeTestHost.Active, "ann")] = 7;
            host.Users.UserIds[(RealtimeTestHost.Other, "ann")] = 7;
            host.Users.UserIds[(RealtimeTestHost.Active, "bob")] = 8;
            string ann = host.Cookie("ann");
            await using HubClient annTab1 = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient annTab2 = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient annOther = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, ann);
            await using HubClient bob = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));

            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(InboxChanged, [7], null));

            // --- Both of Ann's tenant-1 tabs, with the tenant named ---
            foreach (HubClient tab in new[] { annTab1, annTab2 })
            {
                JsonElement envelope = await tab.NextAsync();
                Assert.Equal(InboxChanged, Envelope.Name(envelope));
                Assert.Equal(RealtimeTestHost.Active, Envelope.TenantId(envelope));
            }

            // --- Neither Bob nor Ann's tenant-2 connection: the barrier is the first thing each receives ---
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Other, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            Assert.Equal(Barrier, Envelope.Name(await bob.NextAsync()));
            JsonElement other = await annOther.NextAsync();
            Assert.Equal(Barrier, Envelope.Name(other));
            Assert.Equal(RealtimeTestHost.Other, Envelope.TenantId(other));

            Assert.Contains(events.GetMeasurementSnapshot(), static m => Equals(m.Tags[ApiTelemetryNames.EventTag], InboxChanged));
        }

        [Fact]
        public async Task An_event_for_no_user_reaches_every_connection_of_the_tenant_and_no_other_tenant()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            host.Users.UserIds[(RealtimeTestHost.Active, "bob")] = 8;
            await using HubClient ann = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("ann"));
            await using HubClient bob = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));
            await using HubClient other = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Other, host.Cookie("ann"));

            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(InboxChanged, [], null));
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Other, new ClientEvent(Barrier, [], new { tag = "barrier" }));

            Assert.Equal(InboxChanged, Envelope.Name(await ann.NextAsync()));
            Assert.Equal(InboxChanged, Envelope.Name(await bob.NextAsync()));
            Assert.Equal(Barrier, Envelope.Name(await other.NextAsync()));
        }

        [Fact]
        public async Task The_publisher_refuses_an_unregistered_name_an_oversized_payload_and_a_tenantless_scope()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();

            // --- Unregistered, and too large: refused before anything is sent ---
            InvalidOperationException unregistered = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent("made.up", [], null)));
            Assert.Contains("made.up", unregistered.Message, StringComparison.Ordinal);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(InboxChanged, [], new string('x', ClientEvent.MaxPayloadBytes))));

            // --- A scope without a tenant: its event would name no tenant's groups ---
            AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await using (scope)
            {
                IClientEventPublisher publisher = scope.ServiceProvider.GetRequiredService<IClientEventPublisher>();
                InvalidOperationException tenantless = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new ClientEvent(InboxChanged, [], null)));
                Assert.Contains("without a tenant", tenantless.Message, StringComparison.Ordinal);
            }
        }
    }
}
