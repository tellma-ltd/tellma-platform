// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     SQL error numbers used by the <c>THROW</c> statements in generated table-type SQL, so
    ///     tooling and tests can assert on them. User-defined THROW numbers must be ≥ 50000.
    /// </summary>
    public static class TableTypeErrorNumbers
    {
        /// <summary>
        ///     Thrown by the pre-flight check of a memory-optimized <c>CREATE TYPE</c> when the
        ///     database tier/edition does not support In-Memory OLTP
        ///     (<c>DATABASEPROPERTYEX(DB_NAME(), 'IsXTPSupported') &lt;&gt; 1</c>).
        /// </summary>
        public const int MemoryOptimizedNotSupported = 53101;

        /// <summary>
        ///     Thrown by the drop-time dependency guard when a persisted SQL module references the
        ///     type being dropped (per the architecture, all UDTT consumers must be dynamic SQL).
        /// </summary>
        public const int DroppedTypeHasDependents = 53102;
    }
}
