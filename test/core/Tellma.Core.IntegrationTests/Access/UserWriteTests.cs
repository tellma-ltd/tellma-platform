// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Access;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The users' and roles' writes against a real tenant database: the tags each kind of write
    ///     moves, the stamp row a new user gains in its own save, a filter's stamp across re-saves,
    ///     escalation through a membership or a permission, a role's duplicate permissions kept
    ///     once, deletes by query refused, the memberships' window codes, and the preference bag's
    ///     statements, whose key cap holds under two concurrent writes of one bag, by its owner or
    ///     by another user.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class UserWriteTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The tags each write moves ---

        [Fact]
        public async Task A_role_save_moves_the_tenant_s_permissions_tag_and_no_user_s()
        {
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            Guid permissions = await fixture.TenantTagAsync(VersionTagNames.Permissions);
            (Guid? Permissions, Guid? Preferences) tags = await fixture.UserTagsAsync(ann);

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                Role role = new() { Name = "Clerks", Permissions = [new Permission { Resource = CoreResources.User, Action = AccessActions.Read }] };
                _ = await scope.Roles().SaveAsync([role], new SaveOptions { ReturnEntities = false }, Token);
            }

            Assert.NotEqual(permissions, await fixture.TenantTagAsync(VersionTagNames.Permissions));
            Assert.Equal(tags, await fixture.UserTagsAsync(ann));
        }

        [Fact]
        public async Task A_membership_save_moves_that_user_s_permissions_tag_only()
        {
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            (int cal, _) = await fixture.SeedCallerAsync("Cal");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));
            Guid permissions = await fixture.TenantTagAsync(VersionTagNames.Permissions);
            (Guid? Permissions, Guid? Preferences) annTags = await fixture.UserTagsAsync(ann);
            Guid? benPermissions = (await fixture.UserTagsAsync(ben)).Permissions;
            (Guid? Permissions, Guid? Preferences) calTags = await fixture.UserTagsAsync(cal);

            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                User user = await scope.LoadUserAsync(ben);
                user.RoleMemberships = [.. user.RoleMemberships!, new RoleMembership { RoleId = role }];
                _ = await scope.SaveUsersAsync(user);
            }

            Assert.Equal(permissions, await fixture.TenantTagAsync(VersionTagNames.Permissions));
            Assert.NotEqual(benPermissions, (await fixture.UserTagsAsync(ben)).Permissions);
            Assert.Equal(annTags, await fixture.UserTagsAsync(ann));
            Assert.Equal(calTags, await fixture.UserTagsAsync(cal));
        }

        [Fact]
        public async Task A_save_of_the_user_alone_moves_its_preferences_tag_and_no_permissions_tag()
        {
            (_, string subject) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            Guid permissions = await fixture.TenantTagAsync(VersionTagNames.Permissions);
            (Guid? permissionsBefore, Guid? preferencesBefore) = await fixture.UserTagsAsync(ben);

            // A payload without the memberships leaves them as they are.
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                User user = await scope.LoadUserAsync(ben);
                user.Name = "Benjamin";
                user.RoleMemberships = null;
                _ = await scope.SaveUsersAsync(user);
            }

            (Guid? permissionsAfter, Guid? preferencesAfter) = await fixture.UserTagsAsync(ben);
            Assert.Equal(permissions, await fixture.TenantTagAsync(VersionTagNames.Permissions));
            Assert.Equal(permissionsBefore, permissionsAfter);
            Assert.NotEqual(preferencesBefore, preferencesAfter);
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[RoleMemberships]", ben)));
        }

        // --- The companion stamp row ---

        [Fact]
        public async Task A_new_user_gains_its_stamp_row_in_its_own_save_under_the_save_s_tag()
        {
            (_, string subject) = await fixture.SeedAdministratorAsync("Ann");

            int dan;
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                dan = checked((int)Assert.Single(await scope.SaveUsersAsync(new User { Kind = UserKind.Individual, Email = "dan@example.com", Name = "Dan" })));
            }

            // --- One row, both tags the save's own, nothing seen or stamped yet ---
            (Guid? permissions, Guid? preferences) = await fixture.UserTagsAsync(dan);
            Assert.NotNull(permissions);
            Assert.Equal(permissions, preferences);
            Assert.Equal(0L, await fixture.ScalarAsync<long>(ConnectDatabase.TenantA, Where("SELECT [InboxVersion] FROM [core].[UserStamps]", dan)));
            Assert.Null(await fixture.ScalarAsync<DateTimeOffset?>(ConnectDatabase.TenantA, Where("SELECT [LastActiveAt] FROM [core].[UserStamps]", dan)));
            Assert.Equal(nameof(UserState.New), await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [State] FROM [core].[Users]", dan, "Id")));
        }

        // --- A filter's stamp ---

        [Fact]
        public async Task A_re_save_keeps_an_untouched_filter_s_stamp_and_a_rewritten_filter_takes_the_running_one()
        {
            (_, string subject) = await fixture.SeedAdministratorAsync("Ann");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, "Id <> 0"));

            // A stamp this engine no longer compiles: kept, it is refused; re-minted, it would pass.
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, Where("UPDATE [core].[Permissions] SET [FilterQueryexVersion] = 0", role, "RoleId"));
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);
            Role loaded = Assert.Single((await scope.Roles().GetByIdAsync(role, DetailsRequest.None, Token)).Entities);
            loaded.Name = "Renamed";

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.Roles().SaveAsync([loaded], new SaveOptions { ReturnEntities = false }, Token));
            Assert.Equal([AccessCodes.FilterVersionUnsupported], refused.Codes());

            // --- Rewritten, the filter is affirmed under the running version, which a later re-save keeps ---
            loaded.Permissions![0].Filter = "Id > 0";
            _ = await scope.Roles().SaveAsync([loaded], new SaveOptions { ReturnEntities = false }, Token);
            Role again = Assert.Single((await scope.Roles().GetByIdAsync(role, DetailsRequest.None, Token)).Entities);
            again.Name = "Renamed again";
            _ = await scope.Roles().SaveAsync([again], new SaveOptions { ReturnEntities = false }, Token);

            Assert.Equal(QueryexLanguage.Version, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT [FilterQueryexVersion] FROM [core].[Permissions]", role, "RoleId")));
        }

        // --- Escalation ---

        [Fact]
        public async Task A_clerk_who_may_save_users_and_read_roles_cannot_make_themselves_an_administrator()
        {
            _ = await fixture.SeedAdministratorAsync("Ann");

            // The clerk sees the roles, so the membership's reference passes and the escalation rule decides.
            (int clerk, string subject) = await fixture.SeedCallerAsync("Clerk");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.User, AccessActions.Save, null), (CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(clerk, role);
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);

            Assert.Equal([AccessCodes.MembershipEscalation], await RefusedAsync(scope, clerk, new RoleMembership { RoleId = WriteSideRig.Administrators }));
            Assert.Equal(0, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[RoleMemberships] WHERE [UserId] = {clerk} AND [RoleId] = {WriteSideRig.Administrators};")));
        }

        [Fact]
        public async Task A_clerk_cannot_send_a_new_administrator_s_invitation_to_another_address_with_or_without_the_memberships()
        {
            // --- A new user holding the administrator role, as the bootstrap leaves one until the invitation ---
            (_, string administrator) = await fixture.SeedAdministratorAsync("Ann");
            int newcomer;
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(administrator))
            {
                User created = new()
                {
                    Kind = UserKind.Individual,
                    Email = "newcomer@example.com",
                    Name = "Newcomer",
                    RoleMemberships = [new RoleMembership { RoleId = WriteSideRig.Administrators }],
                };
                newcomer = checked((int)(await scope.SaveUsersAsync(created))[0]);
            }

            (int clerk, string subject) = await fixture.SeedCallerAsync("Clerk");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.User, AccessActions.Save, null), (CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(clerk, role);
            await using (AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject))
            {
                // --- The memberships sent as they stand, then left out: either way they are tested as granted ---
                User carried = await scope.LoadUserAsync(newcomer);
                carried.Email = "clerk@example.com";
                ValidationException withMemberships = await Assert.ThrowsAsync<ValidationException>(() => scope.SaveUsersAsync(carried));

                User bare = await scope.LoadUserAsync(newcomer);
                bare.Email = "clerk@example.com";
                bare.RoleMemberships = null;
                ValidationException withoutMemberships = await Assert.ThrowsAsync<ValidationException>(() => scope.SaveUsersAsync(bare));

                Assert.Equal([AccessCodes.MembershipEscalation], withMemberships.Codes());
                Assert.Equal([AccessCodes.MembershipEscalation], withoutMemberships.Codes());
            }

            Assert.Equal("newcomer@example.com", await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [Email] FROM [core].[Users]", newcomer, "Id")));
        }

        [Fact]
        public async Task A_role_editor_cannot_give_a_role_they_hold_a_permission_beyond_their_own()
        {
            _ = await fixture.SeedAdministratorAsync("Ann");
            (int editor, string subject) = await fixture.SeedCallerAsync("Editor");
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null), (CoreResources.Role, AccessActions.Save, null));
            await fixture.SeedMembershipAsync(editor, role);
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);

            Role loaded = Assert.Single((await scope.Roles().GetByIdAsync(role, DetailsRequest.None, Token)).Entities);
            loaded.Permissions = [.. loaded.Permissions!, new Permission { Resource = CoreResources.Wildcard, Action = AccessActions.Wildcard }];

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => scope.Roles().SaveAsync([loaded], new SaveOptions { ReturnEntities = false }, Token));
            Assert.Equal([AccessCodes.EscalationBeyondSelf], refused.Codes());
            Assert.Equal(2, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[Permissions]", role, "RoleId")));
        }

        // --- The duplicate permissions ---

        [Fact]
        public async Task A_role_save_keeps_one_row_of_each_exact_duplicate_permission_stored_or_sent()
        {
            (_, string subject) = await fixture.SeedAdministratorAsync("Ann");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, null));
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);

            // --- Two stored duplicates, and a filtered pair sent twice beside them ---
            Role loaded = Assert.Single((await scope.Roles().GetByIdAsync(role, DetailsRequest.None, Token)).Entities);
            loaded.Name = string.Create(CultureInfo.InvariantCulture, $"Collapsed {role}");
            loaded.Permissions =
            [
                .. loaded.Permissions!,
                new Permission { Resource = CoreResources.User, Action = AccessActions.Read, Filter = "Id > 0" },
                new Permission { Resource = CoreResources.User, Action = AccessActions.Read, Filter = "Id > 0" },
            ];
            _ = await scope.Roles().SaveAsync([loaded], new SaveOptions { ReturnEntities = false }, Token);

            // --- One row of each: the unfiltered pair and the filtered one ---
            Assert.Equal(2, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[Permissions]", role, "RoleId")));
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Permissions] WHERE [RoleId] = {role} AND [Filter] IS NULL;")));
        }

        // --- Deletes by query ---

        [Fact]
        public async Task The_users_and_the_roles_serve_no_delete_by_query_and_delete_nothing()
        {
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);

            // --- Neither stack serves the operation, so neither projects its segment ---
            IStackRegistry stacks = scope.ServiceProvider.GetRequiredService<IStackRegistry>();
            foreach (string resource in new[] { CoreResources.User, CoreResources.Role })
            {
                StackDescriptor stack = stacks.Find(resource) ?? throw new InvalidOperationException($"No stack serves {resource}.");
                Assert.False(stack.Operations.HasFlag(StackOperations.DeleteByQuery), resource);
                Assert.True(stack.Operations.HasFlag(StackOperations.Delete), resource);
            }

            // --- A joined user no row references, the caller themselves, and a role without members: refused before any round trip ---
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Users().DeleteByQueryAsync(ById(ben), Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Users().DeleteByQueryAsync(ById(ann), Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Roles().DeleteByQueryAsync(ById(role), Token));

            Assert.Equal(2, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Users] WHERE [Id] IN ({ann}, {ben});")));
            Assert.Equal(1, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[Roles]", role, "Id")));
        }

        // --- The memberships' windows ---

        [Fact]
        public async Task Overlapping_backwards_and_already_ended_windows_are_refused_with_their_codes()
        {
            (_, string subject) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);

            Assert.Equal([AccessCodes.MembershipOverlap], await RefusedAsync(scope, ben, Window(role, null, null), Window(role, now.AddDays(1), null)));
            Assert.Equal([AccessCodes.MembershipEndsBeforeStart], await RefusedAsync(scope, ben, Window(role, now.AddDays(2), now.AddDays(1))));
            Assert.Equal([AccessCodes.MembershipEndsInPast], await RefusedAsync(scope, ben, Window(role, now.AddDays(-2), now.AddDays(-1))));

            // --- Adjacent windows of one role do not overlap ---
            User user = await scope.LoadUserAsync(ben);
            user.RoleMemberships = [.. user.RoleMemberships!, Window(role, now.AddDays(1), now.AddDays(2)), Window(role, now.AddDays(2), null)];
            _ = await scope.SaveUsersAsync(user);
            Assert.Equal(2, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[RoleMemberships] WHERE [UserId] = {ben} AND [RoleId] = {role};")));
        }

        // --- The preference bag ---

        [Fact]
        public async Task The_bag_s_statements_set_delete_and_replace_keys_as_the_database_compares_them_and_move_the_owner_s_tag()
        {
            (int ann, string subject) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, string benSubject) = await fixture.SeedCallerAsync("Ben");
            Guid? preferences = (await fixture.UserTagsAsync(ben)).Preferences;

            // --- The caller's own bag: set, then a change of case alone, then a delete naming the key in another case ---
            await WriteBagAsync(benSubject, static batch => UserPreferenceStatements.Set(batch, null, [Item("theme", "dark"), Item("nav.pinned", "a")], 10));
            Assert.Equal("nav.pinned=a;theme=dark", await BagAsync(ben));
            Assert.NotEqual(preferences, (await fixture.UserTagsAsync(ben)).Preferences);
            await WriteBagAsync(benSubject, static batch => UserPreferenceStatements.Set(batch, null, [Item("THEME", "Dark")], 10));
            await WriteBagAsync(benSubject, static batch => UserPreferenceStatements.Delete(batch, null, ["nav.PINNED", "absent"]));
            Assert.Equal("theme=Dark", await BagAsync(ben));

            // --- Another user's bag, written by an administrator: the owner's tag moves, the writer's does not ---
            (Guid? ownerPermissions, Guid? ownerPreferences) = await fixture.UserTagsAsync(ben);
            (Guid? Permissions, Guid? Preferences) writer = await fixture.UserTagsAsync(ann);
            await WriteBagAsync(subject, batch => UserPreferenceStatements.Replace(batch, ben, [Item("grid.widths", "1,2")], 10));

            Assert.Equal("grid.widths=1,2", await BagAsync(ben));
            Assert.NotEqual(ownerPreferences, (await fixture.UserTagsAsync(ben)).Preferences);
            Assert.Equal(ownerPermissions, (await fixture.UserTagsAsync(ben)).Permissions);
            Assert.Equal(writer, await fixture.UserTagsAsync(ann));
        }

        [Fact]
        public async Task Two_concurrent_writes_of_one_bag_serialise_on_its_owner_s_stamp_row_so_the_key_cap_holds()
        {
            const int Cap = 3;
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) VALUES ({ben}, N'a', N'1'), ({ben}, N'b', N'2');"));

            // --- Two background scopes of the owner, opened before the stamp row is held ---
            await using AsyncServiceScope first = await fixture.ScopeAsync(subject: null, userId: ben);
            await using AsyncServiceScope second = await fixture.ScopeAsync(subject: null, userId: ben);

            // --- Each adds one key to a bag one short of the cap, while the owner's stamp row is held ---
            IReadOnlyList<Task> writes = await fixture.HoldWhileAsync(
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[UserStamps] SET [InboxVersion] = [InboxVersion] WHERE [UserId] = {ben};"),
                2,
                "KEY",
                () => [WriteBagAsync(first, static batch => UserPreferenceStatements.Set(batch, null, [Item("c", "3")], Cap)), WriteBagAsync(second, static batch => UserPreferenceStatements.Set(batch, null, [Item("d", "4")], Cap))]);
            Exception?[] outcomes = [await CaptureAsync(writes[0]), await CaptureAsync(writes[1])];

            // --- One commits; the other, run after it, finds the cap reached ---
            BatchAssertionFailedException refused = Assert.IsType<BatchAssertionFailedException>(Assert.Single(outcomes, static outcome => outcome is not null));
            Assert.Equal("Users.TooManyPreferenceKeys", refused.Code);
            Assert.Equal(Cap, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[UserPreferences]", ben)));
        }

        [Fact]
        public async Task Another_user_s_write_of_a_bag_serialises_with_the_owner_s_own_on_the_owner_s_stamp_row_so_the_key_cap_holds()
        {
            const int Cap = 3;
            (int ann, _) = await fixture.SeedAdministratorAsync("Ann");
            (int ben, _) = await fixture.SeedCallerAsync("Ben");
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) VALUES ({ben}, N'a', N'1'), ({ben}, N'b', N'2');"));

            // --- A background scope of the owner and one of the administrator, opened before the stamp row is held ---
            await using AsyncServiceScope owner = await fixture.ScopeAsync(subject: null, userId: ben);
            await using AsyncServiceScope administrator = await fixture.ScopeAsync(subject: null, userId: ann);

            // --- The owner adds one key to its own bag and the administrator another, while the owner's stamp row is held ---
            // The administrator's statement locks only its own stamp row at the batch's head, so it
            // waits on the owner's row at its first line, before it inserts or counts.
            IReadOnlyList<Task> writes = await fixture.HoldWhileAsync(
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[UserStamps] SET [InboxVersion] = [InboxVersion] WHERE [UserId] = {ben};"),
                2,
                "KEY",
                () => [WriteBagAsync(owner, static batch => UserPreferenceStatements.Set(batch, null, [Item("c", "3")], Cap)), WriteBagAsync(administrator, batch => UserPreferenceStatements.Set(batch, ben, [Item("d", "4")], Cap))]);
            Exception?[] outcomes = [await CaptureAsync(writes[0]), await CaptureAsync(writes[1])];

            // --- One commits; the other, run after it, finds the cap reached ---
            BatchAssertionFailedException refused = Assert.IsType<BatchAssertionFailedException>(Assert.Single(outcomes, static outcome => outcome is not null));
            Assert.Equal("Users.TooManyPreferenceKeys", refused.Code);
            Assert.Equal(Cap, await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Where("SELECT COUNT(*) FROM [core].[UserPreferences]", ben)));
        }

        // --- Fixtures ---

        /// <summary>A query over a user's rows.</summary>
        /// <param name="select">The statement up to its filter.</param>
        /// <param name="id">The key.</param>
        /// <param name="column">The key's column.</param>
        /// <returns>The query.</returns>
        private static string Where(string select, int id, string column = "UserId")
        {
            return string.Create(CultureInfo.InvariantCulture, $"{select} WHERE [{column}] = {id};");
        }

        /// <summary>A delete by query of the one row of an id.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The request.</returns>
        private static DeleteByQueryRequest ById(int id)
        {
            return new DeleteByQueryRequest(string.Create(CultureInfo.InvariantCulture, $"Id = {id}"), null, 1);
        }

        /// <summary>A membership of a role over a window.</summary>
        /// <param name="roleId">The role.</param>
        /// <param name="startsAt">The start.</param>
        /// <param name="endsAt">The end.</param>
        /// <returns>The membership.</returns>
        private static RoleMembership Window(int roleId, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
        {
            return new RoleMembership { RoleId = roleId, StartsAt = startsAt, EndsAt = endsAt };
        }

        /// <summary>A preference item.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>The item.</returns>
        private static UserPreferenceList Item(string key, string value)
        {
            return new UserPreferenceList { Key = key, Value = value };
        }

        /// <summary>Waits for a task, capturing the data layer's refusal.</summary>
        /// <param name="task">The task.</param>
        /// <returns>The exception; null when it succeeded.</returns>
        private static async Task<Exception?> CaptureAsync(Task task)
        {
            try
            {
                await task;
                return null;
            }
            catch (DataAccessException exception)
            {
                return exception;
            }
        }

        /// <summary>Adds memberships to a user and returns the codes of the refusal.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="userId">The user.</param>
        /// <param name="memberships">The memberships added.</param>
        /// <returns>The codes.</returns>
        private static async Task<IReadOnlyList<string>> RefusedAsync(AsyncServiceScope scope, int userId, params RoleMembership[] memberships)
        {
            User user = await scope.LoadUserAsync(userId);
            user.RoleMemberships = [.. user.RoleMemberships!, .. memberships];
            return (await Assert.ThrowsAsync<ValidationException>(() => scope.SaveUsersAsync(user))).Codes();
        }

        /// <summary>Writes a bag in a caller's own scope.</summary>
        /// <param name="subject">The caller.</param>
        /// <param name="write">Appends the statement.</param>
        /// <returns>A task that completes when the write committed.</returns>
        private async Task WriteBagAsync(string subject, Func<IDataBatch, BatchResult<RawResult>> write)
        {
            await using AsyncServiceScope scope = await fixture.SteppedUpScopeAsync(subject);
            await WriteBagAsync(scope, write);
        }

        /// <summary>Writes a bag in a scope, through the guarded runner.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="write">Appends the statement.</param>
        /// <returns>A task that completes when the write committed.</returns>
        private static async Task WriteBagAsync(AsyncServiceScope scope, Func<IDataBatch, BatchResult<RawResult>> write)
        {
            _ = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    _ = write(batch);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
        }

        /// <summary>A user's bag, as its keys and values in key order.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The bag.</returns>
        private Task<string?> BagAsync(int userId)
        {
            return fixture.ScalarAsync<string>(
                ConnectDatabase.TenantA,
                Where("SELECT STRING_AGG(CAST([Key] + N'=' + [Value] AS nvarchar(max)), N';') WITHIN GROUP (ORDER BY [Key]) FROM [core].[UserPreferences]", userId));
        }
    }
}
