// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     How an import would read a workbook, worked out before any row is decoded beyond the header
    ///     rows: what each sheet and column binds to, the row keys on offer and the paths that can run
    ///     the file.
    /// </summary>
    /// <param name="Entity">The entity the workbook's manifest names; null when the workbook carries no manifest.</param>
    /// <param name="ManifestVersion">The manifest's version; 0 when the workbook carries no manifest.</param>
    /// <param name="ManifestPresent">Whether the workbook carries a readable manifest.</param>
    /// <param name="SameSource">Whether the workbook was exported by the same deployment and tenant, so its ids may key rows.</param>
    /// <param name="SchemaFingerprintMatches">Whether the manifest's schema fingerprint is the entity's current one.</param>
    /// <param name="Sheets">Each sheet of the workbook, in workbook order.</param>
    /// <param name="RowKeyCandidates">The row keys the import accepts: the entity's natural keys, and <c>Id</c> on a same-source file.</param>
    /// <param name="DefaultRowKey">The row key an import takes when the request names none; null when none resolves.</param>
    /// <param name="TotalRows">The data rows of every sheet the import reads.</param>
    /// <param name="RequiresBackground">Whether the file is above a synchronous limit, so only a background import can run it.</param>
    /// <param name="AtomicAllowed">Whether the file is within the limits of an import that commits in one transaction.</param>
    /// <param name="Warnings">What the import would warn about without failing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Sheets" />, <paramref name="RowKeyCandidates" /> or <paramref name="Warnings" /> is null.</exception>
    /// <exception cref="ArgumentException">A list holds a null entry.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ManifestVersion" /> or <paramref name="TotalRows" /> is negative.</exception>
    public sealed record ImportPlan(
        string? Entity,
        int ManifestVersion,
        bool ManifestPresent,
        bool SameSource,
        bool SchemaFingerprintMatches,
        IReadOnlyList<ImportPlanSheet> Sheets,
        IReadOnlyList<string> RowKeyCandidates,
        string? DefaultRowKey,
        long TotalRows,
        bool RequiresBackground,
        bool AtomicAllowed,
        IReadOnlyList<ImportError> Warnings)
    {
        /// <inheritdoc cref="ImportPlan(string?, int, bool, bool, bool, IReadOnlyList{ImportPlanSheet}, IReadOnlyList{string}, string?, long, bool, bool, IReadOnlyList{ImportError})" />
        public int ManifestVersion { get; init => field = NotNegative(value, nameof(ManifestVersion)); } =
            NotNegative(ManifestVersion, nameof(ManifestVersion));

        /// <inheritdoc cref="ImportPlan(string?, int, bool, bool, bool, IReadOnlyList{ImportPlanSheet}, IReadOnlyList{string}, string?, long, bool, bool, IReadOnlyList{ImportError})" />
        public IReadOnlyList<ImportPlanSheet> Sheets { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Sheets)); } =
            ReadOnlyCopies.OfEntries(Sheets, requireOne: false, nameof(Sheets));

        /// <inheritdoc cref="ImportPlan(string?, int, bool, bool, bool, IReadOnlyList{ImportPlanSheet}, IReadOnlyList{string}, string?, long, bool, bool, IReadOnlyList{ImportError})" />
        public IReadOnlyList<string> RowKeyCandidates { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(RowKeyCandidates)); } =
            ReadOnlyCopies.OfEntries(RowKeyCandidates, requireOne: false, nameof(RowKeyCandidates));

        /// <inheritdoc cref="ImportPlan(string?, int, bool, bool, bool, IReadOnlyList{ImportPlanSheet}, IReadOnlyList{string}, string?, long, bool, bool, IReadOnlyList{ImportError})" />
        public long TotalRows { get; init => field = NotNegative(value, nameof(TotalRows)); } = NotNegative(TotalRows, nameof(TotalRows));

        /// <inheritdoc cref="ImportPlan(string?, int, bool, bool, bool, IReadOnlyList{ImportPlanSheet}, IReadOnlyList{string}, string?, long, bool, bool, IReadOnlyList{ImportError})" />
        public IReadOnlyList<ImportError> Warnings { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Warnings)); } =
            ReadOnlyCopies.OfEntries(Warnings, requireOne: false, nameof(Warnings));

        /// <summary>Refuses a negative count.</summary>
        /// <param name="value">The count.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The count.</returns>
        private static int NotNegative(int value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, name);
            return value;
        }

        /// <summary>Refuses a negative count.</summary>
        /// <param name="value">The count.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The count.</returns>
        private static long NotNegative(long value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, name);
            return value;
        }
    }
}
