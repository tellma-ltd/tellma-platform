// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Identity;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's migrator against a real server from nothing: the catalog with the identity store and
    ///     the two seed tenants Acme's Development configuration declares, tenants provisioned on
    ///     request, every database in the platform collation, and the provisioning record.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class MigratorTests(SqlServerFixture sql)
    {
        /// <summary>The query that reads a database's collation.</summary>
        private const string CollationQuery = "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));";

        [Fact]
        public async Task Migrate_from_an_empty_server_creates_the_catalog_the_identity_store_and_both_seed_tenants()
        {
            AcmeMigrator acme = new(sql, "acme_migrate");

            MigratorResult result = await acme.MigrateAsync();

            // --- Tenant 1, Acme, and its sandbox, tenant 2, both active ---
            Assert.Contains(result.Lines, static line => line.StartsWith("tenant 1: provisioned", StringComparison.Ordinal));
            Assert.Contains(result.Lines, static line => line.StartsWith("tenant 2: provisioned", StringComparison.Ordinal));
            Assert.Equal("Active", await acme.StateAsync(1));
            Assert.Equal("Active", await acme.StateAsync(2));
            Assert.Equal("Acme", await SqlServerFixture.ScalarAsync<string>(acme.CatalogConnectionString, "SELECT [Name] FROM [catalog].[Tenants] WHERE [Id] = 1;"));
            Assert.Equal("Sandbox", await SqlServerFixture.ScalarAsync<string>(acme.CatalogConnectionString, "SELECT [Category] FROM [catalog].[Tenants] WHERE [Id] = 2;"));

            // --- The identity engine's schema, migrated to its last migration in the catalog ---
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(
                acme.CatalogConnectionString,
                $"SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{TellmaIdentityConstants.LatestMigration}';"));
        }

        [Fact]
        public async Task The_catalog_and_every_tenant_database_are_in_the_platform_collation()
        {
            AcmeMigrator acme = new(sql, "acme_collation");
            _ = await acme.MigrateAsync();

            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(acme.CatalogConnectionString, CollationQuery));
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(acme.TenantConnectionString(1), CollationQuery));
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(acme.TenantConnectionString(2), CollationQuery));
        }

        [Fact]
        public async Task A_second_migrate_applies_no_identity_migration()
        {
            AcmeMigrator acme = new(sql, "acme_idempotent");
            _ = await acme.MigrateAsync();
            int applied = await IdentityHistoryAsync(acme);

            MigratorResult again = await acme.MigrateAsync();

            Assert.Contains("catalog: migrated (0 migration(s))", again.Lines);
            Assert.Equal(applied, await IdentityHistoryAsync(acme));
            Assert.Contains(again.Lines, static line => line.StartsWith("tenant 1: ", StringComparison.Ordinal) && !line.Contains("failed", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Provision_allocates_from_the_sequence_and_refuses_a_requested_id_it_does_not_describe()
        {
            AcmeMigrator acme = new(sql, "acme_provision");
            _ = await acme.MigrateAsync();

            // --- The first tenant drawn from the sequence, after the two seeds ---
            MigratorResult provisioned = await acme.RunAsync("provision", "--name", "Acme Trading", "--category", "Live");
            Assert.True(provisioned.ExitCode == MigratorExitCodes.Converged, provisioned.ToString());
            Assert.Equal("provision: registered tenant 1000", provisioned.Lines[0]);
            Assert.Equal("Active", await acme.StateAsync(1000));
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(acme.TenantConnectionString(1000), CollationQuery));

            // --- The id now held by another registration ---
            MigratorResult taken = await acme.RunAsync("provision", "--tenant", "1000", "--name", "Someone Else", "--category", "Live");
            Assert.True(taken.ExitCode == MigratorExitCodes.PartialFailure, taken.ToString());
            Assert.Contains("is already registered as 'Acme Trading'", taken.Output, StringComparison.Ordinal);

            // --- An id of the sequence's band, which only the sequence hands out ---
            MigratorResult band = await acme.RunAsync("provision", "--tenant", "1001", "--name", "Someone Else", "--category", "Live");
            Assert.True(band.ExitCode == MigratorExitCodes.PartialFailure, band.ToString());
            Assert.Contains("was refused: a requested id lies in 1-999", band.Output, StringComparison.Ordinal);
            Assert.Null(await acme.StateAsync(1001));
        }

        [Fact]
        public async Task Provision_into_an_existing_database_in_another_collation_fails_without_migrating_it()
        {
            AcmeMigrator acme = new(sql, "acme_foreign");
            _ = await acme.MigrateAsync();
            await SqlServerFixture.ExecuteAsync(
                sql.MasterConnectionString, $"CREATE DATABASE [{acme.TenantDatabaseName(1000)}] COLLATE SQL_Latin1_General_CP1_CI_AS;");

            MigratorResult result = await acme.RunAsync("provision", "--name", "Acme Trading", "--category", "Live");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, static line => line.StartsWith("tenant 1000: failed", StringComparison.Ordinal) && line.Contains("SQL_Latin1_General_CP1_CI_AS", StringComparison.Ordinal));
            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(acme.TenantConnectionString(1000), "SELECT OBJECT_ID(N'[dbo].[__EFMigrationsHistory]');"));
            Assert.NotEqual("Active", await acme.StateAsync(1000));
        }

        [Fact]
        public async Task The_provisioning_record_holds_each_step_s_version_and_a_bumped_step_runs_again()
        {
            AcmeMigrator acme = new(sql, "acme_steps") { Compose = static builder => builder.AddFeature(new ProbeStepFeature()) };
            acme.Values[ProbeStep.RunKeyKey] = acme.CatalogName;
            _ = await acme.MigrateAsync();
            Assert.Equal(1, await StepVersionAsync(acme, 1));

            // --- An unchanged step does not run again ---
            _ = await acme.MigrateAsync();
            Assert.Equal([(1, 1), (2, 1)], ProbeStep.RunsOf(acme.CatalogName).Order());

            // --- A bumped step runs again on every tenant, and its record moves with it ---
            acme.Values[ProbeStep.VersionKey] = "2";
            _ = await acme.MigrateAsync();

            Assert.Equal(2, await StepVersionAsync(acme, 1));
            Assert.Equal(2, await StepVersionAsync(acme, 2));
            Assert.Equal([(1, 1), (1, 2), (2, 1), (2, 2)], ProbeStep.RunsOf(acme.CatalogName).Order());
        }

        /// <summary>How many migrations the identity store's history records.</summary>
        /// <param name="acme">The deployment.</param>
        /// <returns>The count.</returns>
        private static Task<int> IdentityHistoryAsync(AcmeMigrator acme)
        {
            return SqlServerFixture.ScalarAsync<int>(acme.CatalogConnectionString, "SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory];");
        }

        /// <summary>The version the provisioning record holds for the probe step in a tenant's database.</summary>
        /// <param name="acme">The deployment.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The version.</returns>
        private static Task<int> StepVersionAsync(AcmeMigrator acme, int tenantId)
        {
            return SqlServerFixture.ScalarAsync<int>(
                acme.TenantConnectionString(tenantId), $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';");
        }
    }
}
