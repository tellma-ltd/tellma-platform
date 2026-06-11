// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     A <see cref="MigrationOperation" /> that creates a SQL Server table type (UDTT),
    ///     including its primary key, optional <c>MEMORY_OPTIMIZED = ON</c> setting, and
    ///     <c>GRANT EXECUTE ON TYPE</c> statements for the configured principals.
    /// </summary>
    /// <remarks>
    ///     SQL Server has no <c>ALTER TYPE</c>, so every definitional change is emitted as a
    ///     <see cref="DropTableTypeOperation" /> followed by a new create within the same migration.
    /// </remarks>
    public class CreateTableTypeOperation : MigrationOperation
    {
        /// <summary>The table type's name, e.g. <c>InvoicesList</c>.</summary>
        public string Name { get; set; } = null!;

        /// <summary>
        ///     The table type's schema, or <see langword="null" /> to create it in the database's
        ///     default schema.
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        ///     The type's columns in ordinal order. The order is part of the type's contract
        ///     because TVP binding is ordinal.
        /// </summary>
        public List<TableTypeColumnDefinition> Columns { get; } = [];

        /// <summary>The primary key column names, in key declaration order. May be empty.</summary>
        public string[] PrimaryKey { get; set; } = [];

        /// <summary>
        ///     Whether the type is created with <c>MEMORY_OPTIMIZED = ON</c>. The generated SQL
        ///     pre-flights In-Memory OLTP support and throws an actionable error on unsupported
        ///     tiers; there is deliberately no silent fallback to an on-disk type.
        /// </summary>
        public bool IsMemoryOptimized { get; set; }

        /// <summary>
        ///     Database principals that receive <c>GRANT EXECUTE ON TYPE</c> immediately after the
        ///     type is created. Grants do not survive a drop, so they are re-emitted on every
        ///     (re)create.
        /// </summary>
        public string[] Grants { get; set; } = [];

        /// <summary>
        ///     Creates an operation equivalent to the given derived <paramref name="definition" />.
        /// </summary>
        /// <param name="definition">The derived table-type definition.</param>
        /// <returns>The create operation.</returns>
        public static CreateTableTypeOperation CreateFrom(TableTypeDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            CreateTableTypeOperation operation = new()
            {
                Name = definition.Name,
                Schema = definition.Schema,
                PrimaryKey = [.. definition.PrimaryKey],
                IsMemoryOptimized = definition.IsMemoryOptimized,
                Grants = [.. definition.Grants],
            };
            operation.Columns.AddRange(definition.Columns);
            return operation;
        }
    }
}
