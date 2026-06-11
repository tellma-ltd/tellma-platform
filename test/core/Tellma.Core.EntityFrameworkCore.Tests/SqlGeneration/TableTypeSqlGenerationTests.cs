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
    ///     Golden-SQL assertions for the table-type operations against the real
    ///     <see cref="IMigrationsSqlGenerator" /> (no database involved).
    /// </summary>
    public class TableTypeSqlGenerationTests
    {
        /// <summary>Generates commands for one operation through the resolved SQL generator.</summary>
        private static IReadOnlyList<MigrationCommand> Generate(
            MigrationOperation operation,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            return generator.Generate([operation], model: null, options);
        }

        /// <summary>Normalizes line endings so goldens are platform-stable.</summary>
        private static string Text(MigrationCommand command)
        {
            return command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static CreateTableTypeOperation CreateOrdersListOperation()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "OrdersList",
                Schema = "gl",
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
        public void Create_emits_type_with_mirrored_primary_key()
        {
            MigrationCommand command = Assert.Single(Generate(CreateOrdersListOperation()));

            Assert.Equal(
                """
                CREATE TYPE [gl].[OrdersList] AS TABLE (
                    [Id] int NOT NULL,
                    [Memo] nvarchar(255) NULL,
                    PRIMARY KEY CLUSTERED ([Id])
                );

                """,
                Text(command));
            Assert.False(command.TransactionSuppressed);
        }

        [Fact]
        public void Create_emits_collation_and_composite_primary_key()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "PeopleList",
                Schema = null,
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

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.Equal(
                """
                CREATE TYPE [PeopleList] AS TABLE (
                    [TenantId] int NOT NULL,
                    [Id] int NOT NULL,
                    [Name] nvarchar(100) COLLATE Latin1_General_100_CI_AS NULL,
                    PRIMARY KEY CLUSTERED ([TenantId], [Id])
                );

                """,
                Text(command));
        }

        [Fact]
        public void Create_emits_grants_after_the_type()
        {
            CreateTableTypeOperation operation = CreateOrdersListOperation();
            operation.Grants = ["tellma_app", "tellma_jobs"];

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.Equal(
                """
                CREATE TYPE [gl].[OrdersList] AS TABLE (
                    [Id] int NOT NULL,
                    [Memo] nvarchar(255) NULL,
                    PRIMARY KEY CLUSTERED ([Id])
                );
                GRANT EXECUTE ON TYPE::[gl].[OrdersList] TO [tellma_app];
                GRANT EXECUTE ON TYPE::[gl].[OrdersList] TO [tellma_jobs];

                """,
                Text(command));
        }

        [Fact]
        public void Create_memory_optimized_preflights_and_suppresses_the_transaction()
        {
            CreateTableTypeOperation operation = new()
            {
                Name = "OrdersList",
                Schema = "gl",
                PrimaryKey = ["Id"],
                IsMemoryOptimized = true,
            };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.Equal(
                """
                IF DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') <> 1
                    THROW 53101, N'Cannot create memory-optimized table type [gl].[OrdersList]: In-Memory OLTP is not supported on this database tier or edition (DATABASEPROPERTYEX ''IsXTPSupported'' <> 1). Use a Premium/Business Critical tier or a memory-optimized filegroup, or remove the memory-optimized configuration from the table type.', 1;
                CREATE TYPE [gl].[OrdersList] AS TABLE (
                    [Id] int NOT NULL,
                    PRIMARY KEY NONCLUSTERED ([Id])
                ) WITH (MEMORY_OPTIMIZED = ON);

                """,
                Text(command));
            Assert.True(command.TransactionSuppressed);
        }

        [Fact]
        public void Create_without_columns_throws()
        {
            CreateTableTypeOperation operation = new() { Name = "EmptyList", Schema = "gl" };

            Assert.Throws<InvalidOperationException>(() => { _ = Generate(operation); });
        }

        [Fact]
        public void Create_memory_optimized_without_primary_key_throws()
        {
            CreateTableTypeOperation operation = new() { Name = "NoPkList", Schema = "gl", IsMemoryOptimized = true };
            operation.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = Generate(operation); });
            Assert.Contains("primary key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Drop_guards_against_persisted_dependents_then_drops()
        {
            DropTableTypeOperation operation = new() { Name = "OrdersList", Schema = "gl" };

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.Equal(
                """
                DECLARE @dependents nvarchar(max) = (
                    SELECT STRING_AGG(QUOTENAME(OBJECT_SCHEMA_NAME(d.[referencing_id])) + N'.' + QUOTENAME(OBJECT_NAME(d.[referencing_id])), N', ')
                    FROM [sys].[sql_expression_dependencies] AS d
                    INNER JOIN [sys].[table_types] AS tt ON tt.[user_type_id] = d.[referenced_id]
                    WHERE d.[referenced_class] = 6 AND tt.[name] = N'OrdersList' AND SCHEMA_NAME(tt.[schema_id]) = N'gl');
                IF @dependents IS NOT NULL
                BEGIN
                    DECLARE @error nvarchar(2048) = N'Cannot drop table type [gl].[OrdersList]: it is referenced by persisted SQL module(s) ' + @dependents + N'. Per the Tellma architecture, no persisted SQL module may reference a generated table type; all consumers must be dynamic SQL composed in C#.';
                    THROW 53102, @error, 1;
                END;
                DROP TYPE [gl].[OrdersList];

                """,
                Text(command));
            Assert.False(command.TransactionSuppressed);
        }

        [Fact]
        public void Drop_without_schema_matches_the_default_schema()
        {
            DropTableTypeOperation operation = new() { Name = "OrdersList" };

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.Contains("SCHEMA_NAME(tt.[schema_id]) = SCHEMA_NAME()", Text(command), StringComparison.Ordinal);
            Assert.Contains("DROP TYPE [OrdersList];", Text(command), StringComparison.Ordinal);
        }

        [Fact]
        public void Drop_of_memory_optimized_type_suppresses_the_transaction()
        {
            DropTableTypeOperation operation = new() { Name = "OrdersList", Schema = "gl", IsMemoryOptimized = true };

            MigrationCommand command = Assert.Single(Generate(operation));

            Assert.True(command.TransactionSuppressed);
        }

        [Fact]
        public void Idempotent_option_generates_identical_command_text()
        {
            // The idempotent wrapper (IF NOT EXISTS ... BEGIN ... END around each command) is
            // added by the migrator when scripting, not by the SQL generator; the operation SQL
            // itself must be identical — and legal inside that wrapper (no batch-position
            // restricted statements).
            CreateTableTypeOperation operation = CreateOrdersListOperation();

            MigrationCommand plain = Assert.Single(Generate(operation));
            MigrationCommand idempotent = Assert.Single(Generate(operation, MigrationsSqlGenerationOptions.Idempotent));

            Assert.Equal(plain.CommandText, idempotent.CommandText);
        }

        [Fact]
        public void Table_and_type_sql_generate_together_for_a_real_model()
        {
            // End-to-end (still no database): diff an empty database against a real model and
            // generate the full SQL batch — the type DDL must come after the table DDL.
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
            Assert.Contains("CREATE TYPE [gl].[OrdersList] AS TABLE", all, StringComparison.Ordinal);
            Assert.True(
                all.IndexOf("CREATE TABLE", StringComparison.Ordinal) < all.IndexOf("CREATE TYPE", StringComparison.Ordinal));
        }
    }
}
