// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     One chunk of an import, resolved and ready to save: the parent rows' entities in the
    ///     session's order, the cell each payload path came from, and the errors resolution found.
    /// </summary>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    /// <param name="Entities">
    ///     The chunk's parent rows as entities: a new row with a zero or temporary id, an existing row
    ///     overlaid on its stored entity with its id and expected stamp; each child collection null
    ///     when its sheet is absent, else the owner's complete children.
    /// </param>
    /// <param name="CoordinateMap">
    ///     Each payload path the workbook can place, mapped to its cell: a row's path to the row, a
    ///     mapped property's path to its column. A path missing from the map is placed by its longest
    ///     prefix that is present.
    /// </param>
    /// <param name="Errors">The errors resolution found: references, row keys, write-once values and child sheets; empty when the chunk can be saved.</param>
    public sealed record ExcelChunk<TEntity>(
        IReadOnlyList<TEntity> Entities,
        IReadOnlyDictionary<ValidationPath, ExcelCell> CoordinateMap,
        IReadOnlyList<ImportError> Errors)
    {
        /// <summary>What the chunk's save does to each parent row and its children, for the outcome and for the save's errors.</summary>
        internal ImportChunkFacts Facts { get; init; } = ImportChunkFacts.Empty;
    }

    /// <summary>A cell of the workbook: the sheet, the Excel row, and the column's letters.</summary>
    /// <param name="Sheet">The sheet's name.</param>
    /// <param name="Row">The Excel row, 1-based.</param>
    /// <param name="Column">The column's letters (<c>C</c>); null for a place that is the row alone.</param>
    public sealed record ExcelCell(string Sheet, int Row, string? Column);

    /// <summary>
    ///     What a resolved chunk's save does: each parent row's identity and where its stamp came
    ///     from, and the counts of the rows it inserts, updates and deletes.
    /// </summary>
    /// <param name="Rows">The parent rows, parallel to the chunk's entities.</param>
    /// <param name="Inserted">The parent rows inserted.</param>
    /// <param name="Updated">The parent rows updated.</param>
    /// <param name="ChildrenInserted">The child rows inserted, at every depth.</param>
    /// <param name="ChildrenUpdated">The child rows updated, at every depth.</param>
    /// <param name="ChildrenDeleted">The existing child rows dropped, never counting their own descendants.</param>
    internal sealed record ImportChunkFacts(
        IReadOnlyList<ImportChunkRow> Rows,
        int Inserted,
        int Updated,
        int ChildrenInserted,
        int ChildrenUpdated,
        int ChildrenDeleted)
    {
        /// <summary>The facts of a chunk with no rows.</summary>
        public static ImportChunkFacts Empty { get; } = new([], 0, 0, 0, 0, 0);
    }

    /// <summary>One parent row of a resolved chunk.</summary>
    /// <param name="Position">The row's position in the session's order.</param>
    /// <param name="Sheet">The parent sheet's name.</param>
    /// <param name="Row">The Excel row.</param>
    /// <param name="Id">The id the payload carries: the stored id of an update, a temporary id or zero for an insert.</param>
    /// <param name="IsNew">Whether the row is inserted.</param>
    /// <param name="SheetStamp">Whether the expected stamp is the sheet's rather than the one read at hydration.</param>
    /// <param name="KeyColumn">The letters of the column that keys the row: its row key's, else its identity's; null when none is mapped.</param>
    /// <param name="StampColumn">The letters of the stamp column; null when none is mapped.</param>
    internal sealed record ImportChunkRow(
        int Position,
        string Sheet,
        int Row,
        long Id,
        bool IsNew,
        bool SheetStamp,
        string? KeyColumn,
        string? StampColumn);
}
