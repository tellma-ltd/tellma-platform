// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The limits of the Excel export and import, bound from <c>Tellma:Excel</c> and validated by
    ///     the startup gate. A deployment changes them in configuration, never in code.
    /// </summary>
    /// <remarks>
    ///     Every value is positive except <see cref="ImportConcurrencyRetries" />, which may be 0. The
    ///     limits nest: <see cref="MaxSynchronousExportRows" /> ≤ <see cref="MaxExportRows" /> ≤
    ///     1,048,575 (a worksheet's rows less its header); <see cref="ImportChunkRows" /> ≤
    ///     <see cref="MaxAtomicImportRows" />; <see cref="MaxSynchronousImportFileBytes" /> ≤
    ///     <see cref="MaxImportFileBytes" /> ≤ <see cref="MaxImportUncompressedBytes" />; and
    ///     <see cref="MaxSynchronousImportFileBytes" /> ≤ <see cref="MaxSynchronousImportUncompressedBytes" />
    ///     ≤ <see cref="MaxImportUncompressedBytes" />.
    /// </remarks>
    public sealed class ExcelOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Excel";

        /// <summary>The most data rows a worksheet holds: its 1,048,576 rows less the header row.</summary>
        public const int MaxWorksheetDataRows = 1_048_575;

        /// <summary>The most rows a synchronous export writes per sheet; 100,000 by default.</summary>
        public int MaxSynchronousExportRows { get; set; } = 100_000;

        /// <summary>The most rows any export writes per sheet; 1,048,575 by default, which is also the most allowed.</summary>
        public int MaxExportRows { get; set; } = MaxWorksheetDataRows;

        /// <summary>The most rows a synchronous import reads across its sheets; 10,000 by default.</summary>
        public int MaxSynchronousImportRows { get; set; } = 10_000;

        /// <summary>The most rows an import that commits in one transaction reads across its sheets; 100,000 by default.</summary>
        public int MaxAtomicImportRows { get; set; } = 100_000;

        /// <summary>The parent rows a background import commits per transaction; 10,000 by default.</summary>
        public int ImportChunkRows { get; set; } = 10_000;

        /// <summary>How often an import re-saves a chunk whose rows changed since it read them; 2 by default, 0 for never.</summary>
        public int ImportConcurrencyRetries { get; set; } = 2;

        /// <summary>The largest file a synchronous import accepts, compressed; 16 MiB by default.</summary>
        public long MaxSynchronousImportFileBytes { get; set; } = 16L * 1024 * 1024;

        /// <summary>The largest package a synchronous import expands, its parts summed uncompressed; 512 MiB by default.</summary>
        public long MaxSynchronousImportUncompressedBytes { get; set; } = 512L * 1024 * 1024;

        /// <summary>The largest file any import accepts, compressed; 100 MiB by default.</summary>
        public long MaxImportFileBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>The largest package any import expands, its parts summed uncompressed; 2 GiB by default.</summary>
        public long MaxImportUncompressedBytes { get; set; } = 2L * 1024 * 1024 * 1024;

        /// <summary>The shared strings an import holds in memory before it spills them to a scratch file; 32 MiB by default.</summary>
        public long MaxInMemorySharedStringBytes { get; set; } = 32L * 1024 * 1024;

        /// <summary>The most entries an import accepts in a workbook's shared-string part; 16,000,000 by default.</summary>
        public int MaxSharedStringEntries { get; set; } = 16_000_000;

        /// <summary>The most errors a failed import reports; 100 by default. The total is always reported.</summary>
        public int MaxReportedErrors { get; set; } = 100;

        /// <summary>The days an exported file is kept for download; 7 by default.</summary>
        public int ExportFileRetentionDays { get; set; } = 7;

        /// <summary>The days an imported file is kept after its import; 7 by default.</summary>
        public int ImportFileRetentionDays { get; set; } = 7;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Every value positive, the retries at least 0 ---
            foreach ((string name, long value) in new[]
            {
                (nameof(MaxSynchronousExportRows), MaxSynchronousExportRows),
                (nameof(MaxExportRows), MaxExportRows),
                (nameof(MaxSynchronousImportRows), MaxSynchronousImportRows),
                (nameof(MaxAtomicImportRows), MaxAtomicImportRows),
                (nameof(ImportChunkRows), ImportChunkRows),
                (nameof(MaxSynchronousImportFileBytes), MaxSynchronousImportFileBytes),
                (nameof(MaxSynchronousImportUncompressedBytes), MaxSynchronousImportUncompressedBytes),
                (nameof(MaxImportFileBytes), MaxImportFileBytes),
                (nameof(MaxImportUncompressedBytes), MaxImportUncompressedBytes),
                (nameof(MaxInMemorySharedStringBytes), MaxInMemorySharedStringBytes),
                (nameof(MaxSharedStringEntries), MaxSharedStringEntries),
                (nameof(MaxReportedErrors), MaxReportedErrors),
                (nameof(ExportFileRetentionDays), ExportFileRetentionDays),
                (nameof(ImportFileRetentionDays), ImportFileRetentionDays),
            })
            {
                if (value <= 0)
                {
                    yield return Problem(name, value, "positive");
                }
            }

            if (ImportConcurrencyRetries < 0)
            {
                yield return Problem(nameof(ImportConcurrencyRetries), ImportConcurrencyRetries, "0 or more");
            }

            // --- The export caps: synchronous ≤ any ≤ a worksheet's data rows ---
            if (MaxExportRows > MaxWorksheetDataRows)
            {
                yield return Problem(
                    nameof(MaxExportRows),
                    MaxExportRows,
                    string.Create(CultureInfo.InvariantCulture, $"at most {MaxWorksheetDataRows}"));
            }

            if (MaxSynchronousExportRows > MaxExportRows)
            {
                yield return Problem(nameof(MaxSynchronousExportRows), MaxSynchronousExportRows, $"at most {nameof(MaxExportRows)}");
            }

            // --- The import row limits: a chunk fits in an atomic import ---
            if (ImportChunkRows > MaxAtomicImportRows)
            {
                yield return Problem(nameof(ImportChunkRows), ImportChunkRows, $"at most {nameof(MaxAtomicImportRows)}");
            }

            // --- The import byte limits: each synchronous limit within its general one, compressed within uncompressed ---
            if (MaxSynchronousImportFileBytes > MaxImportFileBytes)
            {
                yield return Problem(nameof(MaxSynchronousImportFileBytes), MaxSynchronousImportFileBytes, $"at most {nameof(MaxImportFileBytes)}");
            }

            if (MaxSynchronousImportFileBytes > MaxSynchronousImportUncompressedBytes)
            {
                yield return Problem(
                    nameof(MaxSynchronousImportFileBytes),
                    MaxSynchronousImportFileBytes,
                    $"at most {nameof(MaxSynchronousImportUncompressedBytes)}");
            }

            if (MaxImportFileBytes > MaxImportUncompressedBytes)
            {
                yield return Problem(nameof(MaxImportFileBytes), MaxImportFileBytes, $"at most {nameof(MaxImportUncompressedBytes)}");
            }

            if (MaxSynchronousImportUncompressedBytes > MaxImportUncompressedBytes)
            {
                yield return Problem(
                    nameof(MaxSynchronousImportUncompressedBytes),
                    MaxSynchronousImportUncompressedBytes,
                    $"at most {nameof(MaxImportUncompressedBytes)}");
            }
        }

        /// <summary>Describes a value outside its bounds, naming its configuration key.</summary>
        /// <param name="name">The option's name.</param>
        /// <param name="value">Its value.</param>
        /// <param name="bounds">The bounds it must fall within, as words.</param>
        /// <returns>The result, naming the option as its member.</returns>
        private static ValidationResult Problem(string name, long value, string bounds)
        {
            return new ValidationResult(
                string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{name} is {value}; it must be {bounds}."),
                [name]);
        }
    }
}
