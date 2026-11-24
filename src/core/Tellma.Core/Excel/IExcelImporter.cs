// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Reads uploaded workbooks for import: the package within its limits, the manifest, and each
    ///     sheet's header row and size, mapped to an entity; and for an import, every row decoded,
    ///     linked and ordered. Pure: it reads only the stream it is handed and the scratch directory
    ///     it spools shared strings to, and never opens a connection.
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

        /// <summary>
        ///     Reads a whole workbook for import: maps it, decodes every mapped cell by its mapped
        ///     type, checks the row keys and unique values within the file, links each child row to
        ///     its owner row at every depth, counts each owner's rows, and orders the parent rows by
        ///     their self-references. Any error refuses the file before a lookup could run.
        /// </summary>
        /// <typeparam name="TEntity">The stack's leaf.</typeparam>
        /// <param name="workbook">The workbook: a readable, seekable stream positioned at its start, which stays the caller's.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="limits">The stack's limits: the save sizes a synchronous import and one parent row fit within, and the id ceiling of a lookup.</param>
        /// <param name="request">The import's mode, row key and overrides, already checked against the entity.</param>
        /// <param name="context">The request's context: the target tenant, its languages, the culture and the labels.</param>
        /// <param name="synchronous">Whether the import runs in the request, so the synchronous limits apply.</param>
        /// <param name="cancellationToken">Stops the read between shared strings, sheets and rows.</param>
        /// <returns>The session the import's chunks resolve from.</returns>
        /// <exception cref="BadRequestException">An update or upsert names no row key the file offers.</exception>
        /// <exception cref="ImportException">
        ///     The package is above a size limit or not a readable workbook, a synchronous import is
        ///     above its limits, or the mapping or a row holds an error.
        /// </exception>
        Task<ExcelImportSession<TEntity>> ParseAsync<TEntity>(
            Stream workbook,
            EntityMetadata entity,
            StackLimits limits,
            ImportRequest request,
            ExcelContext context,
            bool synchronous,
            CancellationToken cancellationToken = default)
            where TEntity : class;
    }
}
