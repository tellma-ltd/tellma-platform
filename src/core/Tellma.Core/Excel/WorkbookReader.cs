// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace Tellma.Core.Excel
{
    // Spec 0019 §7.2-§7.4: an uploaded workbook, read within its package limits. The checks run before
    // any sheet is parsed, in the spec's order: the compressed size, the directory's bounds and the
    // uncompressed sum from the central directory, then the structure (one workbook part, the content
    // types, no link to another workbook and no relationship of the package or the workbook leaving
    // the package). A sheet's own relationships, its hyperlinks among them, are never read or followed,
    // so they are not checked. The shared strings load at open, under their budget and cap.
    /// <summary>
    ///     An uploaded <c>.xlsx</c> or <c>.xlsm</c> package, opened for reading: its sheets in tab
    ///     order, its date system and its shared strings, each sheet then read forward-only. Every
    ///     size, structure and XML fault is an import fault refusing the workbook whole.
    /// </summary>
    internal sealed class WorkbookReader : IDisposable
    {
        /// <summary>The most sheets a workbook may list.</summary>
        public const int MaxSheets = 4096;

        /// <summary>The content types part.</summary>
        private const string ContentTypesPart = "[Content_Types].xml";

        /// <summary>The package's relationships part.</summary>
        private const string PackageRelationshipsPart = "_rels/.rels";

        /// <summary>The archive, over the caller's stream.</summary>
        private readonly ZipArchive _zip;

        /// <summary>The package's parts by name, compared ignoring case as part names are.</summary>
        private readonly Dictionary<string, ZipArchiveEntry> _parts;

        /// <summary>The bounds the workbook is read within, whose text budgets each sheet's reader keeps.</summary>
        private readonly WorkbookLimits _limits;

        /// <summary>Creates the reader over a checked archive.</summary>
        /// <param name="zip">The archive.</param>
        /// <param name="parts">The parts by name.</param>
        /// <param name="limits">The bounds.</param>
        /// <param name="compressed">The package's size.</param>
        /// <param name="uncompressed">The sum of its parts' lengths.</param>
        private WorkbookReader(ZipArchive zip, Dictionary<string, ZipArchiveEntry> parts, WorkbookLimits limits, long compressed, long uncompressed)
        {
            _zip = zip;
            _parts = parts;
            _limits = limits;
            CompressedBytes = compressed;
            UncompressedBytes = uncompressed;
        }

        /// <summary>The package's size, in bytes.</summary>
        public long CompressedBytes { get; }

        /// <summary>The sum of the package's part lengths as its directory declares them, in bytes.</summary>
        public long UncompressedBytes { get; }

        /// <summary>Whether the workbook counts dates from 1904 rather than 1900.</summary>
        public bool Date1904 { get; private set; }

        /// <summary>The sheets, in tab order.</summary>
        public IReadOnlyList<WorkbookSheet> Sheets { get; private set; } = [];

        /// <summary>The shared strings, empty for a workbook without a table.</summary>
        public SharedStringStore SharedStrings { get; private set; } = SharedStringStore.Empty();

        /// <summary>Opens a package: checks its limits and structure, reads its workbook part and loads its shared strings.</summary>
        /// <param name="package">The package: readable and seekable, positioned anywhere; left open.</param>
        /// <param name="limits">The bounds.</param>
        /// <param name="scratchDirectory">The directory shared strings spill to.</param>
        /// <param name="cancellationToken">Stops the shared strings' load between entries.</param>
        /// <returns>The reader.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The package is above a limit, or is not a readable workbook.</exception>
        /// <exception cref="OperationCanceledException">The open was cancelled.</exception>
        public static WorkbookReader Open(Stream package, WorkbookLimits limits, string scratchDirectory, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
            if (!package.CanRead || !package.CanSeek)
            {
                throw new ArgumentException("The package must be readable and seekable.", nameof(package));
            }

            // --- The compressed size, then the directory's bounds, before the archive is opened ---
            long compressed = package.Length;
            if (compressed > limits.MaxCompressedBytes)
            {
                throw ImportFaults.TooLarge(compressed, limits.MaxCompressedBytes);
            }

            ZipDirectory.Check(package);
            ZipArchive zip;
            try
            {
                package.Position = 0;
                zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                throw ImportFaults.Malformed(ImportFaults.ZipReason);
            }

            try
            {
                // --- The parts, each once, and the sum of their declared lengths ---
                Dictionary<string, ZipArchiveEntry> parts = new(StringComparer.OrdinalIgnoreCase);
                long uncompressed = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (name.Length == 0 || name.EndsWith('/'))
                    {
                        continue;
                    }

                    if (!parts.TryAdd(name, entry))
                    {
                        throw ImportFaults.Malformed(ImportFaults.ZipReason);
                    }

                    uncompressed += entry.Length;
                    if (uncompressed > limits.MaxUncompressedBytes)
                    {
                        // The sum is reported whole, so the refusal says how far above the limit it is.
                        long total = zip.Entries.Sum(static candidate => candidate.Length);
                        throw ImportFaults.TooLarge(total, limits.MaxUncompressedBytes);
                    }
                }

                WorkbookReader reader = new(zip, parts, limits, compressed, uncompressed);
                reader.ReadStructure(scratchDirectory, cancellationToken);
                return reader;
            }
            catch (XmlException)
            {
                zip.Dispose();
                throw ImportFaults.Malformed(ImportFaults.XmlReason);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                zip.Dispose();
                throw ImportFaults.Malformed(ImportFaults.ZipReason);
            }
            catch
            {
                zip.Dispose();
                throw;
            }
        }

        /// <summary>Opens a sheet for reading forward-only.</summary>
        /// <param name="sheet">One of <see cref="Sheets" /> with a worksheet part.</param>
        /// <returns>The sheet's reader, positioned before its first row.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The sheet's part is not well-formed.</exception>
        public SheetReader OpenSheet(WorkbookSheet sheet)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            string part = sheet.Part ?? throw new ArgumentException($"The sheet '{sheet.Name}' holds no cells.", nameof(sheet));
            return SheetReader.Open(OpenPart(part), sheet.Name, SharedStrings, _limits.RowTextBudget, _limits.SheetTextBudget);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            SharedStrings.Dispose();
            _zip.Dispose();
        }

        /// <summary>Opens a part, bounded by its declared length and the token bound.</summary>
        /// <param name="name">The part's name.</param>
        /// <returns>The part's stream.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The archive cannot open the part.</exception>
        private BoundedPartStream OpenPart(string name)
        {
            ZipArchiveEntry entry = _parts[name];
            try
            {
                return new BoundedPartStream(entry.Open(), entry.Length);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                throw ImportFaults.Malformed(ImportFaults.ZipReason);
            }
        }

        /// <summary>Checks the package's structure, reads the workbook part and loads the shared strings.</summary>
        /// <param name="scratchDirectory">The directory shared strings spill to.</param>
        /// <param name="cancellationToken">Stops the shared strings' load between entries.</param>
        private void ReadStructure(string scratchDirectory, CancellationToken cancellationToken)
        {
            // --- The content types: present, one workbook part, no link to another workbook ---
            if (!_parts.ContainsKey(ContentTypesPart))
            {
                throw ImportFaults.Malformed(ImportFaults.ContentTypesReason);
            }

            (Dictionary<string, string> defaults, Dictionary<string, string> overrides) = ReadContentTypes();
            if (overrides.Values.Any(static type => string.Equals(type, XlsxXml.ExternalLinkContentType, StringComparison.OrdinalIgnoreCase)))
            {
                throw ImportFaults.Malformed(ImportFaults.ExternalLinkReason);
            }

            if (overrides.Values.Count(XlsxXml.WorkbookContentTypes.Contains) > 1)
            {
                throw ImportFaults.Malformed(ImportFaults.WorkbookReason);
            }

            // --- The package's relationships: one main document, nothing outside the package ---
            if (!_parts.ContainsKey(PackageRelationshipsPart))
            {
                throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
            }

            List<Relationship> package = ReadRelationships(PackageRelationshipsPart, string.Empty);
            List<Relationship> documents = [.. package.Where(static relationship => relationship.Kind == XlsxXml.OfficeDocument)];
            if (documents.Count != 1 || documents[0].Target is not string workbook || !_parts.ContainsKey(workbook))
            {
                throw ImportFaults.Malformed(ImportFaults.WorkbookReason);
            }

            string? contentType = overrides.GetValueOrDefault(workbook)
                ?? defaults.GetValueOrDefault(Path.GetExtension(workbook).TrimStart('.'));
            if (contentType is null || !XlsxXml.WorkbookContentTypes.Contains(contentType))
            {
                throw ImportFaults.Malformed(ImportFaults.WorkbookReason);
            }

            // --- The workbook's relationships: no external target, no link to another workbook ---
            string relationshipsPart = XlsxXml.RelationshipsOf(workbook);
            if (!_parts.ContainsKey(relationshipsPart))
            {
                throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
            }

            Dictionary<string, Relationship> related = new(StringComparer.Ordinal);
            foreach (Relationship relationship in ReadRelationships(relationshipsPart, workbook))
            {
                if (relationship.Kind == XlsxXml.ExternalLink)
                {
                    throw ImportFaults.Malformed(ImportFaults.ExternalLinkReason);
                }

                if (!related.TryAdd(relationship.Id, relationship))
                {
                    throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
                }
            }

            // --- The workbook part: its date system and its sheets ---
            ReadWorkbook(workbook, related);

            // --- The shared strings, at most one table, loaded whole under the budget and the cap ---
            List<Relationship> tables = [.. related.Values.Where(static relationship => relationship.Kind == XlsxXml.SharedStrings)];
            if (tables.Count > 1)
            {
                throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
            }

            if (tables.Count == 1)
            {
                string table = tables[0].Target is string target && _parts.ContainsKey(target)
                    ? target
                    : throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
                SharedStrings = SharedStringStore.Load(OpenPart(table), _limits.SharedStringBudget, _limits.MaxSharedStringEntries, scratchDirectory, cancellationToken);
            }
        }

        /// <summary>Reads the content types part.</summary>
        /// <returns>The content type of each extension, and of each part named by an override.</returns>
        private (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides) ReadContentTypes()
        {
            Dictionary<string, string> defaults = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);
            using XmlReader reader = XlsxXml.Open(OpenPart(ContentTypesPart));
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != XlsxXml.ContentTypesNamespace)
                {
                    continue;
                }

                string? type = reader.GetAttribute("ContentType");
                if (reader.LocalName == "Default" && reader.GetAttribute("Extension") is string extension && type is not null)
                {
                    defaults[extension] = type;
                }
                else if (reader.LocalName == "Override" && reader.GetAttribute("PartName") is string name && type is not null)
                {
                    overrides[name.TrimStart('/')] = type;
                }
            }

            return (defaults, overrides);
        }

        /// <summary>Reads a relationships part, refusing any relationship that leaves the package.</summary>
        /// <param name="part">The relationships part's name.</param>
        /// <param name="source">The part the relationships belong to; empty for the package.</param>
        /// <returns>The relationships, in order.</returns>
        private List<Relationship> ReadRelationships(string part, string source)
        {
            List<Relationship> relationships = [];
            using XmlReader reader = XlsxXml.Open(OpenPart(part));
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship" || reader.NamespaceURI != XlsxXml.RelationshipsNamespace)
                {
                    continue;
                }

                if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                {
                    throw ImportFaults.Malformed(ImportFaults.ExternalRelationshipReason);
                }

                string id = reader.GetAttribute("Id") ?? throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
                string? target = reader.GetAttribute("Target") is string text ? XlsxXml.ResolveTarget(source, text) : null;
                relationships.Add(new Relationship(id, XlsxXml.RelationshipKind(reader.GetAttribute("Type")), target));
                if (relationships.Count > ZipDirectory.MaxEntries)
                {
                    throw ImportFaults.Malformed(ImportFaults.RelationshipsReason);
                }
            }

            return relationships;
        }

        /// <summary>Reads the workbook part: the date system and the sheets in tab order.</summary>
        /// <param name="workbook">The workbook part's name.</param>
        /// <param name="related">The workbook's relationships, by id.</param>
        private void ReadWorkbook(string workbook, Dictionary<string, Relationship> related)
        {
            List<WorkbookSheet> sheets = [];
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            using XmlReader reader = XlsxXml.Open(OpenPart(workbook));
            while (reader.Read())
            {
                // --- The date system: 1904 when the workbook says so, in either spelling of true ---
                if (XlsxXml.IsSpreadsheet(reader, "workbookPr"))
                {
                    Date1904 = reader.GetAttribute("date1904") is "1" or "true";
                    continue;
                }

                if (!XlsxXml.IsSpreadsheet(reader, "sheet"))
                {
                    continue;
                }

                // --- A sheet: a unique name, its state, and the part its relationship names ---
                string name = reader.GetAttribute("name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || !names.Add(name) || sheets.Count == MaxSheets)
                {
                    throw ImportFaults.Malformed(ImportFaults.SheetsReason);
                }

                WorkbookSheetState state = reader.GetAttribute("state") switch
                {
                    "hidden" => WorkbookSheetState.Hidden,
                    "veryHidden" => WorkbookSheetState.VeryHidden,
                    _ => WorkbookSheetState.Visible,
                };

                if (XlsxXml.RelationshipId(reader) is not string id || !related.TryGetValue(id, out Relationship? relationship))
                {
                    throw ImportFaults.Malformed(ImportFaults.RelationshipsReason, name);
                }

                // Only a worksheet holds cells; a chart, dialog or macro sheet is listed and never read.
                string? part = null;
                if (relationship.Kind == XlsxXml.Worksheet)
                {
                    part = relationship.Target is string target && _parts.ContainsKey(target)
                        ? target
                        : throw ImportFaults.Malformed(ImportFaults.RelationshipsReason, name);
                }

                sheets.Add(new WorkbookSheet(sheets.Count, name, state, part));
            }

            if (sheets.Count == 0)
            {
                throw ImportFaults.Malformed(ImportFaults.SheetsReason);
            }

            Sheets = sheets;
        }

        /// <summary>One relationship of a part.</summary>
        /// <param name="Id">Its id, unique within the part.</param>
        /// <param name="Kind">Its type's last segment; null for a type of no known prefix.</param>
        /// <param name="Target">The part it targets; null when it targets none in the package.</param>
        private sealed record Relationship(string Id, string? Kind, string? Target);
    }

    /// <summary>
    ///     One worksheet, read forward-only: its declared dimension, then its rows in ascending order,
    ///     each with its stored cells in ascending column order. A row or cell without a reference
    ///     takes the next in sequence; one out of order, out of range, or naming another row is a
    ///     malformed sheet. The text a row decodes to is bounded, and so is the text the whole sheet
    ///     decodes to; a row may instead be skimmed, told blank or not without its text decoded.
    /// </summary>
    internal sealed class SheetReader : IDisposable
    {
        /// <summary>The characters a skim reads of a text node at a time.</summary>
        private const int ScanChunk = 1024;

        /// <summary>The part's reader, owned.</summary>
        private readonly XmlReader _reader;

        /// <summary>The sheet's name, which a fault blames.</summary>
        private readonly string _sheet;

        /// <summary>The workbook's shared strings.</summary>
        private readonly SharedStringStore _strings;

        /// <summary>The most UTF-16 bytes of text one row's cells may decode to.</summary>
        private readonly long _rowBudget;

        /// <summary>The most UTF-16 bytes of text the sheet's cells may decode to.</summary>
        private readonly long _sheetBudget;

        /// <summary>The buffer a skim scans text through; made on the first skim.</summary>
        private char[]? _scan;

        /// <summary>The UTF-16 bytes of text the sheet's cells decoded to so far.</summary>
        private long _sheetCharged;

        /// <summary>The depth of the rows: the children of the sheet data element.</summary>
        private int _rowDepth = -1;

        /// <summary>The last row read; 0 before the first.</summary>
        private int _lastRow;

        /// <summary>Whether the rows are exhausted.</summary>
        private bool _done;

        /// <summary>Creates the reader, positioned on the sheet data's first row.</summary>
        /// <param name="reader">The part's reader.</param>
        /// <param name="sheet">The sheet's name.</param>
        /// <param name="strings">The workbook's shared strings.</param>
        /// <param name="rowBudget">The most UTF-16 bytes of text one row's cells may decode to.</param>
        /// <param name="sheetBudget">The most UTF-16 bytes of text the sheet's cells may decode to.</param>
        private SheetReader(XmlReader reader, string sheet, SharedStringStore strings, long rowBudget, long sheetBudget)
        {
            _reader = reader;
            _sheet = sheet;
            _strings = strings;
            _rowBudget = rowBudget;
            _sheetBudget = sheetBudget;
        }

        /// <summary>The last row the sheet's dimension declares; null when the sheet declares none.</summary>
        public int? DimensionLastRow { get; private set; }

        /// <summary>Opens a sheet's part and reads up to its first row.</summary>
        /// <param name="part">The part's stream; owned.</param>
        /// <param name="sheet">The sheet's name.</param>
        /// <param name="strings">The workbook's shared strings.</param>
        /// <param name="rowBudget">The most UTF-16 bytes of text one row's cells may decode to.</param>
        /// <param name="sheetBudget">The most UTF-16 bytes of text the sheet's cells may decode to.</param>
        /// <returns>The reader.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The part is not well-formed.</exception>
        public static SheetReader Open(Stream part, string sheet, SharedStringStore strings, long rowBudget, long sheetBudget)
        {
            ArgumentNullException.ThrowIfNull(strings);
            ArgumentOutOfRangeException.ThrowIfNegative(rowBudget);
            ArgumentOutOfRangeException.ThrowIfNegative(sheetBudget);
            SheetReader reader = new(XlsxXml.Open(part), sheet, strings, rowBudget, sheetBudget);
            try
            {
                reader.Start();
                return reader;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        /// <summary>Reads the next row that holds a row element, empty or not.</summary>
        /// <param name="cells">Cleared, then given the row's stored cells that are not blank, in column order.</param>
        /// <param name="row">The row's one-based number.</param>
        /// <returns>False once the rows are exhausted.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">
        ///     The part is not well-formed, a row or cell is misaddressed, or the row's or the sheet's
        ///     text passes its budget.
        /// </exception>
        public bool Read(List<SheetCell> cells, out int row)
        {
            ArgumentNullException.ThrowIfNull(cells);
            return Next(cells, out row, out _);
        }

        /// <summary>
        ///     Skims the next row that holds a row element: whether a cell of it holds more than white
        ///     space, told without its text decoded. An inline or formula string of escaped white
        ///     space alone counts as filled.
        /// </summary>
        /// <param name="row">The row's one-based number.</param>
        /// <param name="filled">Whether a cell of the row holds more than white space.</param>
        /// <returns>False once the rows are exhausted.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The part is not well-formed, or a row or cell is misaddressed.</exception>
        public bool Skim(out int row, out bool filled)
        {
            _scan ??= new char[ScanChunk];
            return Next(cells: null, out row, out filled);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _reader.Dispose();
        }

        /// <summary>Reads or skims the next row.</summary>
        /// <param name="cells">Receives the row's cells when it is read; null when it is skimmed.</param>
        /// <param name="row">The row's one-based number.</param>
        /// <param name="filled">Whether a cell of the row holds more than white space.</param>
        /// <returns>False once the rows are exhausted.</returns>
        private bool Next(List<SheetCell>? cells, out int row, out bool filled)
        {
            cells?.Clear();
            row = 0;
            filled = false;
            try
            {
                // --- The next row element, at the rows' depth, until the sheet data ends ---
                while (!_done && _reader.Read())
                {
                    if (_reader.Depth < _rowDepth)
                    {
                        break;
                    }

                    if (_reader.Depth != _rowDepth || !XlsxXml.IsSpreadsheet(_reader, "row"))
                    {
                        continue;
                    }

                    row = Number(_reader.GetAttribute("r"), _lastRow);
                    _lastRow = row;
                    if (!_reader.IsEmptyElement)
                    {
                        filled = ReadCells(row, cells);
                    }

                    return true;
                }

                _done = true;
                return false;
            }
            catch (XmlException)
            {
                throw ImportFaults.Malformed(ImportFaults.XmlReason, _sheet);
            }
            catch (InvalidDataException)
            {
                throw ImportFaults.Malformed(ImportFaults.ZipReason, _sheet);
            }
        }

        /// <summary>Reads up to the sheet data, capturing the dimension on the way.</summary>
        private void Start()
        {
            try
            {
                while (_reader.Read())
                {
                    if (XlsxXml.IsSpreadsheet(_reader, "dimension"))
                    {
                        DimensionLastRow = LastRow(_reader.GetAttribute("ref"));
                    }
                    else if (XlsxXml.IsSpreadsheet(_reader, "sheetData"))
                    {
                        _done = _reader.IsEmptyElement;
                        _rowDepth = _reader.Depth + 1;
                        return;
                    }
                }

                _done = true;
            }
            catch (XmlException)
            {
                throw ImportFaults.Malformed(ImportFaults.XmlReason, _sheet);
            }
            catch (InvalidDataException)
            {
                throw ImportFaults.Malformed(ImportFaults.ZipReason, _sheet);
            }
        }

        /// <summary>Reads or skims a row's cells, the reader on the row's start and left on its end.</summary>
        /// <param name="row">The row's number.</param>
        /// <param name="cells">Receives the cells that are not blank; null to skim them.</param>
        /// <returns>Whether a cell of the row holds more than white space.</returns>
        private bool ReadCells(int row, List<SheetCell>? cells)
        {
            int depth = _reader.Depth;
            int lastColumn = -1;
            long rowCharged = 0;
            bool filled = false;
            while (_reader.Read() && !(_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth))
            {
                if (_reader.Depth != depth + 1 || !XlsxXml.IsSpreadsheet(_reader, "c"))
                {
                    continue;
                }

                // --- The cell's column: its reference's, else the next; ascending and in this row ---
                int column;
                if (_reader.GetAttribute("r") is string reference)
                {
                    if (!ColumnLetters.TryParseReference(reference, out column, out int referenced) || referenced != row)
                    {
                        throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet);
                    }
                }
                else
                {
                    column = lastColumn + 1;
                }

                if (column <= lastColumn || column >= ColumnLetters.MaxColumns)
                {
                    throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet);
                }

                lastColumn = column;

                // --- A skim tells the cell filled or not and keeps nothing ---
                if (cells is null)
                {
                    filled |= SkimCell();
                    continue;
                }

                ExcelRawCell value = ReadCell();
                if (value.Kind == ExcelCellKind.Blank)
                {
                    continue;
                }

                // --- The cell's text, charged to the row's and the sheet's budgets before it is kept ---
                long charge = (long)value.Text.Length * sizeof(char);
                rowCharged += charge;
                _sheetCharged += charge;
                if (rowCharged > _rowBudget || _sheetCharged > _sheetBudget)
                {
                    throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet);
                }

                cells.Add(new SheetCell(column, value));
                filled = filled || !string.IsNullOrWhiteSpace(value.Text);
            }

            return filled;
        }

        /// <summary>Reads a cell, the reader on its start and left on its end: its cached value or its inline string, the formula skipped.</summary>
        /// <returns>The cell.</returns>
        private ExcelRawCell ReadCell()
        {
            string? type = _reader.GetAttribute("t");
            string? value = null;
            string? inline = null;
            if (!_reader.IsEmptyElement)
            {
                int depth = _reader.Depth;
                bool more = _reader.Read();
                while (more && !(_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth))
                {
                    if (XlsxXml.IsSpreadsheet(_reader, "v"))
                    {
                        value = XlsxXml.ReadText(_reader, _sheet);
                    }
                    else if (XlsxXml.IsSpreadsheet(_reader, "is"))
                    {
                        inline = XlsxXml.ReadStringItem(_reader, _sheet);
                    }
                    else if (_reader.NodeType == XmlNodeType.Element)
                    {
                        _reader.Skip();
                        more = !_reader.EOF;
                        continue;
                    }

                    more = _reader.Read();
                }
            }

            try
            {
                return CellReader.Read(type, value, inline, _strings.Get);
            }
            catch (FormatException)
            {
                // A shared string index the table lacks.
                throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet);
            }
        }

        /// <summary>
        ///     Skims a cell, the reader on its start and left on its end: whether its value holds more
        ///     than white space, by the rules a read decodes it with. Text is scanned in chunks and
        ///     never kept, and a shared string is told by its bit.
        /// </summary>
        /// <returns>Whether the cell holds more than white space.</returns>
        private bool SkimCell()
        {
            string? type = _reader.GetAttribute("t");
            if (_reader.IsEmptyElement)
            {
                return false;
            }

            int depth = _reader.Depth;
            bool filled = false;
            while (_reader.Read() && !(_reader.NodeType == XmlNodeType.EndElement && _reader.Depth == depth))
            {
                if (_reader.Depth != depth + 1)
                {
                    continue;
                }

                // --- An inline string's own element; any other kind's cached value, a shared string's an index ---
                if (type == CellReader.InlineStringType)
                {
                    filled |= XlsxXml.IsSpreadsheet(_reader, "is") && XlsxXml.HasStringItemText(_reader, _scan!);
                }
                else if (XlsxXml.IsSpreadsheet(_reader, "v"))
                {
                    filled |= type == CellReader.SharedStringType ? SharedStringFilled() : XlsxXml.HasText(_reader, _scan!);
                }
            }

            return filled;
        }

        /// <summary>Whether the shared string a cell's value names holds more than white space, the reader on the value's start and left on its end.</summary>
        /// <returns>True when it does.</returns>
        private bool SharedStringFilled()
        {
            // An index is a handful of digits; one the table lacks is a malformed cell, as a read refuses it.
            string text = XlsxXml.ReadText(_reader, _sheet);
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && _strings.IsFilled(index) is bool filled
                ? filled
                : throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet);
        }

        /// <summary>A row's number: its reference's, else the next; ascending and within the sheet.</summary>
        /// <param name="reference">The row's <c>r</c> attribute; null when it has none.</param>
        /// <param name="last">The last row read.</param>
        /// <returns>The number.</returns>
        private int Number(string? reference, int last)
        {
            int number = last + 1;
            bool valid = reference is null || int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out number);
            return !valid || number <= last || number > ColumnLetters.MaxRows
                ? throw ImportFaults.Malformed(ImportFaults.CellsReason, _sheet)
                : number;
        }

        /// <summary>The last row of a dimension's range: <c>200</c> for <c>A1:H200</c>, <c>1</c> for <c>A1</c>.</summary>
        /// <param name="reference">The range; null when absent.</param>
        /// <returns>The row; null for a range that does not parse.</returns>
        private static int? LastRow(string? reference)
        {
            if (reference is null)
            {
                return null;
            }

            int colon = reference.IndexOf(':', StringComparison.Ordinal);
            ReadOnlySpan<char> last = colon < 0 ? reference : reference.AsSpan(colon + 1);
            return ColumnLetters.TryParseReference(last, out _, out int row) ? row : null;
        }
    }
}
