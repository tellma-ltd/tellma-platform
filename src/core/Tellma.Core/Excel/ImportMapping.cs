// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>A workbook mapped to a stack's entity: the plan the caller sees, and each sheet's binding and columns for the reader that follows.</summary>
    /// <param name="Plan">The plan.</param>
    /// <param name="Sheets">Every sheet of the workbook, in tab order.</param>
    /// <param name="Manifest">The workbook's manifest; null when it carries none.</param>
    internal sealed record ImportMapping(ImportPlan Plan, IReadOnlyList<ImportMappedSheet> Sheets, ImportManifest? Manifest);

    /// <summary>One sheet as the mapping bound it.</summary>
    /// <param name="Sheet">The workbook's sheet.</param>
    /// <param name="Status">What the sheet was bound to.</param>
    /// <param name="Collection">The collection a child or duplicate child sheet stands for; null for the parent sheet and an unbound one.</param>
    /// <param name="Entity">The entity of the sheet's rows; null for a sheet bound to nothing.</param>
    /// <param name="HeaderRow">The one-based row of the sheet's headers; 0 when it holds none or was not read.</param>
    /// <param name="Rows">The data rows below the header row, from the sheet's dimension or a forward count.</param>
    /// <param name="Columns">The header row's columns, in column order.</param>
    internal sealed record ImportMappedSheet(
        WorkbookSheet Sheet,
        ImportSheetStatus Status,
        ImportCollection? Collection,
        EntityMetadata? Entity,
        int HeaderRow,
        long Rows,
        IReadOnlyList<ImportMappedColumn> Columns)
    {
        /// <summary>The sheet's collection path as the plan reports it: <c>""</c> for the parent, null for a sheet bound to nothing.</summary>
        public string? CollectionPath => Status switch
        {
            ImportSheetStatus.Parent => string.Empty,
            ImportSheetStatus.Child => Collection?.Path,
            ImportSheetStatus.Duplicate => Collection?.Path ?? string.Empty,
            ImportSheetStatus.Manifest or ImportSheetStatus.Ignored => null,
            _ => null,
        };
    }

    /// <summary>One header cell as the mapping resolved it.</summary>
    /// <param name="Index">The column's zero-based index.</param>
    /// <param name="Header">The header as the sheet holds it.</param>
    /// <param name="Path">The resolved path; null for a column that maps to nothing.</param>
    /// <param name="Status">What the column does.</param>
    /// <param name="Language">The tenant language the column resolved to, or the unconfigured language it names; null for none.</param>
    /// <param name="Suggestions">For an unmapped column, the paths nearest its header.</param>
    internal sealed record ImportMappedColumn(int Index, string Header, ImportPath? Path, ImportColumnStatus Status, string? Language, IReadOnlyList<string> Suggestions)
    {
        /// <summary>The column's letter.</summary>
        public string Letter => ColumnLetters.Of(Index);
    }
}
