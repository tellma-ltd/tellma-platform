// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     One sheet of a parsed import: the parent sheet or a present child sheet, with the columns
    ///     the import reads, its rows, and the sheets above and below it.
    /// </summary>
    /// <param name="mapped">The sheet as the mapping bound it.</param>
    /// <param name="entity">The entity of the sheet's rows.</param>
    /// <param name="columns">The columns the import reads, in column order.</param>
    internal sealed class ImportSheet(ImportMappedSheet mapped, EntityMetadata entity, IReadOnlyList<ImportColumn> columns)
    {
        /// <summary>The sheet as the mapping bound it.</summary>
        public ImportMappedSheet Mapped { get; } = mapped ?? throw new ArgumentNullException(nameof(mapped));

        /// <summary>The sheet's name.</summary>
        public string Name => Mapped.Sheet.Name;

        /// <summary>The entity of the sheet's rows.</summary>
        public EntityMetadata Entity { get; } = entity ?? throw new ArgumentNullException(nameof(entity));

        /// <summary>The collection the sheet stands for; null for the parent sheet.</summary>
        public ImportCollection? Collection => Mapped.Collection;

        /// <summary>The collection path the sheet stands for; empty for the parent sheet.</summary>
        public string Path => Collection?.Path ?? string.Empty;

        /// <summary>The columns the import reads, in column order.</summary>
        public IReadOnlyList<ImportColumn> Columns { get; } = columns ?? throw new ArgumentNullException(nameof(columns));

        /// <summary>The identity column; null when the sheet maps none the import reads.</summary>
        public ImportColumn? Id => Columns.FirstOrDefault(static column => column.Path.Kind == ImportPathKind.Id);

        /// <summary>The stamp column; null when the sheet maps none the import reads.</summary>
        public ImportColumn? Stamp => Columns.FirstOrDefault(static column => column.Path.Kind == ImportPathKind.Stamp);

        /// <summary>A child sheet's link to its owner row; null for the parent sheet, and for a child sheet mapping none.</summary>
        public ImportColumn? ParentLink => Columns.FirstOrDefault(static column => column.Path.Kind == ImportPathKind.ParentReference);

        /// <summary>The sheet of the owner rows; null for the parent sheet, and for a child sheet whose owner sheet is absent.</summary>
        public ImportSheet? Owner { get; set; }

        /// <summary>The present child sheets of the collections the sheet's rows own, in collection order.</summary>
        public List<ImportSheet> Children { get; } = [];

        /// <summary>The sheet's data rows, in Excel order; a fully blank row is none.</summary>
        public List<ImportRow> Rows { get; } = [];
    }

    /// <summary>A column the import reads: its mapping, its path, what its cells decode into, and its slot in a row's values.</summary>
    /// <param name="Mapped">The column as the mapping resolved it.</param>
    /// <param name="Path">The column's path.</param>
    /// <param name="Target">What the column's cells decode into.</param>
    /// <param name="Slot">The column's index in its sheet's columns, and in each row's values.</param>
    internal sealed record ImportColumn(ImportMappedColumn Mapped, ImportPath Path, CellDecodeTarget Target, int Slot)
    {
        /// <summary>The column's letters.</summary>
        public string Letter => Mapped.Letter;

        /// <summary>The column's header.</summary>
        public string Header => Mapped.Header;

        /// <summary>Whether the column is a reference to the stack's own entity, which may name a row of the workbook.</summary>
        public bool ToStackEntity { get; init; }

        /// <summary>
        ///     For a reference to the stack's entity, the parent sheet's column holding the same key,
        ///     against which its values are matched within the workbook first; null when the parent
        ///     sheet maps none.
        /// </summary>
        public ImportColumn? InSheetKey { get; init; }
    }

    /// <summary>
    ///     A cycle of self-references among parent rows whose newness the parse cannot tell, refused
    ///     at resolution only when every row on it is new.
    /// </summary>
    /// <param name="Rows">The parent rows on the cycle, in Excel order.</param>
    /// <param name="Columns">For each parent row, the column naming another row of the cycle.</param>
    /// <param name="Referrers">For each parent row, the row whose cell of that column names it: the parent row itself, or a row under it.</param>
    internal sealed record ImportCycle(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportColumn> Columns, IReadOnlyList<ImportRow> Referrers);

    /// <summary>One data row of a parsed sheet: its Excel row, its decoded values, and its links.</summary>
    /// <param name="sheet">The row's sheet.</param>
    /// <param name="row">The row's one-based Excel row.</param>
    /// <param name="values">The decoded values, parallel to the sheet's columns.</param>
    internal sealed class ImportRow(ImportSheet sheet, int row, object?[] values)
    {
        /// <summary>The row's sheet.</summary>
        public ImportSheet Sheet { get; } = sheet ?? throw new ArgumentNullException(nameof(sheet));

        /// <summary>The row's one-based Excel row.</summary>
        public int Row { get; } = row;

        /// <summary>The decoded values, parallel to the sheet's columns; null for a blank or refused cell.</summary>
        public object?[] Values { get; } = values ?? throw new ArgumentNullException(nameof(values));

        /// <summary>The row's identity cell, as a whole number; null when blank or unmapped.</summary>
        public long? Id => Sheet.Id is ImportColumn id ? (long?)Values[id.Slot] : null;

        /// <summary>The owner row a child row links to; null for a parent row and an orphan.</summary>
        public ImportRow? Owner { get; set; }

        /// <summary>The rows linked to this one, by child sheet; null while none are.</summary>
        public Dictionary<ImportSheet, List<ImportRow>>? Children { get; set; }

        /// <summary>A parent row's position in the import's order; -1 for a child row.</summary>
        public int Position { get; set; } = -1;

        /// <summary>
        ///     The rows of the workbook a reference cell of this row names, by the column's slot: a
        ///     reference to the stack's entity whose value matched exactly one parent row. Null while
        ///     none matched.
        /// </summary>
        public Dictionary<int, ImportRow>? InSheetTargets { get; set; }

        /// <summary>The value of a column.</summary>
        /// <param name="column">The column, of the row's sheet.</param>
        /// <returns>The decoded value; null for a blank or refused cell.</returns>
        public object? this[ImportColumn column]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(column);
                return Values[column.Slot];
            }
        }

        /// <summary>The rows linked to this one on a child sheet.</summary>
        /// <param name="child">The child sheet.</param>
        /// <returns>The rows, in Excel order; empty for none.</returns>
        public IReadOnlyList<ImportRow> ChildrenOn(ImportSheet child)
        {
            ArgumentNullException.ThrowIfNull(child);
            return Children?.GetValueOrDefault(child) ?? (IReadOnlyList<ImportRow>)[];
        }

        /// <summary>Links a child row to this one.</summary>
        /// <param name="child">The child row.</param>
        public void Adopt(ImportRow child)
        {
            ArgumentNullException.ThrowIfNull(child);
            Children ??= [];
            if (!Children.TryGetValue(child.Sheet, out List<ImportRow>? rows))
            {
                rows = [];
                Children[child.Sheet] = rows;
            }

            rows.Add(child);
            child.Owner = this;
        }

        /// <summary>The rows under this one at every depth, this one excluded.</summary>
        /// <returns>The rows, depth first.</returns>
        public IEnumerable<ImportRow> Descendants()
        {
            foreach (ImportSheet child in Sheet.Children)
            {
                foreach (ImportRow row in ChildrenOn(child))
                {
                    yield return row;
                    foreach (ImportRow descendant in row.Descendants())
                    {
                        yield return descendant;
                    }
                }
            }
        }
    }
}
