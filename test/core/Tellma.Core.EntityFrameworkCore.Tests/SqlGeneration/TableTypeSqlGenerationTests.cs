// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.SqlGeneration
{
    /// <summary>
    ///     SQL assertions for the table-type operations against the real
    ///     <see cref="IMigrationsSqlGenerator" /> (no database involved). The content-addressed
    ///     create/cleanup SQL is large and embeds hashes, so these assert the load-bearing fragments
    ///     (the idempotency gate, the stamps, the integrity/ownership THROWs, the sweep, escaping)
    ///     rather than a brittle byte-exact golden.
    /// </summary>
    public class TableTypeSqlGenerationTests
    {
        /// <summary>Generates the single command for one operation through the resolved SQL generator.</summary>
        private static MigrationCommand Generate(
            MigrationOperation operation,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            return Assert.Single(generator.Generate([operation], model: null, options));
        }

        /// <summary>Normalizes line endings so assertions are platform-stable.</summary>
        private static string Text(MigrationCommand command)
        {
            return command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

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
            };
            operation.Columns.AddRange(
            [
                new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                new TableTypeColumnDefinition { Name = "Memo", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
            ]);
            return operation;
        }

        [Fact]
        public void Create_is_idempotent_and_creates_under_the_physical_name_with_mirrored_primary_key()
        {
            string sql = Text(Generate(CreateOrdersListOperation()));

            Assert.Contains("IF TYPE_ID(@fq_abc12345def67890) IS NULL", sql, StringComparison.Ordinal);
            Assert.Contains("CREATE TYPE [gl].[OrdersList_abc12345] AS TABLE (", sql, StringComparison.Ordinal);
            Assert.Contains("[Id] int NOT NULL,", sql, StringComparison.Ordinal);
            Assert.Contains("[Memo] nvarchar(255) NULL,", sql, StringComparison.Ordinal);
            Assert.Contains("PRIMARY KEY CLUSTERED ([Id])", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Create_stamps_logical_name_scope_and_hash()
        {
            string sql = Text(Generate(CreateOrdersListOperation()));

            Assert.Contains("sp_addextendedproperty", sql, StringComparison.Ordinal);
            Assert.Contains("N'Tellma:TableType:LogicalName'", sql, StringComparison.Ordinal);
            Assert.Contains("N'Tellma:TableType:Scope'", sql, StringComparison.Ordinal);
            Assert.Contains("N'Tellma:TableType:DefinitionHash'", sql, StringComparison.Ordinal);
            Assert.Contains("N'OrdersList'", sql, StringComparison.Ordinal);
            Assert.Contains("N'TestScope'", sql, StringComparison.Ordinal);
            Assert.Contains("N'abc12345def67890'", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Create_else_branch_repairs_unstamped_and_throws_53103_then_53104()
        {
            string sql = Text(Generate(CreateOrdersListOperation()));

            // Aborted-create repair, then the two distinct THROWs.
            Assert.Contains("IF @existingHash_abc12345def67890 IS NULL", sql, StringComparison.Ordinal);
            Assert.Contains("sp_updateextendedproperty", sql, StringComparison.Ordinal);
            Assert.Contains("THROW 53103", sql, StringComparison.Ordinal);
            Assert.Contains("THROW 53104", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Create_emits_collation_and_composite_primary_key()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "PeopleList",
                PhysicalName = "PeopleList_0011aabb",
                Schema = null,
                Scope = "TestScope",
                DefinitionHash = "0011aabb",
                PrimaryKey = ["TenantId", "Id"],
            };
            operation.Columns.AddRange(
            [
                new TableTypeColumnDefinition { Name = "TenantId", StoreType = "int" },
                new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                new TableTypeColumnDefinition
                {
                    Name = "Name",
                    StoreType = "nvarchar(100)",
                    IsNullable = true,
                    MaxLength = 100,
                    Collation = "Latin1_General_100_CI_AS",
                },
            ]);

            string sql = Text(Generate(operation));

            Assert.Contains("CREATE TYPE [PeopleList_0011aabb] AS TABLE (", sql, StringComparison.Ordinal);
            Assert.Contains("[Name] nvarchar(100) COLLATE Latin1_General_100_CI_AS NULL", sql, StringComparison.Ordinal);
            Assert.Contains("PRIMARY KEY CLUSTERED ([TenantId], [Id])", sql, StringComparison.Ordinal);
            // Null schema resolves to the database default for both create and stamps.
            Assert.Contains("sysname = SCHEMA_NAME()", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Create_emits_grants_under_the_physical_name()
        {
            CreateTableTypeOperation operation = CreateOrdersListOperation();
            operation.Grants = ["tellma_app", "tellma_jobs"];

            string sql = Text(Generate(operation));

            Assert.Contains("GRANT EXECUTE ON TYPE::[gl].[OrdersList_abc12345] TO [tellma_app];", sql, StringComparison.Ordinal);
            Assert.Contains("GRANT EXECUTE ON TYPE::[gl].[OrdersList_abc12345] TO [tellma_jobs];", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Create_memory_optimized_preflights_and_suppresses_the_transaction()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "OrdersList",
                PhysicalName = "OrdersList_abc12345",
                Schema = "gl",
                Scope = "TestScope",
                DefinitionHash = "abc12345",
                PrimaryKey = ["Id"],
                IsMemoryOptimized = true,
            };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });

            MigrationCommand command = Generate(operation);
            string sql = Text(command);

            Assert.Contains("IF DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') <> 1", sql, StringComparison.Ordinal);
            Assert.Contains("THROW 53101", sql, StringComparison.Ordinal);
            Assert.Contains("PRIMARY KEY NONCLUSTERED ([Id])", sql, StringComparison.Ordinal);
            Assert.Contains("WITH (MEMORY_OPTIMIZED = ON)", sql, StringComparison.Ordinal);
            Assert.True(command.TransactionSuppressed);
        }

        [Fact]
        public void Create_without_columns_throws()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "EmptyList",
                PhysicalName = "EmptyList_0",
                Schema = "gl",
                Scope = "TestScope",
                DefinitionHash = "0",
            };

            Assert.Throws<InvalidOperationException>(() => { _ = Generate(operation); });
        }

        [Fact]
        public void Create_memory_optimized_without_primary_key_throws()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "NoPkList",
                PhysicalName = "NoPkList_0",
                Schema = "gl",
                Scope = "TestScope",
                DefinitionHash = "0",
                IsMemoryOptimized = true,
            };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = Generate(operation); });
            Assert.Contains("primary key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Drop_guards_against_persisted_dependents_then_drops_by_physical_name()
        {
            DropTableTypeOperation operation = new() { Name = "OrdersList_abc12345", Schema = "gl" };

            MigrationCommand command = Generate(operation);
            string sql = Text(command);

            Assert.Contains("[sys].[sql_expression_dependencies]", sql, StringComparison.Ordinal);
            Assert.Contains("tt.[name] = N'OrdersList_abc12345'", sql, StringComparison.Ordinal);
            Assert.Contains("THROW 53102, @error, 1;", sql, StringComparison.Ordinal);
            Assert.Contains("DROP TYPE [gl].[OrdersList_abc12345];", sql, StringComparison.Ordinal);
            Assert.False(command.TransactionSuppressed);
        }

        [Fact]
        public void Drop_of_memory_optimized_type_suppresses_the_transaction()
        {
            DropTableTypeOperation operation = new() { Name = "OrdersList_x", Schema = "gl", IsMemoryOptimized = true };

            Assert.True(Generate(operation).TransactionSuppressed);
        }

        [Fact]
        public void Cleanup_sweeps_the_scope_marks_clears_and_collects_and_is_suppressed()
        {
            CleanupTableTypesOperation operation = new()
            {
                Scope = "TestScope",
                KeepList = ["OrdersList_abc12345", "IdList_999"],
                GracePeriodHours = 48,
            };

            MigrationCommand command = Generate(operation);
            string sql = Text(command);

            Assert.Contains("DECLARE @scope nvarchar(450) = N'TestScope'", sql, StringComparison.Ordinal);
            Assert.Contains("DECLARE @grace int = 48", sql, StringComparison.Ordinal);
            Assert.Contains("(N'OrdersList_abc12345'), (N'IdList_999')", sql, StringComparison.Ordinal);
            Assert.Contains("SYSUTCDATETIME()", sql, StringComparison.Ordinal);
            Assert.Contains("sp_dropextendedproperty", sql, StringComparison.Ordinal); // clear orphan mark
            Assert.Contains("N'Tellma:TableType:OrphanedAtUtc'", sql, StringComparison.Ordinal); // mark
            Assert.Contains("DATEADD(HOUR, @grace, @orphanedAt) <= @now", sql, StringComparison.Ordinal); // collect gate
            Assert.Contains("RAISERROR", sql, StringComparison.Ordinal); // skip-and-surface, not THROW
            Assert.DoesNotContain("THROW 5310", sql, StringComparison.Ordinal); // sweep never throws the guard
            Assert.True(command.TransactionSuppressed);
        }

        [Fact]
        public void Generated_sql_escapes_values_and_delimits_principals()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "List",
                PhysicalName = "List_00000000",
                Schema = "gl",
                Scope = "weird' scope--",
                DefinitionHash = "00000000",
                PrimaryKey = ["Id"],
                Grants = ["odd]principal"],
            };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });

            string sql = Text(Generate(operation));

            // Value: the single quote is doubled inside the N'...' literal (no injection).
            Assert.Contains("N'weird'' scope--'", sql, StringComparison.Ordinal);
            // Identifier: the principal's bracket is doubled by QUOTENAME/DelimitIdentifier.
            Assert.Contains("TO [odd]]principal]", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void Idempotent_option_generates_identical_command_text()
        {
            CreateTableTypeOperation operation = CreateOrdersListOperation();

            MigrationCommand plain = Generate(operation);
            MigrationCommand idempotent = Generate(operation, MigrationsSqlGenerationOptions.Idempotent);

            Assert.Equal(plain.CommandText, idempotent.CommandText);
        }

        [Fact]
        public void Table_type_and_cleanup_sql_generate_together_for_a_real_model()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.HasTableTypeGrants("public");
                }));

            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            IReadOnlyList<MigrationOperation> operations =
                differ.GetDifferences(null, TestModel.GetRelationalModel(context));
            IReadOnlyList<MigrationCommand> commands = generator.Generate(operations, TestModel.GetFinalizedModel(context));

            string all = string.Join("\n---\n", commands.Select(Text));
            Assert.Contains("CREATE TABLE [gl].[Orders]", all, StringComparison.Ordinal);
            Assert.Contains("CREATE TYPE [gl].[OrdersList_", all, StringComparison.Ordinal);
            Assert.Contains("DECLARE @scope nvarchar(450) = N'TestScope'", all, StringComparison.Ordinal);
            Assert.True(
                all.IndexOf("CREATE TABLE", StringComparison.Ordinal) < all.IndexOf("CREATE TYPE", StringComparison.Ordinal));
            // The cleanup sweep is the last command.
            Assert.Contains("tellma_tt_cleanup", Text(commands[^1]), StringComparison.Ordinal);
        }
    }
}
