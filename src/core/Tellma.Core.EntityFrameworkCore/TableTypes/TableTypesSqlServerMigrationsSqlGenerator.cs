// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     A <see cref="SqlServerMigrationsSqlGenerator" /> that additionally generates SQL for the
    ///     table-type operations (<see cref="CreateTableTypeOperation" /> /
    ///     <see cref="DropTableTypeOperation" />). Installed by <c>UseTableTypes()</c> in place of
    ///     the provider's generator; all other operations fall through to the base class.
    /// </summary>
    /// <remarks>
    ///     The generated statements are deliberately legal inside the per-command
    ///     <c>IF NOT EXISTS (...) BEGIN ... END</c> wrapper of idempotent scripts
    ///     (<c>dotnet ef migrations script --idempotent</c>): unlike <c>CREATE PROCEDURE</c>,
    ///     <c>CREATE TYPE</c>, <c>DECLARE</c> and <c>THROW</c> carry no batch-position restrictions.
    /// </remarks>
    /// <param name="dependencies">The dependencies of the migrations SQL generator; passes through to the provider's generator.</param>
    /// <param name="commandBatchPreparer">The command batch preparer; passes through to the provider's generator.</param>
    public class TableTypesSqlServerMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        ICommandBatchPreparer commandBatchPreparer)
        : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
    {
        /// <summary>
        ///     Dispatches the table-type operations; every other operation goes to the SQL Server
        ///     provider's generator. This override of the base class's unknown-operation fallback is
        ///     the supported extension point for custom operations.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="model">The target model, when available.</param>
        /// <param name="builder">The command builder.</param>
        protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            switch (operation)
            {
                case CreateTableTypeOperation createTableType:
                    Generate(createTableType, builder);
                    break;
                case DropTableTypeOperation dropTableType:
                    Generate(dropTableType, builder);
                    break;
                default:
                    base.Generate(operation, model, builder);
                    break;
            }
        }

        /// <summary>
        ///     Generates <c>CREATE TYPE ... AS TABLE</c> with the mirrored primary key, the
        ///     In-Memory OLTP pre-flight guard when memory-optimized, and the configured
        ///     <c>GRANT EXECUTE ON TYPE</c> statements.
        /// </summary>
        /// <param name="operation">The create operation.</param>
        /// <param name="builder">The command builder.</param>
        protected virtual void Generate(CreateTableTypeOperation operation, MigrationCommandListBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            if (operation.Columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create table type {DisplayName(operation.Name, operation.Schema)} without columns.");
            }

            if (operation.IsMemoryOptimized && operation.PrimaryKey.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Cannot create memory-optimized table type {DisplayName(operation.Name, operation.Schema)} without a " +
                    "primary key: SQL Server requires at least one index on memory-optimized table types.");
            }

            string typeName = Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema);

            if (operation.IsMemoryOptimized)
            {
                // Pre-flight In-Memory OLTP support and fail actionably on unsupported tiers.
                // Deliberately no silent fallback to an on-disk type: the two declarations differ
                // structurally (index kinds), so a fallback would create cross-environment drift.
                string message =
                    $"Cannot create memory-optimized table type {DisplayName(operation.Name, operation.Schema)}: In-Memory " +
                    "OLTP is not supported on this database tier or edition (DATABASEPROPERTYEX 'IsXTPSupported' <> 1). " +
                    "Use a Premium/Business Critical tier or a memory-optimized filegroup, or remove the memory-optimized " +
                    "configuration from the table type.";
                builder
                    .AppendLine("IF DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') <> 1")
                    .Append("    THROW ")
                    .Append(TableTypeErrorNumbers.MemoryOptimizedNotSupported.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(", ")
                    .Append(SqlLiteral(message))
                    .Append(", 1")
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }

            builder
                .Append("CREATE TYPE ")
                .Append(typeName)
                .AppendLine(" AS TABLE (");

            for (int i = 0; i < operation.Columns.Count; i++)
            {
                TableTypeColumnDefinition column = operation.Columns[i];
                builder
                    .Append("    ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(column.Name))
                    .Append(" ")
                    .Append(column.StoreType);
                if (column.Collation is not null)
                {
                    builder.Append(" COLLATE ").Append(column.Collation);
                }

                builder.Append(column.IsNullable ? " NULL" : " NOT NULL");

                if (i < operation.Columns.Count - 1 || operation.PrimaryKey.Length > 0)
                {
                    builder.Append(",");
                }

                builder.AppendLine();
            }

            if (operation.PrimaryKey.Length > 0)
            {
                // Memory-optimized types cannot have a clustered index; on-disk types mirror the
                // table's clustered primary key.
                builder
                    .Append("    PRIMARY KEY ")
                    .Append(operation.IsMemoryOptimized ? "NONCLUSTERED" : "CLUSTERED")
                    .Append(" (")
                    .Append(string.Join(", ", operation.PrimaryKey.Select(Dependencies.SqlGenerationHelper.DelimitIdentifier)))
                    .AppendLine(")");
            }

            builder.Append(")");
            if (operation.IsMemoryOptimized)
            {
                builder.Append(" WITH (MEMORY_OPTIMIZED = ON)");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            AppendGrants(operation.Grants, typeName, builder);

            // Mirror the provider's handling of memory-optimized DDL: such statements cannot run
            // inside the migration's transaction.
            builder.EndCommand(suppressTransaction: operation.IsMemoryOptimized);
        }

        /// <summary>
        ///     Generates the drop of a table type, preceded by a guard that resolves all persisted
        ///     SQL modules referencing the type (<c>sys.sql_expression_dependencies</c>,
        ///     <c>referenced_class = 6</c>) and fails with their names — converting the cryptic
        ///     error 3732 into an actionable failure that enforces the no-persisted-consumers rule
        ///     at the moment it matters.
        /// </summary>
        /// <param name="operation">The drop operation.</param>
        /// <param name="builder">The command builder.</param>
        protected virtual void Generate(DropTableTypeOperation operation, MigrationCommandListBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(builder);

            string typeName = Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema);

            // A null schema means the database user's default schema, mirrored by SCHEMA_NAME().
            string schemaPredicate = operation.Schema is null
                ? "SCHEMA_NAME()"
                : SqlLiteral(operation.Schema);
            string messagePrefix =
                $"Cannot drop table type {DisplayName(operation.Name, operation.Schema)}: it is referenced by persisted " +
                "SQL module(s) ";
            string messageSuffix =
                ". Per the Tellma architecture, no persisted SQL module may reference a generated table type; all " +
                "consumers must be dynamic SQL composed in C#.";

            builder
                .AppendLine("DECLARE @dependents nvarchar(max) = (")
                .AppendLine("    SELECT STRING_AGG(QUOTENAME(OBJECT_SCHEMA_NAME(d.[referencing_id])) + N'.' + QUOTENAME(OBJECT_NAME(d.[referencing_id])), N', ')")
                .AppendLine("    FROM [sys].[sql_expression_dependencies] AS d")
                .AppendLine("    INNER JOIN [sys].[table_types] AS tt ON tt.[user_type_id] = d.[referenced_id]")
                .Append("    WHERE d.[referenced_class] = 6 AND tt.[name] = ")
                .Append(SqlLiteral(operation.Name))
                .Append(" AND SCHEMA_NAME(tt.[schema_id]) = ")
                .Append(schemaPredicate)
                .Append(")")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .AppendLine("IF @dependents IS NOT NULL")
                .AppendLine("BEGIN")
                .Append("    DECLARE @error nvarchar(2048) = ")
                .Append(SqlLiteral(messagePrefix))
                .Append(" + @dependents + ")
                .Append(SqlLiteral(messageSuffix))
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .Append("    THROW ")
                .Append(TableTypeErrorNumbers.DroppedTypeHasDependents.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(", @error, 1")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .Append("END")
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator)
                .Append("DROP TYPE ")
                .Append(typeName)
                .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);

            builder.EndCommand(suppressTransaction: operation.IsMemoryOptimized);
        }

        /// <summary>
        ///     Appends one <c>GRANT EXECUTE ON TYPE</c> statement per principal. Grants do not
        ///     survive a drop, so they are part of every (re)create by construction.
        /// </summary>
        private void AppendGrants(string[] grants, string typeName, MigrationCommandListBuilder builder)
        {
            foreach (string principal in grants)
            {
                builder
                    .Append("GRANT EXECUTE ON TYPE::")
                    .Append(typeName)
                    .Append(" TO ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(principal))
                    .AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            }
        }

        /// <summary>Renders a string as an escaped N'...' SQL literal.</summary>
        private string SqlLiteral(string value)
        {
            return Dependencies.TypeMappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(value);
        }

        /// <summary>The bracket-delimited display name used in error messages.</summary>
        private static string DisplayName(string name, string? schema)
        {
            return schema is null ? $"[{name}]" : $"[{schema}].[{name}]";
        }
    }
}
