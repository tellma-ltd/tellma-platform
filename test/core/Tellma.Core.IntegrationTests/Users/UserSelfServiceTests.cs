// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Testing.Blobs;
using Tellma.Testing.Support.Logging;

namespace Tellma.Core.IntegrationTests.Users
{
    /// <summary>
    ///     The caller's self-service against a real tenant database: the envelope, the profile edit
    ///     a member makes without a role grant, the locale change and the preference bag, then another
    ///     user's bag under the users' preferences grant and the users' activity extra, each within
    ///     its round-trip budget.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class UserSelfServiceTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IAsyncLifetime
    {
        /// <summary>Every log event of the instance, with every structured value.</summary>
        private readonly StateCapturingLoggerProvider _logs = new();

        /// <summary>The instance over the fixture's databases, with in-memory blobs and the captured log; set by the initialisation.</summary>
        private ServiceProvider? _instance;

        /// <summary>Takes the lock that serialises the security writes, as a side transaction holds it.</summary>
        private const string TakeSecurityLock = "EXEC sp_getapplock @Resource = N'tellma.access', @LockMode = N'Exclusive', @LockOwner = N'Transaction';";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The instance.</summary>
        private ServiceProvider Instance => _instance ?? throw new InvalidOperationException("The instance is not composed yet.");

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            _instance = fixture.CreateInstance(configure: Configure);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_instance is not null)
            {
                await _instance.DisposeAsync();
            }

            _logs.Dispose();
        }

        // --- The envelope ---

        [Fact]
        public async Task The_envelope_answers_the_profile_the_bag_the_access_and_the_tags_in_one_round_trip()
        {
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");
            await SeedBagAsync(ann, ("nav.pinned", "[1]"), ("theme", "dark"));
            _ = await MeAsync(admin);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(admin, services: Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            var me = (MeResult)(await InvokeAsync(scope, "me", null))!;

            Assert.Equal(1, access.RoundTrips - start);
            Assert.Equal((ann, "Ann", UserState.Joined), (me.User.Id, me.User.Name, me.User.State));
            Assert.Equal(new Dictionary<string, string> { ["nav.pinned"] = "[1]", ["theme"] = "dark" }, me.Preferences);

            // --- The four tags, the preferences one the caller's stamped tag, the access summary under the permissions one ---
            Assert.Equal([VersionTagWire.SettingsItem, VersionTagWire.PermissionsItem, VersionTagWire.PreferencesItem, VersionTagWire.EntitiesItem], me.Tags.Keys);
            Assert.Equal(VersionTagWire.Preferences(new VersionTag((await fixture.UserTagsAsync(ann)).Preferences!.Value)), me.PreferencesTag);
            Assert.Equal(me.Tags[VersionTagWire.PreferencesItem], me.PreferencesTag);
            Assert.Equal(me.Tags[VersionTagWire.PermissionsItem], me.Access.Tag);
            Assert.Equal(MeResult.FormatVersion, me.Access.FormatVersion);
            Assert.Contains(new SecurableSummary(CoreResources.User, AccessActions.Save, false), me.Access.Securables);
        }

        [Fact]
        public async Task A_member_without_a_grant_reads_their_own_row_and_no_other()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            (int other, _) = await fixture.SeedCallerAsync("Other");

            MeResult me = await MeAsync(member);
            Assert.Contains(new SecurableSummary(CoreResources.User, AccessActions.Read, true), me.Access.Securables);
            Assert.DoesNotContain(me.Access.Securables, static securable => securable.Resource == CoreResources.User && securable.Action == AccessActions.Save);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance);
            EntitiesResult<User> read = await scope.Users().GetByIdsAsync([dee, other], DetailsRequest.None, Token);
            Assert.Equal([dee], read.Ids);
        }

        // --- The profile edit ---

        [Fact]
        public async Task A_member_without_a_save_grant_edits_their_own_profile_in_three_round_trips_and_nothing_else()
        {
            // A write to the users' table needs the tenant to keep a human administrator.
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Invariant($"UPDATE [core].[Users] SET [PreferredTimeZone] = N'Asia/Dubai', [PreferredRegion] = N'GB', [PreferredDigits] = N'Native' WHERE [Id] = {dee};"));
            ProfileRow before = await ProfileAsync(dee);
            Guid? preferences = (await fixture.UserTagsAsync(dee)).Preferences;
            _ = await MeAsync(member);

            MeResult saved;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int start = access.RoundTrips;
                saved = (MeResult)(await InvokeAsync(scope, "me/save", Edit(before.ModifiedAt, "Dee L.", mobile: "+971501234567")))!;
                Assert.Equal(3, access.RoundTrips - start);
            }

            // --- The request's members stored, every other member as it was, the caller's tag moved ---
            ProfileRow after = await ProfileAsync(dee);
            Assert.Equal(("Dee L.", "+971501234567", "Female"), (after.Name, after.ContactMobile, after.Gender));
            Assert.Equal((before.Email, before.TimeZone, before.Region, before.Digits, before.State), (after.Email, after.TimeZone, after.Region, after.Digits, after.State));
            Assert.Equal(dee, after.ModifiedById);
            Assert.True(after.ModifiedAt > before.ModifiedAt);
            Guid? moved = (await fixture.UserTagsAsync(dee)).Preferences;
            Assert.NotEqual(preferences, moved);

            // --- The envelope over the saved row and the persist's tags ---
            Assert.Equal(("Dee L.", after.ModifiedAt, "Asia/Dubai"), (saved.User.Name, saved.User.ModifiedAt, saved.User.PreferredTimeZone));
            Assert.Equal(VersionTagWire.Preferences(new VersionTag(moved!.Value)), saved.PreferencesTag);
        }

        [Fact]
        public async Task The_same_member_s_save_of_their_own_row_through_the_users_save_is_refused_for_want_of_a_grant()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");

            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(member, Instance);
            User row = await scope.LoadUserAsync(dee);
            row.Name = "Dee L.";
            row.RoleMemberships = null;
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => scope.SaveUsersAsync(row));

            Assert.Equal((CoreResources.User, ForbiddenReason.NoGrant), (refused.Resource, refused.Reason));
            Assert.Equal("Dee", (await ProfileAsync(dee)).Name);
        }

        [Fact]
        public async Task A_stale_stamp_conflicts_naming_the_last_modifier_and_an_override_saves_over_it()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            DateTimeOffset read = (await ProfileAsync(dee)).ModifiedAt;
            (int ann, string admin) = await fixture.SeedAdministratorAsync("Ann");

            // --- Another user saves the row after the member read it ---
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                User row = await scope.LoadUserAsync(dee);
                row.ContactMobile = "+971509876543";
                row.RoleMemberships = null;
                _ = await scope.SaveUsersAsync(row);
            }

            Assert.NotEqual(read, (await ProfileAsync(dee)).ModifiedAt);

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance))
            {
                ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => InvokeAsync(scope, "me/save", Edit(read, "Dee L.")));
                ConcurrencyConflict only = Assert.Single(conflict.Conflicts);
                Assert.Equal((dee, ann, "Ann", false), (only.Id, only.ModifiedById, only.ModifiedByName, only.IsMissing));
            }

            Assert.Equal("Dee", (await ProfileAsync(dee)).Name);

            // --- The member chooses to overwrite ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance))
            {
                _ = await InvokeAsync(scope, "me/save", Edit(read, "Dee L.", concurrency: ConcurrencyMode.Override));
            }

            Assert.Equal("Dee L.", (await ProfileAsync(dee)).Name);
        }

        [Fact]
        public async Task A_signature_is_attached_through_the_profile_edit_and_through_the_users_save()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            (int bob, _) = await fixture.SeedCallerAsync("Bob");
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");

            // --- The member's own signature, through the profile edit ---
            int mine = await StageSignatureAsync(member);
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(member, services: Instance))
            {
                _ = await InvokeAsync(scope, "me/save", Edit((await ProfileAsync(dee)).ModifiedAt, "Dee", signatureId: mine));
            }

            Assert.Equal(mine, (await ProfileAsync(dee)).SignatureId);
            Assert.Equal("Committed", await BlobStateAsync(mine));

            // --- Another user's, through the users' save ---
            int theirs = await StageSignatureAsync(admin);
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(admin, Instance))
            {
                User row = await scope.LoadUserAsync(bob);
                row.SignatureId = theirs;
                row.RoleMemberships = null;
                _ = await scope.SaveUsersAsync(row);
            }

            Assert.Equal(theirs, (await ProfileAsync(bob)).SignatureId);
            Assert.Equal("Committed", await BlobStateAsync(theirs));
        }

        // --- The locale change ---

        [Fact]
        public async Task Each_locale_member_is_stored_alone_in_one_round_trip_and_moves_the_caller_s_tag()
        {
            // A write to the users' table needs the tenant to keep a human administrator.
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            _ = await MeAsync(member);

            (MeLocaleRequest Request, Func<ProfileRow, object?> Stored, object? Expected)[] cases =
            [
                (Locale(language: "EN"), static row => row.Language, "en"),
                (Locale(calendar: "GC"), static row => row.Calendar, "gc"),
                (Locale(timeZone: "asia/riyadh"), static row => row.TimeZone, "Asia/Riyadh"),
                (Locale(region: "gb"), static row => row.Region, "GB"),
                (Locale(digits: DigitStyle.Native), static row => row.Digits, "Native"),
            ];
            foreach ((MeLocaleRequest request, Func<ProfileRow, object?> stored, object? expected) in cases)
            {
                Guid? tag = (await fixture.UserTagsAsync(dee)).Preferences;
                (MeLocaleResult result, int roundTrips) = await SetLocaleAsync(member, request);

                Assert.Equal(1, roundTrips);
                ProfileRow row = await ProfileAsync(dee);
                Assert.Equal(expected, stored(row));
                Guid? moved = (await fixture.UserTagsAsync(dee)).Preferences;
                Assert.NotEqual(tag, moved);
                Assert.Equal((VersionTagWire.Preferences(new VersionTag(moved!.Value)), row.ModifiedAt), (result.PreferencesTag, result.ModifiedAt));
            }
        }

        [Fact]
        public async Task All_five_members_are_stored_at_once_and_a_reset_stores_null()
        {
            // A write to the users' table needs the tenant to keep a human administrator.
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");

            (MeLocaleResult all, _) = await SetLocaleAsync(member, new MeLocaleRequest("en", "gc", "Asia/Riyadh", "GB", DigitStyle.Native));
            Assert.Equal(("en", "gc", "Asia/Riyadh", "GB", (DigitStyle?)DigitStyle.Native), (all.PreferredLanguage, all.PreferredCalendar, all.PreferredTimeZone, all.PreferredRegion, all.PreferredDigits));

            (MeLocaleResult reset, _) = await SetLocaleAsync(member, new MeLocaleRequest(null, null, null, null, null, [MeLocaleMember.Region, MeLocaleMember.Digits]));
            Assert.Equal(("en", "gc", "Asia/Riyadh", null, null), (reset.PreferredLanguage, reset.PreferredCalendar, reset.PreferredTimeZone, reset.PreferredRegion, reset.PreferredDigits));
            ProfileRow row = await ProfileAsync(dee);
            Assert.Equal(("en", "gc", "Asia/Riyadh", null, null), (row.Language, row.Calendar, row.TimeZone, row.Region, row.Digits));
        }

        [Fact]
        public async Task A_locale_change_is_refused_at_its_members_before_anything_is_written()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            ProfileRow before = await ProfileAsync(dee);

            Assert.Equal([("Language", AccessCodes.LocaleEmpty)], await LocaleRefusalsAsync(member, new MeLocaleRequest(null, null, null, null, null)));
            Assert.Equal([("Reset", AccessCodes.LocaleResetConflict)], await LocaleRefusalsAsync(member, new MeLocaleRequest(null, null, "Asia/Riyadh", null, null, [MeLocaleMember.TimeZone])));
            Assert.Equal([("Language", AccessCodes.LanguageNotOffered)], await LocaleRefusalsAsync(member, Locale(language: "ar-SA")));
            Assert.Equal([("Language", AccessCodes.LanguageNotOffered)], await LocaleRefusalsAsync(member, Locale(language: "xx")));
            Assert.Equal([("TimeZone", AccessCodes.TimeZoneUnknown)], await LocaleRefusalsAsync(member, Locale(timeZone: "Asia/Calcutta")));
            Assert.Equal([("Region", AccessCodes.RegionNotOffered)], await LocaleRefusalsAsync(member, Locale(region: "ZZ")));

            Assert.Equal(before, await ProfileAsync(dee));
        }

        [Fact]
        public async Task An_unchanged_locale_value_writes_no_history_and_leaves_the_stamp_and_the_tag()
        {
            // A write to the users' table needs the tenant to keep a human administrator.
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            _ = await SetLocaleAsync(member, Locale(timeZone: "Asia/Dubai"));
            int history = await HistoryAsync(dee);
            ProfileRow stored = await ProfileAsync(dee);
            Guid? tag = (await fixture.UserTagsAsync(dee)).Preferences;

            (MeLocaleResult again, _) = await SetLocaleAsync(member, Locale(timeZone: "Asia/Dubai"));

            Assert.Equal(history, await HistoryAsync(dee));
            Assert.Equal(stored, await ProfileAsync(dee));
            Assert.Equal(tag, (await fixture.UserTagsAsync(dee)).Preferences);
            Assert.Equal((stored.ModifiedAt, VersionTagWire.Preferences(new VersionTag(tag!.Value))), (again.ModifiedAt, again.PreferencesTag));
        }

        [Fact]
        public async Task A_read_only_tenant_stores_a_locale_change_and_a_frozen_one_refuses_it()
        {
            // A write to the users' table needs the tenant to keep a human administrator.
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");

            _ = await SetLocaleAsync(member, Locale(timeZone: "Asia/Riyadh"), TenantState.ReadOnly);
            Assert.Equal("Asia/Riyadh", (await ProfileAsync(dee)).TimeZone);

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(() => SetLocaleAsync(member, Locale(timeZone: "Asia/Dubai"), TenantState.Frozen));
            Assert.Equal(TenantProblemCodes.Frozen, refused.Code);
            Assert.Equal("Asia/Riyadh", (await ProfileAsync(dee)).TimeZone);
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task A_role_save_racing_a_membership_edit_and_a_locale_change_completes_each_with_no_deadlock()
        {
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));

            // A longer lock wait than the default, so no writer times out while the lock is held.
            await using ServiceProvider instance = fixture.CreateInstance(configure: services =>
            {
                Configure(services);
                _ = services.PostConfigure<AccessOptions>(static options => options.SecurityLockTimeout = TimeSpan.FromSeconds(20));
            });

            // --- The three writers pass their reads and wait on the security lock together, then run
            // in the lock's order: each takes the lock before the version tags it guards ---
            IReadOnlyList<Task> writers = await fixture.HoldWhileAsync(
                TakeSecurityLock,
                3,
                "APPLICATION",
                () => [RenameRoleAsync(instance, admin, role), JoinRoleAsync(instance, admin, dee, role), SetLocaleAsync(member, Locale(timeZone: "Asia/Riyadh"), services: instance)]);

            // --- The role save and the locale change committed: a deadlock victim or a lock timeout
            // would fault its writer here ---
            await writers[0];
            await writers[2];
            Assert.Equal(Invariant($"Racing {role}"), await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Invariant($"SELECT [Name] FROM [core].[Roles] WHERE [Id] = {role};")));
            Assert.Equal("Asia/Riyadh", (await ProfileAsync(dee)).TimeZone);

            // --- The membership edit ran whole before or after the locale change: it committed, or,
            // run after it, found the user's stamp moved since its load and conflicted; never a deadlock ---
            int joined = await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Invariant($"SELECT COUNT(*) FROM [core].[RoleMemberships] WHERE [UserId] = {dee} AND [RoleId] = {role};"));
            try
            {
                await writers[1];
                Assert.Equal(1, joined);
            }
            catch (ConcurrencyException conflict)
            {
                Assert.Equal([dee], conflict.Conflicts.Select(static row => row.Id));
                Assert.Equal(0, joined);
            }
        }

        // --- The caller's preference bag ---

        [Fact]
        public async Task The_caller_sets_and_deletes_keys_of_their_own_bag_in_one_round_trip_each_and_moves_only_their_tag()
        {
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            (int bob, _) = await fixture.SeedCallerAsync("Bob");
            await SeedBagAsync(dee, ("theme", "light"));
            Guid? deeTag = (await fixture.UserTagsAsync(dee)).Preferences;
            Guid? bobTag = (await fixture.UserTagsAsync(bob)).Preferences;
            _ = await MeAsync(member);

            (PreferencesResult set, int setTrips) = await OwnBagAsync(
                member, "me/preferences/set", new UserPreferencesArguments([new KeyValueItem("theme", "dark"), new KeyValueItem("report.acme-credit-exposure.params", "{}")]));
            Assert.Equal(1, setTrips);
            Assert.Equal(new Dictionary<string, string> { ["report.acme-credit-exposure.params"] = "{}", ["theme"] = "dark" }, set.Preferences);
            Guid? moved = (await fixture.UserTagsAsync(dee)).Preferences;
            Assert.NotEqual(deeTag, moved);
            Assert.Equal(VersionTagWire.Preferences(new VersionTag(moved!.Value)), set.PreferencesTag);

            (PreferencesResult deleted, int deleteTrips) = await OwnBagAsync(member, "me/preferences/delete", new UserPreferenceKeysArguments(["tHEME", "absent"]));
            Assert.Equal(1, deleteTrips);
            Assert.Equal(new Dictionary<string, string> { ["report.acme-credit-exposure.params"] = "{}" }, deleted.Preferences);
            Assert.Equal("report.acme-credit-exposure.params={}", await BagAsync(dee));
            Assert.Equal(bobTag, (await fixture.UserTagsAsync(bob)).Preferences);
        }

        [Fact]
        public async Task A_bad_key_is_refused_at_its_item_and_the_key_cap_at_the_request_writing_nothing()
        {
            await using ServiceProvider capped = fixture.CreateInstance(configure: static services => services.Configure<AccessOptions>(static options => options.MaxPreferenceKeys = 2));
            (int dee, string member) = await fixture.SeedCallerAsync("Dee");
            await SeedBagAsync(dee, ("a", "1"), ("b", "2"));

            ValidationException key = await Assert.ThrowsAsync<ValidationException>(
                () => OwnBagAsync(member, "me/preferences/set", new UserPreferencesArguments([new KeyValueItem("Report.exposure", "x")]), capped));
            ValidationError badKey = Assert.Single(key.Errors);
            Assert.Equal(("Items[0].Key", AccessCodes.PreferenceKeyInvalid), (badKey.Path.ToString(), badKey.Code));

            ValidationException cap = await Assert.ThrowsAsync<ValidationException>(
                () => OwnBagAsync(member, "me/preferences/set", new UserPreferencesArguments([new KeyValueItem("c", "3")]), capped));
            ValidationError tooMany = Assert.Single(cap.Errors);
            Assert.Equal((ValidationPath.Root, AccessCodes.TooManyPreferenceKeys), (tooMany.Path, tooMany.Code));
            Assert.Equal("a=1;b=2", await BagAsync(dee));
        }

        // --- Another user's preference bag ---

        [Fact]
        public async Task Another_user_s_bag_is_read_set_and_deleted_under_the_preferences_filter_within_its_budgets()
        {
            (int vis, _) = await fixture.SeedCallerAsync("Vis");
            (int hid, _) = await fixture.SeedCallerAsync("Hid");
            (int carer, string subject) = await fixture.SeedCallerAsync("Carer");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Preferences, "Name = 'Vis'"));
            await fixture.SeedMembershipAsync(carer, role);
            await SeedBagAsync(vis, ("theme", "light"));
            await SeedBagAsync(hid, ("theme", "light"));
            Guid? carerTag = (await fixture.UserTagsAsync(carer)).Preferences;
            _ = await MeAsync(subject);

            // --- The read: one round trip, and a hidden user not found ---
            UserPreferencesResult read;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: Instance))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int start = access.RoundTrips;
                read = (UserPreferencesResult)(await InvokeAsync(scope, "preferences/get", new UserPreferencesRequest(vis)))!;
                Assert.Equal(1, access.RoundTrips - start);
                NotFoundException hidden = await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(scope, "preferences/get", new UserPreferencesRequest(hid)));
                Assert.Equal([hid.ToString(CultureInfo.InvariantCulture)], hidden.Ids);
            }

            Assert.Equal(new Dictionary<string, string> { ["theme"] = "light" }, read.Preferences);

            // --- The set and the delete: two round trips each, the target's bag answered and its tag moved ---
            Guid? visTag = (await fixture.UserTagsAsync(vis)).Preferences;
            (UserPreferencesResult set, int setTrips) = await TheirBagAsync(subject, "preferences/set", vis, new UserPreferencesArguments([new KeyValueItem("theme", "dark"), new KeyValueItem("grid.1.widths", "1,2")]));
            Assert.Equal(2, setTrips);
            Assert.Equal(new Dictionary<string, string> { ["grid.1.widths"] = "1,2", ["theme"] = "dark" }, set.Preferences);
            Guid? moved = (await fixture.UserTagsAsync(vis)).Preferences;
            Assert.NotEqual(visTag, moved);

            (UserPreferencesResult deleted, int deleteTrips) = await TheirBagAsync(subject, "preferences/delete", vis, new UserPreferenceKeysArguments(["theme"]));
            Assert.Equal(2, deleteTrips);
            Assert.Equal(new Dictionary<string, string> { ["grid.1.widths"] = "1,2" }, deleted.Preferences);
            Assert.NotEqual(moved, (await fixture.UserTagsAsync(vis)).Preferences);

            // --- The writer's own tag stays; each write is logged for the target ---
            Assert.Equal(carerTag, (await fixture.UserTagsAsync(carer)).Preferences);
            Assert.Equal("theme=light", await BagAsync(hid));
            CapturedLogEntry[] logged = [.. _logs.Entries.Where(static entry => entry.EventId.Name == "UserPreferencesChangedByAdministrator")];
            Assert.Equal(2, logged.Length);
            Assert.All(logged, entry =>
            {
                Assert.Equal(LogLevel.Information, entry.Level);
                Assert.Contains(vis.ToString(CultureInfo.InvariantCulture), entry.Texts);
            });
        }

        [Fact]
        public async Task Another_user_s_bad_key_is_refused_at_the_action_s_arguments()
        {
            (int vis, _) = await fixture.SeedCallerAsync("Vis");
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => TheirBagAsync(admin, "preferences/set", vis, new UserPreferencesArguments([new KeyValueItem("report.-exposure", "x")])));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("Arguments.Items[0].Key", AccessCodes.PreferenceKeyInvalid), (error.Path.ToString(), error.Code));
        }

        [Fact]
        public async Task Another_user_s_bag_past_its_caps_or_named_twice_is_refused_writing_nothing()
        {
            await using ServiceProvider capped = fixture.CreateInstance(configure: static services => services.Configure<AccessOptions>(static options =>
            {
                options.MaxPreferenceKeys = 2;
                options.MaxPreferenceValueBytes = 8;
            }));
            (int vis, _) = await fixture.SeedCallerAsync("Vis");
            (_, string admin) = await fixture.SeedAdministratorAsync("Ann");
            await SeedBagAsync(vis, ("a", "1"), ("b", "2"));
            Guid? tag = (await fixture.UserTagsAsync(vis)).Preferences;

            // --- A third key: the statement's cap, reported at the request ---
            ValidationException cap = await Assert.ThrowsAsync<ValidationException>(
                () => TheirBagAsync(admin, "preferences/set", vis, new UserPreferencesArguments([new KeyValueItem("c", "3")]), capped));
            ValidationError tooMany = Assert.Single(cap.Errors);
            Assert.Equal((ValidationPath.Root, AccessCodes.TooManyPreferenceKeys), (tooMany.Path, tooMany.Code));

            // --- A value past its size, at the item's value ---
            ValidationException size = await Assert.ThrowsAsync<ValidationException>(
                () => TheirBagAsync(admin, "preferences/set", vis, new UserPreferencesArguments([new KeyValueItem("a", "123456789")]), capped));
            ValidationError tooLarge = Assert.Single(size.Errors);
            Assert.Equal(("Arguments.Items[0].Value", AccessCodes.PreferenceValueTooLarge), (tooLarge.Path.ToString(), tooLarge.Code));

            // --- A delete naming one key twice, ignoring case, at the second ---
            ValidationException twice = await Assert.ThrowsAsync<ValidationException>(
                () => TheirBagAsync(admin, "preferences/delete", vis, new UserPreferenceKeysArguments(["ab", "aB"]), capped));
            ValidationError duplicate = Assert.Single(twice.Errors);
            Assert.Equal(("Arguments.Keys[1]", AccessCodes.PreferenceKeyDuplicate), (duplicate.Path.ToString(), duplicate.Code));

            // --- Nothing written, the target's tag unmoved ---
            Assert.Equal("a=1;b=2", await BagAsync(vis));
            Assert.Equal(tag, (await fixture.UserTagsAsync(vis)).Preferences);
        }

        // --- The activity extra ---

        [Fact]
        public async Task The_activity_extra_gives_each_visible_user_s_last_activity_and_nothing_of_a_hidden_one()
        {
            (int vis, _) = await fixture.SeedCallerAsync("Vis");
            (int hid, _) = await fixture.SeedCallerAsync("Hid");
            (int reader, string subject) = await fixture.SeedCallerAsync("Reader");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, "Name = 'Vis'"));
            await fixture.SeedMembershipAsync(reader, role);
            DateTimeOffset active = new(2026, 11, 20, 8, 15, 0, TimeSpan.Zero);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Invariant($"UPDATE [core].[UserStamps] SET [LastActiveAt] = '2026-11-20T08:15:00+00:00' WHERE [UserId] IN ({vis}, {hid});"));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: Instance);
            EntitiesResult<User> read = await scope.Users().GetByIdsAsync([vis, hid], new DetailsRequest { Include = ["activity"] }, Token);

            Assert.Equal([vis], read.Ids);
            JsonElement extra = read.Extras!["activity"];
            Assert.Equal(["UserId", "LastActiveAt"], extra.GetProperty("columns").EnumerateArray().Select(static column => column.GetProperty("name").GetString()));
            JsonElement row = Assert.Single(extra.GetProperty("rows").EnumerateArray());
            Assert.Equal((vis, active), (row[0].GetInt32(), row[1].GetDateTimeOffset()));
        }

        // --- Helpers ---

        /// <summary>Renames a role through the roles' save, as a stepped-up administrator.</summary>
        /// <param name="instance">The instance to save through.</param>
        /// <param name="subject">The administrator.</param>
        /// <param name="role">The role.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private async Task RenameRoleAsync(IServiceProvider instance, string subject, int role)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, instance);
            Role loaded = Assert.Single((await scope.Roles().GetByIdAsync(role, DetailsRequest.None, Token)).Entities);
            loaded.Name = Invariant($"Racing {role}");
            _ = await scope.Roles().SaveAsync([loaded], new SaveOptions { ReturnEntities = false }, Token);
        }

        /// <summary>Adds a user to a role through the users' save, as a stepped-up administrator.</summary>
        /// <param name="instance">The instance to save through.</param>
        /// <param name="subject">The administrator.</param>
        /// <param name="userId">The user.</param>
        /// <param name="role">The role.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private async Task JoinRoleAsync(IServiceProvider instance, string subject, int userId, int role)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject, instance);
            User user = await scope.LoadUserAsync(userId);
            user.RoleMemberships = [.. user.RoleMemberships ?? [], new RoleMembership { RoleId = role }];
            _ = await scope.SaveUsersAsync(user);
        }

        /// <summary>Adds the in-memory blobs and the captured log to the instance.</summary>
        /// <param name="services">The instance's services.</param>
        private void Configure(IServiceCollection services)
        {
            _ = services.AddInMemoryBlobs();
            _ = services.AddSingleton<ILoggerProvider>(_logs);
        }

        /// <summary>Calls one of the users' own actions through the action invoker, as its route does.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <returns>The answer.</returns>
        private static Task<object?> InvokeAsync(AsyncServiceScope scope, string action, object? body)
        {
            StackDescriptor users = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Find(CoreResources.User)!;
            ApiActionDescriptor descriptor = users.Actions.OfType<ApiActionDescriptor>().Single(candidate => candidate.Name == action);
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(descriptor, body, Token);
        }

        /// <summary>Reads the caller's envelope in a scope of their own, which also warms their connection.</summary>
        /// <param name="subject">The caller.</param>
        /// <returns>The envelope.</returns>
        private async Task<MeResult> MeAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: Instance);
            return (MeResult)(await InvokeAsync(scope, "me", null))!;
        }

        /// <summary>Changes the caller's locale in a scope of their own and counts its round trips.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="request">The change.</param>
        /// <param name="state">The state the request sees the tenant in.</param>
        /// <param name="services">The instance to call; null for the test's own.</param>
        /// <returns>The answer and the round trips.</returns>
        private async Task<(MeLocaleResult Result, int RoundTrips)> SetLocaleAsync(string subject, MeLocaleRequest request, TenantState state = TenantState.Active, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, state: state, services: services ?? Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            var result = (MeLocaleResult)(await InvokeAsync(scope, "me/locale", request))!;
            return (result, access.RoundTrips - start);
        }

        /// <summary>The refusals of a locale change, each as its path and code.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="request">The change.</param>
        /// <returns>The refusals.</returns>
        private async Task<(string Path, string Code)[]> LocaleRefusalsAsync(string subject, MeLocaleRequest request)
        {
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SetLocaleAsync(subject, request));
            return [.. refused.Errors.Select(static error => (error.Path.ToString(), error.Code))];
        }

        /// <summary>Writes the caller's own bag in a scope of their own and counts the round trips.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="action">The action: the set or the delete.</param>
        /// <param name="body">The body.</param>
        /// <param name="services">The instance; the test's own when null.</param>
        /// <returns>The answer and the round trips.</returns>
        private async Task<(PreferencesResult Result, int RoundTrips)> OwnBagAsync(string subject, string action, object body, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: services ?? Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            var result = (PreferencesResult)(await InvokeAsync(scope, action, body))!;
            return (result, access.RoundTrips - start);
        }

        /// <summary>Writes another user's bag through the users' entity action in a scope of the caller and counts the round trips.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="action">The action: the set or the delete.</param>
        /// <param name="target">The user whose bag is written.</param>
        /// <param name="arguments">The action's arguments.</param>
        /// <param name="services">The instance to call; null for the test's own.</param>
        /// <returns>The answer and the round trips.</returns>
        private async Task<(UserPreferencesResult Result, int RoundTrips)> TheirBagAsync(string subject, string action, int target, object arguments, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: services ?? Instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int start = access.RoundTrips;
            UserPreferencesResult result = await scope.Users().ExecuteActionAsync<UserPreferencesResult>(action, [target], arguments, new ActionOptions(), Token);
            return (result, access.RoundTrips - start);
        }

        /// <summary>Stages a signature as a member, in a scope of their own.</summary>
        /// <param name="subject">The member.</param>
        /// <returns>The staged blob's id.</returns>
        private async Task<int> StageSignatureAsync(string subject)
        {
            // A PNG's signature, then filler; the stub processor stores it as it is.
            byte[] bytes = new byte[200];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: Instance);
            BlobStageRequest request = new("user-signature", new MemoryStream(bytes, writable: false), bytes.LongLength, BlobContentTypes.Png, "signature.png");
            return (await scope.ServiceProvider.GetRequiredService<IBlobService>().StageAsync(request, Token)).Id;
        }

        /// <summary>A blob's state.</summary>
        /// <param name="id">The blob.</param>
        /// <returns>The state.</returns>
        private Task<string?> BlobStateAsync(int id)
        {
            return fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Invariant($"SELECT [State] FROM [core].[Blobs] WHERE [Id] = {id};"));
        }

        /// <summary>A profile edit that keeps the gender female and every unnamed member empty.</summary>
        /// <param name="stamp">The stamp the edit was made against.</param>
        /// <param name="name">The name.</param>
        /// <param name="mobile">The contact number.</param>
        /// <param name="signatureId">The signature.</param>
        /// <param name="concurrency">How a moved stamp is treated.</param>
        /// <returns>The edit.</returns>
        private static MeSaveRequest Edit(DateTimeOffset stamp, string name, string? mobile = null, int? signatureId = null, ConcurrencyMode concurrency = ConcurrencyMode.Check)
        {
            return new MeSaveRequest(name, null, null, null, signatureId, Gender.Female, null, mobile, stamp, concurrency);
        }

        /// <summary>A locale change of the members named.</summary>
        /// <param name="language">The language.</param>
        /// <param name="calendar">The calendar.</param>
        /// <param name="timeZone">The time zone.</param>
        /// <param name="region">The region.</param>
        /// <param name="digits">The digits.</param>
        /// <returns>The change.</returns>
        private static MeLocaleRequest Locale(string? language = null, string? calendar = null, string? timeZone = null, string? region = null, DigitStyle? digits = null)
        {
            return new MeLocaleRequest(language, calendar, timeZone, region, digits);
        }

        /// <summary>Seeds keys of a user's bag, raw.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="items">The keys and values.</param>
        /// <returns>A task that completes when they are seeded.</returns>
        private Task SeedBagAsync(int userId, params (string Key, string Value)[] items)
        {
            string rows = string.Join(", ", items.Select(item => Invariant($"({userId}, N'{item.Key}', N'{item.Value}')")));
            return fixture.ExecuteAsync(ConnectDatabase.TenantA, $"INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) VALUES {rows};");
        }

        /// <summary>A user's bag as stored, as <c>key=value</c> items by key.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The items; null for an empty bag.</returns>
        private Task<string?> BagAsync(int userId)
        {
            return fixture.ScalarAsync<string>(
                ConnectDatabase.TenantA,
                Invariant($"SELECT STRING_AGG(CAST([Key] + N'=' + [Value] AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY [Key]) FROM [core].[UserPreferences] WHERE [UserId] = {userId};"));
        }

        /// <summary>The history rows of a user.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The count.</returns>
        private Task<int> HistoryAsync(int userId)
        {
            return fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Invariant($"SELECT COUNT(*) FROM [core].[UsersHistory] WHERE [Id] = {userId};"));
        }

        /// <summary>Reads the members of a user row the self-service writes or must leave.</summary>
        /// <param name="id">The user.</param>
        /// <returns>The row.</returns>
        private async Task<ProfileRow> ProfileAsync(int id)
        {
            await using SqlConnection connection = new(fixture.ConnectionString(ConnectDatabase.TenantA));
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = Invariant(
                $"""
                SELECT [Name], [Email], [ContactMobile], [Gender], [SignatureId], [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone], [PreferredRegion], [PreferredDigits], [State], [ModifiedAt], [ModifiedById]
                FROM [core].[Users] WHERE [Id] = {id};
                """);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            return new ProfileRow(
                reader.GetString(0),
                Text(reader, 1),
                Text(reader, 2),
                Text(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Text(reader, 5),
                Text(reader, 6),
                Text(reader, 7),
                Text(reader, 8),
                Text(reader, 9),
                reader.GetString(10),
                reader.GetDateTimeOffset(11),
                reader.GetInt32(12));
        }

        /// <summary>A nullable text column.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="ordinal">The column.</param>
        /// <returns>The text; null for NULL.</returns>
        private static string? Text(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        /// <summary>Formats with the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The members of a user row the self-service writes or must leave.</summary>
        /// <param name="Name">The name.</param>
        /// <param name="Email">The sign-in address.</param>
        /// <param name="ContactMobile">The contact number.</param>
        /// <param name="Gender">The gender.</param>
        /// <param name="SignatureId">The signature.</param>
        /// <param name="Language">The preferred language.</param>
        /// <param name="Calendar">The preferred calendar.</param>
        /// <param name="TimeZone">The preferred time zone.</param>
        /// <param name="Region">The preferred region.</param>
        /// <param name="Digits">The preferred digits.</param>
        /// <param name="State">The computed state.</param>
        /// <param name="ModifiedAt">The row's stamp.</param>
        /// <param name="ModifiedById">Who stamped it.</param>
        private sealed record ProfileRow(
            string Name,
            string? Email,
            string? ContactMobile,
            string? Gender,
            int? SignatureId,
            string? Language,
            string? Calendar,
            string? TimeZone,
            string? Region,
            string? Digits,
            string State,
            DateTimeOffset ModifiedAt,
            int ModifiedById);
    }
}
