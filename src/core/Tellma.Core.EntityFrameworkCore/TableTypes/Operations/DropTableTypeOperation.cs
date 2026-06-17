// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     A <see cref="MigrationOperation" /> that drops one SQL Server table type (UDTT) by its
    ///     <b>physical</b> name, preceded by a guard that fails with error
    ///     <see cref="TableTypeErrorNumbers.DroppedTypeHasDependents" /> — naming the offending
    ///     modules — if any persisted SQL module references the type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The differ never emits this operation: routine retirement is the cleanup sweep's job
    ///         (<see cref="CleanupTableTypesOperation" />). It exists for <b>manual authoring</b> —
    ///         an immediate, deliberate removal — and therefore keeps the hard dependency THROW,
    ///         where the operator's intent is "this must go now" (spec 0001 §3 → Drop safety).
    ///     </para>
    ///     <para>
    ///         <see cref="MigrationOperation.IsDestructiveChange" /> stays <see langword="false" />:
    ///         dropping a type is pure DDL metadata with no data loss.
    ///     </para>
    /// </remarks>
    public class DropTableTypeOperation : MigrationOperation
    {
        /// <summary>
        ///     The table type's <b>physical</b> name (<c>&lt;logical&gt;_&lt;hash8&gt;</c>) — a drop
        ///     targets exactly one deployed version.
        /// </summary>
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
