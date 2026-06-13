// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Names of the SQL Server <b>extended properties</b> (<c>sp_addextendedproperty</c>, class 6
    ///     = TYPE, queryable from <c>sys.extended_properties</c>) stamped on every created table type
    ///     (spec 0001 §3 → Versioning). They make a deployed type self-describing: which logical type
    ///     and owning context it belongs to, the exact definition it was created from, and — once the
    ///     cleanup sweep marks it — when it was orphaned.
    /// </summary>
    /// <remarks>
    ///     These are catalog-metadata keys written into generated SQL, not EF model annotations (for
    ///     which see <see cref="TableTypeAnnotationNames" />). They share the <c>Tellma:TableType:</c>
    ///     prefix purely for legibility in <c>sys.extended_properties</c>.
    /// </remarks>
    public static class TableTypeStampNames
    {
        /// <summary>
        ///     The logical (configured) name of the type — the grouping key the cleanup sweep uses to
        ///     relate physical versions of one logical type, and what restores DBA legibility of
        ///     hash-suffixed names in <c>sys.table_types</c>.
        /// </summary>
        public const string LogicalName = "Tellma:TableType:LogicalName";

        /// <summary>
        ///     The owning context's sweep scope (see <see cref="TableTypesDbContextOptionsBuilderExtensions" />).
        ///     A context's cleanup sweep touches only types carrying its own scope, and the idempotent
        ///     create rejects a same-named type stamped with a foreign scope
        ///     (<see cref="TableTypeErrorNumbers.TableTypeOwnedByAnotherScope" />).
        /// </summary>
        public const string Scope = "Tellma:TableType:Scope";

        /// <summary>
        ///     The full SHA-256 of the definition's canonical JSON (see <see cref="TableTypeNaming" />).
        ///     The idempotent create compares it to tell a content collision
        ///     (<see cref="TableTypeErrorNumbers.TableTypeContentMismatch" />) from a mere ownership
        ///     conflict, and it is the ready-made hook for the future runtime definition-hash check.
        /// </summary>
        public const string DefinitionHash = "Tellma:TableType:DefinitionHash";

        /// <summary>
        ///     The UTC time (from the database server's <c>SYSUTCDATETIME()</c>) at which the cleanup
        ///     sweep marked the type an orphan — absent on live types, written when a type leaves the
        ///     keep-list, cleared if it returns, and the clock the grace period is measured against.
        /// </summary>
        public const string OrphanedAtUtc = "Tellma:TableType:OrphanedAtUtc";
    }
}
