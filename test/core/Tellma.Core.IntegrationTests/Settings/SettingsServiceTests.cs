// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.IntegrationTests.Access;

namespace Tellma.Core.IntegrationTests.Settings
{
    /// <summary>
    ///     The settings service's actions through the action invoker against real databases: the
    ///     stamps of a save, a concurrent first write, the settings tag, a language change, the name
    ///     mirror, refresh-caches, the grants, the editor's view and its related rows, the cached views
    ///     after a save, and the round trips a save and a stale instance take.
    /// </summary>
    /// <param name="fixture">The class's databases, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class SettingsServiceTests(SettingsDatabase fixture) : IClassFixture<SettingsDatabase>, IAsyncLifetime
    {
        /// <summary>The resource of the <c>fx</c> category.</summary>
        private const string FxResource = CoreResources.SettingsPrefix + "fx";

        /// <summary>The resource of the <c>ops</c> category.</summary>
        private const string OpsResource = CoreResources.SettingsPrefix + "ops";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        // --- Stamps ---

        [Fact]
        public async Task A_stale_typed_stamp_is_a_conflict()
        {
            string admin = await AdminAsync();
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");
            await StaleTypedRowAsync();

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(
                () => InvokeAsync<SettingsSaveResult>(admin, "save", Typed(["Name"]) with { Name = "Acme", ExpectedModifiedAt = details.General!.ModifiedAt }));

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal((Abstractions.Settings.Settings.SingleRowId, false), (row.Id, row.IsMissing));
            Assert.NotNull(row.ModifiedAt);
            Assert.Equal("Tenant 1", await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT [Name] FROM [core].[Settings];"));
        }

        [Fact]
        public async Task A_stale_entry_stamp_is_a_conflict()
        {
            string admin = await AdminAsync();
            SettingsSaveResult first = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            DateTimeOffset stamp = first.Details.EntryStamps[SettingsSuiteKeys.Width.Name];
            await fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, "UPDATE [core].[SettingEntries] SET [ModifiedAt] = DATEADD(SECOND, 5, [ModifiedAt]);");

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(
                () => InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "9", stamp))));

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.False(row.IsMissing);
            Assert.NotEqual(0L, row.Id);
            Assert.Equal("8", await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        [Fact]
        public async Task A_typed_row_changed_between_the_read_and_the_persist_is_a_conflict_naming_its_modifier()
        {
            string admin = await AdminAsync();
            (int saraId, _) = await fixture.Connect.SeedCallerAsync("Sara");
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");

            // --- Another save of the row, uncommitted until this save's persist waits on the row's lock ---
            IReadOnlyList<Task> work = await fixture.Connect.HoldWhileAsync(
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [Name] = N'Raced', [ModifiedAt] = DATEADD(SECOND, 5, [ModifiedAt]), [ModifiedById] = {saraId};"),
                waiters: 1,
                resourceType: "KEY",
                () => [InvokeAsync<SettingsSaveResult>(admin, "save", Typed(["Name"]) with { Name = "Acme", ExpectedModifiedAt = details.General!.ModifiedAt })]);

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => work[0]);

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal((Abstractions.Settings.Settings.SingleRowId, false, saraId, "Sara"), (row.Id, row.IsMissing, row.ModifiedById, row.ModifiedByName));
            Assert.NotNull(row.ModifiedAt);
            Assert.Equal("Raced", await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT [Name] FROM [core].[Settings];"));
        }

        [Fact]
        public async Task An_entry_changed_between_the_read_and_the_persist_is_a_conflict_naming_its_modifier()
        {
            string admin = await AdminAsync();
            (int saraId, _) = await fixture.Connect.SeedCallerAsync("Sara");
            SettingsSaveResult first = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            long id = await EntryIdAsync(SettingsSuiteKeys.Width.Name);

            // --- Another save of the entry, uncommitted until this save's persist waits on the row's lock ---
            IReadOnlyList<Task> work = await fixture.Connect.HoldWhileAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"UPDATE [core].[SettingEntries] SET [Value] = N'7', [ModifiedAt] = DATEADD(SECOND, 5, [ModifiedAt]), [ModifiedById] = {saraId} WHERE [Id] = {id};"),
                waiters: 1,
                resourceType: "KEY",
                () => [InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "9", first.Details.EntryStamps[SettingsSuiteKeys.Width.Name])))]);

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => work[0]);

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal((id, false, saraId, "Sara"), (row.Id, row.IsMissing, row.ModifiedById, row.ModifiedByName));
            Assert.NotNull(row.ModifiedAt);
            Assert.Equal("7", await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        [Theory]
        [InlineData("9")]
        [InlineData(null)]
        public async Task An_entry_deleted_between_the_read_and_the_persist_is_a_missing_conflict_never_a_not_found(string? value)
        {
            string admin = await AdminAsync();
            SettingsSaveResult first = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            long id = await EntryIdAsync(SettingsSuiteKeys.Width.Name);

            // --- Another save's reset of the entry, uncommitted until this save's persist waits on the row's lock ---
            IReadOnlyList<Task> work = await fixture.Connect.HoldWhileAsync(
                string.Create(CultureInfo.InvariantCulture, $"DELETE FROM [core].[SettingEntries] WHERE [Id] = {id};"),
                waiters: 1,
                resourceType: "KEY",
                () => [InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, value, first.Details.EntryStamps[SettingsSuiteKeys.Width.Name])))]);

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => work[0]);

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal((id, true, null, null, null), (row.Id, row.IsMissing, row.ModifiedAt, row.ModifiedById, row.ModifiedByName));
            Assert.Null(await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        [Fact]
        public async Task An_entries_only_save_neither_reads_nor_checks_the_typed_row()
        {
            string admin = await AdminAsync();
            await StaleTypedRowAsync();

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            Assert.Equal("8", await StoredAsync(SettingsSuiteKeys.Width.Name));
            Assert.Equal(8, saved.Details.Values[SettingsSuiteKeys.Width.Name].GetInt32());
        }

        [Fact]
        public async Task A_concurrent_first_write_of_one_key_is_a_conflict_not_a_unique_violation()
        {
            string admin = await AdminAsync();

            // --- Another request's first write of the key, uncommitted until the save waits on its key lock ---
            IReadOnlyList<Task> work = await fixture.Connect.HoldWhileAsync(
                $"""
                INSERT INTO [core].[SettingEntries] ([Id], [Key], [Value], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (NEXT VALUE FOR [core].[sq_SettingEntries], N'{SettingsSuiteKeys.Width.Name}', N'1', SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId});
                """,
                waiters: 1,
                resourceType: "KEY",
                () => [InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)))]);

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => work[0]);

            ConcurrencyConflict row = Assert.Single(conflict.Conflicts);
            Assert.Equal((0L, false), (row.Id, row.IsMissing));
            Assert.Equal("1", await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        [Fact]
        public async Task Override_skips_the_stamps()
        {
            string admin = await AdminAsync();
            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            await StaleTypedRowAsync();
            await fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, "UPDATE [core].[SettingEntries] SET [ModifiedAt] = DATEADD(SECOND, 5, [ModifiedAt]);");

            TenantSettingsPatch patch = Typed(["Name", Entry(SettingsSuiteKeys.Width.Name)]) with
            {
                Name = "Acme",
                Entries = new Dictionary<string, JsonElement?> { [SettingsSuiteKeys.Width.Name] = Json("9") },
                Concurrency = ConcurrencyMode.Override,
            };
            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(admin, "save", patch);

            Assert.Equal("Acme", saved.Details.General!.Name);
            Assert.Equal("9", await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        // --- The settings tag and a language change ---

        [Fact]
        public async Task The_settings_tag_of_a_save_is_the_one_the_header_carries()
        {
            string admin = await AdminAsync();

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Welcome.Name, "\"hello\"", null)));

            Guid stored = await TagAsync(VersionTagNames.Settings);
            Assert.Equal(VersionTagWire.Settings(new VersionTag(stored)), saved.SettingsTag);
            VersionTagSnapshot snapshot = fixture.Instance.GetRequiredService<IVersionTagSnapshots>().Current(ConnectDatabase.TenantA);
            Assert.Contains(saved.SettingsTag, VersionTagWire.HeaderValue(snapshot, null), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_language_change_bumps_permissions_and_every_entity_tag_and_switches_the_shape()
        {
            string admin = await AdminAsync();
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");
            Dictionary<string, Guid> before = await TagsAsync();

            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Typed(["SecondaryLanguage"]) with { SecondaryLanguage = "ar", ExpectedModifiedAt = details.General!.ModifiedAt });

            Dictionary<string, Guid> after = await TagsAsync();
            Assert.NotEqual(before[VersionTagNames.Permissions], after[VersionTagNames.Permissions]);
            Assert.NotEqual(before[VersionTagNames.Entities], after[VersionTagNames.Entities]);
            string[] entityTags = [.. before.Keys.Where(static name => name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal))];
            Assert.NotEmpty(entityTags);
            Assert.All(entityTags, name => Assert.NotEqual(before[name], after[name]));

            // --- A fresh request sees two content languages ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(admin);
            CacheResult<TenantSettings> settings = await scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetAsync();
            Assert.Equal(MultilingualShape.PrimaryAndSecondary, settings.Value!.Shape);
        }

        [Fact]
        public async Task A_patch_that_keeps_the_languages_leaves_the_permissions_tag()
        {
            string admin = await AdminAsync();
            Guid before = await TagAsync(VersionTagNames.Permissions);

            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            Assert.Equal(before, await TagAsync(VersionTagNames.Permissions));
        }

        // --- The name mirror ---

        [Fact]
        public async Task A_name_change_is_mirrored_after_commit()
        {
            string admin = await AdminAsync();
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");

            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Typed(["Name"]) with { Name = "Acme", ExpectedModifiedAt = details.General!.ModifiedAt });

            (int tenantId, TenantDisplayNames names) = Assert.Single(fixture.Catalog.Renames);
            Assert.Equal(ConnectDatabase.TenantA, tenantId);
            Assert.Equal(new TenantDisplayNames("Acme", null, null, ["en"]), names);
        }

        [Fact]
        public async Task A_save_that_keeps_the_names_mirrors_nothing()
        {
            string admin = await AdminAsync();

            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            Assert.Empty(fixture.Catalog.Renames);
        }

        [Fact]
        public async Task A_failing_mirror_is_logged_and_the_save_stands()
        {
            string admin = await AdminAsync();
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");
            fixture.Catalog.Fails = true;

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(admin, "save", Typed(["Name"]) with { Name = "Acme", ExpectedModifiedAt = details.General!.ModifiedAt });

            Assert.Equal("Acme", saved.Details.General!.Name);
            Assert.Equal("Acme", await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT [Name] FROM [core].[Settings];"));
            Assert.Contains(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "SettingsNamesMirrorFailed");
        }

        // --- refresh-caches ---

        [Fact]
        public async Task Refresh_caches_moves_every_tag_and_both_user_columns_to_one_value()
        {
            string admin = await AdminAsync();
            fixture.Events.Clear();

            _ = await InvokeAsync<object?>(admin, "refresh-caches");

            Assert.Equal(1, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(DISTINCT [Tag]) FROM [core].[VersionTags];"));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(
                ConnectDatabase.TenantA,
                """
                SELECT COUNT(*) FROM [core].[UserStamps] AS s CROSS JOIN (SELECT TOP (1) [Tag] FROM [core].[VersionTags]) AS t
                WHERE s.[PermissionsTag] IS NULL OR s.[PreferencesTag] IS NULL OR s.[PermissionsTag] <> t.[Tag] OR s.[PreferencesTag] <> t.[Tag];
                """));
            Assert.Contains(
                fixture.Events.Published,
                static published => published.Name == VersionTagBumpContributor.CacheChangedEvent
                    && published.UserIds.Count == 0
                    && Equals(published.Payload, new CacheChangedPayload(VersionTagWire.PreferencesItem, null)));
        }

        // --- Grants ---

        [Fact]
        public async Task Details_and_save_by_a_member_without_grants_are_forbidden()
        {
            (_, string member) = await fixture.SeedCallerAsync("Member");

            _ = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync<TenantSettingsDetails>(member, "details"));
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync<SettingsSaveResult>(member, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null))));
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync<SettingsSaveResult>(member, "save", Typed(["Name"]) with { Name = "Acme", Concurrency = ConcurrencyMode.Override }));

            // --- A mask that names nothing, or nothing declared, is refused before any batch ---
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync<SettingsSaveResult>(member, "save", Typed([])));
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync<SettingsSaveResult>(member, "save", Typed(["Bogus", Entry("fx.nowhere")])));
            Assert.Null(await StoredAsync(SettingsSuiteKeys.Width.Name));
        }

        [Fact]
        public async Task A_save_that_names_nothing_needs_one_settings_save_grant()
        {
            (_, string clerk) = await fixture.SeedCallerAsync("Clerk", (OpsResource, AccessActions.Save));

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(clerk, "save", Typed([]));

            Assert.Null(saved.Details.General);
            Assert.Equal([SettingsSuiteKeys.Shelf.Name], saved.Details.Values.Keys);
        }

        [Fact]
        public async Task A_save_needs_save_on_every_category_it_lists()
        {
            (_, string clerk) = await fixture.SeedCallerAsync("Clerk", (FxResource, AccessActions.Save));

            _ = await Assert.ThrowsAsync<ForbiddenException>(
                () => InvokeAsync<SettingsSaveResult>(clerk, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null), (SettingsSuiteKeys.Shelf.Name, "4", null))));

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(clerk, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            Assert.Null(saved.Details.General);
        }

        // --- The editor's view ---

        [Fact]
        public async Task Details_holds_only_the_held_categories_with_their_related_rows_and_a_deleted_target_bare()
        {
            string admin = await AdminAsync();
            int kept = await fixture.Connect.SeedRoleAsync();
            int deleted = await fixture.Connect.SeedRoleAsync();
            _ = await InvokeAsync<SettingsSaveResult>(
                admin,
                "save",
                Entries(
                    (SettingsSuiteKeys.Owner.Name, Number(kept), null),
                    (SettingsSuiteKeys.Reviewers.Name, $"[{Number(kept)},{Number(deleted)}]", null),
                    (SettingsSuiteKeys.Shelf.Name, "4", null),
                    (SettingsSuiteKeys.ApiKey.Name, "\"s3cret\"", null)));
            await fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"DELETE FROM [core].[Roles] WHERE [Id] = {deleted};"));
            (_, string reader) = await fixture.SeedCallerAsync("Reader", (FxResource, AccessActions.Read));

            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(reader, "details");

            // --- Only the fx values, never the secret's, and no typed row ---
            Assert.Null(details.General);
            Assert.Equal(
                [SettingsSuiteKeys.Owner.Name, SettingsSuiteKeys.AutoNumber.Name, SettingsSuiteKeys.Width.Name, SettingsSuiteKeys.Reviewers.Name],
                details.Values.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(kept, details.Values[SettingsSuiteKeys.Owner.Name].GetInt32());
            Assert.Equal([kept, deleted], details.Values[SettingsSuiteKeys.Reviewers.Name].EnumerateArray().Select(static id => id.GetInt32()));
            Assert.Equal([SettingsSuiteKeys.ApiKey.Name, SettingsSuiteKeys.Owner.Name, SettingsSuiteKeys.Reviewers.Name], details.SetKeys.Order(StringComparer.Ordinal));

            // --- The related roles: the kept one only ---
            RelatedEntitySet roles = Assert.Single(details.Related.Sets).Value;
            Assert.Equal(typeof(Role), roles.EntityType);
            Assert.Equal([kept], roles.Entities.Cast<Role>().Select(static role => role.Id));
        }

        [Fact]
        public async Task A_saves_details_carry_the_stamps_of_unlisted_rows()
        {
            string admin = await AdminAsync();
            SettingsSaveResult first = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            SettingsSaveResult second = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Shelf.Name, "4", null)));

            Assert.Equal(first.Details.EntryStamps[SettingsSuiteKeys.Width.Name], second.Details.EntryStamps[SettingsSuiteKeys.Width.Name]);
            Assert.Equal([SettingsSuiteKeys.Width.Name, SettingsSuiteKeys.Shelf.Name], second.Details.SetKeys.Order(StringComparer.Ordinal));
            Assert.NotNull(second.Details.General);
        }

        [Fact]
        public async Task A_reset_removes_the_entry_and_serves_the_default()
        {
            string admin = await AdminAsync();
            SettingsSaveResult first = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            SettingsSaveResult reset = await InvokeAsync<SettingsSaveResult>(
                admin,
                "save",
                Entries((SettingsSuiteKeys.Width.Name, null, first.Details.EntryStamps[SettingsSuiteKeys.Width.Name])));

            Assert.Null(await StoredAsync(SettingsSuiteKeys.Width.Name));
            Assert.Equal(SettingsSuiteKeys.Width.DefaultValue, reset.Details.Values[SettingsSuiteKeys.Width.Name].GetInt32());
            Assert.DoesNotContain(SettingsSuiteKeys.Width.Name, reset.Details.SetKeys);
        }

        // --- The cached views ---

        [Fact]
        public async Task Client_and_entity_tags_answer_the_caches_views_in_one_round_trip_each()
        {
            (_, string member) = await fixture.SeedCallerAsync("Member");
            _ = await InvokeAsync<(byte[], string)>(member, "client");
            _ = await InvokeAsync<IReadOnlyDictionary<string, string>>(member, "entity-tags");

            ((byte[] Json, string WireTag) client, long clientTrips) = await CountedAsync<(byte[], string)>(member, "client");
            (IReadOnlyDictionary<string, string> tags, long tagTrips) = await CountedAsync<IReadOnlyDictionary<string, string>>(member, "entity-tags");

            // --- The prologue's round trip each, and the caches' own answers ---
            Assert.Equal((1L, 1L), (clientTrips, tagTrips));
            await using AsyncServiceScope scope = await fixture.ScopeAsync(member);
            (byte[] json, string wireTag) = await scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>().GetForClientAsync();
            Assert.Equal(wireTag, client.WireTag);
            Assert.Equal(json, client.Json);
            IReadOnlyDictionary<string, string> expected = await scope.ServiceProvider.GetRequiredService<ICacheableEntities>().GetWireTagsAsync();
            Assert.NotEmpty(tags);
            Assert.Equal(expected.OrderBy(static tag => tag.Key, StringComparer.Ordinal), tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal));
        }

        [Fact]
        public async Task The_cached_views_after_a_save_equal_a_cold_load()
        {
            string admin = await AdminAsync();
            TenantSettingsDetails details = await InvokeAsync<TenantSettingsDetails>(admin, "details");
            TenantSettingsPatch patch = Typed(["Region", "Digits", Entry(SettingsSuiteKeys.Welcome.Name), Entry(SettingsSuiteKeys.Width.Name)]) with
            {
                Region = "SA",
                Digits = DigitStyle.Native,
                Entries = new Dictionary<string, JsonElement?> { [SettingsSuiteKeys.Welcome.Name] = Json("\"hello\""), [SettingsSuiteKeys.Width.Name] = Json("8") },
                ExpectedModifiedAt = details.General!.ModifiedAt,
                ExpectedEntryStamps = new Dictionary<string, DateTimeOffset?> { [SettingsSuiteKeys.Welcome.Name] = null, [SettingsSuiteKeys.Width.Name] = null },
            };

            SettingsSaveResult saved = await InvokeAsync<SettingsSaveResult>(admin, "save", patch);

            // --- The saving instance's views, offered by the save ---
            (TenantSettings warm, (byte[] Json, string WireTag) warmClient) = await ViewsAsync(admin, fixture.Instance);

            // --- A cold instance's, loaded from the database ---
            await using ServiceProvider other = fixture.CreateInstance();
            (TenantSettings cold, (byte[] Json, string WireTag) coldClient) = await ViewsAsync(admin, other);

            Assert.Equal(("SA", DigitStyle.Native), (warm.Region, warm.Digits));
            Assert.Equal((cold.Tag, cold.ModifiedAt, cold.Region, cold.Digits, cold.TimeZone.Id), (warm.Tag, warm.ModifiedAt, warm.Region, warm.Digits, warm.TimeZone.Id));
            Assert.Equal(cold.Names, warm.Names);
            Assert.Equal(cold.Languages.Select(static language => language.Code), warm.Languages.Select(static language => language.Code));
            Assert.Equal(cold.Calendars.Select(static calendar => calendar.Code), warm.Calendars.Select(static calendar => calendar.Code));
            Assert.Equal(
                cold.Entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal).Select(static entry => (entry.Key, entry.Value.GetRawText())),
                warm.Entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal).Select(static entry => (entry.Key, entry.Value.GetRawText())));
            Assert.Equal(8, warm.Get(SettingsSuiteKeys.Width));
            Assert.Equal(coldClient.WireTag, warmClient.WireTag);
            Assert.Equal(coldClient.Json, warmClient.Json);
            Assert.Equal(saved.Client.Region, cold.Region);
            Assert.Equal(saved.Client.Digits, cold.Digits);
        }

        // --- Round trips ---

        [Fact]
        public async Task A_save_takes_two_round_trips()
        {
            string admin = await AdminAsync();
            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));

            (SettingsSaveResult saved, long trips) = await CountedAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Shelf.Name, "4", null)));

            Assert.Equal(2L, trips);
            Assert.Equal(4, saved.Details.Values[SettingsSuiteKeys.Shelf.Name].GetInt32());
        }

        [Fact]
        public async Task A_bump_from_another_instance_fails_a_validate_batch_once_while_a_read_batch_is_served()
        {
            string admin = await AdminAsync();
            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Width.Name, "8", null)));
            await using ServiceProvider other = fixture.CreateInstance();

            // --- A save after the other instance's: the guard fails once and the retry reads fresh rows ---
            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Welcome.Name, "\"from b\"", null)), other);
            (SettingsSaveResult saved, long saveTrips) = await CountedAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Shelf.Name, "4", null)));

            Assert.Equal(3L, saveTrips);
            Assert.Equal("from b", saved.Details.Values[SettingsSuiteKeys.Welcome.Name].GetString());

            // --- A read after the other instance's next save: served in its one round trip, with fresh rows ---
            _ = await InvokeAsync<SettingsSaveResult>(admin, "save", Entries((SettingsSuiteKeys.Shelf.Name, "5", saved.Details.EntryStamps[SettingsSuiteKeys.Shelf.Name])), other);
            (TenantSettingsDetails details, long readTrips) = await CountedAsync<TenantSettingsDetails>(admin, "details");

            Assert.Equal(1L, readTrips);
            Assert.Equal(5, details.Values[SettingsSuiteKeys.Shelf.Name].GetInt32());
        }

        // --- Helpers ---

        /// <summary>A patch of typed members, checked against no stamp until the test sets one.</summary>
        /// <param name="fields">The listed fields.</param>
        /// <returns>The patch.</returns>
        private static TenantSettingsPatch Typed(IReadOnlyList<string> fields)
        {
            return new TenantSettingsPatch(fields);
        }

        /// <summary>A patch of entries, each with its value (null resets it) and the stamp the caller loaded (null for a key it found unset).</summary>
        /// <param name="entries">The entries.</param>
        /// <returns>The patch.</returns>
        private static TenantSettingsPatch Entries(params (string Key, string? Json, DateTimeOffset? Stamp)[] entries)
        {
            return new TenantSettingsPatch(
                [.. entries.Select(static entry => Entry(entry.Key))],
                Entries: entries.ToDictionary(static entry => entry.Key, static entry => entry.Json is null ? (JsonElement?)null : Json(entry.Json)),
                ExpectedEntryStamps: entries.ToDictionary(static entry => entry.Key, static entry => entry.Stamp));
        }

        /// <summary>The field that lists an entry.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The field.</returns>
        private static string Entry(string key)
        {
            return TenantSettingsPatch.EntryFieldPrefix + key;
        }

        /// <summary>A JSON value.</summary>
        /// <param name="text">The JSON text.</param>
        /// <returns>The value.</returns>
        private static JsonElement Json(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        /// <summary>An id as JSON text.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The text.</returns>
        private static string Number(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Seeds a caller who may save the typed row and both categories.</summary>
        /// <returns>The caller's subject.</returns>
        private async Task<string> AdminAsync()
        {
            (_, string subject) = await fixture.SeedCallerAsync(
                "Admin",
                (CoreResources.SettingsGeneral, AccessActions.Save),
                (FxResource, AccessActions.Save),
                (OpsResource, AccessActions.Save));
            return subject;
        }

        /// <summary>Moves the typed row's stamp out of band, as another request's save would.</summary>
        /// <returns>A task that completes when the stamp moved.</returns>
        private Task StaleTypedRowAsync()
        {
            return fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, "UPDATE [core].[Settings] SET [ModifiedAt] = DATEADD(SECOND, 5, [ModifiedAt]);");
        }

        /// <summary>The stored JSON of a key; null when it has no row.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The stored JSON.</returns>
        private Task<string?> StoredAsync(string key)
        {
            return fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, $"SELECT [Value] FROM [core].[SettingEntries] WHERE [Key] = N'{key}';");
        }

        /// <summary>The id of a key's row.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The id.</returns>
        private Task<long> EntryIdAsync(string key)
        {
            return fixture.Connect.ScalarAsync<long>(ConnectDatabase.TenantA, $"SELECT CAST([Id] AS bigint) FROM [core].[SettingEntries] WHERE [Key] = N'{key}';");
        }

        /// <summary>A stored version tag.</summary>
        /// <param name="name">The tag's name.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> TagAsync(string name)
        {
            return (await TagsAsync())[name];
        }

        /// <summary>Every stored version tag of the first tenant.</summary>
        /// <returns>The tags, by name.</returns>
        private async Task<Dictionary<string, Guid>> TagsAsync()
        {
            string json = await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT [Name], [Tag] FROM [core].[VersionTags] FOR JSON PATH;")
                ?? throw new InvalidOperationException("The tenant has no version tags.");
            return JsonSerializer.Deserialize<TagRow[]>(json)!.ToDictionary(static row => row.Name, static row => row.Tag, StringComparer.Ordinal);
        }

        /// <summary>The settings and the client view an instance serves a caller.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="services">The instance.</param>
        /// <returns>The views.</returns>
        private async Task<(TenantSettings Settings, (byte[] Json, string WireTag) Client)> ViewsAsync(string subject, IServiceProvider services)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services);
            ITenantSettingsCache cache = scope.ServiceProvider.GetRequiredService<ITenantSettingsCache>();
            CacheResult<TenantSettings> settings = await cache.GetAsync();
            return (settings.Value!, await cache.GetForClientAsync());
        }

        /// <summary>Invokes an action of the settings service in a scope of the caller.</summary>
        /// <typeparam name="T">What the action answers.</typeparam>
        /// <param name="subject">The caller.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <param name="services">The instance; the serving one when null.</param>
        /// <returns>The answer.</returns>
        private async Task<T> InvokeAsync<T>(string subject, string action, object? body = null, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services);
            return (T)(await InvokeAsync(scope, action, body))!;
        }

        /// <summary>Invokes an action in a scope of the caller and counts the round trips the action took, from the scope's start.</summary>
        /// <typeparam name="T">What the action answers.</typeparam>
        /// <param name="subject">The caller.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <returns>The answer and the round trips.</returns>
        private async Task<(T Result, long Trips)> CountedAsync<T>(string subject, string action, object? body = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            fixture.RoundTrips.Reset();
            var result = (T)(await InvokeAsync(scope, action, body))!;
            return (result, fixture.RoundTrips.Count);
        }

        /// <summary>Invokes an action of the settings service through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <returns>The answer.</returns>
        private static Task<object?> InvokeAsync(AsyncServiceScope scope, string action, object? body)
        {
            ApiActionDescriptor descriptor = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(static service => service.Route == "settings").Actions
                .Single(candidate => candidate.Name == action);
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(descriptor, body, Token);
        }

        /// <summary>A row of the version tags.</summary>
        /// <param name="Name">The tag's name.</param>
        /// <param name="Tag">The tag.</param>
        private sealed record TagRow(string Name, Guid Tag);
    }
}
