// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>The facts a parsed import resolves under: the stack, the request's mode and row key, and the file's source.</summary>
    /// <param name="Entity">The stack's entity.</param>
    /// <param name="Limits">The stack's limits, whose id ceiling splits a lookup's values.</param>
    /// <param name="Context">The request's context, whose tenant languages a replaced collection's columns are counted in.</param>
    /// <param name="Mode">The import's mode.</param>
    /// <param name="RowKey">The row key of an update or upsert: <c>Id</c> or a natural key's property; null for an insert.</param>
    /// <param name="KeyColumn">The parent sheet's column of the row key; for an insert, its identity column; null for none.</param>
    /// <param name="SameSource">Whether the file is same-source.</param>
    /// <param name="UseStamps">Whether a parent row's non-blank stamp cell is its expected stamp.</param>
    internal sealed record ImportSessionShape(
        EntityMetadata Entity,
        StackLimits Limits,
        ExcelContext Context,
        ImportMode Mode,
        string? RowKey,
        ImportColumn? KeyColumn,
        bool SameSource,
        bool UseStamps);

    /// <summary>A resolved chunk: the parent rows' entities, the cell of each payload path, the errors, and the facts of the save.</summary>
    /// <param name="Entities">The parent rows' entities, in the session's order.</param>
    /// <param name="CoordinateMap">Each payload path the workbook places, mapped to its cell.</param>
    /// <param name="Errors">The errors resolution found.</param>
    /// <param name="Facts">What the chunk's save does to each parent row and its children.</param>
    internal sealed record ImportChunk(
        IReadOnlyList<object> Entities,
        IReadOnlyDictionary<ValidationPath, ExcelCell> CoordinateMap,
        IReadOnlyList<ImportError> Errors,
        ImportChunkFacts Facts);

    // Spec 0019 §10.4: the session a parse leaves, which every chunk of an import resolves from. It
    // holds the decoded rows in their order and knows nothing of the database: each chunk's lookups
    // are handed back with their rows, its stored rows with their details, and the ids an earlier
    // chunk's new rows were given, and resolution is a pure function of those, so a retry after a
    // stamp conflict re-runs it with refreshed rows.
    /// <summary>A parsed import: its rows in order, and the lookups, hydration and resolution of each chunk of them.</summary>
    internal sealed partial class ImportSession
    {
        /// <summary>The reason of an identity column an insert only links rows by, or ignores.</summary>
        public const string InsertModeReason = "InsertMode";

        /// <summary>The cells of the workbook's columns, by sheet and letters, for an error's header.</summary>
        private readonly Dictionary<(string Sheet, string Column), string> _headers = new(SheetColumnComparer.Instance);

        /// <summary>Creates the session.</summary>
        /// <param name="shape">The facts the import resolves under.</param>
        /// <param name="parent">The parent sheet; null when the workbook binds none.</param>
        /// <param name="sheets">Every sheet the import reads, in tab order.</param>
        /// <param name="order">The parent rows in their order, each given its position.</param>
        /// <param name="warnings">The warnings the import reports.</param>
        /// <param name="orderHash">The order's fingerprint.</param>
        /// <param name="workbookSheets">The workbook's sheets, whose tab order sorts the errors.</param>
        /// <param name="cycles">The cycles among rows whose newness the parse could not tell, refused at resolution when every row on one is new.</param>
        public ImportSession(
            ImportSessionShape shape,
            ImportSheet? parent,
            IReadOnlyList<ImportSheet> sheets,
            IReadOnlyList<ImportRow> order,
            IReadOnlyList<ImportError> warnings,
            string orderHash,
            IReadOnlyList<WorkbookSheet> workbookSheets,
            IReadOnlyList<ImportCycle> cycles)
        {
            Shape = shape ?? throw new ArgumentNullException(nameof(shape));
            Parent = parent;
            Sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
            Order = order ?? throw new ArgumentNullException(nameof(order));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
            OrderHash = orderHash ?? throw new ArgumentNullException(nameof(orderHash));
            WorkbookSheets = workbookSheets ?? throw new ArgumentNullException(nameof(workbookSheets));
            Cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));
            SheetRows = [.. order.Select(static row => row.Row)];
            foreach (ImportSheet sheet in sheets)
            {
                foreach (ImportColumn column in sheet.Columns)
                {
                    _headers[(sheet.Name, column.Letter)] = column.Header;
                }
            }
        }

        /// <summary>The facts the import resolves under.</summary>
        public ImportSessionShape Shape { get; }

        /// <summary>The parent sheet; null when the workbook binds none.</summary>
        public ImportSheet? Parent { get; }

        /// <summary>Every sheet the import reads, in tab order.</summary>
        public IReadOnlyList<ImportSheet> Sheets { get; }

        /// <summary>The parent rows in their order.</summary>
        public IReadOnlyList<ImportRow> Order { get; }

        /// <summary>The warnings the import reports.</summary>
        public IReadOnlyList<ImportError> Warnings { get; }

        /// <summary>The order's fingerprint.</summary>
        public string OrderHash { get; }

        /// <summary>The workbook's sheets.</summary>
        public IReadOnlyList<WorkbookSheet> WorkbookSheets { get; }

        /// <summary>The cycles among rows whose newness the parse could not tell.</summary>
        public IReadOnlyList<ImportCycle> Cycles { get; }

        /// <summary>The Excel row of each position.</summary>
        public IReadOnlyList<int> SheetRows { get; }

        /// <summary>
        ///     The lookups of a chunk: one query per target and key across every sheet's reference
        ///     cells, the natural row key's cells and the references to the stack's entity the chunk
        ///     does not bind itself, each restricted to the distinct normal values, a query per id
        ///     ceiling's worth. A negative or zero identity names no stored row and is never looked up.
        /// </summary>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <returns>The queries; empty for none.</returns>
        public IReadOnlyList<ExcelQuery> LookupsFor(int fromRow, int toRow)
        {
            CheckRange(fromRow, toRow);

            // --- The distinct values of each target and key, in first-seen order ---
            Dictionary<(string Target, string Key), LookupGroup> groups = [];
            List<LookupGroup> ordered = [];
            foreach ((EntityMetadata target, PropertyMetadata key, object value) in LookupValues(fromRow, toRow))
            {
                if (!groups.TryGetValue((target.Name, key.Name), out LookupGroup? group))
                {
                    group = new LookupGroup(target, key);
                    groups[(target.Name, key.Name)] = group;
                    ordered.Add(group);
                }

                if (group.Seen.Add(value))
                {
                    group.Values.Add(value);
                }
            }

            // --- One query per group of at most the id ceiling's values ---
            int ceiling = Math.Max(1, Shape.Limits.MaxIds);
            List<ExcelQuery> queries = [];
            foreach (LookupGroup group in ordered)
            {
                bool byId = group.Key.Name == group.Target.Key.Name;
                IReadOnlyList<string> select = byId ? [group.Target.Key.Name] : [group.Key.Name, group.Target.Key.Name];
                foreach (object[] chunk in group.Values.Chunk(ceiling))
                {
                    queries.Add(new ExcelQuery(group.Target.Name, select, OrderBy: null, new KeySetRestriction(group.Key.Name, LookupTable), chunk, Take: null, CountCap: null, Dependents: null));
                }
            }

            return queries;
        }

        /// <summary>
        ///     The stored rows an update or upsert chunk overlays: the positive identities under an
        ///     <c>Id</c> row key, the row key's lookup's ids under a natural one; none for an insert.
        /// </summary>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <param name="lookupRows">Each of the chunk's lookups with the rows it read.</param>
        /// <returns>The distinct ids, in position order.</returns>
        public IReadOnlyList<long> HydrationIds(int fromRow, int toRow, IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows)
        {
            CheckRange(fromRow, toRow);
            ArgumentNullException.ThrowIfNull(lookupRows);
            if (Shape.Mode == ImportMode.Insert || Shape.KeyColumn is null)
            {
                return [];
            }

            Lookups lookups = new(lookupRows);
            List<long> ids = [];
            HashSet<long> seen = [];
            for (int position = fromRow; position <= toRow; position++)
            {
                if (StoredId(Order[position], lookups) is long id && seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>
        ///     The negative sheet identities that the rows at a position or later name through a
        ///     reference to the stack's entity by its identity: the new rows whose committed ids a
        ///     resumed import must still bind them to.
        /// </summary>
        /// <param name="fromRow">The first position; the session's row count for none.</param>
        /// <returns>The identities.</returns>
        public IReadOnlySet<long> ReferencedFrom(int fromRow)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fromRow);
            HashSet<long> referenced = [];
            for (int position = fromRow; position < Order.Count; position++)
            {
                foreach (ImportRow row in Family(Order[position]))
                {
                    foreach (ImportColumn column in row.Sheet.Columns.Where(column => column.ToStackEntity && column.Path.Key?.Name == Shape.Entity.Key.Name))
                    {
                        if (row[column] is long id and < 0)
                        {
                            _ = referenced.Add(id);
                        }
                    }
                }
            }

            return referenced;
        }

        /// <summary>
        ///     For each new row of a saved chunk that a row at a later position names by its negative
        ///     sheet identity, that identity mapped to the id the save gave the row.
        /// </summary>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <param name="entities">The chunk's entities after the save, which carry the ids it gave.</param>
        /// <returns>The map.</returns>
        public IReadOnlyDictionary<long, long> LaterReferencedIds(int fromRow, int toRow, IReadOnlyList<object> entities)
        {
            CheckRange(fromRow, toRow);
            ArgumentNullException.ThrowIfNull(entities);
            if (entities.Count != toRow - fromRow + 1)
            {
                throw new ArgumentException("The chunk's entities are one per position of its range.", nameof(entities));
            }

            IReadOnlySet<long> later = ReferencedFrom(toRow + 1);
            Dictionary<long, long> ids = [];
            for (int position = fromRow; position <= toRow; position++)
            {
                if (Order[position].Id is long sheetId and < 0 && later.Contains(sheetId)
                    && Shape.Entity.Key.Getter(entities[position - fromRow]) is object saved && Convert.ToInt64(saved, System.Globalization.CultureInfo.InvariantCulture) is long id and > 0)
                {
                    ids[sheetId] = id;
                }
            }

            return ids;
        }

        /// <summary>
        ///     Maps a validation error of a chunk's save to the workbook: the cell of the longest
        ///     prefix of its path the chunk places, matched segment by segment, with the property its
        ///     path ends at; an error the chunk cannot place has no sheet and no row.
        /// </summary>
        /// <param name="error">The error.</param>
        /// <param name="coordinateMap">The saved chunk's payload paths, each mapped to its cell.</param>
        /// <returns>The import error, with the pipeline's code and arguments unchanged.</returns>
        public ImportError Translate(ValidationError error, IReadOnlyDictionary<ValidationPath, ExcelCell> coordinateMap)
        {
            ArgumentNullException.ThrowIfNull(error);
            ArgumentNullException.ThrowIfNull(coordinateMap);
            IReadOnlyList<PathSegment> segments = error.Path.Segments;
            string? property = segments.Count > 0 && segments[^1] is PropertySegment last ? last.Name : null;
            for (int length = segments.Count; length > 0; length--)
            {
                ValidationPath prefix = length == segments.Count ? error.Path : new ValidationPath([.. segments.Take(length)]);
                if (coordinateMap.TryGetValue(prefix, out ExcelCell? cell))
                {
                    return new ImportError(cell.Sheet, cell.Row, cell.Column, Header(cell), property, error.Code, error.Arguments);
                }
            }

            return new ImportError(Sheet: null, Row: null, Column: null, Header: null, property, error.Code, error.Arguments);
        }

        /// <summary>An error at a chunk's parent row, at one of its sheet's columns or at the row alone.</summary>
        /// <param name="row">The parent row's facts.</param>
        /// <param name="column">The column's letters; null for the row alone.</param>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The error.</returns>
        public ImportError AtRow(ImportChunkRow row, string? column, string code, IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentNullException.ThrowIfNull(row);
            ExcelCell cell = new(row.Sheet, row.Row, column);
            return new ImportError(row.Sheet, row.Row, column, column is null ? null : Header(cell), column is null ? null : PropertyOf(row.Sheet, column), code, arguments);
        }

        /// <summary>The row key's value of a chunk's parent row, as an error's argument names it.</summary>
        /// <param name="row">The parent row's facts.</param>
        /// <returns>The value; null when the row has none.</returns>
        public object? KeyValue(ImportChunkRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            return Shape.KeyColumn is ImportColumn key ? ImportKeys.Shown(Order[row.Position][key]) : null;
        }

        /// <summary>The sorted, capped exception of errors.</summary>
        /// <param name="errors">The errors.</param>
        /// <param name="max">The most errors reported.</param>
        /// <returns>The exception.</returns>
        public ImportException Refusal(IReadOnlyList<ImportError> errors, int max)
        {
            return ImportMapper.Refusal(WorkbookSheets, errors, max);
        }

        /// <summary>The header of a cell's column.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The header; null for a cell of the row alone.</returns>
        private string? Header(ExcelCell cell)
        {
            return cell.Column is string column ? _headers.GetValueOrDefault((cell.Sheet, column)) : null;
        }

        /// <summary>The property a sheet's column writes.</summary>
        /// <param name="sheet">The sheet's name.</param>
        /// <param name="column">The column's letters.</param>
        /// <returns>The property's name; null for a column the import does not read.</returns>
        private string? PropertyOf(string sheet, string column)
        {
            return Sheets.FirstOrDefault(candidate => string.Equals(candidate.Name, sheet, StringComparison.OrdinalIgnoreCase))?
                .Columns.FirstOrDefault(candidate => candidate.Letter == column)?.Path.Property.Name;
        }

        /// <summary>Refuses a range outside the order.</summary>
        /// <param name="fromRow">The first position.</param>
        /// <param name="toRow">The last position.</param>
        private void CheckRange(int fromRow, int toRow)
        {
            if (fromRow < 0 || toRow < fromRow || toRow >= Order.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(toRow), $"The range {fromRow}..{toRow} is not within the {Order.Count} positions of the import.");
            }
        }

        /// <summary>A parent row and every row under it, depth first.</summary>
        /// <param name="row">The parent row.</param>
        /// <returns>The rows.</returns>
        private static IEnumerable<ImportRow> Family(ImportRow row)
        {
            return [row, .. row.Descendants()];
        }

        /// <summary>Compares a sheet and a column: the sheet ignoring case, as sheet names are.</summary>
        private sealed class SheetColumnComparer : IEqualityComparer<(string Sheet, string Column)>
        {
            /// <summary>The comparer.</summary>
            public static readonly SheetColumnComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((string Sheet, string Column) x, (string Sheet, string Column) y)
            {
                return string.Equals(x.Column, y.Column, StringComparison.Ordinal) && string.Equals(x.Sheet, y.Sheet, StringComparison.OrdinalIgnoreCase);
            }

            /// <inheritdoc />
            public int GetHashCode((string Sheet, string Column) obj)
            {
                return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sheet), StringComparer.Ordinal.GetHashCode(obj.Column));
            }
        }
    }
}
