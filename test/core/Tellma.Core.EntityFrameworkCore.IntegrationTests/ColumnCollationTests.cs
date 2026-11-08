// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.EntityFrameworkCore.IntegrationTests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;

namespace Tellma.Core.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    ///     A column collation on a class-derived standalone table type, deployed by the
    ///     MigrationsHost's migrations: the catalog reports the configured collation on that column
    ///     only, and a variable of the type joins a binary-collated table column — the comparison
    ///     SQL Server refuses with Msg 468 when the type's column takes the database collation.
    /// </summary>
    /// <param name="fixture">The shared SQL Server.</param>
    [Trait("Category", "Integration")]
    public class ColumnCollationTests(SqlServerFixture fixture)
    {
        /// <summary>SQL Server's error number for a collation conflict in a comparison.</summary>
        private const int CollationConflict = 468;

        [Fact]
        public async Task The_collated_column_reads_its_collation_in_the_catalog()
        {
            string connectionString = await MigrateAsync("collation");
            string physical = (await IntegrationHelpers.GetPhysicalNameAsync(connectionString, "dbo", "SubjectsList"))!;

            List<string> collations = await IntegrationHelpers.ColumnAsync(
                connectionString,
                $"""
                SELECT c.[name] + N'=' + c.[collation_name]
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[columns] c ON c.[object_id] = tt.[type_table_object_id]
                WHERE tt.[name] = N'{physical}'
                ORDER BY c.[column_id]
                """);

            // The configured collation on Subject; the database's on the column without one.
            Assert.Equal(
                [$"Subject={MigrationsHostContext.SubjectCollation}", $"DisplayName={DatabaseCollations.Platform}"],
                collations);
        }

        [Fact]
        public async Task A_variable_of_the_type_joins_a_binary_collated_column()
        {
            string connectionString = await MigrateAsync("collationjoin");
            await CreateSubjectsTableAsync(connectionString);
            string physical = (await IntegrationHelpers.GetPhysicalNameAsync(connectionString, "dbo", "SubjectsList"))!;

            // Compiles without Msg 468, and compares binary: 'alice' matches, 'BOB' does not
            // match 'bob' (the database collation would match it case-insensitively).
            int matches = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                $"""
                DECLARE @subjects [dbo].[{physical}];
                INSERT INTO @subjects ([Subject]) VALUES (N'alice'), (N'BOB');
                SELECT COUNT(*) FROM @subjects s INNER JOIN [dbo].[Principals] p ON p.[Subject] = s.[Subject];
                """);

            Assert.Equal(1, matches);
        }

        [Fact]
        public async Task A_variable_of_a_type_without_the_collation_fails_the_same_join_with_Msg_468()
        {
            // The control: the same join through StringList, whose column takes the database
            // collation, does not compile. This is what the collated type exists to prevent.
            string connectionString = await MigrateAsync("collationcontrol");
            await CreateSubjectsTableAsync(connectionString);
            string physical = (await IntegrationHelpers.GetPhysicalNameAsync(connectionString, "dbo", "StringList"))!;

            SqlException exception = await Assert.ThrowsAsync<SqlException>(() => IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                $"""
                DECLARE @subjects [dbo].[{physical}];
                INSERT INTO @subjects ([Id]) VALUES (N'alice');
                SELECT COUNT(*) FROM @subjects s INNER JOIN [dbo].[Principals] p ON p.[Subject] = s.[Id];
                """));

            Assert.Equal(CollationConflict, exception.Number);
        }

        /// <summary>Creates a fresh database and applies the MigrationsHost's migrations to it.</summary>
        private async Task<string> MigrateAsync(string prefix)
        {
            string connectionString = await fixture.CreateDatabaseAsync(prefix);
            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            return connectionString;
        }

        /// <summary>Creates a table whose <c>Subject</c> column is binary-collated, with two rows.</summary>
        private static Task CreateSubjectsTableAsync(string connectionString)
        {
            return IntegrationHelpers.ExecuteAsync(
                connectionString,
                $"CREATE TABLE [dbo].[Principals] ([Subject] nvarchar(255) COLLATE {MigrationsHostContext.SubjectCollation} NOT NULL PRIMARY KEY)",
                "INSERT INTO [dbo].[Principals] ([Subject]) VALUES (N'alice'), (N'bob')");
        }
    }
}
