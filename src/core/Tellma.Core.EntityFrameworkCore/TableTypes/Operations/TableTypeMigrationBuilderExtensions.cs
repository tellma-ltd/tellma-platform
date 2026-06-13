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
        ///     Creates a SQL Server table type (UDTT) under its content-addressed physical name,
        ///     stamps it with the owning scope, logical name and definition hash, and grants
        ///     <c>EXECUTE</c> to the given principals. The physical name and hash are frozen here at
        ///     scaffold time and never recomputed at apply time.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="name">The logical (configured) name of the type.</param>
        /// <param name="physicalName">The deployed physical name (<c>&lt;name&gt;_&lt;hash8&gt;</c>).</param>
        /// <param name="schema">The table type's schema, or <see langword="null" /> for the database default.</param>
        /// <param name="scope">The owning context's sweep scope, stamped on the type.</param>
        /// <param name="definitionHash">The full SHA-256 of the definition's canonical JSON, stamped on the type.</param>
        /// <param name="columns">The type's columns, in ordinal order (the TVP binding contract).</param>
        /// <param name="primaryKey">The primary key column names, in key order; empty for no primary key.</param>
        /// <param name="memoryOptimized">Whether to create the type with <c>MEMORY_OPTIMIZED = ON</c>.</param>
        /// <param name="grants">Database principals granted <c>EXECUTE</c> on the type after creation.</param>
        /// <returns>A builder to allow annotations to be added to the operation.</returns>
        public static OperationBuilder<CreateTableTypeOperation> CreateTableType(
            this MigrationBuilder migrationBuilder,
            string name,
            string physicalName,
            string? schema,
            string scope,
            string definitionHash,
            TableTypeColumnDefinition[] columns,
            string[]? primaryKey = null,
            bool memoryOptimized = false,
            string[]? grants = null)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(physicalName);
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentException.ThrowIfNullOrEmpty(definitionHash);
            ArgumentNullException.ThrowIfNull(columns);

            CreateTableTypeOperation operation = new()
            {
                Name = name,
                PhysicalName = physicalName,
                Schema = schema,
                Scope = scope,
                DefinitionHash = definitionHash,
                PrimaryKey = primaryKey ?? [],
                IsMemoryOptimized = memoryOptimized,
                Grants = grants ?? [],
            };
            operation.Columns.AddRange(columns);

            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<CreateTableTypeOperation>(operation);
        }

        /// <summary>
        ///     Drops one SQL Server table type (UDTT) by its <b>physical</b> name. The generated SQL
        ///     first verifies that no persisted SQL module references the type and fails with the
        ///     dependents' names (error <see cref="TableTypeErrorNumbers.DroppedTypeHasDependents" />)
        ///     otherwise. Authored by hand for deliberate, immediate removals — the differ never emits
        ///     it.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="name">The <b>physical</b> name of the type to drop.</param>
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

        /// <summary>
        ///     Garbage-collects stale table-type versions in a scope against an explicit keep-list of
        ///     physical names. This is the form the scaffolder emits (the literal list is part of what
        ///     review sees).
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="scope">The sweep scope (only types stamped with it are touched).</param>
        /// <param name="keepList">The physical names to keep; everything else in scope ages out.</param>
        /// <param name="gracePeriodHours">Hours an orphan must remain marked before collection.</param>
        /// <returns>A builder to allow annotations to be added to the operation.</returns>
        public static OperationBuilder<CleanupTableTypesOperation> CleanupTableTypes(
            this MigrationBuilder migrationBuilder,
            string scope,
            string[] keepList,
            int gracePeriodHours = CleanupTableTypesOperation.DefaultGracePeriodHours)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentNullException.ThrowIfNull(keepList);

            CleanupTableTypesOperation operation = new()
            {
                Scope = scope,
                KeepList = keepList,
                GracePeriodHours = gracePeriodHours,
            };

            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<CleanupTableTypesOperation>(operation);
        }

        /// <summary>
        ///     Garbage-collects stale table-type versions in a scope, resolving the keep-list from the
        ///     migration's target model at SQL-generation time. The form for hand-written migrations,
        ///     where listing hash-suffixed physical names by hand is impractical.
        /// </summary>
        /// <param name="migrationBuilder">The migration builder.</param>
        /// <param name="scope">The sweep scope (only types stamped with it are touched).</param>
        /// <param name="gracePeriodHours">Hours an orphan must remain marked before collection.</param>
        /// <returns>A builder to allow annotations to be added to the operation.</returns>
        public static OperationBuilder<CleanupTableTypesOperation> CleanupTableTypes(
            this MigrationBuilder migrationBuilder,
            string scope,
            int gracePeriodHours = CleanupTableTypesOperation.DefaultGracePeriodHours)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);

            CleanupTableTypesOperation operation = new()
            {
                Scope = scope,
                KeepList = null,
                GracePeriodHours = gracePeriodHours,
            };

            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<CleanupTableTypesOperation>(operation);
        }
    }
}
