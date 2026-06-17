// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.IntegrationTests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    ///     Memory-optimized table types: the pre-flight passes on XTP-capable hosts and the type
    ///     deploys with <c>is_memory_optimized = 1</c>, including a JSON column carried as
    ///     <c>nvarchar(max)</c> (the on-disk <c>varchar(max)</c> UTF-8 form and the native <c>json</c>
    ///     type are both rejected on memory-optimized tables — spec 0001 §2). (The THROW path on
    ///     unsupported tiers is covered by the golden SQL unit tests; a supporting server cannot
    ///     exercise it.)
    /// </summary>
    /// <param name="fixture">The shared SQL Server.</param>
    [Trait("Category", "Integration")]
    public class MemoryOptimizedTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Memory_optimized_type_deploys_on_xtp_capable_hosts()
        {
            string connectionString = await fixture.CreateDatabaseAsync("memopt");

            int xtpSupported = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') AS int)");
            Assert.SkipWhen(xtpSupported != 1, "In-Memory OLTP is not supported on this SQL Server host (e.g. LocalDB).");

            // Creating any memory-optimized object requires a MEMORY_OPTIMIZED_DATA filegroup
            // (implicit on Azure Premium/Business Critical; explicit on-prem/container).
            string? dataPath = await IntegrationHelpers.ScalarAsync<string>(
                connectionString, "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512))");
            string dbName = (await IntegrationHelpers.ScalarAsync<string>(connectionString, "SELECT DB_NAME()"))!;
            char separator = dataPath!.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
            string filePath = dataPath.TrimEnd('\\', '/') + separator + dbName + "_mod";
            await IntegrationHelpers.ExecuteAsync(
                connectionString,
                $"ALTER DATABASE [{dbName}] ADD FILEGROUP [mod] CONTAINS MEMORY_OPTIMIZED_DATA",
                $"ALTER DATABASE [{dbName}] ADD FILE (NAME = N'{dbName}_mod', FILENAME = N'{filePath}') TO FILEGROUP [mod]");

            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            CreateTableTypeOperation operation = new()
            {
                Name = "HotIdList",
                PhysicalName = "HotIdList_0badf00d",
                Schema = "dbo",
                Scope = MigrationsHostContext.SweepScope,
                DefinitionHash = new string('0', 64),
                PrimaryKey = ["Id"],
                IsMemoryOptimized = true,
            };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });
            // A JSON column on a memory-optimized type: nvarchar(max), since UTF-8 collations (12356)
            // and the native json type (10794) are both rejected there. If that were wrong, the
            // CREATE TYPE below would throw and fail this test.
            operation.Columns.Add(new TableTypeColumnDefinition
            {
                Name = "Payload",
                StoreType = "nvarchar(max)",
                IsNullable = true,
                IsJson = true,
            });

            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            string sql = Assert.Single(generator.Generate([operation])).CommandText;
            await IntegrationHelpers.ExecuteAsync(connectionString, sql);

            int isMemoryOptimized = await IntegrationHelpers.ScalarAsync<int>(
                connectionString,
                "SELECT CAST([is_memory_optimized] AS int) FROM [sys].[table_types] WHERE [name] = N'HotIdList_0badf00d'");
            Assert.Equal(1, isMemoryOptimized);

            // The JSON column deployed as nvarchar(max) (max_length -1 for the (max) LOB form).
            string payloadType = await IntegrationHelpers.ScalarAsync<string>(
                connectionString,
                "SELECT ty.[name] FROM [sys].[table_types] tt "
                + "JOIN [sys].[columns] c ON c.[object_id] = tt.[type_table_object_id] "
                + "JOIN [sys].[types] ty ON ty.[user_type_id] = c.[user_type_id] "
                + "WHERE tt.[name] = N'HotIdList_0badf00d' AND c.[name] = N'Payload'") ?? string.Empty;
            Assert.Equal("nvarchar", payloadType);
        }
    }
}
