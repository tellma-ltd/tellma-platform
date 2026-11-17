// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>What an import plan made of one sheet: what it holds, how many rows, and each column.</summary>
    /// <param name="Sheet">The sheet's name, as the workbook spells it.</param>
    /// <param name="Collection">
    ///     The collection path the sheet holds: an empty string for the parent rows, null when the
    ///     sheet holds nothing the import reads.
    /// </param>
    /// <param name="Status">What the plan made of the sheet.</param>
    /// <param name="Rows">The sheet's data rows, the header excluded.</param>
    /// <param name="Columns">The sheet's columns, in workbook order.</param>
    /// <exception cref="ArgumentException"><paramref name="Sheet" /> is null, empty or white space, or a column is null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="Columns" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="Rows" /> is negative.</exception>
    public sealed record ImportPlanSheet(
        string Sheet,
        string? Collection,
        ImportSheetStatus Status,
        long Rows,
        IReadOnlyList<ImportPlanColumn> Columns)
    {
        /// <inheritdoc cref="ImportPlanSheet(string, string?, ImportSheetStatus, long, IReadOnlyList{ImportPlanColumn})" />
        public string Sheet { get; init => field = NotBlank(value); } = NotBlank(Sheet);

        /// <inheritdoc cref="ImportPlanSheet(string, string?, ImportSheetStatus, long, IReadOnlyList{ImportPlanColumn})" />
        public long Rows { get; init => field = NotNegative(value); } = NotNegative(Rows);

        /// <inheritdoc cref="ImportPlanSheet(string, string?, ImportSheetStatus, long, IReadOnlyList{ImportPlanColumn})" />
        public IReadOnlyList<ImportPlanColumn> Columns { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: false, nameof(Columns)); } =
            ReadOnlyCopies.OfEntries(Columns, requireOne: false, nameof(Columns));

        /// <summary>Refuses a blank sheet name.</summary>
        /// <param name="value">The name.</param>
        /// <returns>The name.</returns>
        private static string NotBlank(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Sheet));
            return value;
        }

        /// <summary>Refuses a negative row count.</summary>
        /// <param name="value">The count.</param>
        /// <returns>The count.</returns>
        private static long NotNegative(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Rows));
            return value;
        }
    }
}
