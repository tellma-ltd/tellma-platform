// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     One import failure or warning at the workbook cell it concerns: a codec error, or a
    ///     pipeline validation error mapped from its path to the cell that produced the value.
    /// </summary>
    /// <remarks>
    ///     A failure the workbook cannot place, such as a validator's error on the whole payload, has
    ///     neither sheet nor row.
    /// </remarks>
    /// <param name="Sheet">The sheet's name; null when the failure has no place in the workbook.</param>
    /// <param name="Row">The Excel row, 1-based; null when the failure concerns no row.</param>
    /// <param name="Column">The column's letters (<c>C</c>); null when the failure concerns no column.</param>
    /// <param name="Header">The column's header text; null when the failure concerns no column.</param>
    /// <param name="Property">The CLR name of the property the value maps to; null when the failure ends at a row or concerns the whole file.</param>
    /// <param name="Code">The failure's code, a dotted PascalCase resource key.</param>
    /// <param name="Arguments">The raw values its message renders from; empty when it has none.</param>
    public sealed record ImportError(
        string? Sheet,
        int? Row,
        string? Column,
        string? Header,
        string? Property,
        string Code,
        IReadOnlyDictionary<string, object?> Arguments)
        : CodedError(Code, Arguments);
}
