// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>The import's workbook reader and mapper, one per process: it holds the limits and the caches and no request state.</summary>
    internal sealed class ExcelImporter : IExcelImporter
    {
        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options;

        /// <summary>The directory shared strings spill to.</summary>
        private readonly string _scratchDirectory;

        /// <summary>The bounds every workbook is read within.</summary>
        private readonly WorkbookLimits _limits;

        /// <summary>The header and sheet-name indexes.</summary>
        private readonly HeaderIndexCache _cache;

        /// <summary>The codec's instruments.</summary>
        private readonly ExcelMetrics _metrics;

        /// <summary>The codec's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the importer.</summary>
        /// <param name="options">The codec's options.</param>
        /// <param name="scratchDirectory">The directory shared strings spill to.</param>
        /// <param name="kinds">The blob kinds, whose import-file policy caps an upload's size.</param>
        /// <param name="cache">The header and sheet-name indexes.</param>
        /// <param name="metrics">The codec's instruments.</param>
        /// <param name="loggers">The logger factory.</param>
        public ExcelImporter(ExcelOptions options, string scratchDirectory, IBlobKindRegistry kinds, HeaderIndexCache cache, ExcelMetrics metrics, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(kinds);
            ArgumentNullException.ThrowIfNull(loggers);
            ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _scratchDirectory = scratchDirectory;
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = loggers.CreateLogger(ExcelLog.Category);

            _limits = new WorkbookLimits(
                MaxPackageBytes(options, kinds),
                options.MaxImportUncompressedBytes,
                options.MaxInMemorySharedStringBytes,
                options.MaxSharedStringEntries,
                RowTextBudget: options.MaxInMemorySharedStringBytes,
                SheetTextBudget: options.MaxImportUncompressedBytes);
        }

        /// <summary>The largest package an import accepts: the codec's limit, or the import-file kind's smaller one.</summary>
        /// <param name="options">The codec's options.</param>
        /// <param name="kinds">The blob kinds.</param>
        /// <returns>The limit, in bytes.</returns>
        public static long MaxPackageBytes(ExcelOptions options, IBlobKindRegistry kinds)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(kinds);

            // Spec 0019 §7.2: an upload never exceeds its kind's size, so the smaller of the two bounds the package.
            return kinds.Find(Import.FileKind) is BlobKindDescriptor kind
                ? Math.Min(options.MaxImportFileBytes, kind.Policy.MaxSize)
                : options.MaxImportFileBytes;
        }

        /// <inheritdoc />
        public Task<ImportPlan> InspectAsync(
            Stream workbook,
            EntityMetadata entity,
            StackLimits limits,
            InspectImportRequest request,
            ExcelContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workbook);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            // --- The package within its limits, its shared strings loaded, then the mapping; each checks the token as it reads ---
            using WorkbookReader reader = Open(workbook, cancellationToken);
            ImportMapping mapping = new ImportMapper(entity, limits, _options, context, _cache).Map(reader, request.SheetMappings, request.ColumnMappings, cancellationToken);
            return Task.FromResult(mapping.Plan);
        }

        /// <summary>Opens a workbook within the limits, recording a spill of its shared strings.</summary>
        /// <param name="workbook">The workbook's stream.</param>
        /// <param name="cancellationToken">Stops the shared strings' load between entries.</param>
        /// <returns>The reader.</returns>
        private WorkbookReader Open(Stream workbook, CancellationToken cancellationToken)
        {
            var reader = WorkbookReader.Open(workbook, _limits, _scratchDirectory, cancellationToken);
            if (reader.SharedStrings.Spilled)
            {
                _metrics.SharedStringsSpilled();
                ExcelLog.SharedStringsSpilled(_logger, reader.SharedStrings.ChargedBytes);
            }

            return reader;
        }
    }
}
