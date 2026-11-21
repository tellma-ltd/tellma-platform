// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
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
    ///     The hub on a real Azure SignalR Service instance: the hub-only token's claims reach the
    ///     connect, events reach their groups and no other, the closes tell before they abort, and
    ///     two deployments on one instance never hear each other.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The service has no emulator for a server SDK's traffic, so this suite runs nightly
    ///         against a dedicated instance; the in-process suites cover the same behaviour on every
    ///         pull request.
    ///     </para>
    ///     <para>
    ///         Two sends may reach the service over different server connections, so the service
    ///         keeps no order between them: what a client must not receive is asserted after a
    ///         barrier event and a settling time, not by the barrier's order alone.
    ///     </para>
    /// </remarks>
    [Trait("Category", "Integration")]
    [Trait("Live", "true")]
    public sealed class AzureSignalRLiveTests
    {
        private const string ConnectionStringVariable = "TELLMA_SIGNALR_TEST_CONNECTIONSTRING";

        private const string SkipReason =
            "Set TELLMA_SIGNALR_TEST_CONNECTIONSTRING to a test Azure SignalR Service instance's connection string to run the live hub suite.";

        /// <summary>A registered event the tests publish.</summary>
        private const string InboxChanged = "inbox.changed";

        /// <summary>A registered event the barrier sends.</summary>
        private const string Barrier = "cache.changed";

        /// <summary>How long a client is watched, after its barrier, for an event it must not receive.</summary>
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

        /// <summary>Whether the environment carries the service's connection string.</summary>
        public static bool HasCredentials => !string.IsNullOrWhiteSpace(ConnectionString);

        /// <summary>The test instance's connection string.</summary>
        private static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AzureSignalRLiveTests))]
        public async Task A_member_connects_through_the_service_with_the_hub_claims_alone_and_hears_its_own_events()
        {
            await using TellmaTestHost host = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            Assert.Equal(RealtimeMode.Azure, host.Services.GetRequiredService<RealtimeHosting>().Mode);

            // --- Ann is user 7 and Bob user 8, in the same tenant ---
            host.Users.UserIds[(RealtimeTestHost.Active, "ann")] = 7;
            host.Users.UserIds[(RealtimeTestHost.Active, "bob")] = 8;
            TestSession ann = host.Session("ann");
            await using HubClient annTab = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann.Cookie);
            await using HubClient bobTab = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));

            // --- The connect saw the service token's claims: the subject and the hub ids, nothing else of the cookie ---
            ClaimsPrincipal? user = host.Services.GetRequiredService<ConnectedProbe>().UserOf(annTab.Connection.ConnectionId!);
            Assert.True(TellmaHubClaims.TryRead(user, out HubConnectionClaims? claims));
            Assert.Equal(new HubConnectionClaims(RealtimeTestHost.Active, 7, "ann", ann.Handle), claims);
            Assert.Equal(
                ["sub", TellmaHubClaims.SessionHandle, TellmaHubClaims.TenantId, TellmaHubClaims.UserId],
                user!.Claims.Select(static claim => claim.Type).Order(StringComparer.Ordinal));

            // --- An event for Ann reaches Ann, and Bob hears only the barrier ---
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(InboxChanged, [7], null));
            JsonElement envelope = await annTab.NextAsync();
            Assert.Equal(InboxChanged, Envelope.Name(envelope));
            Assert.Equal(RealtimeTestHost.Active, Envelope.TenantId(envelope));
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(bobTab);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AzureSignalRLiveTests))]
        public async Task A_signed_out_session_is_told_then_closed_through_the_service_and_no_other_session_is()
        {
            await using TellmaTestHost host = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            TestSession ending = host.Session("ann", sessionId: "sid-a");
            TestSession staying = host.Session("ann", sessionId: "sid-b");
            await using HubClient first = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, ending.Cookie);
            await using HubClient second = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Other, ending.Cookie);
            await using HubClient other = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, staying.Cookie);

            await host.Services.GetRequiredService<CatalogSessionStore>()
                .EndAsync(new EndedSession(ending.Key, ending.Handle, "ann"), TestContext.Current.CancellationToken);

            // --- Each connection of the session hears session.ended in its own tenant before it closes ---
            foreach ((HubClient client, int tenantId) in new[] { (first, RealtimeTestHost.Active), (second, RealtimeTestHost.Other) })
            {
                await AssertToldThenClosedAsync(client, tenantId, "session-terminated");
            }

            // --- The other session is untouched ---
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(other);
            Assert.Equal(HubConnectionState.Connected, other.Connection.State);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AzureSignalRLiveTests))]
        public async Task A_revoked_access_closes_the_subject_s_connections_in_that_tenant_alone_through_the_service()
        {
            await using TellmaTestHost host = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            string ann = host.Cookie("ann");
            await using HubClient revoked = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, ann);
            await using HubClient elsewhere = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Other, ann);
            await using HubClient colleague = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("bob"));

            await host.Services.GetRequiredService<HubConnectionTracker>().TenantAccessRevokedAsync(RealtimeTestHost.Active, "ann");

            await AssertToldThenClosedAsync(revoked, RealtimeTestHost.Active, "access-revoked");
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Other, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(colleague);
            await AssertHearsTheBarrierAloneAsync(elsewhere);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AzureSignalRLiveTests))]
        public async Task A_suspended_tenant_s_connections_are_told_then_closed_through_the_service()
        {
            await using TellmaTestHost host = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            await using HubClient closing = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie("ann"));
            await using HubClient staying = await AzureSignalRTestHost.ConnectAsync(host, RealtimeTestHost.Other, host.Cookie("ann"));
            TenantDescriptor tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;

            await host.Services.GetRequiredService<HubConnectionTracker>().OnStateChangedAsync(tenant with { State = TenantState.Suspended }, TenantState.Active);

            await AssertToldThenClosedAsync(closing, RealtimeTestHost.Active, "tenant-unavailable");
            await RealtimeTestHost.PublishAsync(host, RealtimeTestHost.Other, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(staying);
        }

        [Fact(Skip = SkipReason, SkipUnless = nameof(HasCredentials), SkipType = typeof(AzureSignalRLiveTests))]
        public async Task Two_deployments_on_one_instance_never_hear_each_other()
        {
            // --- The same tenant, user and session ids in two deployments, each its own application on the service ---
            await using TellmaTestHost first = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            await using TellmaTestHost second = await AzureSignalRTestHost.StartAsync(ConnectionString!, AzureSignalRTestHost.UniqueEnvironment());
            await using HubClient firstTab = await AzureSignalRTestHost.ConnectAsync(first, RealtimeTestHost.Active, first.Cookie("ann"));
            await using HubClient secondTab = await AzureSignalRTestHost.ConnectAsync(second, RealtimeTestHost.Active, second.Cookie("ann"));

            // --- The first deployment's tenant-wide event reaches its own tab alone ---
            await RealtimeTestHost.PublishAsync(first, RealtimeTestHost.Active, new ClientEvent(InboxChanged, [], null));
            Assert.Equal(InboxChanged, Envelope.Name(await firstTab.NextAsync()));
            await RealtimeTestHost.PublishAsync(second, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(secondTab);

            // --- And a close in the first ends nothing in the second ---
            TenantDescriptor tenant = first.Services.GetRequiredService<ITenantRegistry>().Find(RealtimeTestHost.Active)!.Descriptor;
            await first.Services.GetRequiredService<HubConnectionTracker>().OnRemovedAsync(tenant);
            await AssertToldThenClosedAsync(firstTab, RealtimeTestHost.Active, "tenant-unavailable");
            await RealtimeTestHost.PublishAsync(second, RealtimeTestHost.Active, new ClientEvent(Barrier, [], new { tag = "barrier" }));
            await AssertHearsTheBarrierAloneAsync(secondTab);
            Assert.Equal(HubConnectionState.Connected, secondTab.Connection.State);
        }

        /// <summary>Asserts that a client's next event is <c>session.ended</c> for its tenant, and that it then closed.</summary>
        /// <param name="client">The client.</param>
        /// <param name="tenantId">The client's tenant.</param>
        /// <param name="reason">The close's reason.</param>
        /// <returns>A task that completes when the client closed.</returns>
        private static async Task AssertToldThenClosedAsync(HubClient client, int tenantId, string reason)
        {
            JsonElement envelope = await client.NextAsync();
            Assert.Equal("session.ended", Envelope.Name(envelope));
            Assert.Equal(tenantId, Envelope.TenantId(envelope));
            Assert.Equal(reason, Envelope.Payload(envelope, "reason"));
            _ = await client.ClosedAsync();
        }

        /// <summary>Asserts that a client hears the barrier and, for the settling time after it, nothing else.</summary>
        /// <param name="client">The client.</param>
        /// <returns>A task that completes when the settling time passed.</returns>
        private static async Task AssertHearsTheBarrierAloneAsync(HubClient client)
        {
            Assert.Equal(Barrier, Envelope.Name(await client.NextAsync()));
            await Task.Delay(Settle, TestContext.Current.CancellationToken);
            Assert.False(client.HasUnread());
        }
    }
}
