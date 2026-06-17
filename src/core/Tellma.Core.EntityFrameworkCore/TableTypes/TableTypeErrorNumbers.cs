// Copyright (c) Tellma Ltd. All rights reserved.
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
        ///     Thrown by the drop-time dependency guard of an explicitly authored <c>DropTableType</c>
        ///     when a persisted SQL module references the type being dropped (per the architecture,
        ///     all UDTT consumers must be dynamic SQL). The cleanup sweep runs the same check but
        ///     skips and surfaces instead of throwing — see spec 0001 §3 → Drop safety.
        /// </summary>
        public const int DroppedTypeHasDependents = 53102;

        /// <summary>
        ///     Thrown by the idempotent <c>CREATE TYPE</c> when a type already exists at the
        ///     content-addressed physical name but its stamped definition hash does not match — i.e.
        ///     the bytes at that name are not the shape this app would bind. An <b>integrity</b>
        ///     failure: an astronomically unlikely truncated-hash collision, or an out-of-band type
        ///     squatting on the name. Distinct from
        ///     <see cref="TableTypeOwnedByAnotherScope" />, where the data is safe.
        /// </summary>
        public const int TableTypeContentMismatch = 53103;

        /// <summary>
        ///     Thrown by the idempotent <c>CREATE TYPE</c> when a type already exists at the physical
        ///     name with a <b>matching</b> hash but a foreign owning scope — i.e. two contexts both
        ///     try to own one physical type. An <b>ownership</b> error (the data is safe): one context
        ///     must own it and the others declare <c>ExcludeFromMigrations()</c>. Distinct from the
        ///     content-integrity <see cref="TableTypeContentMismatch" />.
        /// </summary>
        public const int TableTypeOwnedByAnotherScope = 53104;
    }
}
