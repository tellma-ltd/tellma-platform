// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Tests.Excel
{
    /// <summary>
    ///     The workbook options: their defaults, and the startup validation that keeps every limit
    ///     positive and each limit within the one that contains it.
    /// </summary>
    public class ExcelOptionsTests
    {
        [Fact]
        public void The_defaults_are_the_documented_limits_and_valid()
        {
            ExcelOptions options = new();

            Assert.Equal("Tellma:Excel", ExcelOptions.SectionPath);
            Assert.Equal(100_000, options.MaxSynchronousExportRows);
            Assert.Equal(1_048_575, options.MaxExportRows);
            Assert.Equal(10_000, options.MaxSynchronousImportRows);
            Assert.Equal(100_000, options.MaxAtomicImportRows);
            Assert.Equal(10_000, options.ImportChunkRows);
            Assert.Equal(2, options.ImportConcurrencyRetries);
            Assert.Equal(16L * 1024 * 1024, options.MaxSynchronousImportFileBytes);
            Assert.Equal(512L * 1024 * 1024, options.MaxSynchronousImportUncompressedBytes);
            Assert.Equal(100L * 1024 * 1024, options.MaxImportFileBytes);
            Assert.Equal(2L * 1024 * 1024 * 1024, options.MaxImportUncompressedBytes);
            Assert.Equal(32L * 1024 * 1024, options.MaxInMemorySharedStringBytes);
            Assert.Equal(16_000_000, options.MaxSharedStringEntries);
            Assert.Equal(100, options.MaxReportedErrors);
            Assert.Equal(7, options.ExportFileRetentionDays);
            Assert.Equal(7, options.ImportFileRetentionDays);
            Assert.Empty(Validate(options));
        }

        [Theory]
        [InlineData(nameof(ExcelOptions.MaxSynchronousExportRows))]
        [InlineData(nameof(ExcelOptions.MaxExportRows))]
        [InlineData(nameof(ExcelOptions.MaxSynchronousImportRows))]
        [InlineData(nameof(ExcelOptions.MaxAtomicImportRows))]
        [InlineData(nameof(ExcelOptions.ImportChunkRows))]
        [InlineData(nameof(ExcelOptions.MaxSynchronousImportFileBytes))]
        [InlineData(nameof(ExcelOptions.MaxSynchronousImportUncompressedBytes))]
        [InlineData(nameof(ExcelOptions.MaxImportFileBytes))]
        [InlineData(nameof(ExcelOptions.MaxImportUncompressedBytes))]
        [InlineData(nameof(ExcelOptions.MaxInMemorySharedStringBytes))]
        [InlineData(nameof(ExcelOptions.MaxSharedStringEntries))]
        [InlineData(nameof(ExcelOptions.MaxReportedErrors))]
        [InlineData(nameof(ExcelOptions.ExportFileRetentionDays))]
        [InlineData(nameof(ExcelOptions.ImportFileRetentionDays))]
        public void Every_limit_must_be_positive(string limit)
        {
            ExcelOptions options = new();
            PropertyInfo property = typeof(ExcelOptions).GetProperty(limit)!;
            property.SetValue(options, Convert.ChangeType(0, property.PropertyType, CultureInfo.InvariantCulture));

            // Zeroing a limit may also break an ordering with a limit it contains; the positive rule is reported either way.
            ValidationResult failure = Assert.Single(Validate(options), result => result.MemberNames.Single() == limit);
            Assert.Equal($"Tellma:Excel:{limit} is 0; it must be positive.", failure.ErrorMessage);
        }

        [Fact]
        public void The_concurrency_retries_may_be_zero_but_not_negative()
        {
            Assert.Empty(Validate(new ExcelOptions { ImportConcurrencyRetries = 0 }));

            ValidationResult failure = Assert.Single(Validate(new ExcelOptions { ImportConcurrencyRetries = -1 }));
            Assert.Equal([nameof(ExcelOptions.ImportConcurrencyRetries)], failure.MemberNames);
        }

        [Fact]
        public void An_export_never_exceeds_a_worksheets_data_rows()
        {
            ValidationResult failure = Assert.Single(Validate(new ExcelOptions { MaxExportRows = 1_048_576 }));

            Assert.Equal("Tellma:Excel:MaxExportRows is 1048576; it must be at most 1048575.", failure.ErrorMessage);
            Assert.Equal(1_048_575, ExcelOptions.MaxWorksheetDataRows);
        }

        [Fact]
        public void A_synchronous_export_never_exceeds_any_export()
        {
            ValidationResult failure = Assert.Single(Validate(new ExcelOptions { MaxExportRows = 500, MaxSynchronousExportRows = 501 }));

            Assert.Equal([nameof(ExcelOptions.MaxSynchronousExportRows)], failure.MemberNames);
            Assert.Empty(Validate(new ExcelOptions { MaxExportRows = 500, MaxSynchronousExportRows = 500 }));
        }

        [Fact]
        public void An_import_chunk_fits_in_an_atomic_import()
        {
            ValidationResult failure = Assert.Single(Validate(new ExcelOptions { ImportChunkRows = 100_001 }));

            Assert.Equal([nameof(ExcelOptions.ImportChunkRows)], failure.MemberNames);
        }

        [Fact]
        public void Each_synchronous_byte_limit_fits_its_general_limit_and_compressed_fits_uncompressed()
        {
            const long Gigabyte = 1024L * 1024 * 1024;

            Assert.Equal(
                [nameof(ExcelOptions.MaxSynchronousImportFileBytes)],
                Validate(new ExcelOptions { MaxSynchronousImportFileBytes = 101L * 1024 * 1024 }).SelectMany(static r => r.MemberNames));
            Assert.Equal(
                [nameof(ExcelOptions.MaxSynchronousImportFileBytes)],
                Validate(new ExcelOptions { MaxSynchronousImportUncompressedBytes = 8L * 1024 * 1024 }).SelectMany(static r => r.MemberNames));
            Assert.Equal(
                [nameof(ExcelOptions.MaxImportFileBytes)],
                Validate(new ExcelOptions { MaxImportFileBytes = 3 * Gigabyte }).SelectMany(static r => r.MemberNames));
            Assert.Equal(
                [nameof(ExcelOptions.MaxSynchronousImportUncompressedBytes)],
                Validate(new ExcelOptions { MaxImportUncompressedBytes = 256L * 1024 * 1024 }).SelectMany(static r => r.MemberNames));
        }

        /// <summary>Runs the options' validation.</summary>
        /// <param name="options">The options.</param>
        /// <returns>The failures.</returns>
        private static List<ValidationResult> Validate(ExcelOptions options)
        {
            return [.. options.Validate(new ValidationContext(options))];
        }
    }
}
