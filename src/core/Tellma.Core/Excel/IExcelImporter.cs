// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Reads uploaded workbooks for import: the package within its limits, the manifest, and each
    ///     sheet's header row and size, mapped to an entity. Pure: it reads only the stream it is
    ///     handed and the scratch directory it spools shared strings to, and never opens a connection.
    /// </summary>
    public interface IExcelImporter
    {
        /// <summary>Maps a workbook to an entity and reports the plan: every sheet's binding and size, and every header's path and status.</summary>
        /// <param name="workbook">The workbook: a readable, seekable stream positioned at its start, which stays the caller's.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="limits">The stack's limits, whose save sizes decide whether the file fits a synchronous or an atomic import.</param>
        /// <param name="request">The caller's sheet and column overrides, already checked against the entity.</param>
        /// <param name="context">The request's context: the target tenant, its languages, the culture and the labels.</param>
        /// <param name="cancellationToken">Stops the read between shared strings, sheets and rows.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ImportException">
        ///     The package is above a size limit or not a readable workbook, its manifest names another
        ///     entity, or an override names a sheet, column or path the workbook or entity lacks.
        /// </exception>
        Task<ImportPlan> InspectAsync(
            Stream workbook,
            EntityMetadata entity,
            StackLimits limits,
            InspectImportRequest request,
            ExcelContext context,
            CancellationToken cancellationToken = default);
    }
}
