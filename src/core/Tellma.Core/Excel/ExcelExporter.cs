// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The platform's exporter: the planner's two entity plans and the workbook writer's two
    ///     streams, over the deployment's Excel limits and scratch directory. A singleton; it holds no
    ///     request state.
    /// </summary>
    internal sealed class ExcelExporter : IExcelExporter
    {
        /// <summary>The planner.</summary>
        private readonly ExportPlanner _planner;

        /// <summary>The writer.</summary>
        private readonly WorkbookWriter _writer;

        /// <summary>Creates the exporter.</summary>
        /// <param name="options">The Excel limits.</param>
        /// <param name="scratchDirectory">The directory child sheets spool to.</param>
        /// <param name="platformVersion">The platform version the manifest records.</param>
        /// <param name="valueSets">The value sets, whose stored values an editable column offers.</param>
        public ExcelExporter(ExcelOptions options, string scratchDirectory, string platformVersion, IValueSetRegistry valueSets)
        {
            ArgumentNullException.ThrowIfNull(options);
            _planner = new ExportPlanner(valueSets, platformVersion);
            _writer = new WorkbookWriter(options, scratchDirectory);
        }

        /// <inheritdoc />
        public ExcelWorkbookPlan PlanDisplay(EntityMetadata entity, IReadOnlyList<ExcelColumnSpec> columns, ExcelQuery query, ExcelContext context)
        {
            return _planner.PlanDisplay(entity, columns, query, context);
        }

        /// <inheritdoc />
        public ExcelWorkbookPlan PlanEditable(EntityMetadata entity, ExportForImportRequest request, int cap, ExcelContext context)
        {
            return _planner.PlanEditable(entity, request, cap, context);
        }

        /// <inheritdoc />
        public Task<int> WriteAsync(ExcelWorkbookPlan plan, IExcelRowSource rows, ExcelContext context, Func<Stream> openDestination, CancellationToken cancellationToken = default)
        {
            return _writer.WriteAsync(plan, rows, context, openDestination, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> WriteTableAsync(ExcelTableSpec spec, IAsyncEnumerable<ExcelTableRow> rows, ExcelContext context, Stream destination, CancellationToken cancellationToken = default)
        {
            return WorkbookWriter.WriteTableAsync(spec, rows, context, destination, cancellationToken);
        }
    }
}
