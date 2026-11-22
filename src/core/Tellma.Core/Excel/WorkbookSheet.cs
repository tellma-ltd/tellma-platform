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

    /// <summary>The bounds an uploaded workbook is read within.</summary>
    /// <param name="MaxCompressedBytes">The largest package accepted, in bytes.</param>
    /// <param name="MaxUncompressedBytes">The largest sum of the package's part lengths accepted, in bytes.</param>
    /// <param name="SharedStringBudget">The bytes the shared strings may be charged before they spill to scratch files.</param>
    /// <param name="MaxSharedStringEntries">The most entries the shared-string table may hold.</param>
    internal sealed record WorkbookLimits(long MaxCompressedBytes, long MaxUncompressedBytes, long SharedStringBudget, int MaxSharedStringEntries);

    /// <summary>One stored cell of a sheet's row, read.</summary>
    /// <param name="Column">The cell's zero-based column.</param>
    /// <param name="Value">The cell's kind and text.</param>
    internal readonly record struct SheetCell(int Column, ExcelRawCell Value);
}
