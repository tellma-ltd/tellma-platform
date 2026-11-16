// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Streams workbooks: an entity export from its plan and row source, or a report's table from
    ///     its rows. The parent sheet streams into the package as pages arrive, each child sheet spools
    ///     to a scratch file of its own that is copied in after the parent sheet, and every string is
    ///     inline, so memory holds one page and its dependents whatever the row count.
    /// </summary>
    /// <param name="options">The Excel limits: the row cap of a child sheet.</param>
    /// <param name="scratchDirectory">The directory child sheets spool to; it must exist and be writable.</param>
    internal sealed class WorkbookWriter(ExcelOptions options, string scratchDirectory)
    {
        /// <summary>The width of the manifest sheet's columns.</summary>
        private const double ManifestColumnWidth = 20;

        /// <summary>The Excel limits.</summary>
        private readonly ExcelOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        /// <summary>The directory child sheets spool to.</summary>
        private readonly string _scratchDirectory = !string.IsNullOrWhiteSpace(scratchDirectory)
            ? scratchDirectory
            : throw new ArgumentException("The scratch directory is required.", nameof(scratchDirectory));

        /// <summary>
        ///     Writes an entity export: its data sheets in plan order and the hidden manifest. The
        ///     parent sheet's query is pulled page by page; each child sheet's rows ride its pages as
        ///     the dependents the child sheet's query stands for.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The row source; each row lists one value per column of its sheet.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="destination">The writable stream the package is written to.</param>
        /// <param name="cancellationToken">Stops the write between pages.</param>
        /// <returns>The data rows written to the parent sheet.</returns>
        /// <exception cref="ArgumentException">A child sheet's query is not among the dependents of the parent sheet's query, or two sheets share a query.</exception>
        /// <exception cref="LimitExceededException">A sheet's rows exceed its cap.</exception>
        /// <exception cref="InvalidOperationException">The row source yields a row or page that does not fit its query.</exception>
        public async Task<int> WriteAsync(
            ExcelWorkbookPlan plan,
            IExcelRowSource rows,
            ExcelContext context,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(destination);

            // --- Names: the manifest's is reserved first, so no data sheet can take it ---
            SheetNames names = new();
            _ = names.Take(SheetNames.Manifest);
            StylesheetBuilder styles = new();
            uint headerStyle = styles.Register(new ExcelCellStyle(NumberFormatBuilder.General, CellFont.Bold));
            bool editable = plan.Shape == ExportKind.ForImport;
            bool rightToLeft = context.Culture.TextInfo.IsRightToLeft;

            List<DataSheet> sheets = [];
            for (int index = 0; index < plan.Sheets.Count; index++)
            {
                ExcelSheetPlan sheetPlan = plan.Sheets[index];
                sheets.Add(DataSheet.For(sheetPlan, names.Take(sheetPlan.Name), index + 1, plan.Shape, context, styles, headerStyle, rightToLeft, editable));
            }

            // --- Each child sheet stands for one dependent of the parent sheet's query, at any depth ---
            ExcelQuery root = plan.Sheets[0].Query;
            Dictionary<ExcelQuery, DataSheet> children = MapChildSheets(root, sheets);

            // --- Caps: the parent sheet's from its query, every child sheet's from the options ---
            int rootCap = Math.Min(root.CountCap ?? (root.Take is int take ? take - 1 : _options.MaxExportRows), ExcelOptions.MaxWorksheetDataRows);
            sheets[0].Cap = Math.Max(rootCap, 0);
            int childCap = Math.Min(_options.MaxExportRows, ExcelOptions.MaxWorksheetDataRows);
            foreach (DataSheet child in sheets.Skip(1))
            {
                child.Cap = childCap;
            }

            using XlsxPackage package = new(destination, context.Now);
            try
            {
                package.WriteStructure(sheets.Count + 1);

                // --- Child sheets spool to scratch files, headers first ---
                foreach (DataSheet child in sheets.Skip(1))
                {
                    child.Spool = ScratchFiles.Create(_scratchDirectory);
                    child.Writer = new WorksheetWriter(child.Spool, child.Layout);
                    child.WriteHeader();
                }

                // --- The parent sheet streams into its part as the pages arrive ---
                DataSheet parent = sheets[0];
                parent.Part = package.CreatePart(XlsxPackage.WorksheetPath(parent.Number));
                parent.Writer = new WorksheetWriter(parent.Part, parent.Layout);
                parent.WriteHeader();

                bool first = true;
                await foreach (ExcelPage page in rows.Pages(root, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // A by-query export's first page carries the capped count, which decides before any row is written.
                    if (first && page.Count is int count && count > parent.Cap)
                    {
                        throw new LimitExceededException(ExcelErrorCodes.ExportRowLimitExceeded, count, parent.Cap);
                    }

                    first = false;
                    foreach (IReadOnlyList<object?> row in page.Rows)
                    {
                        parent.WriteRow(row);
                    }

                    WriteDependents(root, page, children);
                }

                parent.Finish();

                // --- Child sheets are copied in after the parent sheet, each in plan order ---
                foreach (DataSheet child in sheets.Skip(1))
                {
                    child.Finish();
                    using Stream part = package.CreatePart(XlsxPackage.WorksheetPath(child.Number));
                    child.Spool!.Position = 0;
                    child.Spool.CopyTo(part);
                    child.Release();
                }

                // --- The manifest, the styles and the workbook close the package ---
                WriteManifest(package, plan, sheets, sheets.Count + 1);
                WriteStyles(package, styles);
                WriteWorkbook(package, sheets, manifest: true);
                return parent.Rows;
            }
            finally
            {
                // A failed write releases every spool, whose files the system then deletes.
                foreach (DataSheet sheet in sheets)
                {
                    sheet.Release();
                }
            }
        }

        /// <summary>
        ///     Writes a report's one-sheet table: its bold header rows, then its rows forward-only, with
        ///     the pane frozen below the headers and after the frozen columns. No manifest is written.
        /// </summary>
        /// <param name="spec">The table.</param>
        /// <param name="rows">The data rows, in order.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="destination">The writable stream the package is written to.</param>
        /// <param name="cancellationToken">Stops the write between rows.</param>
        /// <returns>The data rows written.</returns>
        /// <exception cref="ArgumentException">A row's cells are not parallel to the table's columns.</exception>
        /// <exception cref="LimitExceededException">The rows do not fit one worksheet below its header rows.</exception>
        public static async Task<int> WriteTableAsync(
            ExcelTableSpec spec,
            IAsyncEnumerable<ExcelTableRow> rows,
            ExcelContext context,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(destination);

            // --- Encodings, column styles and the layout ---
            StylesheetBuilder styles = new();
            uint headerStyle = styles.Register(new ExcelCellStyle(NumberFormatBuilder.General, CellFont.Bold));
            bool rightToLeft = context.Culture.TextInfo.IsRightToLeft;
            int columnCount = spec.Columns.Count;
            var encodings = new ColumnEncoding[columnCount];
            var columns = new SheetColumn[columnCount];
            for (int index = 0; index < columnCount; index++)
            {
                int headerLength = spec.HeaderRows.Max(row => index < row.Count ? row[index]?.Length ?? 0 : 0);
                encodings[index] = ColumnEncoding.For(spec.Columns[index], ExportKind.Report, context, spec.NumberFormats[index], headerLength);
                uint style = styles.Register(new ExcelCellStyle(encodings[index].NumberFormat));
                columns[index] = new SheetColumn(encodings[index].Width, Hidden: false, style);
            }

            int headerRows = spec.HeaderRows.Count;
            SheetLayout layout = new(columns, headerRows, spec.FrozenColumns, rightToLeft, Selected: true, Protected: false, FilterRow: headerRows, Validations: []);
            int cap = ColumnLetters.MaxRows - headerRows;
            string name = new SheetNames().Take(spec.SheetName);

            using XlsxPackage package = new(destination, context.Now);
            package.WriteStructure(1);

            int written = 0;
            int lastRow;
            using (Stream part = package.CreatePart(XlsxPackage.WorksheetPath(1)))
            using (WorksheetWriter writer = new(part, layout))
            {
                // --- The header rows, bold, every cell its own ---
                foreach (IReadOnlyList<string?> header in spec.HeaderRows)
                {
                    writer.StartRow();
                    for (int index = 0; index < header.Count; index++)
                    {
                        if (header[index] is string text)
                        {
                            writer.WriteCell(index, EncodedCell.OfText(text), headerStyle);
                        }
                    }

                    writer.EndRow();
                }

                // --- The data rows, each cell styled by its column's format and the row's style ---
                await foreach (ExcelTableRow row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Cells.Count != columnCount)
                    {
                        throw new ArgumentException("A table row's cells must be parallel to the table's columns.", nameof(rows));
                    }

                    if (written == cap)
                    {
                        throw new LimitExceededException(ExcelErrorCodes.ExportRowLimitExceeded, written + 1, cap);
                    }

                    writer.StartRow();
                    for (int index = 0; index < columnCount; index++)
                    {
                        uint style = styles.Register(RowStyle(encodings[index].NumberFormat, row.Style, index == 0 ? row.Indent : 0, rightToLeft));

                        // A styled row bands every cell, blank or not.
                        writer.WriteCell(index, CellCodec.Encode(row.Cells[index], encodings[index]), style, styleBlank: row.Style != ExcelRowStyle.Normal);
                    }

                    writer.EndRow();
                    written++;
                }

                writer.Finish();
                lastRow = writer.RowNumber;
            }

            // --- The styles and the workbook follow the closed sheet part ---
            WriteStyles(package, styles);
            WriteWorkbook(package, [new TableSheet(name, columnCount, headerRows, lastRow)], manifest: false);
            return written;
        }

        /// <summary>The style of a table cell: its column's number format with the row style's font, fill and border.</summary>
        /// <param name="numberFormat">The column's number format.</param>
        /// <param name="rowStyle">The row's style.</param>
        /// <param name="indent">The cell's indent.</param>
        /// <param name="rightToLeft">Whether the sheet reads right to left.</param>
        /// <returns>The style.</returns>
        internal static ExcelCellStyle RowStyle(string numberFormat, ExcelRowStyle rowStyle, int indent, bool rightToLeft)
        {
            return rowStyle switch
            {
                ExcelRowStyle.Phantom => new ExcelCellStyle(numberFormat, CellFont.Italic, Indent: indent, RightToLeft: rightToLeft),
                ExcelRowStyle.Subtotal1 => new ExcelCellStyle(numberFormat, CellFont.Bold, CellFill.Lightest, Indent: indent, RightToLeft: rightToLeft),
                ExcelRowStyle.Subtotal2 => new ExcelCellStyle(numberFormat, CellFont.Bold, Indent: indent, RightToLeft: rightToLeft),
                ExcelRowStyle.Total => new ExcelCellStyle(numberFormat, CellFont.Bold, Border: CellBorder.DoubleTop, Indent: indent, RightToLeft: rightToLeft),
                ExcelRowStyle.Normal or ExcelRowStyle.Subtotal3 => new ExcelCellStyle(numberFormat, Indent: indent, RightToLeft: rightToLeft),
                _ => new ExcelCellStyle(numberFormat, Indent: indent, RightToLeft: rightToLeft),
            };
        }

        /// <summary>
        ///     Maps each child sheet to its query among the dependents of the root query, at any depth,
        ///     by reference: the row source's pages are parallel to the queries, not to the sheets.
        /// </summary>
        /// <param name="root">The parent sheet's query.</param>
        /// <param name="sheets">The data sheets, the parent first.</param>
        /// <returns>Each child sheet by its query.</returns>
        private static Dictionary<ExcelQuery, DataSheet> MapChildSheets(ExcelQuery root, List<DataSheet> sheets)
        {
            // Every query the root's pages carry, at every depth.
            HashSet<ExcelQuery> reachable = new(ReferenceEqualityComparer.Instance);
            Stack<ExcelQuery> pending = new([root]);
            while (pending.TryPop(out ExcelQuery? query))
            {
                foreach (ExcelQuery dependent in query.Dependents ?? [])
                {
                    if (reachable.Add(dependent))
                    {
                        pending.Push(dependent);
                    }
                }
            }

            Dictionary<ExcelQuery, DataSheet> children = new(ReferenceEqualityComparer.Instance);
            foreach (DataSheet child in sheets.Skip(1))
            {
                if (!reachable.Contains(child.Plan.Query) || !children.TryAdd(child.Plan.Query, child))
                {
                    throw new ArgumentException("Each child sheet's query must be its own dependent of the parent sheet's query.", nameof(sheets));
                }
            }

            return children;
        }

        /// <summary>Writes a page's dependent rows into the child sheets they stand for, at every depth.</summary>
        /// <param name="query">The query the page belongs to.</param>
        /// <param name="page">The page.</param>
        /// <param name="children">Each child sheet by its query.</param>
        /// <exception cref="InvalidOperationException">The page's dependents are not parallel to the query's.</exception>
        private static void WriteDependents(ExcelQuery query, ExcelPage page, Dictionary<ExcelQuery, DataSheet> children)
        {
            IReadOnlyList<ExcelQuery> dependents = query.Dependents ?? [];
            if (page.Dependents.Count != dependents.Count)
            {
                throw new InvalidOperationException("A page's dependents must be parallel to its query's.");
            }

            for (int index = 0; index < dependents.Count; index++)
            {
                ExcelPage dependentPage = page.Dependents[index];
                if (children.TryGetValue(dependents[index], out DataSheet? sheet))
                {
                    foreach (IReadOnlyList<object?> row in dependentPage.Rows)
                    {
                        sheet.WriteRow(row);
                    }
                }

                WriteDependents(dependents[index], dependentPage, children);
            }
        }

        /// <summary>
        ///     Writes the hidden, protected manifest sheet: the plan's header block, a blank row, and
        ///     the column table naming every data column by the sheet name actually written.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <param name="plan">The plan.</param>
        /// <param name="sheets">The data sheets.</param>
        /// <param name="number">The manifest's worksheet number.</param>
        private static void WriteManifest(XlsxPackage package, ExcelWorkbookPlan plan, List<DataSheet> sheets, int number)
        {
            SheetColumn[] columns = [.. ExcelManifest.ColumnTableHeaders.Select(static _ => new SheetColumn(ManifestColumnWidth, Hidden: false, Style: 0))];
            SheetLayout layout = new(columns, 0, 0, RightToLeft: false, Selected: false, Protected: true, FilterRow: null, Validations: []);

            using Stream part = package.CreatePart(XlsxPackage.WorksheetPath(number));
            using WorksheetWriter writer = new(part, layout);

            // --- The header block: key in A, value in B ---
            foreach ((string key, string value) in plan.Manifest)
            {
                WriteTextRow(writer, [key, value]);
            }

            // --- One blank row, then the column table ---
            writer.StartRow();
            writer.EndRow();
            WriteTextRow(writer, ExcelManifest.ColumnTableHeaders);
            foreach (DataSheet sheet in sheets)
            {
                for (int index = 0; index < sheet.Encodings.Length; index++)
                {
                    ColumnEncoding encoding = sheet.Encodings[index];
                    ExcelColumnSpec column = encoding.Column;
                    WriteTextRow(writer,
                    [
                        sheet.Name,
                        sheet.Plan.Collection,
                        ColumnLetters.Of(index),
                        column.Header,
                        column.PathText,
                        ExcelManifest.TypeName(column.Type),
                        column.Role.ToString(),
                        column.Language,
                        column.ReferenceEntity,
                        column.Role == ExcelColumnRole.Reference ? column.KeyKind?.ToString() : null,
                        encoding.DateEncoding?.ToString(),
                        encoding.DatePattern,
                    ]);
                }
            }

            writer.Finish();
        }

        /// <summary>Writes a row of text cells, leaving blank the null ones.</summary>
        /// <param name="writer">The worksheet.</param>
        /// <param name="cells">The cells' text.</param>
        private static void WriteTextRow(WorksheetWriter writer, IReadOnlyList<string?> cells)
        {
            writer.StartRow();
            for (int index = 0; index < cells.Count; index++)
            {
                if (!string.IsNullOrEmpty(cells[index]))
                {
                    writer.WriteCell(index, EncodedCell.OfText(cells[index]!), style: 0);
                }
            }

            writer.EndRow();
        }

        /// <summary>Writes the styles part.</summary>
        /// <param name="package">The package.</param>
        /// <param name="styles">The styles registered.</param>
        private static void WriteStyles(XlsxPackage package, StylesheetBuilder styles)
        {
            using Stream part = package.CreatePart(XlsxPackage.StylesPath);
            using var writer = OpenXmlWriter.Create(part);
            writer.WriteStartDocument(true);
            writer.WriteElement(styles.Build());
            writer.Close();
        }

        /// <summary>
        ///     Writes the workbook part: the 1900 date system, the data sheets and the hidden manifest,
        ///     and each sheet's filter range as Excel names it.
        /// </summary>
        /// <param name="package">The package.</param>
        /// <param name="sheets">The data sheets, in order.</param>
        /// <param name="manifest">Whether the manifest sheet follows them.</param>
        private static void WriteWorkbook(XlsxPackage package, IReadOnlyList<IWrittenSheet> sheets, bool manifest)
        {
            // --- The sheets, numbered as their parts, the manifest last and hidden ---
            Sheets list = new();
            DefinedNames definedNames = new();
            for (int index = 0; index < sheets.Count; index++)
            {
                IWrittenSheet sheet = sheets[index];
                list.Append(new Sheet
                {
                    Name = sheet.Name,
                    SheetId = (uint)(index + 1),
                    Id = XlsxPackage.WorksheetRelationshipId(index + 1),
                });

                if (sheet.FilterReference is string filter)
                {
                    definedNames.Append(new DefinedName(string.Concat("'", sheet.Name.Replace("'", "''", StringComparison.Ordinal), "'!", filter))
                    {
                        Name = "_xlnm._FilterDatabase",
                        LocalSheetId = (uint)index,
                        Hidden = true,
                    });
                }
            }

            if (manifest)
            {
                list.Append(new Sheet
                {
                    Name = SheetNames.Manifest,
                    SheetId = (uint)(sheets.Count + 1),
                    Id = XlsxPackage.WorksheetRelationshipId(sheets.Count + 1),
                    State = SheetStateValues.Hidden,
                });
            }

            // The schema orders the workbook's children: properties, views, sheets, defined names.
            Workbook workbook = new(
                new WorkbookProperties { Date1904 = false },
                new BookViews(new WorkbookView { ActiveTab = 0 }),
                list);
            if (definedNames.HasChildren)
            {
                workbook.Append(definedNames);
            }

            using Stream part = package.CreatePart(XlsxPackage.WorkbookPath);
            using var writer = OpenXmlWriter.Create(part);
            writer.WriteStartDocument(true);
            writer.WriteElement(workbook);
            writer.Close();
        }

        /// <summary>What the workbook part records of a written data sheet.</summary>
        private interface IWrittenSheet
        {
            /// <summary>The sheet's name, as written.</summary>
            string Name { get; }

            /// <summary>The filter's absolute range (<c>$A$1:$H$101</c>); null for no filter.</summary>
            string? FilterReference { get; }
        }

        /// <summary>A report's written table sheet.</summary>
        /// <param name="Name">The sheet's name.</param>
        /// <param name="Columns">The columns.</param>
        /// <param name="FilterRow">The header row the filter sits on.</param>
        /// <param name="LastRow">The last row written.</param>
        private sealed record TableSheet(string Name, int Columns, int FilterRow, int LastRow) : IWrittenSheet
        {
            /// <inheritdoc />
            public string? FilterReference => ColumnLetters.AbsoluteRange(0, FilterRow, Columns - 1, Math.Max(FilterRow, LastRow));
        }

        /// <summary>One data sheet of an entity export being written.</summary>
        private sealed class DataSheet : IWrittenSheet
        {
            /// <summary>Creates the sheet.</summary>
            /// <param name="plan">The sheet's plan.</param>
            /// <param name="name">The name written.</param>
            /// <param name="number">The worksheet number.</param>
            /// <param name="encodings">The columns' encodings.</param>
            /// <param name="styles">The columns' data styles.</param>
            /// <param name="headerStyle">The header style.</param>
            /// <param name="layout">The layout.</param>
            private DataSheet(ExcelSheetPlan plan, string name, int number, ColumnEncoding[] encodings, uint[] styles, uint headerStyle, SheetLayout layout)
            {
                Plan = plan;
                Name = name;
                Number = number;
                Encodings = encodings;
                Styles = styles;
                HeaderStyle = headerStyle;
                Layout = layout;
            }

            /// <summary>The sheet's plan.</summary>
            public ExcelSheetPlan Plan { get; }

            /// <inheritdoc />
            public string Name { get; }

            /// <summary>The worksheet's one-based number in the package.</summary>
            public int Number { get; }

            /// <summary>The columns' encodings.</summary>
            public ColumnEncoding[] Encodings { get; }

            /// <summary>The columns' data styles.</summary>
            public uint[] Styles { get; }

            /// <summary>The header style.</summary>
            public uint HeaderStyle { get; }

            /// <summary>The layout.</summary>
            public SheetLayout Layout { get; }

            /// <summary>The most data rows the sheet takes.</summary>
            public int Cap { get; set; }

            /// <summary>The data rows written.</summary>
            public int Rows { get; private set; }

            /// <summary>The worksheet writer, once started.</summary>
            public WorksheetWriter? Writer { get; set; }

            /// <summary>The scratch file a child sheet spools to.</summary>
            public FileStream? Spool { get; set; }

            /// <summary>The package part the parent sheet streams into.</summary>
            public Stream? Part { get; set; }

            /// <inheritdoc />
            public string? FilterReference => ColumnLetters.AbsoluteRange(0, 1, Encodings.Length - 1, Rows + 1);

            /// <summary>Resolves a sheet's encodings, styles and layout.</summary>
            /// <param name="plan">The sheet's plan.</param>
            /// <param name="name">The name written.</param>
            /// <param name="number">The worksheet number.</param>
            /// <param name="shape">The export's shape.</param>
            /// <param name="context">The request's context.</param>
            /// <param name="styles">The workbook's styles.</param>
            /// <param name="headerStyle">The header style.</param>
            /// <param name="rightToLeft">Whether the sheet reads right to left.</param>
            /// <param name="editable">Whether the export is editable, which adds validations.</param>
            /// <returns>The sheet.</returns>
            public static DataSheet For(
                ExcelSheetPlan plan,
                string name,
                int number,
                ExportKind shape,
                ExcelContext context,
                StylesheetBuilder styles,
                uint headerStyle,
                bool rightToLeft,
                bool editable)
            {
                int count = plan.Columns.Count;
                var encodings = new ColumnEncoding[count];
                uint[] dataStyles = new uint[count];
                var columns = new SheetColumn[count];
                List<SheetValidation> validations = [];
                for (int index = 0; index < count; index++)
                {
                    ExcelColumnSpec column = plan.Columns[index];
                    var encoding = ColumnEncoding.For(column, shape, context, plan.NumberFormats[index], column.Header.Length);
                    encodings[index] = encoding;
                    dataStyles[index] = styles.Register(new ExcelCellStyle(encoding.NumberFormat));
                    columns[index] = new SheetColumn(encoding.Width, encoding.Hidden, dataStyles[index]);

                    // Validation lists and length checks go on an editable export's data cells only.
                    if (editable && (encoding.ListValues is not null || encoding.MaxLength is not null))
                    {
                        validations.Add(new SheetValidation(index, encoding.ListValues, encoding.ListValues is null ? encoding.MaxLength : null));
                    }
                }

                SheetLayout layout = new(columns, FrozenRows: 1, FrozenColumns: 0, rightToLeft, Selected: number == 1, Protected: false, FilterRow: 1, validations);
                return new DataSheet(plan, name, number, encodings, dataStyles, headerStyle, layout);
            }

            /// <summary>Writes the header row.</summary>
            public void WriteHeader()
            {
                WorksheetWriter writer = Writer!;
                writer.StartRow();
                for (int index = 0; index < Encodings.Length; index++)
                {
                    writer.WriteCell(index, EncodedCell.OfText(Encodings[index].Column.Header), HeaderStyle);
                }

                writer.EndRow();
            }

            /// <summary>Writes a data row, refusing the row past the cap.</summary>
            /// <param name="row">The row's values, one per column.</param>
            /// <exception cref="InvalidOperationException">The row is not as wide as the sheet.</exception>
            /// <exception cref="LimitExceededException">The sheet already holds its cap.</exception>
            public void WriteRow(IReadOnlyList<object?> row)
            {
                if (row.Count != Encodings.Length)
                {
                    throw new InvalidOperationException("Each row lists one value per column of its sheet.");
                }

                if (Rows >= Cap)
                {
                    throw new LimitExceededException(ExcelErrorCodes.ExportRowLimitExceeded, Rows + 1, Cap);
                }

                WorksheetWriter writer = Writer!;
                writer.StartRow();
                for (int index = 0; index < Encodings.Length; index++)
                {
                    writer.WriteCell(index, CellCodec.Encode(row[index], Encodings[index]), Styles[index]);
                }

                writer.EndRow();
                Rows++;
            }

            /// <summary>Ends the sheet's rows and writes what follows them, then closes the parent sheet's part.</summary>
            public void Finish()
            {
                Writer?.Finish();
                Writer?.Dispose();
                Writer = null;
                Part?.Dispose();
                Part = null;
            }

            /// <summary>Releases the writer, the part and the spool; the system deletes a spool's file.</summary>
            public void Release()
            {
                Writer?.Dispose();
                Writer = null;
                Part?.Dispose();
                Part = null;
                Spool?.Dispose();
                Spool = null;
            }
        }
    }
}
