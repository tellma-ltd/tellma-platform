// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     <see cref="MigrationBuilder" /> extensions for authoring table-type operations. Scaffolded
    ///     migrations call these methods; they can equally be written by hand in manual migrations.
    /// </summary>
    public static class TableTypeMigrationBuilderExtensions
    {
        /// <summary>
        ///     Creates a SQL Server table type (UDTT) with the given columns and primary key, then
        ///     grants <c>EXECUTE</c> on it to the given principals.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="name">The table type's name.</param>
        /// <param name="schema">The table type's schema, or <see langword="null" /> for the database default.</param>
        /// <param name="columns">The type's columns, in ordinal order (the TVP binding contract).</param>
        /// <param name="primaryKey">The primary key column names, in key order; empty for no primary key.</param>
        /// <param name="memoryOptimized">Whether to create the type with <c>MEMORY_OPTIMIZED = ON</c>.</param>
        /// <param name="grants">Database principals granted <c>EXECUTE</c> on the type after creation.</param>
        /// <returns>A builder to allow annotations to be added to the operation.</returns>
        public static OperationBuilder<CreateTableTypeOperation> CreateTableType(
            this MigrationBuilder migrationBuilder,
            string name,
            string? schema,
            TableTypeColumnDefinition[] columns,
            string[]? primaryKey = null,
            bool memoryOptimized = false,
            string[]? grants = null)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(columns);

            CreateTableTypeOperation operation = new()
            {
                Name = name,
                Schema = schema,
                PrimaryKey = primaryKey ?? [],
                IsMemoryOptimized = memoryOptimized,
                Grants = grants ?? [],
            };
            operation.Columns.AddRange(columns);

            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<CreateTableTypeOperation>(operation);
        }

        /// <summary>
        ///     Drops a SQL Server table type (UDTT). The generated SQL first verifies that no
        ///     persisted SQL module references the type and fails with the dependents' names
        ///     (error <see cref="TableTypeErrorNumbers.DroppedTypeHasDependents" />) otherwise.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="name">The table type's name.</param>
        /// <param name="schema">The table type's schema, or <see langword="null" /> for the database default.</param>
        /// <param name="memoryOptimized">Whether the type was created with <c>MEMORY_OPTIMIZED = ON</c>.</param>
        /// <returns>A builder to allow annotations to be added to the operation.</returns>
        public static OperationBuilder<DropTableTypeOperation> DropTableType(
            this MigrationBuilder migrationBuilder,
            string name,
            string? schema = null,
            bool memoryOptimized = false)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrEmpty(name);

            DropTableTypeOperation operation = new()
            {
                Name = name,
                Schema = schema,
                IsMemoryOptimized = memoryOptimized,
            };

            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<DropTableTypeOperation>(operation);
        }
    }
}
