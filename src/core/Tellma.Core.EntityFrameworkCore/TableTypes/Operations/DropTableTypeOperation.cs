// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     A <see cref="MigrationOperation" /> that drops a SQL Server table type (UDTT), preceded
    ///     by a guard that fails with error <see cref="TableTypeErrorNumbers.DroppedTypeHasDependents" />
    ///     — naming the offending modules — if any persisted SQL module references the type.
    /// </summary>
    /// <remarks>
    ///     <see cref="MigrationOperation.IsDestructiveChange" /> stays <see langword="false" />:
    ///     dropping a type is pure DDL metadata with no data loss, and every definitional change
    ///     scaffolds a drop + create pair, so a destructive warning would be constant noise.
    /// </remarks>
    public class DropTableTypeOperation : MigrationOperation
    {
        /// <summary>The table type's name, e.g. <c>InvoicesList</c>.</summary>
        public string Name { get; set; } = null!;

        /// <summary>
        ///     The table type's schema, or <see langword="null" /> when it lives in the database's
        ///     default schema.
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        ///     Whether the type being dropped was created with <c>MEMORY_OPTIMIZED = ON</c>; the
        ///     SQL generator suppresses the surrounding transaction for memory-optimized DDL,
        ///     mirroring the SQL Server provider's handling of memory-optimized tables.
        /// </summary>
        public bool IsMemoryOptimized { get; set; }
    }
}
