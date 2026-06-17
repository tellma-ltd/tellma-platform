// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     A <see cref="MigrationOperation" /> that garbage-collects stale table-type versions within
    ///     one sweep scope (spec 0001 §3 → Versioning). It is appended to every type-touching
    ///     migration and carries the target model's complete physical-name keep-list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         At apply time the sweep, over every Tellma-stamped type in <see cref="Scope" />:
    ///         (1) clears the orphan mark of any type in the keep-list; (2) marks
    ///         <see cref="TableTypeStampNames.OrphanedAtUtc" /> (from the server's
    ///         <c>SYSUTCDATETIME()</c>) on any type not in the keep-list and not already marked;
    ///         (3) drops any type marked longer than <see cref="GracePeriodHours" /> ago — unless a
    ///         persisted module references it, in which case it is skipped and surfaced rather than
    ///         thrown (the hard THROW is reserved for <see cref="DropTableTypeOperation" />).
    ///     </para>
    ///     <para>
    ///         It is always the migration's <b>last</b> command and runs with the transaction
    ///         suppressed: its drop set is discovered at apply time, so whether any orphan is
    ///         memory-optimized (and thus needs non-transactional DDL) is unknowable at scaffold time.
    ///         This is safe because the sweep is idempotent GC — every step converges on re-run, and
    ///         partial completion just leaves orphans for the next sweep.
    ///     </para>
    /// </remarks>
    public class CleanupTableTypesOperation : MigrationOperation
    {
        /// <summary>The default grace period, in hours, before an orphaned version is collected.</summary>
        public const int DefaultGracePeriodHours = 48;

        /// <summary>The sweep scope: the sweep touches only types stamped with this scope.</summary>
        public string Scope { get; set; } = null!;

        /// <summary>
        ///     The physical names to keep (everything else in scope is orphaned and eventually
        ///     collected), or <see langword="null" /> to resolve the keep-list from the migration's
        ///     target model at SQL-generation time — the shape for hand-written migrations, where
        ///     listing hash-suffixed names by hand is impractical.
        /// </summary>
        public string[]? KeepList { get; set; }

        /// <summary>
        ///     The grace period, in hours, an orphan must remain marked before it is collected.
        ///     Frozen into the migration file at scaffold time, so changing the library default never
        ///     retroactively changes already-scaffolded sweeps. Zero collects immediately (only safe
        ///     where no app has ever bound the database — fresh provisioning / post-swap cleanup).
        /// </summary>
        public int GracePeriodHours { get; set; } = DefaultGracePeriodHours;
    }
}
