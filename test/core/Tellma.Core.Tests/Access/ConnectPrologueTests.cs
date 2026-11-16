// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;
using Tellma.Core.Settings;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The connect prologue with no database: the three texts and their parameters, each the
    ///     same for every batch of its variant, and the reader against scripted result sets in every
    ///     combination of the sets that are present only sometimes.
    /// </summary>
    public sealed class ConnectPrologueTests : IDisposable
    {
        /// <summary>The settings tag the store holds current, unless a test empties it.</summary>
        private static readonly VersionTag SettingsTag = new(Guid.Parse("5e77a000-0000-0000-0000-000000000001"));

        /// <summary>The tenant-level permissions tag the scripted tag set returns.</summary>
        private static readonly Guid PermissionsTag = Guid.Parse("9e40a000-0000-0000-0000-000000000002");

        /// <summary>The user's preferences tag the scripted connect row returns.</summary>
        private static readonly Guid PreferencesTag = Guid.Parse("9ef0a000-0000-0000-0000-000000000003");

        /// <summary>The user's permissions tag the scripted connect row returns.</summary>
        private static readonly Guid UserPermissionsTag = Guid.Parse("05e40000-0000-0000-0000-000000000004");

        /// <summary>A membership window's start in the scripted rows.</summary>
        private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>A membership window's end in the scripted rows.</summary>
        private static readonly DateTimeOffset End = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>The harness over the default composition.</summary>
        private readonly BatchHarness _harness = new();

        /// <summary>The scope the settings cold load is resolved in.</summary>
        private readonly IServiceScope _scope;

        /// <summary>Creates the harness, with the tenant's settings current in the store.</summary>
        public ConnectPrologueTests()
        {
            _scope = _harness.Services.CreateScope();
            StoreSettings(current: true);
        }

        /// <summary>Every combination of the connect row's flags that select the optional sets.</summary>
        public static TheoryData<bool, bool, bool, bool> Combinations
        {
            get
            {
                bool[] flags = [false, true];
                TheoryData<bool, bool, bool, bool> data = [];
                foreach (bool permissions in flags)
                {
                    foreach (bool profile in flags)
                    {
                        foreach (bool settings in flags)
                        {
                            foreach (bool guard in flags)
                            {
                                data.Add(permissions, profile, settings, guard);
                            }
                        }
                    }
                }

                return data;
            }
        }

        /// <summary>The settings cold load the prologue absorbs.</summary>
        private SettingsColdLoadContributor ColdLoad => _scope.ServiceProvider.GetServices<IDataBatchContributor>().OfType<SettingsColdLoadContributor>().Single();

        /// <inheritdoc />
        public void Dispose()
        {
            _scope.Dispose();
            _harness.Dispose();
        }

        // --- The texts and their parameters ---

        [Fact]
        public void Every_batch_of_a_variant_carries_the_same_text_and_binds_the_same_parameters_with_the_same_types()
        {
            ConnectExpectations warm = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            ConnectPremises[][] variants =
            [
                [ConnectPremises.Cold("sub-1", Guid.Empty, stampActivity: true, allowStateFlip: true), new ConnectPremises.ForSubject("sub-2", 7, warm, false, false, true)],
                [new ConnectPremises.ForUser(7, new ConnectExpectations(null, null, null, Guid.Empty), false), new ConnectPremises.ForUser(9, warm, true)],
                [new ConnectPremises.ForSystem(Guid.Empty), new ConnectPremises.ForSystem(Guid.NewGuid())],
            ];

            foreach (ConnectPremises[] variant in variants)
            {
                BatchStatement first = Prologue(variant[0]).Statements.Single();
                BatchStatement second = Prologue(variant[1]).Statements.Single();
                Assert.Equal(first.Text, second.Text);
                Assert.Equal(Shape(first), Shape(second));
                Assert.Equal(BatchSection.Prologue, first.Section);
                Assert.True(first.Idempotent);
            }
        }

        [Fact]
        public void The_subject_variant_binds_the_subject_and_the_bookkeeping_flags_and_resolves_by_subject()
        {
            BatchStatement statement = Prologue(ConnectPremises.Cold("sub-1", Guid.Empty, stampActivity: true, allowStateFlip: false)).Statements.Single();

            Assert.StartsWith("DECLARE @tm_UserId int,", statement.Text, StringComparison.Ordinal);
            Assert.Contains("WHERE U.[Subject] = @tm_Subject;", statement.Text, StringComparison.Ordinal);
            Assert.Contains("UPDATE [core].[UserStamps] SET [LastActiveAt] = @tm_Now", statement.Text, StringComparison.Ordinal);
            Assert.Contains("INSERT INTO @tm_callerIds ([Id]) VALUES (@tm_UserId);", statement.Text, StringComparison.Ordinal);
            Assert.Equal(
                [
                    "@tm_AllowFlip:Bit", "@tm_ExpectedPermissionsTag:UniqueIdentifier", "@tm_ExpectedPreferencesTag:UniqueIdentifier",
                    "@tm_ExpectedSettingsTag:UniqueIdentifier", "@tm_ExpectedUserId:Int", "@tm_ExpectedUserPermissionsTag:UniqueIdentifier",
                    "@tm_LoadSettings:Bit", "@tm_ReloadPermissions:Bit", "@tm_SettingsStrict:Bit", "@tm_StampActivity:Bit", "@tm_Subject:VarChar",
                ],
                Shape(statement));
            Assert.True(Value(statement, ReservedSqlNames.StampActivity) is true);
            Assert.True(Value(statement, ReservedSqlNames.AllowFlip) is false);
            Assert.Null(Value(statement, ReservedSqlNames.ExpectedUserId));
            Assert.Equal(255, ((ScalarParameter)statement.Parameters.Single(static parameter => parameter.Name == ReservedSqlNames.Subject)).Size);
        }

        [Fact]
        public void The_user_variant_binds_the_user_id_and_neither_stamps_nor_flips()
        {
            BatchStatement statement = Prologue(new ConnectPremises.ForUser(7, new ConnectExpectations(null, null, null, Guid.Empty), false)).Statements.Single();

            Assert.StartsWith("DECLARE\n", statement.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("DECLARE @tm_UserId", statement.Text, StringComparison.Ordinal);
            Assert.Contains("WHERE U.[Id] = @tm_UserId;\nIF @tm_Kind IS NULL SET @tm_UserId = NULL;", statement.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("UPDATE", statement.Text, StringComparison.Ordinal);
            Assert.Equal(7, Value(statement, ReservedSqlNames.UserId));
            Assert.Equal(7, Value(statement, ReservedSqlNames.ExpectedUserId));
            Assert.DoesNotContain(statement.Parameters, static parameter => parameter.Name is ReservedSqlNames.Subject or ReservedSqlNames.StampActivity or ReservedSqlNames.AllowFlip);
            Assert.Empty(statement.Writes);
        }

        [Theory]
        [InlineData(BatchPurpose.Read, false)]
        [InlineData(BatchPurpose.Validate, true)]
        [InlineData(BatchPurpose.Persist, true)]
        public void The_system_variant_declares_the_system_user_and_fails_its_guard_only_on_strict_stale_settings(BatchPurpose purpose, bool strict)
        {
            var expected = Guid.NewGuid();
            BatchStatement statement = Prologue(new ConnectPremises.ForSystem(expected), purpose).Statements.Single();

            Assert.StartsWith("DECLARE @tm_UserId int = 1, @tm_SettingsTag uniqueidentifier, @tm_SettingsStale bit = 0, @tm_Guard bit = 0;", statement.Text, StringComparison.Ordinal);
            Assert.Contains("SET @tm_Guard = CASE WHEN @tm_SettingsStrict = 0 OR @tm_SettingsStale = 0 THEN 1 ELSE 0 END;", statement.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("[core].[Users]", statement.Text, StringComparison.Ordinal);
            Assert.Equal(["@tm_ExpectedSettingsTag:UniqueIdentifier", "@tm_LoadSettings:Bit", "@tm_SettingsStrict:Bit"], Shape(statement));
            Assert.Equal(strict, Value(statement, ReservedSqlNames.SettingsStrict));
            Assert.Equal(expected, Value(statement, ReservedSqlNames.ExpectedSettingsTag));
        }

        [Fact]
        public void The_bookkeeping_writes_are_audited_but_never_count_among_the_batchs_writes()
        {
            DataBatch batch = Prologue(ConnectPremises.Cold("sub-1", Guid.Empty, stampActivity: true, allowStateFlip: true), BatchPurpose.Persist);

            Assert.Empty(batch.WrittenTables);
            Assert.Equal(["[core].[UserStamps]", "[core].[Users]"], batch.AuditedWrites.Select(static table => table.Delimited).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task An_explicit_transaction_is_refused_on_a_batch_whose_prologue_may_stamp_and_allowed_otherwise()
        {
            DataBatch stamping = Prologue(ConnectPremises.Cold("sub-1", Guid.Empty, stampActivity: false, allowStateFlip: false));
            stamping.TransactionMode = TransactionMode.Explicit;
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => stamping.ExecuteAsync(TestContext.Current.CancellationToken));
            Assert.Contains("activity stamp", refused.Message, StringComparison.Ordinal);

            DataBatch byId = Prologue(new ConnectPremises.ForUser(7, new ConnectExpectations(null, null, null, Guid.Empty), false));
            byId.TransactionMode = TransactionMode.Explicit;
            _ = await BatchHarness.SealAsync(byId);
        }

        [Theory]
        [InlineData("sub-1", true)]
        [InlineData("a|b~c!", true)]
        [InlineData("", false)]
        [InlineData("two words", false)]
        [InlineData("café", false)]
        [InlineData("tab\there", false)]
        public void A_subject_is_bound_only_when_the_column_could_hold_it_byte_for_byte(string subject, bool bindable)
        {
            Assert.Equal(bindable, UserConnector.IsBindable(subject));
        }

        [Fact]
        public void A_subject_longer_than_the_column_is_never_bound()
        {
            Assert.True(UserConnector.IsBindable(new string('a', 255)));
            Assert.False(UserConnector.IsBindable(new string('a', 256)));
        }

        // --- The reader ---

        [Theory]
        [MemberData(nameof(Combinations))]
        public async Task The_reader_reads_exactly_the_sets_the_connect_row_announces(bool permissionsStale, bool profileStale, bool settingsStale, bool guard)
        {
            DataBatch batch = Batch(BatchPurpose.Read);
            BatchResult<ConnectResult> handle = ConnectPrologue.Append(
                batch, new ConnectPremises.ForSubject("sub-1", 7, new ConnectExpectations(PermissionsTag, UserPermissionsTag, PreferencesTag, SettingsTag.Value), true, true, false), ColdLoad, Reading());
            AppendBody(batch);
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            List<DataTable> sets = [ConnectRow(7, "Individual", true, "Joined", permissionsStale, profileStale, settingsStale, guard), Tags()];
            if (permissionsStale)
            {
                sets.Add(PermissionRows());
            }

            if (profileStale)
            {
                sets.Add(Profile());
            }

            if (settingsStale)
            {
                sets.AddRange(Settings());
            }

            if (guard)
            {
                sets.Add(Body());
            }

            (bool ran, ResultSetCursor cursor) = await ReadAsync(assembly, batch, sets);

            ConnectResult result = handle.Value;
            Assert.Equal(guard, ran);
            Assert.Equal(guard, result.GuardPassed);
            Assert.Equal(guard, batch.GuardPassed);
            Assert.Equal(sets.Count, cursor.SetsRead);
            Assert.Equal(7, result.UserId);
            Assert.Equal(UserKind.Individual, result.Kind);
            Assert.Equal(UserState.Joined, result.State);
            Assert.True(result.IsActive);
            Assert.Equal(permissionsStale, result.PermissionsStale);
            Assert.Equal(profileStale, result.ProfileStale);
            Assert.Equal(settingsStale, result.SettingsStale);
            Assert.Equal(permissionsStale ? 2 : null, result.PermissionRows?.Count);
            Assert.Equal(profileStale ? "Ada" : null, result.Profile?.Name);
            Assert.Equal(PermissionsTag, result.TenantTags[VersionTagNames.Permissions]);
            Assert.Equal(new VersionTag(PermissionsTag), batch.ReadVersionTags![VersionTagNames.Permissions]);
            UserVersionTagSnapshot userTags = Assert.IsType<UserVersionTagSnapshot>(batch.ReadUserVersionTags);
            Assert.Equal((1, 7, new VersionTag(PreferencesTag), new VersionTag(UserPermissionsTag)), (userTags.TenantId, userTags.UserId, userTags.Preferences, userTags.Permissions));
        }

        [Fact]
        public async Task The_reader_reads_the_permission_rows_a_reload_asked_for_although_the_tags_match()
        {
            DataBatch batch = Batch(BatchPurpose.Read);
            BatchResult<ConnectResult> handle = ConnectPrologue.Append(
                batch, new ConnectPremises.ForSubject("sub-1", 7, new ConnectExpectations(PermissionsTag, UserPermissionsTag, PreferencesTag, SettingsTag.Value), false, false, true), ColdLoad, Reading());
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            (_, ResultSetCursor cursor) = await ReadAsync(assembly, batch, [ConnectRow(7, "Individual", true, "Joined", false, false, false, true), Tags(), PermissionRows()]);

            Assert.Equal(3, cursor.SetsRead);
            Assert.False(handle.Value.PermissionsStale);
            Assert.Equal(2, handle.Value.PermissionRows!.Count);
            PermissionRow windowed = handle.Value.PermissionRows[1];
            Assert.Equal(new PermissionRow(12, 4, "Clerks", false, "core.User", "Read", "IsActive", 1, Start, End), windowed);
        }

        [Fact]
        public async Task The_reader_reads_the_settings_the_load_asked_for_although_they_are_not_stale()
        {
            StoreSettings(current: false);
            DataBatch batch = Batch(BatchPurpose.Read);
            BatchResult<ConnectResult> handle = ConnectPrologue.Append(batch, ConnectPremises.Cold("sub-1", Guid.Empty, false, false), ColdLoad, Reading());
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);
            Assert.True(Value(batch.Statements.Single(), ReservedSqlNames.LoadSettings) is true);

            List<DataTable> sets = [ConnectRow(null, null, false, null, false, false, false, false), Tags(), .. Settings()];
            (bool ran, ResultSetCursor cursor) = await ReadAsync(assembly, batch, sets);

            Assert.False(ran);
            Assert.Equal(4, cursor.SetsRead);
            Assert.Null(handle.Value.UserId);
            Assert.False(handle.Value.IsActive);
            Assert.Null(batch.ReadUserVersionTags);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task An_inactive_users_row_brings_neither_rows_nor_profile_and_reports_its_tags(bool stale)
        {
            DataBatch batch = Batch(BatchPurpose.Read);
            BatchResult<ConnectResult> handle = ConnectPrologue.Append(batch, ConnectPremises.Cold("sub-1", SettingsTag.Value, false, false), ColdLoad, Reading());
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            // The flags are computed only for an active caller; a stale-looking row still brings nothing.
            (_, ResultSetCursor cursor) = await ReadAsync(assembly, batch, [ConnectRow(7, "Individual", false, "Joined", stale, stale, false, false), Tags()]);

            Assert.Equal(2, cursor.SetsRead);
            Assert.False(handle.Value.IsActive);
            Assert.Null(handle.Value.PermissionRows);
            Assert.Null(handle.Value.Profile);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task The_system_variant_reads_its_constant_row_and_the_settings_only_when_stale_and_never_reports_user_tags(bool settingsStale)
        {
            DataBatch batch = Batch(BatchPurpose.Validate);
            BatchResult<ConnectResult> handle = ConnectPrologue.Append(batch, new ConnectPremises.ForSystem(SettingsTag.Value), ColdLoad, Reading());
            AppendBody(batch);
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            List<DataTable> sets = [ConnectRow(1, "System", true, null, false, false, settingsStale, !settingsStale), Tags()];
            if (settingsStale)
            {
                sets.AddRange(Settings());
            }
            else
            {
                sets.Add(Body());
            }

            (bool ran, ResultSetCursor cursor) = await ReadAsync(assembly, batch, sets);

            Assert.Equal(!settingsStale, ran);
            Assert.Equal(sets.Count, cursor.SetsRead);
            Assert.Equal(UserKind.System, handle.Value.Kind);
            Assert.Null(handle.Value.State);
            Assert.Null(batch.ReadUserVersionTags);
        }

        [Fact]
        public async Task Another_users_tags_are_never_reported_as_the_callers()
        {
            DataBatch batch = Batch(BatchPurpose.Read);
            _ = ConnectPrologue.Append(batch, new ConnectPremises.ForUser(9, new ConnectExpectations(null, null, null, SettingsTag.Value), false), ColdLoad, Reading(reportUserTags: false));
            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            _ = await ReadAsync(assembly, batch, [ConnectRow(9, "Individual", true, "Joined", true, true, false, false), Tags(), PermissionRows(), Profile()]);

            Assert.Null(batch.ReadUserVersionTags);
        }

        // --- Helpers ---

        /// <summary>The reading options of a test.</summary>
        /// <param name="reportUserTags">Whether the user's tags are the caller's.</param>
        /// <returns>The options.</returns>
        private static ConnectReading Reading(bool reportUserTags = true)
        {
            return new ConnectReading(TimeProvider.System, reportUserTags);
        }

        /// <summary>Each parameter of a statement as its name and SQL type, in name order.</summary>
        /// <param name="statement">The statement.</param>
        /// <returns>The shape.</returns>
        private static string[] Shape(BatchStatement statement)
        {
            return [.. statement.Parameters.Cast<ScalarParameter>().Select(static parameter => $"{parameter.Name}:{parameter.Type}").Order(StringComparer.Ordinal)];
        }

        /// <summary>The value a statement binds to a parameter.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The value.</returns>
        private static object? Value(BatchStatement statement, string name)
        {
            return ((ScalarParameter)statement.Parameters.Single(parameter => parameter.Name == name)).Value;
        }

        /// <summary>Appends a body statement that reads one set; its read shows in the cursor's set count.</summary>
        /// <param name="batch">The batch.</param>
        private static void AppendBody(DataBatch batch)
        {
            StatementDraft draft = batch.Begin();
            int ordinal = draft.Ordinal;
            batch.Append(draft, "SELECT 1 AS [Body];", reader: async (cursor, cancellationToken) =>
            {
                DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken);
                while (await set.ReadAsync(cancellationToken))
                {
                    cursor.CountRows(1);
                }
            });
        }

        /// <summary>Hands scripted sets to the batch's statements and completes their results.</summary>
        /// <param name="assembly">The sealed batch's assembly.</param>
        /// <param name="batch">The batch.</param>
        /// <param name="sets">The sets, in order.</param>
        /// <returns>Whether the body ran, and the cursor.</returns>
        private static async Task<(bool Ran, ResultSetCursor Cursor)> ReadAsync(BatchAssembly assembly, DataBatch batch, List<DataTable> sets)
        {
            using DataTableReader reader = new([.. sets]);
            ResultSetCursor cursor = new(reader);
            bool ran = await assembly.ReadAsync(cursor, () => batch.GuardPassed, TestContext.Current.CancellationToken);
            cursor.ApplyCompletions();
            return (ran, cursor);
        }

        /// <summary>Set 0.</summary>
        /// <param name="userId">The user id.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="active">Whether the user is active.</param>
        /// <param name="state">The state.</param>
        /// <param name="permissions">The permissions flag.</param>
        /// <param name="profile">The profile flag.</param>
        /// <param name="settings">The settings flag.</param>
        /// <param name="guard">The guard.</param>
        /// <returns>The set.</returns>
        private static DataTable ConnectRow(int? userId, string? kind, bool active, string? state, bool permissions, bool profile, bool settings, bool guard)
        {
            DataTable table = new();
            _ = table.Columns.Add("UserId", typeof(int));
            _ = table.Columns.Add("Kind", typeof(string));
            _ = table.Columns.Add("IsActive", typeof(bool));
            _ = table.Columns.Add("State", typeof(string));
            _ = table.Columns.Add("PreferencesTag", typeof(Guid));
            _ = table.Columns.Add("UserPermissionsTag", typeof(Guid));
            _ = table.Columns.Add("PermissionsStale", typeof(bool));
            _ = table.Columns.Add("ProfileStale", typeof(bool));
            _ = table.Columns.Add("SettingsStale", typeof(bool));
            _ = table.Columns.Add("GuardPassed", typeof(bool));
            bool found = userId is not null && kind != "System";
            _ = table.Rows.Add(
                (object?)userId ?? DBNull.Value,
                (object?)kind ?? DBNull.Value,
                userId is null ? DBNull.Value : active,
                (object?)state ?? DBNull.Value,
                found ? PreferencesTag : DBNull.Value,
                found ? UserPermissionsTag : DBNull.Value,
                permissions,
                profile,
                settings,
                guard);
            return table;
        }

        /// <summary>Set 1.</summary>
        /// <returns>The set.</returns>
        private static DataTable Tags()
        {
            DataTable table = new();
            _ = table.Columns.Add("Name", typeof(string));
            _ = table.Columns.Add("Tag", typeof(Guid));
            _ = table.Rows.Add(VersionTagNames.Permissions, PermissionsTag);
            _ = table.Rows.Add(VersionTagNames.Settings, SettingsTag.Value);
            return table;
        }

        /// <summary>Set 2: a public role's permission and a windowed membership's.</summary>
        /// <returns>The set.</returns>
        private static DataTable PermissionRows()
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(int));
            _ = table.Columns.Add("RoleId", typeof(int));
            _ = table.Columns.Add("Name", typeof(string));
            _ = table.Columns.Add("IsPublic", typeof(bool));
            _ = table.Columns.Add("Resource", typeof(string));
            _ = table.Columns.Add("Action", typeof(string));
            _ = table.Columns.Add("Filter", typeof(string));
            _ = table.Columns.Add("FilterQueryexVersion", typeof(int));
            _ = table.Columns.Add("StartsAt", typeof(DateTimeOffset));
            _ = table.Columns.Add("EndsAt", typeof(DateTimeOffset));
            _ = table.Rows.Add(11, 3, "Everyone", true, "core.Settings.General", "Read", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            _ = table.Rows.Add(12, 4, "Clerks", false, "core.User", "Read", "IsActive", 1, Start, End);
            return table;
        }

        /// <summary>Set 3.</summary>
        /// <returns>The set.</returns>
        private static DataTable Profile()
        {
            DataTable table = new();
            foreach (string column in new[] { "Name", "Name2", "Name3", "Email", "ContactEmail", "ContactMobile" })
            {
                _ = table.Columns.Add(column, typeof(string));
            }

            _ = table.Columns.Add("ImageId", typeof(int));
            _ = table.Columns.Add("SignatureId", typeof(int));
            foreach (string column in new[] { "PreferredLanguage", "PreferredCalendar", "PreferredTimeZone", "PreferredRegion", "PreferredDigits", "Gender" })
            {
                _ = table.Columns.Add(column, typeof(string));
            }

            _ = table.Columns.Add("ModifiedAt", typeof(DateTimeOffset));
            _ = table.Rows.Add("Ada", DBNull.Value, DBNull.Value, "ada@example.com", DBNull.Value, DBNull.Value, DBNull.Value, 3, "ar", "uq", "Asia/Riyadh", "SA", "Native", "Female", Start);
            return table;
        }

        /// <summary>The two settings sets: the typed row, then no entries.</summary>
        /// <returns>The sets.</returns>
        private static DataTable[] Settings()
        {
            DataTable row = new();
            _ = row.Columns.Add("Id", typeof(int));
            _ = row.Columns.Add("TenantId", typeof(int));
            foreach (string column in new[] { "Name", "Name2", "Name3", "PrimaryLanguage", "SecondaryLanguage", "TertiaryLanguage", "PrimaryCalendar", "SecondaryCalendar", "TimeZone", "Region", "Digits" })
            {
                _ = row.Columns.Add(column, typeof(string));
            }

            _ = row.Columns.Add("CreatedAt", typeof(DateTimeOffset));
            _ = row.Columns.Add("CreatedById", typeof(int));
            _ = row.Columns.Add("ModifiedAt", typeof(DateTimeOffset));
            _ = row.Columns.Add("ModifiedById", typeof(int));
            _ = row.Rows.Add(1, 1, "Acme", DBNull.Value, DBNull.Value, "en", DBNull.Value, DBNull.Value, "gc", DBNull.Value, "UTC", DBNull.Value, DBNull.Value, Start, 1, Start, 1);

            DataTable entries = new();
            _ = entries.Columns.Add("Key", typeof(string));
            _ = entries.Columns.Add("Value", typeof(string));
            return [row, entries];
        }

        /// <summary>The body's set.</summary>
        /// <returns>The set.</returns>
        private static DataTable Body()
        {
            DataTable table = new();
            _ = table.Columns.Add("Body", typeof(int));
            _ = table.Rows.Add(1);
            return table;
        }

        /// <summary>Makes the store hold the tenant's settings current under the snapshot's tag, or hold none.</summary>
        /// <param name="current">Whether the store holds them.</param>
        private void StoreSettings(bool current)
        {
            IVersionTagSnapshots snapshots = _harness.Services.GetRequiredService<IVersionTagSnapshots>();
            VersionTag tag = current ? SettingsTag : VersionTag.New();
            snapshots.Replace(new VersionTagSnapshot(1, new Dictionary<string, VersionTag> { [VersionTagNames.Settings] = tag }, DateTimeOffset.UtcNow));
            if (current)
            {
                TenantSettingsStore store = _harness.Services.GetRequiredService<TenantSettingsStore>();
                store.Offer(1, store.Build(1, false, tag, new SettingsRows(
                    new SettingsRow(1, 1, "Acme", null, null, "en", null, null, "gc", null, "UTC", null, null, Start, 1, Start, 1), [])), tag);
            }
        }

        /// <summary>A fresh batch of the harness whose scope holds the settings cold load.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(BatchPurpose purpose)
        {
            return _harness.Batch(purpose, ColdLoad);
        }

        /// <summary>A batch carrying only the prologue for the premises.</summary>
        /// <param name="premises">The premises.</param>
        /// <param name="purpose">The purpose.</param>
        /// <returns>The batch.</returns>
        private DataBatch Prologue(ConnectPremises premises, BatchPurpose purpose = BatchPurpose.Read)
        {
            DataBatch batch = Batch(purpose);
            _ = ConnectPrologue.Append(batch, premises, ColdLoad, Reading());
            return batch;
        }
    }
}
