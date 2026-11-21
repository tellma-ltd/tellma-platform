// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     A caller's mapping of one workbook column, which wins over the manifest and the headers. A
    ///     column is addressed by its letters, never by its header text, which may repeat, be blank or
    ///     look like a path.
    /// </summary>
    /// <param name="Sheet">The sheet's name, as the workbook spells it.</param>
    /// <param name="Column">The column's letters (<c>A</c> to <c>XFD</c>, either case), as the import plan reports them.</param>
    /// <param name="Path">The column path to map the column to (<c>Name</c>, <c>Parent.Code</c>); null ignores the column.</param>
    public sealed record ImportColumnMapping(string Sheet, string Column, string? Path);
}
