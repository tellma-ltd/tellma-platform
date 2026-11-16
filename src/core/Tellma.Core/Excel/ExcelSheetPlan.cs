// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     One data sheet of a workbook plan: its name, the collection it holds, its columns and the
    ///     query its rows come from.
    /// </summary>
    /// <remarks>
    ///     Each row the query yields lists one value per column, in column order: a planner whose
    ///     columns read several select terms folds them before the writer sees the row. The writer
    ///     makes the name a valid, unique sheet name, and the manifest records the name it wrote.
    /// </remarks>
    /// <param name="Name">The sheet's name before scrubbing: an entity's plural label, or the labels of a collection path.</param>
    /// <param name="Collection">The collection path the sheet holds (<c>WidgetParts.WidgetPartNotes</c>); null for the parent sheet.</param>
    /// <param name="Columns">The sheet's columns, in order; at least one.</param>
    /// <param name="NumberFormats">Parallel to <paramref name="Columns" />: a column's number format; null for the format its type takes.</param>
    /// <param name="Query">
    ///     The query the sheet's rows come from: the parent sheet's root query, or, for a child
    ///     sheet, the very instance among the dependents of its owner sheet's query.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="Name" /> is blank, <paramref name="Collection" /> is empty or white space,
    ///     <paramref name="Columns" /> is empty or holds a null entry, or
    ///     <paramref name="NumberFormats" /> is not parallel to it.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="NumberFormats" /> or <paramref name="Query" /> is null.</exception>
    public sealed record ExcelSheetPlan(
        string Name,
        string? Collection,
        IReadOnlyList<ExcelColumnSpec> Columns,
        IReadOnlyList<string?> NumberFormats,
        ExcelQuery Query)
    {
        /// <inheritdoc cref="ExcelSheetPlan(string, string?, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, ExcelQuery)" />
        public string Name { get; init => field = NotBlank(value, nameof(Name)); } = NotBlank(Name, nameof(Name));

        /// <inheritdoc cref="ExcelSheetPlan(string, string?, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, ExcelQuery)" />
        public string? Collection { get; init => field = value is null ? null : NotBlank(value, nameof(Collection)); } =
            Collection is null ? null : NotBlank(Collection, nameof(Collection));

        /// <inheritdoc cref="ExcelSheetPlan(string, string?, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, ExcelQuery)" />
        public IReadOnlyList<ExcelColumnSpec> Columns { get; init => field = ReadOnlyCopies.OfEntries(value, requireOne: true, nameof(Columns)); } =
            ReadOnlyCopies.OfEntries(Columns, requireOne: true, nameof(Columns));

        /// <inheritdoc cref="ExcelSheetPlan(string, string?, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, ExcelQuery)" />
        public IReadOnlyList<string?> NumberFormats { get; init => field = Formats(value, Columns.Count, nameof(NumberFormats)); } =
            Formats(NumberFormats, Columns?.Count ?? 0, nameof(NumberFormats));

        /// <inheritdoc cref="ExcelSheetPlan(string, string?, IReadOnlyList{ExcelColumnSpec}, IReadOnlyList{string?}, ExcelQuery)" />
        public ExcelQuery Query { get; init => field = value ?? throw new ArgumentNullException(nameof(Query)); } =
            Query ?? throw new ArgumentNullException(nameof(Query));

        /// <summary>Refuses a blank value.</summary>
        /// <param name="value">The value.</param>
        /// <param name="name">The member it came from.</param>
        /// <returns>The value.</returns>
        private static string NotBlank(string value, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
            return value;
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
    }
}
