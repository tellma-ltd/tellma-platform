// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Access;

namespace Tellma.Core.IntegrationTests.Client
{
    /// <summary>
    ///     The client's one revalidation call against a real tenant database: a cold call answers every
    ///     member, a warm one only what its held tags miss, in one round trip, with the composite
    ///     permissions tag, the envelope composed as the caller's own read composes it, the moved lists,
    ///     unknown names ignored and the hub's availability from the host.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class ClientSyncTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The fixture's one cacheable list.</summary>
        private const string Lookup = "fixture.Lookup";

        /// <summary>The held name of the fixture's cacheable list.</summary>
        private static readonly string LookupItem = VersionTagNames.ForEntity(Lookup);

        /// <summary>The four tags every answer carries, in the version-tags header's order.</summary>
        private static readonly string[] ClientItems =
            [VersionTagWire.SettingsItem, VersionTagWire.PermissionsItem, VersionTagWire.PreferencesItem, VersionTagWire.EntitiesItem];

        /// <summary>The held tags of a cold call: none.</summary>
        private static readonly IReadOnlyDictionary<string, string> Cold = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- Cold and warm ---

        [Fact]
        public async Task A_cold_call_answers_the_settings_the_envelope_and_every_lists_tag()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");

            (ClientSyncResult result, int trips) = await SyncAsync(admin, Cold);

            // --- Every member, the settings written from the cache's bytes ---
            TenantSettingsForClient settings = result.Settings!.Deserialize();
            Assert.Equal(ConnectDatabase.TenantA, settings.TenantId);
            Assert.Equal(ann, result.Me!.User.Id);
            Assert.Equal(ClientItems, result.Tags.Keys.Take(4));
            Assert.Equal(VersionTagWire.Entities(new VersionTag(await fixture.TenantTagAsync(LookupItem))), result.Tags[LookupItem]);
            Assert.Empty(result.StaleEntities);

            // --- The tenant's facts; a cold call costs at most the prologue's cold load besides its own round trip ---
            Assert.Equal(new TenantFacts(ConnectDatabase.TenantA, TenantCategory.Live, TenantState.Active, null, Realtime: false), result.Tenant);
            Assert.InRange(trips, 1, 2);
        }

        [Fact]
        public async Task A_warm_call_holding_every_current_tag_answers_only_the_facts_and_the_four_tags_in_one_round_trip()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);

            (ClientSyncResult warm, int trips) = await SyncAsync(admin, cold.Tags);

            Assert.Equal(1, trips);
            Assert.Null(warm.Settings);
            Assert.Null(warm.Me);
            Assert.Empty(warm.StaleEntities);
            Assert.Equal(ClientItems, warm.Tags.Keys);
            Assert.All(ClientItems, item => Assert.Equal(cold.Tags[item], warm.Tags[item]));
        }

        // --- Each held tag ---

        [Theory]
        [InlineData(VersionTagWire.SettingsItem, true, false, false)]
        [InlineData(VersionTagWire.PreferencesItem, false, true, false)]
        [InlineData(VersionTagWire.PermissionsItem, false, true, false)]
        [InlineData(VersionTagWire.EntitiesItem, false, false, true)]
        public async Task A_held_tag_that_differs_answers_only_the_member_it_covers(string item, bool settings, bool me, bool lists)
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);

            (ClientSyncResult result, int trips) = await SyncAsync(admin, Held(cold.Tags, (item, "1.stale")));

            Assert.Equal(1, trips);
            Assert.Equal((settings, me, lists), (result.Settings is not null, result.Me is not null, result.Tags.ContainsKey(LookupItem)));
            Assert.Equal(cold.Tags[item], result.Tags[item]);
            Assert.Empty(result.StaleEntities);
        }

        [Fact]
        public async Task A_held_tag_that_is_absent_answers_the_member_it_covers()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);
            Dictionary<string, string> held = new(cold.Tags, StringComparer.Ordinal);
            _ = held.Remove(VersionTagWire.SettingsItem);
            _ = held.Remove(VersionTagWire.EntitiesItem);

            (ClientSyncResult result, _) = await SyncAsync(admin, held);

            Assert.NotNull(result.Settings);
            Assert.Null(result.Me);
            Assert.True(result.Tags.ContainsKey(LookupItem));
        }

        [Fact]
        public async Task A_held_list_whose_tag_moved_is_stale_and_its_new_tag_comes_with_the_moved_composite()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);

            // --- A list edit moves the list's tag and the composite ---
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] IN (N'{LookupItem}', N'{VersionTagNames.Entities}');");
            (ClientSyncResult result, int trips) = await SyncAsync(admin, cold.Tags);

            Assert.Equal(1, trips);
            Assert.Equal([Lookup], result.StaleEntities);
            Assert.NotEqual(cold.Tags[LookupItem], result.Tags[LookupItem]);
            Assert.Equal(VersionTagWire.Entities(new VersionTag(await fixture.TenantTagAsync(LookupItem))), result.Tags[LookupItem]);
            Assert.NotEqual(cold.Tags[VersionTagWire.EntitiesItem], result.Tags[VersionTagWire.EntitiesItem]);

            // --- Holding the new composite but the old list tag: stale, with no list tag sent again ---
            (ClientSyncResult again, _) = await SyncAsync(admin, Held(result.Tags, (LookupItem, cold.Tags[LookupItem])));
            Assert.Equal([Lookup], again.StaleEntities);
            Assert.False(again.Tags.ContainsKey(LookupItem));
        }

        // --- The tags ---

        [Fact]
        public async Task The_permissions_tag_is_the_tenants_tag_then_the_callers_own_and_a_move_of_either_answers_the_envelope()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);

            // --- A membership edit moves the tenant's permissions tag and the caller's own ---
            await fixture.BumpPermissionsAsync(ann);
            (ClientSyncResult result, _) = await SyncAsync(admin, cold.Tags);

            Guid tenant = await fixture.TenantTagAsync(VersionTagNames.Permissions);
            Guid caller = (await fixture.UserTagsAsync(ann)).Permissions!.Value;
            string composite = string.Create(CultureInfo.InvariantCulture, $"{MeResult.FormatVersion}.{tenant:N}{caller:N}");
            Assert.Equal(composite, result.Tags[VersionTagWire.PermissionsItem]);
            Assert.Equal(composite, result.Me!.Access.Tag);
            Assert.Null(result.Settings);
        }

        [Fact]
        public async Task The_envelope_is_the_one_the_callers_own_read_answers()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) VALUES ({ann}, N'nav.pinned', N'[1]'), ({ann}, N'theme', N'dark');"));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[UserStamps] SET [PreferencesTag] = NEWID() WHERE [UserId] = {ann};"));

            (ClientSyncResult result, _) = await SyncAsync(admin, Cold);
            MeResult own = await MeAsync(admin);

            MeResult me = result.Me!;
            Assert.Equal(own.User, me.User);
            Assert.Equal(own.Preferences, me.Preferences);
            Assert.Equal(own.PreferencesTag, me.PreferencesTag);
            Assert.Equal(own.Tags, me.Tags);
            Assert.Equal((own.Access.Tag, own.Access.IsSystem, own.Access.ValidUntil), (me.Access.Tag, me.Access.IsSystem, me.Access.ValidUntil));
            Assert.Equal(own.Access.Securables, me.Access.Securables);
            Assert.Equal(new Dictionary<string, string> { ["nav.pinned"] = "[1]", ["theme"] = "dark" }, me.Preferences);
        }

        [Fact]
        public async Task Unknown_held_names_are_ignored()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);

            (ClientSyncResult result, int trips) = await SyncAsync(admin, Held(cold.Tags, ("acme.unknown", "1.x"), ("entity:acme.Missing", "1.y")));

            Assert.Equal(1, trips);
            Assert.Null(result.Settings);
            Assert.Null(result.Me);
            Assert.Empty(result.StaleEntities);
            Assert.Equal(ClientItems, result.Tags.Keys);
        }

        // --- The tenant's facts ---

        [Fact]
        public async Task Realtime_is_the_hosts_hub_status_and_the_state_is_the_tenants()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            await using ServiceProvider hub = fixture.CreateInstance(configure: static services => services.Replace(ServiceDescriptor.Singleton<IRealtimeStatus>(new HubStatus())));

            (ClientSyncResult served, _) = await SyncAsync(admin, Cold, services: hub, state: TenantState.ReadOnly);
            (ClientSyncResult plain, _) = await SyncAsync(admin, Cold);

            Assert.Equal((true, TenantState.ReadOnly), (served.Tenant.Realtime, served.Tenant.State));
            Assert.Equal((false, TenantState.Active), (plain.Tenant.Realtime, plain.Tenant.State));
        }

        // --- The wire ---

        [Fact]
        public async Task The_answer_writes_the_settings_bytes_as_they_are_and_omits_the_members_it_does_not_carry()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (ClientSyncResult cold, _) = await SyncAsync(admin, Cold);
            (ClientSyncResult warm, _) = await SyncAsync(admin, cold.Tags);

            using var coldWire = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(cold, TellmaJson.Options));
            using var warmWire = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(warm, TellmaJson.Options));

            Assert.Equal(JsonSerializer.Serialize(JsonDocument.Parse(cold.Settings!.Utf8).RootElement), JsonSerializer.Serialize(coldWire.RootElement.GetProperty("settings")));
            Assert.Equal(JsonSerializer.Serialize(cold.Me, TellmaJson.Options), coldWire.RootElement.GetProperty("me").GetRawText());
            Assert.Equal(["tenant", "tags", "staleEntities"], warmWire.RootElement.EnumerateObject().Select(static member => member.Name));
            Assert.False(warmWire.RootElement.GetProperty("tenant").TryGetProperty("liveTenantId", out _));
        }

        // --- Helpers ---

        /// <summary>The held tags with some replaced or added.</summary>
        /// <param name="tags">The tags a call answered.</param>
        /// <param name="changes">The names and the tags to hold instead.</param>
        /// <returns>The held tags.</returns>
        private static Dictionary<string, string> Held(IReadOnlyDictionary<string, string> tags, params (string Name, string Tag)[] changes)
        {
            Dictionary<string, string> held = new(tags, StringComparer.Ordinal);
            foreach ((string name, string tag) in changes)
            {
                held[name] = tag;
            }

            return held;
        }

        /// <summary>Calls the sync in a scope of its own and counts its round trips.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="held">The held tags.</param>
        /// <param name="services">The instance to call; the fixture's own when null.</param>
        /// <param name="state">The state the request sees the tenant in.</param>
        /// <returns>The answer and its round trips.</returns>
        private async Task<(ClientSyncResult Result, int RoundTrips)> SyncAsync(
            string subject, IReadOnlyDictionary<string, string> held, IServiceProvider? services = null, TenantState state = TenantState.Active)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, state: state, services: services);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            var result = (ClientSyncResult)(await InvokeAsync(scope, "client", "sync", new ClientSyncRequest(held)))!;
            return (result, access.RoundTrips - start);
        }

        /// <summary>Reads the caller's own envelope in a scope of its own.</summary>
        /// <param name="subject">The caller.</param>
        /// <returns>The envelope.</returns>
        private async Task<MeResult> MeAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            StackDescriptor users = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Find(CoreResources.User)!;
            ApiActionDescriptor descriptor = users.Actions.OfType<ApiActionDescriptor>().Single(static candidate => candidate.Name == "me");
            return (MeResult)(await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(descriptor, null, Token))!;
        }

        /// <summary>Invokes a routed service's action through the action invoker, as its route does.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="route">The service's route.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body.</param>
        /// <returns>The answer.</returns>
        private static Task<object?> InvokeAsync(AsyncServiceScope scope, string route, string action, object? body)
        {
            ApiActionDescriptor descriptor = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(service => service.Route == route).Actions
                .Single(candidate => candidate.Name == action);
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(descriptor, body, Token);
        }

        /// <summary>A host that serves the real-time hub.</summary>
        private sealed class HubStatus : IRealtimeStatus
        {
            /// <inheritdoc />
            public bool Enabled => true;
        }
    }
}
