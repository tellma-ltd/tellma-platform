// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>A caller's mapping of one workbook sheet, which wins over the manifest and the sheet names.</summary>
    /// <param name="Sheet">The sheet's name, as the workbook spells it.</param>
    /// <param name="Collection">
    ///     What the sheet holds: an empty string for the parent rows, a collection path
    ///     (<c>RoleMemberships</c>, <c>WidgetParts.WidgetPartNotes</c>) for that collection's rows, or
    ///     null to ignore the sheet.
    /// </param>
    public sealed record ImportSheetMapping(string Sheet, string? Collection);
}
