// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Caching;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Version tags against a fixture database whose every batch is audited: the prelude heading
    ///     every batch and replacing the process's snapshot, the bump moving a cacheable list's tags
    ///     and the named users' preferences or permissions tags and announcing each move after the
    ///     commit, the platform's save, update and delete naming the users they write themselves, an
    ///     expectation of no tag guarded, no missing-row warning from a batch that writes the tags
    ///     itself, and a missing row failing the bump.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class VersionTagTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        /// <summary>The fixture's cacheable list.</summary>
        private static readonly TableName Lookups = new(FixtureSchema.Name, "Lookups");

        /// <summary>The jobs table, written where a batch must write something.</summary>
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>The users' preference bags, whose writes bump each named user's preferences tag.</summary>
        private static readonly TableName UserPreferences = new("core", "UserPreferences");

        /// <summary>The roles, whose writes bump the tenant's permissions tag.</summary>
        private static readonly TableName Roles = new("core", "Roles");

        /// <summary>The role memberships, whose writes bump each named member's permissions tag.</summary>
        private static readonly TableName RoleMemberships = new("core", "RoleMemberships");

        /// <summary>The tenant-level tags' table, which only the migrator writes directly.</summary>
        private static readonly TableName VersionTags = new("core", "VersionTags");

        /// <summary>The list's own tag.</summary>
        private static readonly string LookupTag = VersionTagNames.ForEntity("fixture.Lookup");

        /// <summary>A statement that declares a write to the list and changes no row.</summary>
        private static readonly FormattableString TouchLookups = $"DELETE FROM [fixture].[Lookups] WHERE 1 = 0;";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
            await fixture.SeedCallerAsync();
            await fixture.SeedTagsAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        // ===================== The prelude and the snapshot =====================

        [Fact]
        public async Task Every_batch_reads_the_tags_after_the_schema_guard_in_its_one_round_trip_and_replaces_the_process_snapshot()
        {
            Dictionary<string, Guid> stored = await fixture.TagsAsync();
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            DateTimeOffset before = DateTimeOffset.UtcNow;
            fixture.ResetRoundTrips();

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            Assert.StartsWith(
                string.Join("\n", BatchAssembly.SessionSettings, BatchAssembly.SchemaGuardText, VersionTagPrelude.Text),
                batch.Assemble().Text,
                StringComparison.Ordinal);
            Assert.Equal((1, 1L), (outcome.RoundTrips, fixture.ServerRoundTrips));
            Assert.Equal(stored, outcome.VersionTags.Tags.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value));
            Assert.True(outcome.VersionTags.ReadAtUtc >= before.AddSeconds(-1));
            Assert.Same(outcome.VersionTags, fixture.Tenant.Services.GetRequiredService<IVersionTagSnapshots>().Current(FixtureTenant.TenantId));
            Assert.Null(outcome.UserVersionTags);
        }

        // ===================== The bump =====================

        [Fact]
        public async Task A_write_to_a_cacheable_list_moves_its_tag_and_the_entities_tag_alone_and_announces_the_list_after_the_commit()
        {
            Dictionary<string, Guid> before = await fixture.TagsAsync();
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = batch.Sql(TouchLookups, new SqlOptions(writes: Set(Lookups)));
            fixture.ResetRoundTrips();

            BatchOutcome outcome = await batch.ExecuteAsync(Token);
            long roundTrips = fixture.ServerRoundTrips;

            // --- The database: the two names hold the batch's tag; nothing else moved ---
            Dictionary<string, Guid> after = await fixture.TagsAsync();
            Assert.Equal(batch.Tag, after[LookupTag]);
            Assert.Equal(batch.Tag, after[VersionTagNames.Entities]);
            Assert.Equal(before[VersionTagNames.Settings], after[VersionTagNames.Settings]);
            Assert.Equal(before[VersionTagNames.Permissions], after[VersionTagNames.Permissions]);

            // --- The snapshot is the prelude's rows overlaid with the bump, with no read-back ---
            Assert.Equal((1, 1L), (outcome.RoundTrips, roundTrips));
            Assert.Equal(after, outcome.VersionTags.Tags.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value));
            Assert.Same(outcome.VersionTags, fixture.Tenant.Services.GetRequiredService<IVersionTagSnapshots>().Current(FixtureTenant.TenantId));

            // --- One announcement, to the tenant, of the list alone ---
            ClientEvent announced = Assert.Single(fixture.Events.Published);
            Assert.Equal(VersionTagBumpContributor.CacheChangedEvent, announced.Name);
            Assert.Empty(announced.UserIds);
            Assert.Equal(new CacheChangedPayload(LookupTag, VersionTagWire.Entities(new VersionTag(batch.Tag))), announced.Payload);
        }

        [Fact]
        public async Task An_explicit_bump_moves_the_named_tag_and_tells_every_client_the_new_settings()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", new SqlOptions(writes: Set(Jobs)));
            batch.BumpVersionTag(VersionTagNames.Settings);

            _ = await batch.ExecuteAsync(Token);

            Assert.Equal(batch.Tag, (await fixture.TagsAsync())[VersionTagNames.Settings]);
            ClientEvent announced = Assert.Single(fixture.Events.Published);
            Assert.Equal(new CacheChangedPayload(VersionTagNames.Settings, VersionTagWire.Settings(new VersionTag(batch.Tag))), announced.Payload);
        }

        [Fact]
        public async Task A_write_naming_users_moves_their_preferences_tag_alone_and_tells_each_of_them()
        {
            int other = DataBatchDatabase.CallerId + 1;
            Guid callerPermissions = await StampAsync("PermissionsTag", DataBatchDatabase.CallerId);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            SqlIdentifier users = batch.Tvp<IdList>([new() { Id = DataBatchDatabase.CallerId }, new() { Id = other }]);
            _ = batch.Sql($"DELETE FROM [core].[UserPreferences] WHERE [UserId] IN (SELECT [Id] FROM {users});", new SqlOptions(writes: Set(UserPreferences), userIds: users));

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            // The caller's stamp moved; the other user has no stamp to move but is still told.
            Assert.Equal(batch.Tag, await StampAsync("PreferencesTag", DataBatchDatabase.CallerId));
            Assert.Equal(callerPermissions, await StampAsync("PermissionsTag", DataBatchDatabase.CallerId));
            Assert.Null(outcome.UserVersionTags);
            Assert.Equal(
                [(DataBatchDatabase.CallerId, new CacheChangedPayload(VersionTagWire.PreferencesItem, VersionTagWire.Preferences(new VersionTag(batch.Tag)))), (other, new CacheChangedPayload(VersionTagWire.PreferencesItem, VersionTagWire.Preferences(new VersionTag(batch.Tag))))],
                fixture.Events.Published.Select(static clientEvent => (Assert.Single(clientEvent.UserIds), (CacheChangedPayload)clientEvent.Payload!)).OrderBy(static pair => pair.Item1));
        }

        [Fact]
        public async Task A_callers_write_to_their_own_rows_moves_their_tag_into_the_callers_snapshot()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            _ = batch.Sql($"DELETE FROM [core].[UserPreferences] WHERE [UserId] = {DataBatchDatabase.CallerId};", SqlOptions.ForCaller(Set(UserPreferences)));

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            Assert.True(batch.GuardPassed);
            UserVersionTagSnapshot user = Assert.IsType<UserVersionTagSnapshot>(outcome.UserVersionTags);
            Assert.Equal((DataBatchDatabase.CallerId, batch.Tag, DataBatchDatabase.UserPermissionsTag), (user.UserId, user.Preferences.Value, user.Permissions.Value));
            Assert.Same(user, fixture.Tenant.Services.GetRequiredService<IUserVersionTagSnapshots>().Current(FixtureTenant.TenantId, DataBatchDatabase.CallerId));
            Assert.Equal(batch.Tag, await StampAsync("PreferencesTag", DataBatchDatabase.CallerId));
        }

        [Fact]
        public async Task A_role_save_naming_its_administrator_and_another_member_moves_the_permissions_tags_of_the_tenant_and_both()
        {
            int other = DataBatchDatabase.CallerId + 1;
            await fixture.SeedUserAsync(other);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            batch.SetCallerAuthority(Set(Roles, RoleMemberships));
            SqlIdentifier members = batch.Tvp<IdList>([new() { Id = DataBatchDatabase.CallerId }, new() { Id = other }]);
            _ = batch.Sql($"DELETE FROM [core].[Roles] WHERE 1 = 0;", new SqlOptions(writes: Set(Roles)));
            _ = batch.Sql($"DELETE FROM [core].[RoleMemberships] WHERE [UserId] IN (SELECT [Id] FROM {members});", new SqlOptions(writes: Set(RoleMemberships), userIds: members));

            BatchOutcome outcome = await batch.ExecuteAsync(Token);

            // --- The tenant's permissions tag and both members' stamps hold the batch's tag ---
            Assert.True(batch.GuardPassed);
            VersionTag tag = new(batch.Tag);
            Assert.Equal(batch.Tag, (await fixture.TagsAsync())[VersionTagNames.Permissions]);
            Assert.Equal(batch.Tag, await StampAsync("PermissionsTag", DataBatchDatabase.CallerId));
            Assert.Equal(batch.Tag, await StampAsync("PermissionsTag", other));
            Assert.NotEqual(batch.Tag, await StampAsync("PreferencesTag", other));

            // --- The administrator's own snapshot carries the move, with no read-back ---
            Assert.Equal(tag, outcome.VersionTags[VersionTagNames.Permissions]);
            UserVersionTagSnapshot user = Assert.IsType<UserVersionTagSnapshot>(outcome.UserVersionTags);
            Assert.Equal((DataBatchDatabase.CallerId, tag), (user.UserId, user.Permissions));

            // --- The tenant hears that permissions moved, without a value; each member hears its composite ---
            CacheChangedPayload composite = new(VersionTagWire.PermissionsItem, VersionTagWire.Permissions(tag, tag));
            Assert.Equal(
                [(0, new CacheChangedPayload(VersionTagNames.Permissions, null)), (DataBatchDatabase.CallerId, composite), (other, composite)],
                fixture.Events.Published.Select(static clientEvent => (clientEvent.UserIds.SingleOrDefault(), (CacheChangedPayload)clientEvent.Payload!)).OrderBy(static pair => pair.Item1));
        }

        // ===================== The platform statements name the users they write =====================

        [Fact]
        public async Task A_user_save_that_adds_then_removes_a_membership_moves_that_users_tags_alone_and_tells_that_user_alone()
        {
            int member = DataBatchDatabase.CallerId + 1;
            await fixture.SeedUserAsync(member);
            int role = await SeedRoleAsync();
            (Guid Permissions, Guid Preferences) caller = await StampsAsync(DataBatchDatabase.CallerId);

            // --- Adding: the new membership's row names the member, the stamped user row too ---
            User user = await StoredUserAsync(member);
            user.RoleMemberships = [new RoleMembership { RoleId = role }];
            Guid added = await SaveAsync(user);

            Assert.Equal((added, added), await StampsAsync(member));
            Assert.Equal(caller, await StampsAsync(DataBatchDatabase.CallerId));
            Assert.Equal([(member, VersionTagWire.PermissionsItem), (member, VersionTagWire.PreferencesItem)], UserEvents());

            // --- Removing: the deleted membership's captured row names the member ---
            fixture.Events.Clear();
            user = await StoredUserAsync(member);
            user.RoleMemberships = [];
            Guid removed = await SaveAsync(user);

            Assert.Equal(0, await MembershipCountAsync());
            Assert.Equal((removed, removed), await StampsAsync(member));
            Assert.Equal(caller, await StampsAsync(DataBatchDatabase.CallerId));
            Assert.Equal([(member, VersionTagWire.PermissionsItem), (member, VersionTagWire.PreferencesItem)], UserEvents());
        }

        [Fact]
        public async Task A_profile_only_user_save_moves_that_users_preferences_tag_alone()
        {
            int member = DataBatchDatabase.CallerId + 1;
            await fixture.SeedUserAsync(member);
            (Guid permissions, _) = await StampsAsync(member);

            User user = await StoredUserAsync(member);
            user.Name = "Renamed";
            Guid saved = await SaveAsync(user);

            Assert.Equal((permissions, saved), await StampsAsync(member));
            Assert.Equal([(member, VersionTagWire.PreferencesItem)], UserEvents());
        }

        [Fact]
        public async Task A_user_update_moves_the_updated_users_preferences_tag_alone()
        {
            int member = DataBatchDatabase.CallerId + 1;
            await fixture.SeedUserAsync(member);
            (Guid Permissions, Guid Preferences) caller = await StampsAsync(DataBatchDatabase.CallerId);
            Guid permissions = (await StampsAsync(member)).Permissions;

            Guid tag = await RunAsync(batch => _ = batch.Update(new UpdateSpec<User>.ByIds([member], null, null, new Dictionary<string, object?> { [nameof(User.IsActive)] = false })));

            Assert.Equal((permissions, tag), await StampsAsync(member));
            Assert.Equal(caller, await StampsAsync(DataBatchDatabase.CallerId));
            Assert.Equal([(member, VersionTagWire.PreferencesItem)], UserEvents());
        }

        [Fact]
        public async Task A_user_delete_takes_the_memberships_and_tells_the_deleted_user_alone()
        {
            int member = DataBatchDatabase.CallerId + 1;
            await fixture.SeedUserAsync(member);
            int role = await SeedRoleAsync();
            User user = await StoredUserAsync(member);
            user.RoleMemberships = [new RoleMembership { RoleId = role }];
            _ = await SaveAsync(user);
            fixture.Events.Clear();
            (Guid Permissions, Guid Preferences) caller = await StampsAsync(DataBatchDatabase.CallerId);

            _ = await RunAsync(batch => _ = batch.Delete(new DeleteSpec<User>.ByIds([member], null, null)));

            Assert.Equal(0, await MembershipCountAsync());
            Assert.Equal(caller, await StampsAsync(DataBatchDatabase.CallerId));
            Assert.Equal([(member, VersionTagWire.PermissionsItem), (member, VersionTagWire.PreferencesItem)], UserEvents());
        }

        [Fact]
        public async Task A_dependency_that_expects_no_tag_fails_stale_once_the_row_exists()
        {
            // A kind read while its row was missing is expected as no tag; the row seeded since moves it.
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            batch.DependsOn([new VersionTagDependency(LookupTag, VersionTag.None, VersionTagMismatchPolicy.Rerun)]);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES (NEXT VALUE FOR [core].[sq_Jobs], N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());", new SqlOptions(writes: Set(Jobs)));

            BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            Assert.Equal(TellmaSqlErrors.StaleVersionTag, refused.Number);
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_batch_that_writes_the_tags_itself_warns_of_no_missing_row_and_a_later_read_does()
        {
            // No other test of the class reads the tags while the entities row is missing, so the
            // once-per-process warning is still due for it.
            string deleteEntities = $"DELETE FROM [core].[VersionTags] WHERE [Name] = N'{VersionTagNames.Entities}';";
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, deleteEntities);

            // --- The migrator's seed reads the table before it inserts the row: no warning ---
            await using (AsyncServiceScope scope = fixture.Tenant.CreateScope())
            {
                IDataBatch seed = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
                _ = seed.Sql($"INSERT INTO [core].[VersionTags] ([Name], [Tag]) VALUES ({VersionTagNames.Entities}, NEWID());", new SqlOptions(writes: Set(VersionTags)));
                _ = await seed.ExecuteAsync(Token);
            }

            Assert.DoesNotContain(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "VersionTagRowMissing");

            // --- A read that finds the row missing warns ---
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, deleteEntities);
            await using (AsyncServiceScope scope = fixture.Tenant.CreateScope())
            {
                IDataBatch read = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
                _ = read.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
                _ = await read.ExecuteAsync(Token);
            }

            Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "VersionTagRowMissing");
        }

        [Fact]
        public async Task A_bump_of_a_name_the_database_lacks_fails_the_batch_rolls_it_back_and_is_metered()
        {
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, $"DELETE FROM [core].[VersionTags] WHERE [Name] = N'{LookupTag}';");
            using MetricCollector<long> missing = new(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.Missing);
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = batch.Sql($"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES (NEXT VALUE FOR [core].[sq_Jobs], N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());", new SqlOptions(writes: Set(Jobs)));
            _ = batch.Sql(TouchLookups, new SqlOptions(writes: Set(Lookups)));

            InvalidOperationException failed = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(Token));

            SqlException sql = Assert.IsType<SqlException>(failed.InnerException);
            Assert.Equal((TellmaSqlErrors.Invariant, TellmaSqlErrors.VersionTagMissingCode), (sql.Number, sql.Message));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Equal(["entity"], missing.GetMeasurementSnapshot().Select(static m => (string)m.Tags[CacheTelemetryNames.TagKindTag]!));
            Assert.Empty(fixture.Events.Published);

            // The failed batch's prelude still replaced the snapshot, which lacks the row.
            Assert.False(fixture.Tenant.Services.GetRequiredService<IVersionTagSnapshots>().Current(FixtureTenant.TenantId).Has(LookupTag));
        }

        /// <summary>A set of tables.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The set.</returns>
        private static HashSet<TableName> Set(params TableName[] tables)
        {
            return [.. tables];
        }

        /// <summary>Stores an active role above the reserved band.</summary>
        /// <returns>The role's id.</returns>
        private async Task<int> SeedRoleAsync()
        {
            const int role = WellKnownIds.ReservedIdBandEnd + 1;
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[Roles] ([Id], [Name], [IsPublic], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({role}, N'Clerks', 0, 1, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId});"));
            return role;
        }

        /// <summary>How many role memberships the database holds.</summary>
        /// <returns>The count.</returns>
        private Task<int> MembershipCountAsync()
        {
            return SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[RoleMemberships];");
        }

        /// <summary>
        ///     A stored user as a save expects it: the columns the seed wrote, and the stamps the
        ///     concurrency check compares.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <returns>The user.</returns>
        private async Task<User> StoredUserAsync(int userId)
        {
            string where = string.Create(CultureInfo.InvariantCulture, $" FROM [core].[Users] WHERE [Id] = {userId};");
            return new User
            {
                Id = userId,
                Kind = UserKind.Individual,
                Email = await SqlServerFixture.ScalarAsync<string>(fixture.Tenant.ConnectionString, "SELECT [Email]" + where),
                Name = await SqlServerFixture.ScalarAsync<string>(fixture.Tenant.ConnectionString, "SELECT [Name]" + where) ?? string.Empty,
                IsActive = true,
                CreatedAt = await SqlServerFixture.ScalarAsync<DateTimeOffset>(fixture.Tenant.ConnectionString, "SELECT [CreatedAt]" + where),
                CreatedById = WellKnownIds.SystemUserId,
                ModifiedAt = await SqlServerFixture.ScalarAsync<DateTimeOffset>(fixture.Tenant.ConnectionString, "SELECT [ModifiedAt]" + where),
                ModifiedById = WellKnownIds.SystemUserId,
            };
        }

        /// <summary>
        ///     Saves a user through the platform's save statement, ids reserved for its new memberships
        ///     first, in a persist batch that declares no users of its own.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>The persist batch's tag.</returns>
        private async Task<Guid> SaveAsync(User user)
        {
            await using AsyncServiceScope scope = fixture.CreateSettledScope(WellKnownIds.SystemUserId);
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
            IDataBatch validate = database.CreateBatch(BatchPurpose.Validate);
            using IdReservation reservation = scope.ServiceProvider.GetRequiredService<IIdAllocator>().Reserve(validate, typeof(User), [user]);
            if (!reservation.IsImmediate)
            {
                _ = await validate.ExecuteAsync(Token);
            }

            reservation.Assign([user]);
            var persist = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
            _ = persist.Save([user]);
            _ = await persist.ExecuteAsync(Token);
            return persist.Tag;
        }

        /// <summary>Runs a persist batch holding one platform statement, in a settled scope of the system user.</summary>
        /// <param name="write">Adds the statement.</param>
        /// <returns>The batch's tag.</returns>
        private async Task<Guid> RunAsync(Action<DataBatch> write)
        {
            await using AsyncServiceScope scope = fixture.CreateSettledScope(WellKnownIds.SystemUserId);
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            write(batch);
            _ = await batch.ExecuteAsync(Token);
            return batch.Tag;
        }

        /// <summary>Each announcement addressed to a user, as the user and the item it moved, ordered.</summary>
        /// <returns>The announcements.</returns>
        private List<(int UserId, string Item)> UserEvents()
        {
            return [.. fixture.Events.Published
                .Where(static clientEvent => clientEvent.UserIds.Count > 0)
                .Select(static clientEvent => (UserId: Assert.Single(clientEvent.UserIds), Item: ((CacheChangedPayload)clientEvent.Payload!).Tag))
                .OrderBy(static pair => pair.UserId)
                .ThenBy(static pair => pair.Item, StringComparer.Ordinal)];
        }

        /// <summary>A user's permissions and preferences tags.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The tags.</returns>
        private async Task<(Guid Permissions, Guid Preferences)> StampsAsync(int userId)
        {
            return (await StampAsync("PermissionsTag", userId), await StampAsync("PreferencesTag", userId));
        }

        /// <summary>One tag column of a user's stamp.</summary>
        /// <param name="column">The column.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> StampAsync(string column, int userId)
        {
            return await SqlServerFixture.ScalarAsync<Guid>(
                fixture.Tenant.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"SELECT [{column}] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }
    }
}
