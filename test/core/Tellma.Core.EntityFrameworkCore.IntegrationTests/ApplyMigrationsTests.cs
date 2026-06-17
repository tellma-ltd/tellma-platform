// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tellma.Core.EntityFrameworkCore.IntegrationTests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    ///     Applies the MigrationsHost's committed migration chain to a fresh database and asserts
    ///     the deployed table types — existence, columns in order, primary keys, grants, the
    ///     no-persisted-dependents invariant (spec 0001 Rule 5 layer 2), and UDTT/table column-order
    ///     parity (the contract behind ordinal TVP binding).
    /// </summary>
    /// <param name="fixture">The shared SQL Server.</param>
    [Trait("Category", "Integration")]
    public class ApplyMigrationsTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Migrate_creates_all_table_types_with_columns_order_pk_and_grants()
        {
            string connectionString = await fixture.CreateDatabaseAsync("apply");
            await using (MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            // All eight types exist (three table-derived + four built-ins + one standalone),
            // listed by their logical names (physical names carry a content-hash suffix).
            List<string> types = await IntegrationHelpers.ColumnAsync(
                connectionString,
                """
                SELECT SCHEMA_NAME(tt.[schema_id]) + N'.' + CONVERT(nvarchar(max), ep.[value])
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[extended_properties] ep ON ep.[class] = 6 AND ep.[major_id] = tt.[user_type_id]
                    AND ep.[name] = N'Tellma:TableType:LogicalName'
                ORDER BY 1
                """);
            Assert.Equal(
                [
                    "crm.CustomersList",
                    "dbo.BigIdList",
                    "dbo.DocumentStatesList",
                    "dbo.GuidList",
                    "dbo.IdList",
                    "dbo.StringList",
                    "gl.InvoiceLinesList",
                    "gl.InvoicesList",
                ],
                types);

            // Columns, in ordinal order: excluded column absent, rowversion present as binary(8),
            // computed column absent.
            Assert.Equal(
                ["Id", "LoyaltyPoints", "Name"],
                await IntegrationHelpers.GetTypeColumnsAsync(connectionString, "crm", "CustomersList"));
            Assert.Equal(
                ["Id", "CustomerId", "Memo", "Total", "RowVersion"],
                await IntegrationHelpers.GetTypeColumnsAsync(connectionString, "gl", "InvoicesList"));

            string invoicesPhysical = (await IntegrationHelpers.GetPhysicalNameAsync(connectionString, "gl", "InvoicesList"))!;
            string? rowVersionType = await IntegrationHelpers.ScalarAsync<string>(
                connectionString,
                $"""
                SELECT TYPE_NAME(c.[system_type_id]) + N'(' + CAST(c.[max_length] AS nvarchar(10)) + N')'
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[columns] c ON c.[object_id] = tt.[type_table_object_id]
                WHERE tt.[name] = N'{invoicesPhysical}' AND c.[name] = N'RowVersion'
                """);
            Assert.Equal("binary(8)", rowVersionType);

            // The primary key mirrors the table's.
            List<string> pkColumns = await IntegrationHelpers.ColumnAsync(
                connectionString,
                $"""
                SELECT c.[name]
                FROM [sys].[table_types] tt
                INNER JOIN [sys].[indexes] i ON i.[object_id] = tt.[type_table_object_id] AND i.[is_primary_key] = 1
                INNER JOIN [sys].[index_columns] ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
                INNER JOIN [sys].[columns] c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                WHERE tt.[name] = N'{invoicesPhysical}'
                ORDER BY ic.[key_ordinal]
                """);
            Assert.Equal(["Id"], pkColumns);

            // Grants were emitted after every create.
            long grantCount = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                """
                SELECT COUNT(*)
                FROM [sys].[database_permissions] p
                INNER JOIN [sys].[table_types] tt ON p.[major_id] = tt.[user_type_id]
                WHERE p.[class_desc] = N'TYPE' AND p.[permission_name] = N'EXECUTE' AND p.[state] = N'G'
                  AND USER_NAME(p.[grantee_principal_id]) = N'public'
                """);
            Assert.Equal(8, grantCount);

            // Every type is stamped with its owning scope (spec 0001 §3 → Versioning).
            int scopeStamps = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                $"""
                SELECT COUNT(*) FROM [sys].[extended_properties]
                WHERE [class] = 6 AND [name] = N'Tellma:TableType:Scope'
                  AND CONVERT(nvarchar(max), [value]) = N'{MigrationsHostContext.SweepScope}'
                """);
            Assert.Equal(8, scopeStamps);

            // Rule 5 layer 2: nothing persisted references any type after the full chain.
            int dependents = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                "SELECT COUNT(*) FROM [sys].[sql_expression_dependencies] WHERE [referenced_class] = 6");
            Assert.Equal(0, dependents);
        }

        [Fact]
        public async Task A_new_version_coexists_and_the_sweep_collects_the_old_one()
        {
            string connectionString = await fixture.CreateDatabaseAsync("versioning");
            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            string v1 = (await IntegrationHelpers.GetPhysicalNameAsync(connectionString, "gl", "InvoicesList"))!;

            // Deploy a second version of InvoicesList under a different physical name — exactly what a
            // definitional change scaffolds (create-alongside, never an in-place recreate).
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator generator =
                context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator>();
            CreateTableTypeOperation v2 = new()
            {
                Name = "InvoicesList",
                PhysicalName = "InvoicesList_v2c0ffee",
                Schema = "gl",
                Scope = MigrationsHostContext.SweepScope,
                DefinitionHash = new string('a', 64),
                PrimaryKey = ["Id"],
                Grants = ["public"],
            };
            v2.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });
            await IntegrationHelpers.ExecuteAsync(connectionString, Assert.Single(generator.Generate([v2])).CommandText);

            // Both versions coexist (the N−1 app keeps binding v1 through the deployment window).
            int invoicesVersions = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                """
                SELECT COUNT(*) FROM [sys].[table_types] tt
                INNER JOIN [sys].[extended_properties] ep ON ep.[class] = 6 AND ep.[major_id] = tt.[user_type_id]
                    AND ep.[name] = N'Tellma:TableType:LogicalName' AND CONVERT(nvarchar(max), ep.[value]) = N'InvoicesList'
                """);
            Assert.Equal(2, invoicesVersions);

            // The keep-list is every current physical name except v1. A zero-grace sweep marks v1
            // (first pass) then collects it (second pass) — the immediate-collection path.
            List<string> keep = await IntegrationHelpers.ColumnAsync(
                connectionString,
                $"""
                SELECT tt.[name] FROM [sys].[table_types] tt
                INNER JOIN [sys].[extended_properties] ep ON ep.[class] = 6 AND ep.[major_id] = tt.[user_type_id]
                    AND ep.[name] = N'Tellma:TableType:Scope' AND CONVERT(nvarchar(max), ep.[value]) = N'{MigrationsHostContext.SweepScope}'
                WHERE tt.[name] <> N'{v1}'
                """);
            CleanupTableTypesOperation sweep = new()
            {
                Scope = MigrationsHostContext.SweepScope,
                KeepList = [.. keep],
                GracePeriodHours = 0,
            };
            string sweepSql = Assert.Single(generator.Generate([sweep])).CommandText;
            await IntegrationHelpers.ExecuteAsync(connectionString, sweepSql); // marks v1 orphaned
            await IntegrationHelpers.ExecuteAsync(connectionString, sweepSql); // collects v1 (grace elapsed)

            int v1Remaining = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, $"SELECT COUNT(*) FROM [sys].[table_types] WHERE [name] = N'{v1}'");
            Assert.Equal(0, v1Remaining);
            int v2Remaining = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT COUNT(*) FROM [sys].[table_types] WHERE [name] = N'InvoicesList_v2c0ffee'");
            Assert.Equal(1, v2Remaining);
        }

        [Fact]
        public async Task Type_column_order_matches_the_tables_column_order()
        {
            // The ordinal-binding contract: the UDTT's columns appear in the same relative order
            // as the physical table's (the type omits excluded/computed columns). Pins our
            // public-API ordering rule against EF's private CREATE TABLE sorting.
            string connectionString = await fixture.CreateDatabaseAsync("parity");
            await using (MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            foreach ((string schema, string table, string type) in new[]
            {
                ("crm", "Customers", "CustomersList"),
                ("gl", "Invoices", "InvoicesList"),
                ("gl", "InvoiceLines", "InvoiceLinesList"),
            })
            {
                List<string> tableColumns = await IntegrationHelpers.GetTableColumnsAsync(connectionString, schema, table);
                List<string> typeColumns = await IntegrationHelpers.GetTypeColumnsAsync(connectionString, schema, type);

                Assert.Equal(tableColumns.Where(typeColumns.Contains), typeColumns);
            }
        }

        [Fact]
        public async Task Migrate_is_idempotent_and_the_idempotent_script_runs_twice()
        {
            string connectionString = await fixture.CreateDatabaseAsync("idem");
            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            // Re-running Migrate is a no-op.
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            // The full idempotent script also runs cleanly against the migrated database.
            Microsoft.EntityFrameworkCore.Migrations.IMigrator migrator =
                AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(context);
            string script = migrator.GenerateScript(
                options: Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Idempotent);
            await IntegrationHelpers.ExecuteScriptAsync(connectionString, script);

            int typeCount = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT COUNT(*) FROM [sys].[table_types]");
            Assert.Equal(8, typeCount);
        }

        [Fact]
        public async Task EnsureCreated_also_creates_the_types()
        {
            // EnsureCreated flows through the same differ + SQL generator (differences from an
            // empty model), so non-migration scenarios get types too.
            string connectionString = await fixture.CreateDatabaseAsync("ensure");
            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            int typeCount = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT COUNT(*) FROM [sys].[table_types]");
            Assert.Equal(8, typeCount);
        }
    }
}
