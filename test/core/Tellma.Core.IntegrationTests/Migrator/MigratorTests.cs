// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Migrator;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     The migrator's commands against a real server, run in process over the test distribution:
    ///     the catalog's creation, the role and its grants, provisioning, the per-tenant report, the
    ///     schema fingerprint, the provisioning record and the status report.
    /// </summary>
    [Trait("Category", "Integration")]
    public class MigratorTests(SqlServerFixture sql)
    {
        /// <summary>The application role, quoted for a SQL literal.</summary>
        private const string Role = $"N'{DatabaseRoles.Application}'";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Migrate_creates_a_missing_catalog_in_the_platform_collation_with_read_committed_snapshot()
        {
            MigratorHarness migrator = new(sql, "mig_create");

            MigratorResult result = await migrator.RunAsync("migrate", "--catalog-only");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(sql.MasterConnectionString, $"SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(N'{migrator.CatalogName}', 'Collation'));"));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(sql.MasterConnectionString, $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{migrator.CatalogName}';"));
            Assert.Contains("catalog: migrated", result.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Migrate_refuses_an_existing_catalog_in_another_collation()
        {
            MigratorHarness migrator = new(sql, "mig_collation");
            await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, $"CREATE DATABASE [{migrator.CatalogName}] COLLATE SQL_Latin1_General_CP1_CI_AS;");

            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains("catalog: failed", result.Output, StringComparison.Ordinal);
            Assert.Contains("SQL_Latin1_General_CP1_CI_AS", result.Output, StringComparison.Ordinal);
            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(migrator.CatalogConnectionString, "SELECT OBJECT_ID(N'[catalog].[__EFMigrationsHistory]');"));
        }

        [Fact]
        public async Task Migrate_creates_the_role_before_the_catalogs_migration_grants_its_membership_table_type()
        {
            MigratorHarness migrator = new(sql, "mig_role");

            MigratorResult result = await migrator.RunAsync("migrate", "--catalog-only");

            // The table type's migration grants EXECUTE to the role unguarded, so it ran only because the role existed.
            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(migrator.CatalogConnectionString, $"""
                SELECT COUNT(*) FROM sys.database_permissions AS p JOIN sys.table_types AS tt ON p.class = 6 AND p.major_id = tt.user_type_id
                 WHERE tt.name LIKE N'TenantMembershipList[_]%' AND p.permission_name = N'EXECUTE' AND p.state = N'G'
                   AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID({Role});
                """));
        }

        [Fact]
        public async Task Migrate_grants_the_role_every_catalog_schema_and_sequence_and_no_write_to_the_history()
        {
            MigratorHarness migrator = new(sql, "mig_cgrants");

            MigratorResult result = await migrator.RunAsync("migrate", "--catalog-only");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            await AssertSchemaGrantsAsync(migrator.CatalogConnectionString, ["catalog"]);
            await AssertEverySequenceGrantedAsync(migrator.CatalogConnectionString);
            Assert.Equal(
                ["DELETE", "INSERT", "UPDATE"],
                await PermissionsAsync(migrator.CatalogConnectionString, "[catalog].[__EFMigrationsHistory]", "D"));
            Assert.Empty(await PermissionsAsync(migrator.CatalogConnectionString, "[catalog].[__EFMigrationsHistory]", "G"));
        }

        [Fact]
        public async Task Migrate_provisions_a_seed_tenant_and_grants_the_role_over_its_database()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_seed"));
            migrator.Seed(1);

            MigratorResult result = await migrator.RunAsync("migrate");

            // --- The tenant is active, provisioned with its step as a new tenant ---
            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Contains("tenant 1: provisioned", result.Output, StringComparison.Ordinal);
            Assert.Equal("Active", await StateAsync(migrator, 1));
            Assert.Equal([(1, 1, true)], ProbeStep.RunsOf(RunKey(migrator)));

            // --- Every schema but dbo, every sequence, and SELECT alone on the fingerprint table ---
            string tenant = migrator.TenantConnectionString(1);
            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(tenant, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));"));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(sql.MasterConnectionString, $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{migrator.TenantDatabaseName(1)}';"));
            await AssertSchemaGrantsAsync(tenant, ["core", "probe"]);
            await AssertEverySequenceGrantedAsync(tenant);
            Assert.Equal(["SELECT"], await PermissionsAsync(tenant, "[dbo].[__TellmaSchema]", "G"));
            Assert.Empty(await PermissionsAsync(tenant, "[dbo].[__EFMigrationsHistory]", "G"));
            Assert.Empty(await PermissionsAsync(tenant, "[dbo].[__TellmaProvisioning]", "G"));

            // --- The fingerprint and the step's record ---
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant, "SELECT COUNT(*) FROM [dbo].[__TellmaSchema];"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant, $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';"));
        }

        [Fact]
        public async Task Migrate_then_provision_bring_a_new_tenant_to_active_from_an_empty_server()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_provision"));

            MigratorResult migrated = await migrator.RunAsync("migrate");
            MigratorResult provisioned = await migrator.RunAsync("provision", "--name", "Gadgets Inc", "--category", "Live", "--tenant", "5");

            Assert.True(migrated.ExitCode == MigratorExitCodes.Converged, migrated.ToString());
            Assert.True(provisioned.ExitCode == MigratorExitCodes.Converged, provisioned.ToString());
            Assert.Contains("tenant 5: provisioned", provisioned.Output, StringComparison.Ordinal);
            Assert.Equal("Active", await StateAsync(migrator, 5));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(migrator.TenantConnectionString(5), "SELECT COUNT(*) FROM [dbo].[__TellmaSchema];"));
            await AssertSchemaGrantsAsync(migrator.TenantConnectionString(5), ["core", "probe"]);

            // A rerun finds the tenant active and has nothing left to do.
            MigratorResult again = await migrator.RunAsync("provision", "--tenant", "5");
            Assert.True(again.ExitCode == MigratorExitCodes.Converged, again.ToString());
            Assert.Single(ProbeStep.RunsOf(RunKey(migrator)));
        }

        [Fact]
        public async Task Provision_resumes_a_provisioning_row_that_migrate_reported_and_left_untouched()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_resume"));
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            await using (CatalogHost host = await CatalogHost.ComposeAsync(sql, migrator.CatalogConnectionString, values => values["Tellma:Sql:DatabasePrefix"] = migrator.DatabasePrefix))
            {
                _ = await host.Catalog.RegisterAsync(new TenantRegistration("Self-serve", TenantCategory.Live, null, null), "admin", cancellationToken: Token);
            }

            // --- migrate reports the admin surface's row and creates nothing ---
            MigratorResult migrated = await migrator.RunAsync("migrate");
            Assert.True(migrated.ExitCode == MigratorExitCodes.Converged, migrated.ToString());
            Assert.Contains("tenant 1000: provisioning", migrated.Lines);
            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{migrator.TenantDatabaseName(1000)}');"));
            Assert.Empty(ProbeStep.RunsOf(RunKey(migrator)));

            // --- provision --tenant finishes it ---
            MigratorResult provisioned = await migrator.RunAsync("provision", "--tenant", "1000");
            Assert.True(provisioned.ExitCode == MigratorExitCodes.Converged, provisioned.ToString());
            Assert.Equal("Active", await StateAsync(migrator, 1000));
            Assert.Equal([(1000, 1, true)], ProbeStep.RunsOf(RunKey(migrator)));
        }

        [Fact]
        public async Task Each_step_runs_under_a_root_activity_of_its_own_named_after_it()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_activity"));
            ConcurrentQueue<Activity> stopped = new();
            using ActivitySource outer = new("Tellma.Core.IntegrationTests.Migrator");
            using ActivityListener listener = new()
            {
                ShouldListenTo = static source => source.Name is "Tellma.Core" or "Tellma.Core.IntegrationTests.Migrator",
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);

            // The caller's own trace does not become the step's parent.
            using (outer.StartActivity("caller"))
            {
                MigratorResult result = await migrator.RunAsync("provision", "--name", "Traced", "--category", "Live", "--tenant", "3");
                Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            }

            Activity step = Assert.Single(stopped, static activity => activity.OperationName == ProbeStep.StepName && Equals(activity.GetTagItem("tellma.tenant.id"), 3));
            Assert.Null(step.ParentId);
            Assert.Equal(ActivityKind.Internal, step.Kind);
        }

        [Fact]
        public async Task A_Development_seed_records_the_seeded_administrators_membership_hint()
        {
            MigratorHarness migrator = new(sql, "mig_hint");
            migrator.Seed(2);
            migrator.Values["Tellma:Seed:AdminEmail"] = "admin@acme.example";

            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(
                migrator.CatalogConnectionString,
                "SELECT [IsActive] FROM [catalog].[TenantMemberships] WHERE [TenantId] = 2 AND [Subject] = '00000000-0000-0000-0000-000000000001';"));
        }

        [Fact]
        public async Task Migrate_reports_a_tenant_whose_database_is_ahead_and_runs_no_step_on_it()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_ahead"));
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            await SqlServerFixture.ExecuteAsync(
                migrator.TenantConnectionString(1),
                "INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'99991231235959_Future', N'10.0.11');");

            migrator.Values[ProbeStep.VersionKey] = "2";
            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 1: ahead", StringComparison.Ordinal));
            Assert.Single(ProbeStep.RunsOf(RunKey(migrator)));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(migrator.TenantConnectionString(1), $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';"));
        }

        [Fact]
        public async Task Migrate_migrates_a_suspended_tenant_and_defers_its_steps_until_reactivation()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_suspended"));
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("set-state", "--tenant", "1", "--state", "Suspended", "--reason", "unpaid")).ExitCode);

            // --- Suspended: migrated, its bumped step deferred ---
            migrator.Values[ProbeStep.VersionKey] = "2";
            MigratorResult suspended = await migrator.RunAsync("migrate");
            Assert.True(suspended.ExitCode == MigratorExitCodes.Converged, suspended.ToString());
            Assert.Contains(suspended.Lines, line => line.StartsWith("tenant 1: suspended-steps-deferred", StringComparison.Ordinal));
            Assert.Single(ProbeStep.RunsOf(RunKey(migrator)));

            // --- Reactivated: the first migrate runs the step ---
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("set-state", "--tenant", "1", "--state", "Active")).ExitCode);
            MigratorResult reactivated = await migrator.RunAsync("migrate");
            Assert.True(reactivated.ExitCode == MigratorExitCodes.Converged, reactivated.ToString());
            Assert.Contains("tenant 1: migrated (0 migration(s), 1 step(s))", reactivated.Lines);
            Assert.Equal([(1, 1, true), (1, 2, false)], ProbeStep.RunsOf(RunKey(migrator)));
        }

        [Fact]
        public async Task A_step_runs_again_only_when_its_version_exceeds_the_recorded_one()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_rerun"));
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            string tenant = migrator.TenantConnectionString(1);
            DateTimeOffset first = await CompletedAtAsync(tenant);

            // --- The same version: nothing runs ---
            MigratorResult same = await migrator.RunAsync("migrate");
            Assert.Contains("tenant 1: migrated (0 migration(s), 0 step(s))", same.Lines);
            Assert.Single(ProbeStep.RunsOf(RunKey(migrator)));

            // --- A bumped version: the step runs once more and its record moves ---
            migrator.Values[ProbeStep.VersionKey] = "3";
            MigratorResult bumped = await migrator.RunAsync("migrate");
            Assert.Contains("tenant 1: migrated (0 migration(s), 1 step(s))", bumped.Lines);
            Assert.Equal([(1, 1, true), (1, 3, false)], ProbeStep.RunsOf(RunKey(migrator)));
            Assert.Equal(3, await SqlServerFixture.ScalarAsync<int>(tenant, $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';"));
            Assert.True(await CompletedAtAsync(tenant) >= first);
            Assert.False(string.IsNullOrEmpty(await SqlServerFixture.ScalarAsync<string>(tenant, $"SELECT [PlatformVersion] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';")));
        }

        [Fact]
        public async Task The_fingerprint_table_keeps_the_two_newest_rows_across_three_migrations()
        {
            MigratorHarness migrator = new(sql, "mig_fingerprint");
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            string tenant = migrator.TenantConnectionString(1);
            string first = (await SqlServerFixture.ScalarAsync<string>(tenant, "SELECT [Fingerprint] FROM [dbo].[__TellmaSchema];"))!;

            // Two later migrations, each recorded with its own model's fingerprint.
            string second = new('e', 64);
            string third = new('f', 64);
            await RecordAfterMigrationAsync(tenant, "99990101000000_Second", second);
            await RecordAfterMigrationAsync(tenant, "99990102000000_Third", third);

            List<string> rows = [];
            await using (SqlConnection connection = new(tenant))
            {
                await connection.OpenAsync(Token);
                rows.AddRange((await SchemaFingerprints.ReadAsync(connection, Token)).Select(static row => row.Fingerprint));
            }

            Assert.Equal([third, second], rows);
            Assert.DoesNotContain(first, rows);
        }

        [Fact]
        public async Task Status_prints_the_pending_migrations_and_the_recorded_fingerprints()
        {
            MigratorHarness migrator = new(sql, "mig_status");
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            string fingerprint = (await SqlServerFixture.ScalarAsync<string>(migrator.TenantConnectionString(1), "SELECT [Fingerprint] FROM [dbo].[__TellmaSchema];"))!;

            MigratorResult status = await migrator.RunAsync("status");

            Assert.True(status.ExitCode == MigratorExitCodes.Converged, status.ToString());
            Assert.Contains($"model: fingerprint {fingerprint}", status.Lines);
            Assert.Contains("catalog: 0 pending migration(s)", status.Lines);
            Assert.Contains("tenant 1: Active", status.Lines);
            Assert.Contains(status.Lines, line => line.StartsWith($"tenant 1: fingerprint {fingerprint} (matches the model", StringComparison.Ordinal));
            Assert.Contains("tenant 1: 0 pending migration(s)", status.Lines);
        }

        [Fact]
        public async Task Set_state_reports_an_illegal_transition_as_a_failure()
        {
            MigratorHarness migrator = new(sql, "mig_state");
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);

            MigratorResult result = await migrator.RunAsync("set-state", "--tenant", "1", "--state", "Provisioning");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 1: failed", StringComparison.Ordinal));
            Assert.Equal("Active", await StateAsync(migrator, 1));
        }

        /// <summary>Adds the counted step to a harness's runs, counted under the harness's own key.</summary>
        /// <param name="migrator">The harness.</param>
        /// <returns>The harness.</returns>
        private static MigratorHarness Steps(MigratorHarness migrator)
        {
            migrator.Compose = static builder => builder.AddFeature(new ProbeStepFeature());
            migrator.Values[ProbeStep.RunKeyKey] = migrator.CatalogName;
            return migrator;
        }

        /// <summary>The key a harness's step runs are counted under.</summary>
        /// <param name="migrator">The harness.</param>
        /// <returns>The key.</returns>
        private static string RunKey(MigratorHarness migrator)
        {
            return migrator.CatalogName;
        }

        /// <summary>A tenant's state in the catalog.</summary>
        /// <param name="migrator">The harness.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The state's name.</returns>
        private static Task<string?> StateAsync(MigratorHarness migrator, int tenantId)
        {
            return SqlServerFixture.ScalarAsync<string>(migrator.CatalogConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [State] FROM [catalog].[Tenants] WHERE [Id] = {tenantId};"));
        }

        /// <summary>When the counted step's record was last written.</summary>
        /// <param name="tenant">The tenant database.</param>
        /// <returns>The time.</returns>
        private static Task<DateTimeOffset> CompletedAtAsync(string tenant)
        {
            return SqlServerFixture.ScalarAsync<DateTimeOffset>(tenant, $"SELECT [CompletedAt] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';");
        }

        /// <summary>Appends a migration to a tenant's history and records a fingerprint after it, as the migrator does after migrating.</summary>
        /// <param name="tenant">The tenant database.</param>
        /// <param name="migrationId">The migration.</param>
        /// <param name="fingerprint">The model's fingerprint after it.</param>
        /// <returns>A task that completes when recorded.</returns>
        private static async Task RecordAfterMigrationAsync(string tenant, string migrationId, string fingerprint)
        {
            // Rows recorded within one clock tick would tie on their time; a pause keeps them ordered.
            await Task.Delay(TimeSpan.FromMilliseconds(50), Token);
            await SqlServerFixture.ExecuteAsync(tenant, $"INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'{migrationId}', N'10.0.11');");
            await using SqlConnection connection = new(tenant);
            await connection.OpenAsync(Token);
            Assert.True(await SchemaFingerprints.RecordAsync(connection, fingerprint, migrationId, "1.0.0", Token));
        }

        /// <summary>Asserts that the role holds the data grants on exactly the given schemas, and none on <c>dbo</c>.</summary>
        /// <param name="database">The database.</param>
        /// <param name="schemas">The schemas, in ordinal order.</param>
        /// <returns>A task that completes when asserted.</returns>
        private static async Task AssertSchemaGrantsAsync(string database, string[] schemas)
        {
            foreach (string permission in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                List<string> granted = await ListAsync(database, $"""
                    SELECT SCHEMA_NAME(major_id) FROM sys.database_permissions
                     WHERE class = 3 AND permission_name = N'{permission}' AND state = N'G' AND grantee_principal_id = DATABASE_PRINCIPAL_ID({Role})
                     ORDER BY SCHEMA_NAME(major_id);
                    """);
                Assert.Equal(schemas, granted.Order(StringComparer.Ordinal));
            }
        }

        /// <summary>Asserts that every sequence of a database is granted to the role for its next value.</summary>
        /// <param name="database">The database.</param>
        /// <returns>A task that completes when asserted.</returns>
        private static async Task AssertEverySequenceGrantedAsync(string database)
        {
            Assert.True(await SqlServerFixture.ScalarAsync<int>(database, "SELECT COUNT(*) FROM sys.sequences;") > 0);
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(database, $"""
                SELECT COUNT(*) FROM sys.sequences AS s
                 WHERE NOT EXISTS (SELECT 1 FROM sys.database_permissions AS p
                                    WHERE p.class = 1 AND p.major_id = s.object_id AND p.permission_name = N'UPDATE' AND p.state = N'G'
                                      AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID({Role}));
                """));
        }

        /// <summary>The role's permissions of one state on one object.</summary>
        /// <param name="database">The database.</param>
        /// <param name="table">The object, quoted.</param>
        /// <param name="state">G for granted, D for denied.</param>
        /// <returns>The permissions, in ordinal order.</returns>
        private static async Task<List<string>> PermissionsAsync(string database, string table, string state)
        {
            List<string> permissions = await ListAsync(database, $"""
                SELECT permission_name FROM sys.database_permissions
                 WHERE class = 1 AND major_id = OBJECT_ID(N'{table}') AND state = N'{state}' AND grantee_principal_id = DATABASE_PRINCIPAL_ID({Role});
                """);
            return [.. permissions.Order(StringComparer.Ordinal)];
        }

        /// <summary>Reads one text column.</summary>
        /// <param name="database">The database.</param>
        /// <param name="query">The query.</param>
        /// <returns>The values.</returns>
        private static async Task<List<string>> ListAsync(string database, string query)
        {
            List<string> values = [];
            await using SqlConnection connection = new(database);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }
    }
}
