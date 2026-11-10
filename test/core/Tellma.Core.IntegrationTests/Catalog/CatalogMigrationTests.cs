// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Migrator;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Catalog
{
    /// <summary>
    ///     The platform-owned catalog migrations against a real server: from an empty database, again
    ///     as a no-op, as an idempotent script, through the design-time factory onto a database that
    ///     does not exist yet, and the migration state the registry's startup check reads.
    /// </summary>
    [Trait("Category", "Integration")]
    public class CatalogMigrationTests(SqlServerFixture sql)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_migrations_build_the_catalog_from_an_empty_database()
        {
            string catalog = await sql.CreateEmptyCatalogAsync("catalog_empty");

            await CatalogHost.MigrateAsync(catalog);

            // --- The tables, the sequence and the seeded state row ---
            Assert.Equal(4, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N'catalog' AND name IN (N'Tenants', N'CatalogState', N'TenantMemberships', N'Sessions');"));
            Assert.Equal(1000L, Convert.ToInt64(await SqlServerFixture.ScalarAsync<object>(catalog, "SELECT start_value FROM sys.sequences WHERE SCHEMA_NAME(schema_id) = N'catalog' AND name = N'sq_Tenants';"), System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(CatalogDbContext.InitialVersion, await SqlServerFixture.ScalarAsync<Guid>(catalog, "SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;"));
            Assert.Equal(CatalogMigrations.Latest, await SqlServerFixture.ScalarAsync<string>(catalog, "SELECT MAX([MigrationId]) FROM [catalog].[__EFMigrationsHistory];"));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM sys.identity_columns WHERE OBJECT_SCHEMA_NAME(object_id) = N'catalog';"));

            // --- The collations: the platform's for the database, binary for the identifiers ---
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(catalog, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));"));
            Assert.Equal(DatabaseCollations.Binary, await SqlServerFixture.ScalarAsync<string>(catalog, "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'[catalog].[TenantMemberships]') AND name = N'Subject';"));

            // --- The membership table type, binary-collated and granted to the application role ---
            Assert.Equal(DatabaseCollations.Binary, await SqlServerFixture.ScalarAsync<string>(catalog, """
                SELECT c.collation_name FROM sys.table_types AS tt JOIN sys.columns AS c ON c.object_id = tt.type_table_object_id
                 WHERE SCHEMA_NAME(tt.schema_id) = N'catalog' AND tt.name LIKE N'TenantMembershipList[_]%' AND c.name = N'Subject';
                """));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, $"""
                SELECT COUNT(*) FROM sys.database_permissions AS p JOIN sys.table_types AS tt ON p.class = 6 AND p.major_id = tt.user_type_id
                 WHERE SCHEMA_NAME(tt.schema_id) = N'catalog' AND p.permission_name = N'EXECUTE' AND p.state = N'G'
                   AND USER_NAME(p.grantee_principal_id) = N'{DatabaseRoles.Application}';
                """));
        }

        [Fact]
        public async Task The_migrations_run_again_as_a_no_op()
        {
            string catalog = await sql.CreateEmptyCatalogAsync("catalog_again");
            await CatalogHost.MigrateAsync(catalog);

            await CatalogHost.MigrateAsync(catalog);

            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM [catalog].[__EFMigrationsHistory];"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM [catalog].[CatalogState];"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM sys.table_types WHERE SCHEMA_NAME(schema_id) = N'catalog';"));
        }

        [Fact]
        public async Task The_idempotent_script_runs_twice_on_a_fresh_database()
        {
            string catalog = await sql.CreateEmptyCatalogAsync("catalog_script");
            string script;
            await using (var context = CatalogDbContext.Create(catalog))
            {
                script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            }

            string[] batches = [.. script.Split(["GO\r\n", "GO\n"], StringSplitOptions.RemoveEmptyEntries).Where(static batch => !string.IsNullOrWhiteSpace(batch))];
            await SqlServerFixture.ExecuteAsync(catalog, batches);
            await SqlServerFixture.ExecuteAsync(catalog, batches);

            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM [catalog].[__EFMigrationsHistory];"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(catalog, "SELECT COUNT(*) FROM [catalog].[CatalogState];"));
        }

        [Fact]
        public async Task A_database_update_through_the_design_time_factory_creates_the_database_and_the_role_first()
        {
            // As `dotnet ef database update --connection` does: the factory's context, pointed at a
            // database that does not exist yet.
            string name = sql.NewDatabaseName("catalog_design");
            await using CatalogDbContext context = new CatalogDesignTimeFactory().CreateDbContext([]);
            context.Database.SetConnectionString(sql.ConnectionString(name));

            await context.Database.MigrateAsync(Token);

            string catalog = sql.ConnectionString(name);
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(catalog, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));"));
            Assert.NotNull(await SqlServerFixture.ScalarAsync<object>(catalog, $"SELECT DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Application}');"));
            Assert.Null(await SqlServerFixture.ScalarAsync<object>(sql.MasterConnectionString, $"SELECT DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Application}');"));
            Assert.Equal(CatalogMigrations.Latest, await SqlServerFixture.ScalarAsync<string>(catalog, "SELECT MAX([MigrationId]) FROM [catalog].[__EFMigrationsHistory];"));
        }

        [Fact]
        public async Task Without_the_role_the_migration_cannot_grant_the_table_type()
        {
            // Why every fresh catalog gets the role before its first migration.
            string catalog = await sql.CreateEmptyCatalogAsync("catalog_norole", createRole: false);

            _ = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => CatalogHost.MigrateAsync(catalog));
        }

        [Fact]
        public async Task The_startup_check_reads_the_catalogs_migration_state()
        {
            string migrated = await sql.CreateEmptyCatalogAsync("catalog_state");
            await CatalogHost.MigrateAsync(migrated);
            string empty = await sql.CreateEmptyCatalogAsync("catalog_state_empty");

            Assert.Equal(CatalogHistoryState.Current, await ReadHistoryAsync(migrated));
            Assert.Equal(CatalogHistoryState.MissingSchema, await ReadHistoryAsync(empty));
            Assert.Equal(CatalogHistoryState.MissingDatabase, await ReadHistoryAsync(sql.ConnectionString(sql.NewDatabaseName("catalog_absent"))));

            await SqlServerFixture.ExecuteAsync(migrated, "DELETE FROM [catalog].[__EFMigrationsHistory];");
            Assert.Equal(CatalogHistoryState.Behind, await ReadHistoryAsync(migrated));
        }

        /// <summary>Reads a database's migration state with the registry's own reader.</summary>
        /// <param name="connectionString">The database.</param>
        /// <returns>The state.</returns>
        private static Task<CatalogHistoryState> ReadHistoryAsync(string connectionString)
        {
            SqlCatalogReader reader = new(new CatalogConnection(
                Microsoft.Extensions.Options.Options.Create(new TellmaCatalogOptions { ConnectionString = connectionString }),
                new DeploymentIdentity("acme", "Development")));
            return reader.ReadHistoryAsync(Token);
        }
    }
}
