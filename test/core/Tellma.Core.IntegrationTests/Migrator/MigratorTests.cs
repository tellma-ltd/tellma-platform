// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Migrator;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     The migrator's commands against a real server, run in process over the test distribution:
    ///     the catalog's creation, the role and its grants, provisioning, the per-tenant report, the
    ///     schema fingerprint, the provisioning record, the status report, and runs that overlap or
    ///     lose their lock.
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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");

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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");
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
            _ = await migrator.RunConvergedAsync("migrate");

            MigratorResult result = await migrator.RunAsync("set-state", "--tenant", "1", "--state", "Provisioning");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 1: failed", StringComparison.Ordinal));
            Assert.Equal("Active", await StateAsync(migrator, 1));
        }

        [Fact]
        public async Task Provision_turns_on_read_committed_snapshot_and_the_query_store_in_a_fresh_tenant_database()
        {
            MigratorHarness migrator = new(sql, "mig_fresh");
            _ = await migrator.RunConvergedAsync("migrate");

            MigratorResult result = await migrator.RunAsync("provision", "--name", "Fresh", "--category", "Live", "--tenant", "5");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(sql.MasterConnectionString, $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{migrator.TenantDatabaseName(5)}';"));

            // The migrator's own settings, which differ from the server's defaults.
            string tenant = migrator.TenantConnectionString(5);
            Assert.Equal("READ_WRITE", await SqlServerFixture.ScalarAsync<string>(tenant, "SELECT actual_state_desc FROM sys.database_query_store_options;"));
            Assert.Equal(1024L, await SqlServerFixture.ScalarAsync<long>(tenant, "SELECT max_storage_size_mb FROM sys.database_query_store_options;"));
            Assert.Equal(30L, await SqlServerFixture.ScalarAsync<long>(tenant, "SELECT stale_query_threshold_days FROM sys.database_query_store_options;"));
        }

        [Fact]
        public async Task Provision_leaves_the_isolation_query_store_and_users_of_an_existing_database_alone()
        {
            MigratorHarness migrator = new(sql, "mig_existing");
            _ = await migrator.RunConvergedAsync("migrate");

            // --- An operator's database: a history table, read-committed snapshot and the query store off ---
            string name = migrator.TenantDatabaseName(5);
            await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, $"CREATE DATABASE [{name}] COLLATE {DatabaseCollations.Platform};");
            await SqlServerFixture.ExecuteAsync(
                migrator.TenantConnectionString(5),
                "ALTER DATABASE CURRENT SET QUERY_STORE = OFF;",
                "CREATE TABLE [dbo].[__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY, [ProductVersion] nvarchar(32) NOT NULL);");

            // A principal with no login: creating its user would fail the run.
            migrator.Values["Tellma:Sql:ApplicationPrincipal"] = "tellma_no_such_login";
            MigratorResult result = await migrator.RunAsync("provision", "--name", "Existing", "--category", "Live", "--tenant", "5");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Equal("Active", await StateAsync(migrator, 5));
            Assert.False(await SqlServerFixture.ScalarAsync<bool>(sql.MasterConnectionString, $"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{name}';"));
            Assert.Equal("OFF", await SqlServerFixture.ScalarAsync<string>(migrator.TenantConnectionString(5), "SELECT actual_state_desc FROM sys.database_query_store_options;"));
            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(migrator.TenantConnectionString(5), "SELECT DATABASE_PRINCIPAL_ID(N'tellma_no_such_login');"));
        }

        [Fact]
        public async Task The_application_principal_joins_the_role_in_a_fresh_catalog_and_a_fresh_tenant_database()
        {
            MigratorHarness migrator = new(sql, "mig_principal");
            migrator.Seed(1);
            string login = migrator.CatalogName + "_web";
            string password = "Tm!" + Guid.NewGuid().ToString("N") + "aA1";
            await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");
            try
            {
                migrator.Values["Tellma:Sql:ApplicationPrincipal"] = login;

                MigratorResult result = await migrator.RunAsync("migrate");

                Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
                foreach (string database in new[] { migrator.CatalogConnectionString, migrator.TenantConnectionString(1) })
                {
                    Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(database, $"""
                        SELECT COUNT(*) FROM sys.database_role_members AS rm
                          JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
                          JOIN sys.database_principals AS m ON m.principal_id = rm.member_principal_id
                         WHERE r.name = {Role} AND m.name = N'{login}' AND m.type = N'S'
                           AND m.sid = SUSER_SID(N'{login}');
                        """));
                }
            }
            finally
            {
                await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, $"IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];");
            }
        }

        [Fact]
        public async Task Migrate_skips_a_frozen_tenant_even_when_named()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_frozen"));
            migrator.Seed(1);
            _ = await migrator.RunConvergedAsync("migrate");
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("set-state", "--tenant", "1", "--state", "Frozen", "--reason", "relocation")).ExitCode);

            migrator.Values[ProbeStep.VersionKey] = "2";
            MigratorResult result = await migrator.RunAsync("migrate", "--tenant", "1");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Contains("tenant 1: frozen", result.Lines);
            Assert.Single(ProbeStep.RunsOf(RunKey(migrator)));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(migrator.TenantConnectionString(1), $"SELECT [Version] FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'{ProbeStep.StepName}';"));
        }

        [Fact]
        public async Task Migrate_restores_a_revoked_grant_on_the_next_run()
        {
            MigratorHarness migrator = new(sql, "mig_regrant");
            migrator.Seed(1);
            _ = await migrator.RunConvergedAsync("migrate");
            string tenant = migrator.TenantConnectionString(1);
            await SqlServerFixture.ExecuteAsync(
                tenant,
                $"REVOKE SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[probe] FROM [{DatabaseRoles.Application}];",
                $"""
                DECLARE @sequence nvarchar(300) = (SELECT TOP (1) QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) FROM sys.sequences ORDER BY name);
                EXEC (N'REVOKE UPDATE ON OBJECT::' + @sequence + N' FROM [{DatabaseRoles.Application}];');
                """);

            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            await AssertSchemaGrantsAsync(tenant, ["core", "probe"]);
            await AssertEverySequenceGrantedAsync(tenant);
        }

        [Fact]
        public async Task A_cancellation_raised_inside_one_tenants_step_fails_that_tenant_alone()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_cancel"));
            migrator.Seed(1, 2);
            _ = await migrator.RunConvergedAsync("migrate");

            // The bumped step times out on tenant 1, the way an HTTP client's timeout surfaces.
            migrator.Values[ProbeStep.VersionKey] = "2";
            migrator.Values[ProbeStep.FailTenantKey] = "1";
            migrator.Values[ProbeStep.FailureKey] = "canceled";
            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 1: failed (TaskCanceledException", StringComparison.Ordinal));
            Assert.Contains("tenant 2: migrated (0 migration(s), 1 step(s))", result.Lines);
        }

        [Fact]
        public async Task Rerunning_the_registering_provision_line_resumes_a_failed_run_and_then_reports_already()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_rerunline"));
            _ = await migrator.RunConvergedAsync("migrate");
            string[] command = ["provision", "--name", "Gadgets Inc", "--category", "Live", "--tenant", "5"];

            // --- The first run registers the tenant, then fails in its step ---
            migrator.Values[ProbeStep.FailTenantKey] = "5";
            MigratorResult failed = await migrator.RunAsync(command);
            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());
            Assert.Equal("Provisioning", await StateAsync(migrator, 5));

            // --- The same line resumes the row, then finds it done ---
            migrator.Values.Remove(ProbeStep.FailTenantKey);
            MigratorResult resumed = await migrator.RunAsync(command);
            Assert.True(resumed.ExitCode == MigratorExitCodes.Converged, resumed.ToString());
            Assert.Contains(resumed.Lines, line => line.StartsWith("tenant 5: provisioned (0 migration(s), 1 step(s))", StringComparison.Ordinal));
            Assert.Equal("Active", await StateAsync(migrator, 5));

            MigratorResult again = await migrator.RunAsync(command);
            Assert.True(again.ExitCode == MigratorExitCodes.Converged, again.ToString());
            Assert.Contains("tenant 5: provisioned (already)", again.Lines);
            Assert.Equal([(5, 1, true)], ProbeStep.RunsOf(RunKey(migrator)));

            // --- A line that describes another tenant under the same id is refused ---
            MigratorResult other = await migrator.RunAsync("provision", "--name", "Someone Else", "--category", "Live", "--tenant", "5");
            Assert.True(other.ExitCode == MigratorExitCodes.PartialFailure, other.ToString());
            Assert.Contains("is already registered as 'Gadgets Inc'", other.Output, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rerunning_a_line_that_spells_its_server_with_a_protocol_prefix_resumes_the_row()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_rerunsrv"));
            _ = await migrator.RunConvergedAsync("migrate");

            // The catalog stores the server canonically: no protocol prefix, lower case.
            string server = "TCP:" + new SqlConnectionStringBuilder(migrator.CatalogConnectionString).DataSource;
            string[] command = ["provision", "--name", "Gadgets Inc", "--category", "Live", "--tenant", "5", "--server", server, "--database", migrator.TenantDatabaseName(5)];

            migrator.Values[ProbeStep.FailTenantKey] = "5";
            MigratorResult failed = await migrator.RunAsync(command);
            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());

            migrator.Values.Remove(ProbeStep.FailTenantKey);
            MigratorResult resumed = await migrator.RunAsync(command);
            Assert.True(resumed.ExitCode == MigratorExitCodes.Converged, resumed.ToString());
            Assert.Equal("Active", await StateAsync(migrator, 5));
        }

        [Fact]
        public async Task A_registration_drawn_from_the_sequence_prints_its_id_at_once_and_names_the_resume_when_it_fails()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_seqresume"));
            _ = await migrator.RunConvergedAsync("migrate");

            // --- The registering run fails in its step: the id and the resume are printed ---
            migrator.Values[ProbeStep.FailTenantKey] = "1000";
            MigratorResult failed = await migrator.RunAsync("provision", "--name", "Gadgets Inc", "--category", "Live");
            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());
            Assert.Equal("provision: registered tenant 1000", failed.Lines[0]);
            Assert.Contains("provision: resume it with provision --tenant 1000; rerunning this line registers another tenant", failed.Lines);

            // --- The named resume converges the same row ---
            migrator.Values.Remove(ProbeStep.FailTenantKey);
            MigratorResult resumed = await migrator.RunAsync("provision", "--tenant", "1000");
            Assert.True(resumed.ExitCode == MigratorExitCodes.Converged, resumed.ToString());
            Assert.DoesNotContain(resumed.Lines, static line => line.StartsWith("provision: registered", StringComparison.Ordinal));
            Assert.Equal("Active", await StateAsync(migrator, 1000));
        }

        [Fact]
        public async Task One_step_class_registered_under_two_names_runs_and_records_each()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_named"));
            migrator.Seed(1);

            // Two instances of one class, as a feature registers one step per seed it ships.
            migrator.Services = static services =>
            {
                TellmaCompositionRecord record = services.Select(static descriptor => descriptor.ImplementationInstance).OfType<TellmaCompositionRecord>().Single();
                ITellmaFeature owner = record.Features.Single(static feature => feature.Name == "probe-steps");
                record.ProvisioningSteps.Add(new ProvisioningStepRegistration(typeof(NamedProbeStep), owner, static _ => new NamedProbeStep("probe.seed-a")));
                record.ProvisioningSteps.Add(new ProvisioningStepRegistration(typeof(NamedProbeStep), owner, static _ => new NamedProbeStep("probe.seed-b")));
            };

            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Assert.Contains(result.Lines, static line => line.StartsWith("tenant 1: provisioned (", StringComparison.Ordinal) && line.Contains(" 3 step(s)", StringComparison.Ordinal));
            Assert.Equal(
                ["probe.counter", "probe.seed-a", "probe.seed-b"],
                await ListAsync(migrator.TenantConnectionString(1), "SELECT [Step] FROM [dbo].[__TellmaProvisioning] ORDER BY [Step];"));
        }

        [Fact]
        public async Task Provision_refuses_a_provisioning_tenant_whose_database_is_ahead()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_pahead"));
            _ = await migrator.RunConvergedAsync("migrate");
            migrator.Values[ProbeStep.FailTenantKey] = "5";
            Assert.Equal(MigratorExitCodes.PartialFailure, (await migrator.RunAsync("provision", "--name", "Ahead", "--category", "Live", "--tenant", "5")).ExitCode);
            await SqlServerFixture.ExecuteAsync(
                migrator.TenantConnectionString(5),
                "INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'99991231235959_Future', N'10.0.11');");
            migrator.Values.Remove(ProbeStep.FailTenantKey);

            MigratorResult result = await migrator.RunAsync("provision", "--tenant", "5");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 5: failed (its database is ahead of this migrator", StringComparison.Ordinal));
            Assert.Equal("Provisioning", await StateAsync(migrator, 5));
            Assert.Empty(ProbeStep.RunsOf(RunKey(migrator)));
        }

        [Fact]
        public async Task Overlapping_provision_runs_serialise_on_the_tenant_lock_and_both_converge()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_overlap"));
            _ = await migrator.RunConvergedAsync("migrate");
            migrator.Values[ProbeStep.GateKeyKey] = migrator.CatalogName;
            ProbeStep.Gate gate = ProbeStep.Hold(migrator.CatalogName);
            string[] command = ["provision", "--name", "Overlap", "--category", "Live", "--tenant", "5"];

            // --- The first run registers the tenant and stops inside its step, holding the tenant lock ---
            Task<MigratorResult> first = migrator.RunAsync(command);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromMinutes(2), Token);

            // --- The second run, having read the row as provisioning, waits for the lock ---
            Task<MigratorResult> second = migrator.RunAsync(command);
            await WaitForLockWaiterAsync(migrator);
            gate.Released.SetResult();

            MigratorResult[] results = await Task.WhenAll(first, second);
            Assert.All(results, static result => Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString()));
            Assert.Contains("tenant 5: provisioned (already)", results[1].Lines);
            Assert.Equal([(5, 1, true)], ProbeStep.RunsOf(RunKey(migrator)));
            Assert.Equal("Active", await StateAsync(migrator, 5));
        }

        [Fact]
        public async Task A_run_whose_lock_session_ends_stops_before_it_turns_the_tenant_active()
        {
            MigratorHarness migrator = Steps(new(sql, "mig_lostlock"));
            _ = await migrator.RunConvergedAsync("migrate");
            migrator.Values[ProbeStep.GateKeyKey] = migrator.CatalogName;
            ProbeStep.Gate gate = ProbeStep.Hold(migrator.CatalogName);

            // --- The run stops inside its step; the server then ends the session that holds its lock ---
            Task<MigratorResult> run = migrator.RunAsync("provision", "--name", "Lost", "--category", "Live", "--tenant", "5");
            await gate.Entered.Task.WaitAsync(TimeSpan.FromMinutes(2), Token);
            int session = await SqlServerFixture.ScalarAsync<int>(sql.MasterConnectionString, $"""
                SELECT TOP (1) request_session_id FROM sys.dm_tran_locks
                 WHERE resource_type = N'APPLICATION' AND request_owner_type = N'SESSION' AND request_status = N'GRANT'
                   AND resource_database_id = DB_ID(N'{migrator.CatalogName}');
                """);
            await SqlServerFixture.ExecuteAsync(sql.MasterConnectionString, string.Create(CultureInfo.InvariantCulture, $"KILL {session};"));
            gate.Released.SetResult();

            MigratorResult result = await run;
            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.Contains(result.Lines, line => line.StartsWith("tenant 5: failed (The migration lock 'tellma:migrate:tenant:5' was lost", StringComparison.Ordinal));
            Assert.Equal("Provisioning", await StateAsync(migrator, 5));

            // --- A rerun converges ---
            MigratorResult rerun = await migrator.RunAsync("provision", "--tenant", "5");
            Assert.True(rerun.ExitCode == MigratorExitCodes.Converged, rerun.ToString());
            Assert.Equal("Active", await StateAsync(migrator, 5));
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

        /// <summary>Waits until a run waits for a migration lock in a harness's catalog.</summary>
        /// <param name="migrator">The harness.</param>
        /// <returns>A task that completes when a lock request waits.</returns>
        private async Task WaitForLockWaiterAsync(MigratorHarness migrator)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            string waiting = $"""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                 WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID(N'{migrator.CatalogName}');
                """;
            while (await SqlServerFixture.ScalarAsync<int>(sql.MasterConnectionString, waiting) == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
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
