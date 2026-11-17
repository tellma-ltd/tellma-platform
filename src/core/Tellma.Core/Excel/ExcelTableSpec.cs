// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A one-sheet table the table writer writes: a report's sheet, with its header rows, columns
    ///     and frozen columns. The workbook carries no manifest, since a report is never imported.
    /// </summary>
    /// <param name="SheetName">The sheet's name before scrubbing.</param>
    /// <param name="FileNameStem">The report's title, which names the file; <c>report</c> when its ASCII form leaves nothing.</param>
    /// <param name="HeaderRows">The bold rows above the data, in order; at least one, each as wide as the columns at most, leaving a worksheet row for data. A null cell is blank.</param>
    /// <param name="Columns">One per sheet column, at most 16,384; each row's cells encode by its column's type.</param>
    /// <param name="NumberFormats">Parallel to <paramref name="Columns" />: a column's number format; null for the format its type takes.</param>
    /// <param name="FrozenColumns">The columns before the frozen pane; 0 for none.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="SheetName" /> or <paramref name="FileNameStem" /> is blank, a list is empty
    ///     where one entry is required or holds a null entry, there are more columns than a worksheet
    ///     holds, a header row is wider than the columns, the header rows leave no worksheet row for
    ///     data, or <paramref name="NumberFormats" /> is not parallel to the columns.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="NumberFormats" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="FrozenColumns" /> is negative or above the column count.</exception>
    public sealed record ExcelTableSpec(
        string SheetName,
        string FileNameStem,
        IReadOnlyList<IReadOnlyList<string?>> HeaderRows,
        IReadOnlyList<ExcelColumnSpec> Columns,
        IReadOnlyList<string?> NumberFormats,
        int FrozenColumns)
    {
        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public string SheetName { get; init => field = NotBlank(value, nameof(SheetName)); } = NotBlank(SheetName, nameof(SheetName));

        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public string FileNameStem { get; init => field = NotBlank(value, nameof(FileNameStem)); } = NotBlank(FileNameStem, nameof(FileNameStem));

        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public IReadOnlyList<ExcelColumnSpec> Columns { get; init => field = ValidColumns(value, nameof(Columns)); } = ValidColumns(Columns, nameof(Columns));

        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public IReadOnlyList<IReadOnlyList<string?>> HeaderRows { get; init => field = ValidHeaderRows(value, Columns.Count, nameof(HeaderRows)); } =
            ValidHeaderRows(HeaderRows, Columns?.Count ?? 0, nameof(HeaderRows));

        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public IReadOnlyList<string?> NumberFormats { get; init => field = Formats(value, Columns.Count, nameof(NumberFormats)); } =
            Formats(NumberFormats, Columns?.Count ?? 0, nameof(NumberFormats));

        /// <inheritdoc cref="ExcelTableSpec(string, string, IReadOnlyList{IReadOnlyList{string?}}, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, int)" />
        public int FrozenColumns { get; init => field = ValidFrozen(value, Columns.Count); } = ValidFrozen(FrozenColumns, Columns?.Count ?? 0);

        /// <summary>Refuses a blank value.</summary>
        /// <param name="value">The value.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The value.</returns>
        private static string NotBlank(string value, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
            return value;
        }

        /// <summary>Copies the columns, refusing none, a null entry and more than a worksheet holds.</summary>
        /// <param name="value">The columns.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<ExcelColumnSpec> ValidColumns(IReadOnlyList<ExcelColumnSpec> value, string name)
        {
            ReadOnlyCollection<ExcelColumnSpec> columns = ReadOnlyCopies.OfEntries(value, requireOne: true, name);
            return columns.Count <= ColumnLetters.MaxColumns
                ? columns
                : throw new ArgumentException("A worksheet holds at most 16,384 columns.", name);
        }

        /// <summary>Copies the header rows, refusing none, a null row, a row wider than the columns and so many rows that no data row fits.</summary>
        /// <param name="value">The rows.</param>
        /// <param name="columns">The number of columns.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>A read-only copy of each row.</returns>
        private static ReadOnlyCollection<IReadOnlyList<string?>> ValidHeaderRows(IReadOnlyList<IReadOnlyList<string?>> value, int columns, string name)
        {
            ReadOnlyCollection<IReadOnlyList<string?>> rows = ReadOnlyCopies.OfEntries(value, requireOne: true, name);
            return rows.Count >= ColumnLetters.MaxRows
                ? throw new ArgumentException("The header rows must leave a worksheet row for data.", name)
                : rows.Any(row => row.Count > columns)
                ? throw new ArgumentException("A header row is at most as wide as the columns.", name)
                : ReadOnlyCopies.Of(rows.Select(static row => (IReadOnlyList<string?>)ReadOnlyCopies.Of(row)));
        }

        /// <summary>Copies the number formats, refusing a list not parallel to the columns.</summary>
        /// <param name="value">The formats.</param>
        /// <param name="columns">The number of columns.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<string?> Formats(IReadOnlyList<string?> value, int columns, string name)
        {
            ArgumentNullException.ThrowIfNull(value, name);
            return value.Count == columns
                ? ReadOnlyCopies.Of(value)
                : throw new ArgumentException("The number formats must be parallel to the columns.", name);
        }

        /// <summary>Refuses a frozen column count outside the sheet.</summary>
        /// <param name="value">The count.</param>
        /// <param name="columns">The number of columns.</param>
        /// <returns>The count.</returns>
        private static int ValidFrozen(int value, int columns)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(FrozenColumns));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, columns, nameof(FrozenColumns));
            return value;
        }
    }
}
