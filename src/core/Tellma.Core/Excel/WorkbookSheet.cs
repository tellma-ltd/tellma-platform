// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>Whether a sheet shows in the workbook's tabs.</summary>
    internal enum WorkbookSheetState
    {
        /// <summary>A tab the user sees.</summary>
        Visible,

        /// <summary>Hidden, but listed by the workbook's Unhide command; read like a visible sheet.</summary>
        Hidden,

        /// <summary>Hidden from the workbook's own commands; never mapped unless a sheet mapping binds it.</summary>
        VeryHidden,
    }

    /// <summary>One sheet the workbook lists, in tab order.</summary>
    /// <param name="Position">The sheet's zero-based place in the workbook's tab order.</param>
    /// <param name="Name">The sheet's name, unique in the workbook ignoring case.</param>
    /// <param name="State">Whether the sheet shows.</param>
    /// <param name="Part">The worksheet part holding its cells; null for a chart, dialog or macro sheet, which holds none.</param>
    internal sealed record WorkbookSheet(int Position, string Name, WorkbookSheetState State, string? Part);

    // The decoded text of a sheet is held a row at a time, so the row bound is what memory pays; the
    // sheet bound caps the work a sheet whose cells repeat one long shared string can make. Neither is
    // an option of spec 0019 §14.1: the importer sets each to the limit it stands beside, the
    // in-memory shared-string budget and the uncompressed limit.
    /// <summary>The bounds an uploaded workbook is read within.</summary>
    /// <param name="MaxCompressedBytes">The largest package accepted, in bytes.</param>
    /// <param name="MaxUncompressedBytes">The largest sum of the package's part lengths accepted, in bytes.</param>
    /// <param name="SharedStringBudget">The bytes the shared strings may be charged before they spill to scratch files.</param>
    /// <param name="MaxSharedStringEntries">The most entries the shared-string table may hold.</param>
    /// <param name="RowTextBudget">The most UTF-16 bytes of text one row's cells may decode to.</param>
    /// <param name="SheetTextBudget">The most UTF-16 bytes of text one sheet's cells may decode to.</param>
    internal sealed record WorkbookLimits(
        long MaxCompressedBytes,
        long MaxUncompressedBytes,
        long SharedStringBudget,
        int MaxSharedStringEntries,
        long RowTextBudget = WorkbookLimits.DefaultRowTextBudget,
        long SheetTextBudget = WorkbookLimits.DefaultSheetTextBudget)
    {
        /// <summary>The row budget unless one is given: the codec's default in-memory shared-string budget.</summary>
        public const long DefaultRowTextBudget = 32L * 1024 * 1024;

        /// <summary>The sheet budget unless one is given: the codec's default uncompressed limit.</summary>
        public const long DefaultSheetTextBudget = 2L * 1024 * 1024 * 1024;
    }

    /// <summary>One stored cell of a sheet's row, read.</summary>
    /// <param name="Column">The cell's zero-based column.</param>
    /// <param name="Value">The cell's kind and text.</param>
    internal readonly record struct SheetCell(int Column, ExcelRawCell Value);
}
