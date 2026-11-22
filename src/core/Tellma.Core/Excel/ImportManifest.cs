// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Tellma.Core.Excel
{
    // Spec 0019 §5.3: the manifest is untrusted input. It is read, never obeyed: a row that names no
    // sheet, an unparseable column letter, a blank header or path, or a language that is no BCP 47
    // code is dropped, and the mapper drops any row whose path the entity does not carry, so a
    // damaged manifest only falls back to header matching. Nothing it says grants anything: the
    // same-source gate decides whether Id and Stamp are considered, never whether a row is written.
    /// <summary>
    ///     A workbook's manifest as an import reads it from the hidden <c>_tellma</c> sheet: the
    ///     header block of the export that wrote it, and the column table recording each data
    ///     column's sheet, header, path and language.
    /// </summary>
    internal sealed partial class ImportManifest
    {
        /// <summary>The most rows of the header block read.</summary>
        public const int MaxHeaderRows = 64;

        /// <summary>The most rows of the column table read; the rest are ignored.</summary>
        public const int MaxColumnRows = 65_536;

        /// <summary>The longest value a header row or a column table field may hold, in characters; a longer one drops its row.</summary>
        public const int MaxFieldLength = 255;

        /// <summary>The most UTF-16 bytes of text the kept column table rows may hold; the rows past it are dropped.</summary>
        public const long MaxColumnTableBytes = 32L * 1024 * 1024;

        /// <summary>How many rows the read passes between two checks of its cancellation.</summary>
        private const int CancellationStride = 1024;

        /// <summary>The column table's headers the reader needs, without which the table is ignored.</summary>
        private static readonly string[] RequiredColumns = ["Sheet", "Column", "Header", "Path"];

        /// <summary>Creates the manifest.</summary>
        /// <param name="version">The manifest format version.</param>
        /// <param name="header">The header block, key to value.</param>
        /// <param name="columns">The column table's valid rows.</param>
        private ImportManifest(int version, IReadOnlyDictionary<string, string> header, IReadOnlyList<ImportManifestColumn> columns)
        {
            Version = version;
            Header = header;
            Columns = columns;
        }

        /// <summary>The manifest format version: always the one this codec reads.</summary>
        public int Version { get; }

        /// <summary>The header block, key to value; the first of a repeated key wins.</summary>
        public IReadOnlyDictionary<string, string> Header { get; }

        /// <summary>The entity the file was exported from; null when the block names none.</summary>
        public string? Entity => Header.GetValueOrDefault(ExcelManifest.EntityKey);

        /// <summary>The deployment the file was exported from; null when the block names none.</summary>
        public string? Deployment => Header.GetValueOrDefault(ExcelManifest.DeploymentKey);

        /// <summary>The tenant the file was exported from; null when the block names none, or not as an integer.</summary>
        public int? SourceTenantId => Header.GetValueOrDefault(ExcelManifest.SourceTenantIdKey) is string text
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;

        /// <summary>The entity's schema fingerprint when the file was written; null when the block names none.</summary>
        public string? SchemaFingerprint => Header.GetValueOrDefault(ExcelManifest.SchemaFingerprintKey);

        /// <summary>The column table's valid rows, in table order; for one sheet, the first of a repeated header wins.</summary>
        public IReadOnlyList<ImportManifestColumn> Columns { get; }

        /// <summary>Whether a text is a language code as the manifest records one: a BCP 47 tag.</summary>
        /// <param name="code">The text.</param>
        /// <returns>True when it is.</returns>
        public static bool IsLanguageCode(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            return LanguageCode().IsMatch(code);
        }

        /// <summary>Reads a manifest sheet.</summary>
        /// <param name="sheet">The sheet's reader, before its first row.</param>
        /// <param name="cancellationToken">Stops the read between rows.</param>
        /// <returns>The manifest; null when the sheet is no manifest of the format this codec reads.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The sheet's part is not well-formed.</exception>
        /// <exception cref="OperationCanceledException">The read was cancelled.</exception>
        public static ImportManifest? Read(SheetReader sheet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            List<SheetCell> cells = [];

            // --- Row 1: the marker and the format version this codec reads ---
            if (!sheet.Read(cells, out int row) || row != 1
                || Text(cells, 0) != ExcelManifest.VersionKey
                || !int.TryParse(Text(cells, 1), NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                || version != ExcelManifest.Version)
            {
                return null;
            }

            // --- The header block: key and value per row, up to the first blank row ---
            Dictionary<string, string> header = new(StringComparer.Ordinal) { [ExcelManifest.VersionKey] = Text(cells, 1)! };
            int last = row;
            bool more = sheet.Read(cells, out row);
            while (more && row == last + 1 && cells.Count > 0 && header.Count < MaxHeaderRows)
            {
                if (Text(cells, 0) is string key && Text(cells, 1) is string value && key.Length <= MaxFieldLength && value.Length <= MaxFieldLength)
                {
                    _ = header.TryAdd(key, value);
                }

                last = row;
                more = sheet.Read(cells, out row);
            }

            // --- The column table: its header row located by name, then one row per data column ---
            int skipped = 0;
            while (more && cells.Count == 0)
            {
                more = sheet.Read(cells, out row);
                if (++skipped % CancellationStride == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            List<ImportManifestColumn> columns = [];
            if (more)
            {
                Dictionary<string, int> at = new(StringComparer.Ordinal);
                foreach (SheetCell cell in cells)
                {
                    _ = at.TryAdd(CellCodec.AsText(cell.Value).Trim(), cell.Column);
                }

                if (RequiredColumns.All(at.ContainsKey))
                {
                    // Each kept row is charged its fields' text, so the table's memory stays bounded whatever its rows hold.
                    HashSet<(string Sheet, string Header)> seen = new(SheetHeaderComparer.Instance);
                    long charged = 0;
                    int read = 0;
                    while (sheet.Read(cells, out _) && read++ < MaxColumnRows)
                    {
                        if (read % CancellationStride == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (Column(cells, at) is not ImportManifestColumn column || seen.Contains((column.Sheet, column.Header)))
                        {
                            continue;
                        }

                        charged += Charge(column);
                        if (charged > MaxColumnTableBytes)
                        {
                            break;
                        }

                        _ = seen.Add((column.Sheet, column.Header));
                        columns.Add(column);
                    }
                }
            }

            return new ImportManifest(version, header, columns);
        }

        /// <summary>A row of the column table, validated; null when the row is not one the mapper may use.</summary>
        /// <param name="cells">The row's cells.</param>
        /// <param name="at">The table's columns by header.</param>
        /// <returns>The row.</returns>
        private static ImportManifestColumn? Column(List<SheetCell> cells, Dictionary<string, int> at)
        {
            // A row holding a value longer than any field takes is not one an export wrote.
            if (cells.Any(static cell => CellCodec.AsText(cell.Value).Length > MaxFieldLength))
            {
                return null;
            }

            string? sheet = Field(cells, at, "Sheet");
            string? letter = Field(cells, at, "Column");
            string? header = Field(cells, at, "Header", trim: false);
            string? path = Field(cells, at, "Path");
            string? language = Field(cells, at, "Language");
            return sheet is null || letter is null || header is null || string.IsNullOrWhiteSpace(header) || path is null
                || !ColumnLetters.TryParse(letter, out int column)
                || (language is not null && !IsLanguageCode(language))
                ? null
                : new ImportManifestColumn(
                sheet,
                Field(cells, at, "Collection") ?? string.Empty,
                column,
                header,
                path,
                language,
                Field(cells, at, "Role"),
                Field(cells, at, "Type"),
                Field(cells, at, "ReferenceEntity"),
                Field(cells, at, "KeyKind"),
                Field(cells, at, "DateEncoding"),
                Field(cells, at, "DatePattern"));
        }

        /// <summary>The bytes a kept column table row is charged: its fields' UTF-16 text plus a fixed overhead each.</summary>
        /// <param name="column">The row.</param>
        /// <returns>The charge.</returns>
        private static long Charge(ImportManifestColumn column)
        {
            const int FieldOverhead = 16;
            string?[] fields =
            [
                column.Sheet, column.Collection, column.Header, column.Path, column.Language, column.Role,
                column.Type, column.ReferenceEntity, column.KeyKind, column.DateEncoding, column.DatePattern,
            ];
            return fields.Sum(static field => ((long)(field?.Length ?? 0) * sizeof(char)) + FieldOverhead);
        }

        /// <summary>A field of a column table row.</summary>
        /// <param name="cells">The row's cells.</param>
        /// <param name="at">The table's columns by header.</param>
        /// <param name="name">The field's header.</param>
        /// <param name="trim">Whether the text is trimmed; a header is kept as written.</param>
        /// <returns>The text; null when the table lacks the field or the cell is blank.</returns>
        private static string? Field(List<SheetCell> cells, Dictionary<string, int> at, string name, bool trim = true)
        {
            if (!at.TryGetValue(name, out int column) || Text(cells, column) is not string text)
            {
                return null;
            }

            string value = trim ? text.Trim() : text;
            return value.Length == 0 ? null : value;
        }

        /// <summary>A cell's text in a row.</summary>
        /// <param name="cells">The row's cells, in column order.</param>
        /// <param name="column">The column.</param>
        /// <returns>The text; null when the row has no such cell.</returns>
        private static string? Text(List<SheetCell> cells, int column)
        {
            foreach (SheetCell cell in cells)
            {
                if (cell.Column == column)
                {
                    return CellCodec.AsText(cell.Value);
                }
            }

            return null;
        }

        /// <summary>A BCP 47 language tag, as the manifest records a column's language.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex LanguageCode();

        /// <summary>Compares a sheet and a header: the sheet ignoring case, as sheet names are; the header exactly.</summary>
        private sealed class SheetHeaderComparer : IEqualityComparer<(string Sheet, string Header)>
        {
            /// <summary>The comparer.</summary>
            public static readonly SheetHeaderComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((string Sheet, string Header) x, (string Sheet, string Header) y)
            {
                return string.Equals(x.Sheet, y.Sheet, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Header, y.Header, StringComparison.Ordinal);
            }

            /// <inheritdoc />
            public int GetHashCode((string Sheet, string Header) obj)
            {
                return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sheet), StringComparer.Ordinal.GetHashCode(obj.Header));
            }
        }
    }

    /// <summary>One row of a manifest's column table, validated as text; its path is checked against the entity by the mapper.</summary>
    /// <param name="Sheet">The sheet the column is on.</param>
    /// <param name="Collection">The sheet's collection path; empty for the parent sheet.</param>
    /// <param name="Column">The column's zero-based index when the file was written.</param>
    /// <param name="Header">The column's header as written.</param>
    /// <param name="Path">The column's path, language-relative for a multilingual column.</param>
    /// <param name="Language">The column's language; null for a column of none.</param>
    /// <param name="Role">The column's role as recorded; null when blank.</param>
    /// <param name="Type">The column's type as recorded; null when blank.</param>
    /// <param name="ReferenceEntity">The entity a reference column names; null when blank.</param>
    /// <param name="KeyKind">Whether a reference column is in a natural or the surrogate key; null when blank.</param>
    /// <param name="DateEncoding">How a date column was written; null when blank.</param>
    /// <param name="DatePattern">The date pattern a text-encoded date was written in; null when blank.</param>
    internal sealed record ImportManifestColumn(
        string Sheet,
        string Collection,
        int Column,
        string Header,
        string Path,
        string? Language,
        string? Role,
        string? Type,
        string? ReferenceEntity,
        string? KeyKind,
        string? DateEncoding,
        string? DatePattern);
}
