// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>One data row of a table: its cells, its style and the first cell's indent.</summary>
    /// <param name="Cells">Parallel to the table's columns: each cell's value, unformatted; null for a blank cell.</param>
    /// <param name="Style">How the row is styled.</param>
    /// <param name="Indent">The first cell's alignment indent, in Excel's indent steps; 0 for none, at most 250.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Cells" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Indent" /> is negative or above 250.</exception>
    public sealed record ExcelTableRow(IReadOnlyList<object?> Cells, ExcelRowStyle Style, int Indent)
    {
        /// <summary>The deepest indent Excel renders.</summary>
        public const int MaxIndent = 250;

        /// <inheritdoc cref="ExcelTableRow(IReadOnlyList{object?}, ExcelRowStyle, int)" />
        public IReadOnlyList<object?> Cells { get; init => field = Copy(value); } = Copy(Cells);

        /// <inheritdoc cref="ExcelTableRow(IReadOnlyList{object?}, ExcelRowStyle, int)" />
        public int Indent { get; init => field = ValidIndent(value); } = ValidIndent(Indent);

        /// <summary>Copies the cells.</summary>
        /// <param name="value">The cells.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<object?> Copy(IReadOnlyList<object?> value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(Cells));
            return ReadOnlyCopies.Of(value);
        }

        /// <summary>Refuses an indent Excel cannot render.</summary>
        /// <param name="value">The indent.</param>
        /// <returns>The indent.</returns>
        private static int ValidIndent(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Indent));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxIndent, nameof(Indent));
            return value;
        }
    }
}
