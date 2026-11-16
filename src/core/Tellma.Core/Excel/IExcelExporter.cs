// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Plans and writes Excel exports: an entity's display and editable workbooks, and a report's
    ///     one-sheet table. Pure: it reads rows only through the row source it is handed, and never
    ///     opens a connection or reads a clock.
    /// </summary>
    public interface IExcelExporter
    {
        /// <summary>Plans a display export: one sheet with one column per requested column, and the manifest.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="columns">The sheet's columns, in request order.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The plan.</returns>
        ExcelWorkbookPlan PlanDisplay(EntityMetadata entity, IReadOnlyList<ExcelColumnSpec> columns, ExcelContext context);

        /// <summary>Plans an editable export: the parent sheet, a sheet per selected collection at every depth, and the manifest.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="request">The request's columns, reference keys and collections.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The plan.</returns>
        ExcelWorkbookPlan PlanEditable(EntityMetadata entity, ExportForImportRequest request, ExcelContext context);

        /// <summary>
        ///     Writes a planned workbook, pulling the parent sheet's query page by page with each child
        ///     sheet's query riding it, so memory holds one page and its dependents. The first page is
        ///     pulled before the destination is opened, so a capped count over the cap refuses the
        ///     export before any file exists.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The row source the plan's queries run through.</param>
        /// <param name="context">The request's context, the one the plan was made in.</param>
        /// <param name="openDestination">
        ///     Opens the writable stream the package is written to, a scratch file as a rule; called
        ///     once, after the first page passes the cap. The stream stays open and stays the caller's.
        /// </param>
        /// <param name="cancellationToken">Stops the write between rows.</param>
        /// <returns>The data rows written to the parent sheet.</returns>
        /// <exception cref="Abstractions.Errors.LimitExceededException">A sheet's rows exceed its cap.</exception>
        Task<int> WriteAsync(
            ExcelWorkbookPlan plan,
            IExcelRowSource rows,
            ExcelContext context,
            Func<Stream> openDestination,
            CancellationToken cancellationToken = default);

        /// <summary>Writes a report's one-sheet table, its rows streamed forward-only.</summary>
        /// <param name="spec">The table.</param>
        /// <param name="rows">The data rows, in order.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="destination">A writable stream the package is written to, a scratch file as a rule.</param>
        /// <param name="cancellationToken">Stops the write between rows.</param>
        /// <returns>The data rows written.</returns>
        Task<int> WriteTableAsync(
            ExcelTableSpec spec,
            IAsyncEnumerable<ExcelTableRow> rows,
            ExcelContext context,
            Stream destination,
            CancellationToken cancellationToken = default);
    }
}
