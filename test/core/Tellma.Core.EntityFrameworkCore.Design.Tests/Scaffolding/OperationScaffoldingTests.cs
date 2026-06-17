// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.EntityFrameworkCore.Design.Tests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.Scaffolding
{
    /// <summary>
    ///     Scaffolding tests: the C# emitted for the table-type operations (golden), and the full
    ///     round-trip — generated migration code compiles with Roslyn and, when executed, rebuilds
    ///     operations equal to the originals.
    /// </summary>
    public class OperationScaffoldingTests
    {
        private static CreateTableTypeOperation CreateOrdersListOperation()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "OrdersList",
                PhysicalName = "OrdersList_abc12345",
                Schema = "gl",
                Scope = "TestScope",
                DefinitionHash = "abc12345def67890",
                PrimaryKey = ["Id"],
                IsMemoryOptimized = true,
                Grants = ["tellma_app"],
            };
            operation.Columns.AddRange(
            [
                new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                new TableTypeColumnDefinition
                {
                    Name = "Memo",
                    StoreType = "nvarchar(255)",
                    IsNullable = true,
                    MaxLength = 255,
                    Collation = "Latin1_General_100_CI_AS",
                },
                new TableTypeColumnDefinition
                {
                    Name = "Price",
                    StoreType = "decimal(19,4)",
                    Precision = 19,
                    Scale = 4,
                },
                new TableTypeColumnDefinition
                {
                    Name = "RowVersion",
                    StoreType = "binary(8)",
                    IsNullable = true,
                    MaxLength = 8,
                    IsRowVersion = true,
                },
            ]);
            return operation;
        }

        private static string GenerateCode(params MigrationOperation[] operations)
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ServiceProvider services = DesignTestHelpers.BuildDesignServices(context);
            ICSharpMigrationOperationGenerator generator = services.GetRequiredService<ICSharpMigrationOperationGenerator>();

            IndentedStringBuilder builder = new();
            generator.Generate("migrationBuilder", operations, builder);
            return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        [Fact]
        public void The_registered_operation_generator_is_tellmas()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ServiceProvider services = DesignTestHelpers.BuildDesignServices(context);

            Assert.IsType<TableTypesCSharpMigrationOperationGenerator>(
                services.GetRequiredService<ICSharpMigrationOperationGenerator>());
            Assert.IsType<TableTypesCSharpMigrationsGenerator>(
                services.GetRequiredService<IMigrationsCodeGenerator>());
        }

        [Fact]
        public void CreateTableType_scaffolds_golden_csharp()
        {
            string code = GenerateCode(CreateOrdersListOperation());

            Assert.Equal(
                """
                migrationBuilder.CreateTableType(
                    name: "OrdersList",
                    physicalName: "OrdersList_abc12345",
                    schema: "gl",
                    scope: "TestScope",
                    definitionHash: "abc12345def67890",
                    columns: new[]
                    {
                        new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                        new TableTypeColumnDefinition { Name = "Memo", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255, Collation = "Latin1_General_100_CI_AS" },
                        new TableTypeColumnDefinition { Name = "Price", StoreType = "decimal(19,4)", Precision = 19, Scale = 4 },
                        new TableTypeColumnDefinition { Name = "RowVersion", StoreType = "binary(8)", IsNullable = true, MaxLength = 8, IsRowVersion = true },
                    },
                    primaryKey: new[] { "Id" },
                    memoryOptimized: true,
                    grants: new[] { "tellma_app" });
                """,
                code);
        }

        [Fact]
        public void CleanupTableTypes_scaffolds_golden_csharp()
        {
            string code = GenerateCode(new CleanupTableTypesOperation
            {
                Scope = "TestScope",
                KeepList = ["OrdersList_abc12345", "IdList_999"],
                GracePeriodHours = 48,
            });

            Assert.Equal(
                """migrationBuilder.CleanupTableTypes(scope: "TestScope", keepList: new[] { "OrdersList_abc12345", "IdList_999" });""",
                code);
        }

        [Fact]
        public void CleanupTableTypes_with_empty_keep_list_scaffolds_golden_csharp()
        {
            // The common Down()/opt-out shape: an empty keep-list orphans everything in scope.
            string code = GenerateCode(new CleanupTableTypesOperation { Scope = "TestScope", KeepList = [] });

            Assert.Equal(
                """migrationBuilder.CleanupTableTypes(scope: "TestScope", keepList: new string[0]);""",
                code);
        }

        [Fact]
        public void DropTableType_scaffolds_golden_csharp()
        {
            string code = GenerateCode(new DropTableTypeOperation { Name = "OrdersList", Schema = "gl", IsMemoryOptimized = true });

            Assert.Equal(
                """migrationBuilder.DropTableType(name: "OrdersList", schema: "gl", memoryOptimized: true);""",
                code);
        }

        [Fact]
        public void Scaffolded_migration_compiles_and_round_trips_the_operations()
        {
            CreateTableTypeOperation create = CreateOrdersListOperation();
            DropTableTypeOperation drop = new() { Name = "OldList_dead", Schema = "gl" };
            CleanupTableTypesOperation cleanup = new()
            {
                Scope = "TestScope",
                KeepList = ["OrdersList_abc12345"],
                GracePeriodHours = 48,
            };

            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ServiceProvider services = DesignTestHelpers.BuildDesignServices(context);
            IMigrationsCodeGenerator codeGenerator = services.GetRequiredService<IMigrationsCodeGenerator>();

            string migrationCode = codeGenerator.GenerateMigration("RoundTrip.Migrations", "TestMigration", [create, drop, cleanup], []);

            // The migration file must carry the usings for the operation/extension namespaces.
            Assert.Contains("using Tellma.Core.EntityFrameworkCore.TableTypes;", migrationCode, StringComparison.Ordinal);
            Assert.Contains("using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;", migrationCode, StringComparison.Ordinal);

            Assembly assembly = DesignTestHelpers.Compile(migrationCode, "RoundTripMigration");
            Type migrationType = assembly.GetType("RoundTrip.Migrations.TestMigration")!;
            object migration = Activator.CreateInstance(migrationType)!;

            MigrationBuilder migrationBuilder = new("Microsoft.EntityFrameworkCore.SqlServer");
            MethodInfo up = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
            up.Invoke(migration, [migrationBuilder]);

            Assert.Equal(3, migrationBuilder.Operations.Count);
            CreateTableTypeOperation roundTrippedCreate = Assert.IsType<CreateTableTypeOperation>(migrationBuilder.Operations[0]);
            DropTableTypeOperation roundTrippedDrop = Assert.IsType<DropTableTypeOperation>(migrationBuilder.Operations[1]);
            CleanupTableTypesOperation roundTrippedCleanup = Assert.IsType<CleanupTableTypesOperation>(migrationBuilder.Operations[2]);

            Assert.Equal(create.Name, roundTrippedCreate.Name);
            Assert.Equal(create.PhysicalName, roundTrippedCreate.PhysicalName);
            Assert.Equal(create.Schema, roundTrippedCreate.Schema);
            Assert.Equal(create.Scope, roundTrippedCreate.Scope);
            Assert.Equal(create.DefinitionHash, roundTrippedCreate.DefinitionHash);
            Assert.Equal(create.PrimaryKey, roundTrippedCreate.PrimaryKey);
            Assert.Equal(create.IsMemoryOptimized, roundTrippedCreate.IsMemoryOptimized);
            Assert.Equal(create.Grants, roundTrippedCreate.Grants);
            Assert.Equal(create.Columns, roundTrippedCreate.Columns); // record value equality, member by member
            Assert.Equal(drop.Name, roundTrippedDrop.Name);
            Assert.Equal(drop.Schema, roundTrippedDrop.Schema);
            Assert.Equal(cleanup.Scope, roundTrippedCleanup.Scope);
            Assert.Equal(cleanup.KeepList, roundTrippedCleanup.KeepList);
            Assert.Equal(cleanup.GracePeriodHours, roundTrippedCleanup.GracePeriodHours);
        }
    }
}
