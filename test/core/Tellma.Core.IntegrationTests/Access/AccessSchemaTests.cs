// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The access tables and the blob table as the fixture's migrations create them: every name,
    ///     the seeded rows, the derived state, the checks refusing their bad rows, the history a save
    ///     writes, the cascades from a user, and every audit reference resolving to the users table.
    /// </summary>
    /// <param name="database">The class's database.</param>
    [Trait("Category", "Integration")]
    public class AccessSchemaTests(AccessDatabase database) : IClassFixture<AccessDatabase>
    {
        /// <summary>The columns every user row the tests insert names, in order.</summary>
        private const string UserColumns = "[Id], [Kind], [Subject], [Email], [InvitedAt], [InviteStatus], [JoinedAt], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]";

        /// <summary>The SQL Server error number of a constraint violation.</summary>
        private const int ConstraintViolation = 547;

        // --- The schema ---

        [Theory]
        [InlineData("Users", "CK_Users_Id CK_Users_IndividualEvidence CK_Users_KindEmail CK_Users_ServiceEvidence CK_Users_SystemHasNoSubject CK_Users_SystemIsRowOne DF_Users_IsActive DF_Users_Kind FK_Users_CreatedById FK_Users_ImageId FK_Users_ModifiedById FK_Users_SignatureId IX_Users_IsActive_Name PK_Users UX_Users_Email UX_Users_ImageId UX_Users_SignatureId UX_Users_Subject")]
        [InlineData("Roles", "CK_Roles_Id DF_Roles_IsActive DF_Roles_IsPublic FK_Roles_CreatedById FK_Roles_ModifiedById IX_Roles_IsPublic PK_Roles UX_Roles_Code UX_Roles_Name UX_Roles_Name2 UX_Roles_Name3")]
        [InlineData("RoleMemberships", "CK_RoleMemberships_Id CK_RoleMemberships_Window FK_RoleMemberships_RoleId FK_RoleMemberships_UserId IX_RoleMemberships_RoleId_UserId IX_RoleMemberships_UserId_RoleId PK_RoleMemberships")]
        [InlineData("Permissions", "CK_Permissions_FilterVersion CK_Permissions_Id CK_Permissions_WildcardHasNoFilter FK_Permissions_RoleId IX_Permissions_RoleId PK_Permissions")]
        [InlineData("UserStamps", "DF_UserStamps_InboxVersion DF_UserStamps_PermissionsTag DF_UserStamps_PreferencesTag FK_UserStamps_UserId PK_UserStamps")]
        [InlineData("UserPreferences", "FK_UserPreferences_UserId PK_UserPreferences")]
        [InlineData("Blobs", "CK_Blobs_Id FK_Blobs_CreatedById IX_Blobs_Expiry IX_Blobs_StagedBy PK_Blobs UX_Blobs_StorageKey")]
        public async Task Every_constraint_and_index_of_an_access_table_carries_its_name(string table, string names)
        {
            // Constraints of every kind, then indexes; a primary key is both, so the names are distinct.
            List<string> actual = await database.ColumnAsync($"""
                SELECT [name] FROM sys.objects WHERE [parent_object_id] = OBJECT_ID(N'core.{table}') AND [type] IN ('PK', 'UQ', 'F', 'C', 'D')
                UNION
                SELECT [name] FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'core.{table}') AND [name] IS NOT NULL;
                """);

            Assert.Equal(names.Split(' '), actual.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task The_four_access_tables_are_system_versioned_and_the_rest_are_not()
        {
            List<string> versioned = await database.ColumnAsync("""
                SELECT t.[name] + N'>' + h.[name]
                FROM sys.tables AS t
                JOIN sys.tables AS h ON h.[object_id] = t.[history_table_id]
                WHERE t.[schema_id] = SCHEMA_ID(N'core') AND t.[temporal_type] = 2;
                """);

            Assert.Equal(
                ["Permissions>PermissionsHistory", "RoleMemberships>RoleMembershipsHistory", "Roles>RolesHistory", "Users>UsersHistory"],
                versioned.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task The_state_is_stored_and_computed_and_the_subject_compares_byte_for_byte()
        {
            Assert.Equal(["1"], await database.ColumnAsync("SELECT CAST([is_persisted] AS int) FROM sys.computed_columns WHERE [object_id] = OBJECT_ID(N'core.Users') AND [name] = N'State';"));
            Assert.Equal(["varchar(8) NOT NULL"], await database.ColumnAsync("SELECT TYPE_NAME([system_type_id]) + N'(' + CAST([max_length] AS nvarchar(8)) + N')' + IIF([is_nullable] = 1, N' NULL', N' NOT NULL') FROM sys.columns WHERE [object_id] = OBJECT_ID(N'core.Users') AND [name] = N'State';"));
            Assert.Equal([DatabaseCollations.Binary], await database.ColumnAsync("SELECT [collation_name] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'core.Users') AND [name] = N'Subject';"));
        }

        [Theory]
        [InlineData("UX_Users_Subject", "Subject", "Kind IsActive State", "([Subject] IS NOT NULL)")]
        [InlineData("IX_Permissions_RoleId", "RoleId", "Resource Action Filter FilterQueryexVersion", "NULL")]
        [InlineData("IX_RoleMemberships_UserId_RoleId", "UserId RoleId", "StartsAt EndsAt", "NULL")]
        [InlineData("IX_Blobs_StagedBy", "CreatedById", "Size ExpiresAt", "([State]='Staged')")]
        public async Task A_covering_index_holds_its_keys_its_included_columns_and_its_filter(string index, string keys, string included, string filter)
        {
            string columns = $"""
                SELECT c.[name] FROM sys.indexes AS i
                JOIN sys.index_columns AS ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                JOIN sys.columns AS c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                WHERE i.[name] = N'{index}' AND ic.[is_included_column] =
                """;

            Assert.Equal(keys.Split(' '), await database.ColumnAsync(columns + "0 ORDER BY ic.[key_ordinal];"));
            Assert.Equal(included.Split(' ').Order(StringComparer.Ordinal), (await database.ColumnAsync(columns + "1;")).Order(StringComparer.Ordinal));
            Assert.Equal([filter], await database.ColumnAsync($"SELECT [filter_definition] FROM sys.indexes WHERE [name] = N'{index}';"));
        }

        [Fact]
        public async Task Every_audit_column_of_every_current_table_references_the_users_table()
        {
            // Every CreatedById and ModifiedById outside the history tables, with the table its foreign key reaches.
            List<string> audits = await database.ColumnAsync("""
                SELECT SCHEMA_NAME(t.[schema_id]) + N'.' + t.[name] + N'.' + c.[name] + N'>' + ISNULL(OBJECT_SCHEMA_NAME(fk.[referenced_object_id]) + N'.' + OBJECT_NAME(fk.[referenced_object_id]), N'none')
                FROM sys.tables AS t
                JOIN sys.columns AS c ON c.[object_id] = t.[object_id] AND c.[name] IN (N'CreatedById', N'ModifiedById')
                LEFT JOIN sys.foreign_key_columns AS fk ON fk.[parent_object_id] = t.[object_id] AND fk.[parent_column_id] = c.[column_id]
                WHERE t.[temporal_type] <> 1;
                """);

            Assert.NotEmpty(audits);
            Assert.All(audits, static audit => Assert.EndsWith(">core.Users", audit, StringComparison.Ordinal));
            Assert.Contains("fixture.Widgets.CreatedById>core.Users", audits);
            Assert.Contains("core.Blobs.CreatedById>core.Users", audits);
        }

        // --- The seeds ---

        [Fact]
        public async Task The_seeded_rows_are_the_system_user_the_administrator_role_its_wildcard_grant_and_its_membership()
        {
            Assert.Equal(
                ["1|System|NULL|NULL|System|Joined|1|1|1|1|1"],
                await database.ColumnAsync("SELECT CONCAT_WS(N'|', [Id], [Kind], ISNULL([Subject] COLLATE DATABASE_DEFAULT, N'NULL'), ISNULL([Email], N'NULL'), [Name], [State], IIF([JoinedAt] = '2000-01-01T00:00:00+00:00', 1, 0), IIF([CreatedAt] = [JoinedAt], 1, 0), CAST([IsActive] AS int), [CreatedById], [ModifiedById]) FROM [core].[Users];"));
            Assert.Equal(
                ["1|Administrator|Administrator|0|1|1"],
                await database.ColumnAsync("SELECT CONCAT_WS(N'|', [Id], [Name], [Code], CAST([IsPublic] AS int), CAST([IsActive] AS int), [CreatedById]) FROM [core].[Roles];"));
            Assert.Equal(
                ["1|1|*|*|NULL|NULL"],
                await database.ColumnAsync("SELECT CONCAT_WS(N'|', [Id], [RoleId], [Resource], [Action], ISNULL([Filter], N'NULL'), ISNULL(CAST([FilterQueryexVersion] AS nvarchar(8)), N'NULL')) FROM [core].[Permissions];"));
            Assert.Equal(
                ["1|1|1|NULL|NULL"],
                await database.ColumnAsync("SELECT CONCAT_WS(N'|', [Id], [UserId], [RoleId], ISNULL(CONVERT(nvarchar(40), [StartsAt], 127), N'NULL'), ISNULL(CONVERT(nvarchar(40), [EndsAt], 127), N'NULL')) FROM [core].[RoleMemberships];"));
            Assert.Equal(
                ["1|00000000-0000-0000-0000-000000000001|00000000-0000-0000-0000-000000000002|0"],
                await database.ColumnAsync("SELECT CONCAT_WS(N'|', [UserId], [PermissionsTag], [PreferencesTag], [InboxVersion]) FROM [core].[UserStamps];"));
            Assert.Equal(["0"], await database.ColumnAsync("SELECT COUNT(*) FROM [core].[UserPreferences];"));
        }

        [Fact]
        public async Task Every_sequence_of_an_access_table_starts_after_the_reserved_band()
        {
            List<string> sequences = await database.ColumnAsync("""
                SELECT [name] + N'=' + CAST([start_value] AS nvarchar(20)) FROM sys.sequences
                WHERE [schema_id] = SCHEMA_ID(N'core') AND [name] IN (N'sq_Users', N'sq_Roles', N'sq_RoleMemberships', N'sq_Permissions', N'sq_Blobs');
                """);

            Assert.Equal(
                ["sq_Blobs=1000", "sq_Permissions=1000", "sq_RoleMemberships=1000", "sq_Roles=1000", "sq_Users=1000"],
                sequences.Order(StringComparer.Ordinal));
        }

        // --- The derived state ---

        [Fact]
        public async Task The_state_is_derived_from_the_evidence_for_every_kind()
        {
            // Each row is inserted and read back inside one transaction that is never committed.
            List<string> states = await database.ColumnAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {InsertUser("Individual", null, "'new@example.com'", invited: false, joined: false, "New individual")}
                {InsertUser("Individual", "'sub-invited'", "'invited@example.com'", invited: true, joined: false, "Invited individual")}
                {InsertUser("Individual", "'sub-joined'", "'joined@example.com'", invited: true, joined: true, "Joined individual")}
                {InsertUser("Service", null, null, invited: false, joined: false, "New service")}
                {InsertUser("Service", "'sub-service'", null, invited: false, joined: true, "Joined service")}
                SELECT [Name] + N'=' + [State] FROM [core].[Users] ORDER BY [Id];
                ROLLBACK TRANSACTION;
                """);

            Assert.Equal(
                ["System=Joined", "New individual=New", "Invited individual=Invited", "Joined individual=Joined", "New service=New", "Joined service=Joined"],
                states);
        }

        [Fact]
        public async Task The_state_follows_the_evidence_when_an_invited_individual_joins()
        {
            // The state before the update is kept in a variable, so one result set reports both.
            Assert.Equal(["Invited>Joined"], await database.ColumnAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {InsertUser("Individual", "'sub-joining'", "'joining@example.com'", invited: true, joined: false, "Joining")}
                DECLARE @before varchar(8) = (SELECT [State] FROM [core].[Users] WHERE [Name] = N'Joining');
                UPDATE [core].[Users] SET [JoinedAt] = SYSDATETIMEOFFSET() WHERE [Name] = N'Joining';
                SELECT @before + '>' + [State] FROM [core].[Users] WHERE [Name] = N'Joining';
                ROLLBACK TRANSACTION;
                """));
        }

        [Fact]
        public async Task Subjects_differing_only_in_case_are_different_subjects()
        {
            Assert.Equal(["2"], await database.ColumnAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {InsertUser("Service", "'machine-a'", null, invited: false, joined: true, "Lower")}
                {InsertUser("Service", "'MACHINE-A'", null, invited: false, joined: true, "Upper")}
                SELECT COUNT(*) FROM [core].[Users] WHERE [Kind] = 'Service';
                ROLLBACK TRANSACTION;
                """));
        }

        // --- The checks ---

        [Theory]
        [InlineData("CK_Users_KindEmail", "Individual", null, null, false, false)]
        [InlineData("CK_Users_KindEmail", "Service", null, "'svc@example.com'", false, false)]
        [InlineData("CK_Users_SystemIsRowOne", "System", null, null, false, true)]
        [InlineData("CK_Users_IndividualEvidence", "Individual", "'sub-uninvited'", "'a@example.com'", false, false)]
        [InlineData("CK_Users_IndividualEvidence", "Individual", null, "'b@example.com'", true, false)]
        [InlineData("CK_Users_IndividualEvidence", "Individual", null, "'c@example.com'", false, true)]
        [InlineData("CK_Users_ServiceEvidence", "Service", "'sub-unjoined'", null, false, false)]
        [InlineData("CK_Users_ServiceEvidence", "Service", null, null, false, true)]
        [InlineData("CK_Users_ServiceEvidence", "Service", null, null, true, false)]
        public async Task A_user_whose_evidence_disagrees_with_its_kind_is_refused(string check, string kind, string? subject, string? email, bool invited, bool joined)
        {
            await AssertRefusedAsync(check, InsertUser(kind, subject, email, invited, joined, "Refused"));
        }

        [Fact]
        public async Task The_system_user_is_refused_a_subject()
        {
            await AssertRefusedAsync("CK_Users_SystemHasNoSubject", "UPDATE [core].[Users] SET [Subject] = 'sub-system' WHERE [Id] = 1;");
        }

        [Theory]
        [InlineData("CK_Permissions_WildcardHasNoFilter", "'*'", "'Read'", "N'Id > 0'", "1")]
        [InlineData("CK_Permissions_FilterVersion", "'core.Users'", "'Read'", "N'Id > 0'", "NULL")]
        [InlineData("CK_Permissions_FilterVersion", "'core.Users'", "'Read'", "NULL", "1")]
        public async Task A_permission_whose_filter_disagrees_with_its_resource_or_version_is_refused(string check, string resource, string action, string filter, string version)
        {
            await AssertRefusedAsync(
                check,
                $"INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterQueryexVersion]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], 1, {resource}, {action}, {filter}, {version});");
        }

        [Fact]
        public async Task A_membership_that_ends_before_it_starts_is_refused()
        {
            await AssertRefusedAsync(
                "CK_RoleMemberships_Window",
                "INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId], [StartsAt], [EndsAt]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], 1, 1, '2026-02-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');");
        }

        // --- History and cascades ---

        [Fact]
        public async Task Every_save_of_a_versioned_row_writes_one_history_row()
        {
            // Committed saves, one transaction each; the rows are removed again whatever happens.
            try
            {
                await SqlServerFixture.ExecuteAsync(
                    database.ConnectionString,
                    "INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (NEXT VALUE FOR [core].[sq_Roles], N'History', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);",
                    "INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action]) SELECT NEXT VALUE FOR [core].[sq_Permissions], [Id], 'core.Users', 'Read' FROM [core].[Roles] WHERE [Name] = N'History';",
                    "INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) SELECT NEXT VALUE FOR [core].[sq_RoleMemberships], 1, [Id] FROM [core].[Roles] WHERE [Name] = N'History';",
                    "UPDATE [core].[Roles] SET [Name2] = N'History 2' WHERE [Name] = N'History';",
                    "UPDATE [core].[Roles] SET [Name3] = N'History 3' WHERE [Name] = N'History';",
                    "UPDATE p SET [Action] = 'Update' FROM [core].[Permissions] AS p JOIN [core].[Roles] AS r ON r.[Id] = p.[RoleId] WHERE r.[Name] = N'History';",
                    "UPDATE m SET [Notes] = N'Noted' FROM [core].[RoleMemberships] AS m JOIN [core].[Roles] AS r ON r.[Id] = m.[RoleId] WHERE r.[Name] = N'History';");

                // An insert writes no history; each update writes the row as it was.
                Assert.Equal(["2"], await database.ColumnAsync("SELECT COUNT(*) FROM [core].[RolesHistory] WHERE [Name] = N'History';"));
                Assert.Equal(["1"], await database.ColumnAsync("SELECT COUNT(*) FROM [core].[PermissionsHistory] WHERE [Action] = 'Read' AND [Resource] = 'core.Users';"));
                Assert.Equal(["1"], await database.ColumnAsync("SELECT COUNT(*) FROM [core].[RoleMembershipsHistory] WHERE [UserId] = 1 AND [RoleId] >= 1000;"));
            }
            finally
            {
                await SqlServerFixture.ExecuteAsync(
                    database.ConnectionString,
                    "DELETE m FROM [core].[RoleMemberships] AS m JOIN [core].[Roles] AS r ON r.[Id] = m.[RoleId] WHERE r.[Name] = N'History';",
                    "DELETE p FROM [core].[Permissions] AS p JOIN [core].[Roles] AS r ON r.[Id] = p.[RoleId] WHERE r.[Name] = N'History';",
                    "DELETE FROM [core].[Roles] WHERE [Name] = N'History';");
            }
        }

        [Fact]
        public async Task Removing_a_user_removes_its_stamp_and_its_preferences()
        {
            Assert.Equal(["0|0"], await database.ColumnAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {InsertUser("Service", null, null, invited: false, joined: false, "Cascaded")}
                INSERT INTO [core].[UserStamps] ([UserId]) SELECT [Id] FROM [core].[Users] WHERE [Name] = N'Cascaded';
                INSERT INTO [core].[UserPreferences] ([UserId], [Key], [Value]) SELECT [Id], 'theme', N'"dark"' FROM [core].[Users] WHERE [Name] = N'Cascaded';
                DELETE FROM [core].[Users] WHERE [Name] = N'Cascaded';
                SELECT CONCAT((SELECT COUNT(*) FROM [core].[UserStamps] WHERE [UserId] >= 1000), N'|', (SELECT COUNT(*) FROM [core].[UserPreferences]));
                ROLLBACK TRANSACTION;
                """));
        }

        [Fact]
        public async Task A_new_stamp_takes_fresh_tags_and_a_zero_inbox_version()
        {
            Assert.Equal(["1|1|0"], await database.ColumnAsync($"""
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                {InsertUser("Service", null, null, invited: false, joined: false, "Stamped")}
                INSERT INTO [core].[UserStamps] ([UserId]) SELECT [Id] FROM [core].[Users] WHERE [Name] = N'Stamped';
                SELECT CONCAT_WS(N'|', IIF(s.[PermissionsTag] <> '00000000-0000-0000-0000-000000000001', 1, 0), IIF(s.[PermissionsTag] <> s.[PreferencesTag], 1, 0), s.[InboxVersion])
                FROM [core].[UserStamps] AS s JOIN [core].[Users] AS u ON u.[Id] = s.[UserId] WHERE u.[Name] = N'Stamped';
                ROLLBACK TRANSACTION;
                """));
        }

        /// <summary>The statement that inserts one user row created by the system user.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="subject">The subject as a SQL literal, or null.</param>
        /// <param name="email">The email as a SQL literal, or null.</param>
        /// <param name="invited">Whether the row records an invitation.</param>
        /// <param name="joined">Whether the row records a join.</param>
        /// <param name="name">The name.</param>
        /// <returns>The statement.</returns>
        private static string InsertUser(string kind, string? subject, string? email, bool invited, bool joined, string name)
        {
            string invitedAt = invited ? "SYSDATETIMEOFFSET()" : "NULL";
            string inviteStatus = invited ? "'Invited'" : "NULL";
            string joinedAt = joined ? "SYSDATETIMEOFFSET()" : "NULL";
            return $"INSERT INTO [core].[Users] ({UserColumns}) VALUES (NEXT VALUE FOR [core].[sq_Users], '{kind}', {subject ?? "NULL"}, {email ?? "NULL"}, {invitedAt}, {inviteStatus}, {joinedAt}, N'{name}', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);";
        }

        /// <summary>Asserts that a statement is refused by one check and leaves nothing behind.</summary>
        /// <param name="check">The check's name.</param>
        /// <param name="statement">The statement.</param>
        /// <returns>A task that completes when the assertion ran.</returns>
        private async Task AssertRefusedAsync(string check, string statement)
        {
            SqlException refusal = await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ExecuteAsync(database.ConnectionString, statement));

            Assert.Equal(ConstraintViolation, refusal.Number);
            Assert.Contains($"\"{check}\"", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(["1"], await database.ColumnAsync("SELECT COUNT(*) FROM [core].[Users];"));
        }
    }
}
