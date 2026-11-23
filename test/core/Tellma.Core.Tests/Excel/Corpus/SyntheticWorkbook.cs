// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Tellma.Core.Tests.Excel.Corpus
{
    /// <summary>The spreadsheet application whose saved package a synthetic workbook is shaped like.</summary>
    internal enum Producer
    {
        /// <summary>Desktop Excel: a shared-string table, a dimension, view and extension elements of other namespaces.</summary>
        Excel,

        /// <summary>LibreOffice Calc: a shared-string table, a dimension, a sheet properties element and column widths.</summary>
        LibreOffice,

        /// <summary>Google Sheets: a shared-string table and no dimension.</summary>
        GoogleSheets,

        /// <summary>A producer of the strict dialect: every namespace the strict one.</summary>
        Strict,
    }

    /// <summary>One cell of a synthetic sheet, as stored.</summary>
    /// <param name="Type">The cell's <c>t</c> attribute; null for none.</param>
    /// <param name="Value">The cell's <c>v</c> text; null for none.</param>
    /// <param name="Formula">The cell's <c>f</c> text; null for none.</param>
    /// <param name="Inline">The text of an inline string; null for none.</param>
    internal readonly record struct RawCell(string? Type, string? Value, string? Formula = null, string? Inline = null)
    {
        /// <summary>Text, through the shared-string table.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The cell.</returns>
        public static RawCell Text(string text)
        {
            return new RawCell("s", text);
        }

        /// <summary>An inline string.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The cell.</returns>
        public static RawCell InlineText(string text)
        {
            return new RawCell("inlineStr", Value: null, Inline: text);
        }

        /// <summary>A number.</summary>
        /// <param name="value">The stored <c>xsd:double</c>.</param>
        /// <returns>The cell.</returns>
        public static RawCell Number(double value)
        {
            return new RawCell(Type: null, value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>An error value.</summary>
        /// <param name="error">The error, such as <c>#N/A</c>.</param>
        /// <returns>The cell.</returns>
        public static RawCell Error(string error)
        {
            return new RawCell("e", error);
        }

        /// <summary>A formula no application calculated, so it holds no cached value.</summary>
        /// <param name="formula">The formula.</param>
        /// <returns>The cell.</returns>
        public static RawCell Uncalculated(string formula)
        {
            return new RawCell(Type: null, Value: null, formula);
        }
    }

    /// <summary>One sheet of a synthetic workbook.</summary>
    /// <param name="Name">The sheet's name.</param>
    /// <param name="Rows">The rows from row 1, each its cells from column A; a null cell is absent.</param>
    internal sealed record RawSheet(string Name, IReadOnlyList<IReadOnlyList<RawCell?>> Rows)
    {
        /// <summary>The sheet's state: <c>visible</c>, <c>hidden</c> or <c>veryHidden</c>.</summary>
        public string State { get; init; } = "visible";

        /// <summary>Whether the sheet declares its dimension; null for the producer's habit.</summary>
        public bool? Dimension { get; init; }

        /// <summary>The range the declared dimension states in place of the sheet's own; null for the sheet's own.</summary>
        public string? DimensionReference { get; init; }

        /// <summary>A sheet of text rows.</summary>
        /// <param name="name">The sheet's name.</param>
        /// <param name="rows">The rows, each its texts; a null text is an absent cell.</param>
        /// <returns>The sheet.</returns>
        public static RawSheet OfText(string name, params string?[][] rows)
        {
            return new RawSheet(name, [.. rows.Select(static row => (IReadOnlyList<RawCell?>)[.. row.Select(static text => text is null ? (RawCell?)null : RawCell.Text(text))])]);
        }
    }

    // A corpus of saved files would pin what each application writes, but none of the three
    // applications can be driven from the test run, so the corpus is generated: each producer's
    // package is written the way that application lays out its parts (see the README beside this
    // file). The builder writes nothing a real producer would not, except where a test asks.
    /// <summary>Builds an <c>.xlsx</c> package part by part, shaped like a producer's saved file, with any part replaced or added.</summary>
    /// <param name="producer">The producer the package is shaped like.</param>
    /// <param name="sheets">The sheets, in tab order.</param>
    internal sealed class SyntheticWorkbook(Producer producer, params RawSheet[] sheets)
    {
        /// <summary>The content type of a workbook's main part.</summary>
        public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

        /// <summary>The content type of a macro-enabled workbook's main part.</summary>
        public const string MacroWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

        /// <summary>The producer the package is shaped like.</summary>
        public Producer Producer { get; } = producer;

        /// <summary>The sheets, in tab order.</summary>
        public IReadOnlyList<RawSheet> Sheets { get; } = sheets;

        /// <summary>Whether the workbook counts dates from 1904.</summary>
        public bool Date1904 { get; init; }

        /// <summary>Whether the workbook is macro-enabled, with a macro part.</summary>
        public bool MacroEnabled { get; init; }

        /// <summary>Parts that replace the generated ones or are added, by name.</summary>
        public Dictionary<string, string> Parts { get; } = new(StringComparer.Ordinal);

        /// <summary>Generated parts left out of the package, by name.</summary>
        public HashSet<string> Omitted { get; } = new(StringComparer.Ordinal);

        /// <summary>The spreadsheet namespace the producer writes.</summary>
        private string Main => Producer == Producer.Strict ? "http://purl.oclc.org/ooxml/spreadsheetml/main" : "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>The relationship-id namespace the producer writes.</summary>
        private string RelationshipIds => Producer == Producer.Strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary>The prefix of the relationship types the producer writes.</summary>
        private string RelationshipTypes => Producer == Producer.Strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

        /// <summary>Writes the package.</summary>
        /// <returns>The package's bytes.</returns>
        public byte[] Build()
        {
            // --- The shared strings, in first-use order, then every generated part ---
            List<string> strings = [];
            Dictionary<string, int> indexes = new(StringComparer.Ordinal);
            Dictionary<string, string> parts = new(StringComparer.Ordinal);
            for (int i = 0; i < Sheets.Count; i++)
            {
                parts[$"xl/worksheets/sheet{i + 1}.xml"] = Worksheet(Sheets[i], strings, indexes);
            }

            parts["[Content_Types].xml"] = ContentTypes();
            parts["_rels/.rels"] = Relationships(("officeDocument", "xl/workbook.xml"));
            parts["xl/workbook.xml"] = Workbook();
            parts["xl/_rels/workbook.xml.rels"] = Relationships(
                [.. Sheets.Select(static (_, i) => (Kind: "worksheet", Target: $"worksheets/sheet{i + 1}.xml")), ("sharedStrings", "sharedStrings.xml"), ("styles", "styles.xml")]);
            parts["xl/sharedStrings.xml"] = SharedStrings(strings);
            parts["xl/styles.xml"] = $"{Declaration}<styleSheet xmlns=\"{Main}\"><fonts count=\"1\"><font/></fonts><fills count=\"1\"><fill/></fills><borders count=\"1\"><border/></borders><cellXfs count=\"1\"><xf/></cellXfs></styleSheet>";
            if (MacroEnabled)
            {
                parts["xl/vbaProject.bin"] = "not a real macro project";
            }

            foreach ((string name, string content) in Parts)
            {
                parts[name] = content;
            }

            // --- The zip, its entries in the order the producers write them ---
            using MemoryStream stream = new();
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string name, string content) in parts.OrderBy(static part => part.Key == "[Content_Types].xml" ? 0 : 1).ThenBy(static part => part.Key, StringComparer.Ordinal))
                {
                    if (Omitted.Contains(name))
                    {
                        continue;
                    }

                    ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using Stream writer = entry.Open();
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    writer.Write(bytes);
                }
            }

            return stream.ToArray();
        }

        /// <summary>The XML declaration each producer writes.</summary>
        private string Declaration => Producer == Producer.Excel
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
            : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

        /// <summary>The content types: the defaults and one override per generated part.</summary>
        /// <returns>The part.</returns>
        private string ContentTypes()
        {
            StringBuilder builder = new($"{Declaration}<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            _ = builder.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            if (MacroEnabled)
            {
                _ = builder.Append("<Default Extension=\"bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>");
            }

            _ = builder.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/workbook.xml\" ContentType=\"{(MacroEnabled ? MacroWorkbookContentType : WorkbookContentType)}\"/>");
            for (int i = 0; i < Sheets.Count; i++)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }

            _ = builder.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            _ = builder.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            return builder.Append("</Types>").ToString();
        }

        /// <summary>A relationships part.</summary>
        /// <param name="relationships">Each relationship's kind and target, given ids in order.</param>
        /// <returns>The part.</returns>
        private string Relationships(params (string Kind, string Target)[] relationships)
        {
            StringBuilder builder = new($"{Declaration}<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < relationships.Length; i++)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<Relationship Id=\"rId{i + 1}\" Type=\"{RelationshipTypes}{relationships[i].Kind}\" Target=\"{relationships[i].Target}\"/>");
            }

            return builder.Append("</Relationships>").ToString();
        }

        /// <summary>The workbook part: its properties and its sheets.</summary>
        /// <returns>The part.</returns>
        private string Workbook()
        {
            StringBuilder builder = new($"{Declaration}<workbook xmlns=\"{Main}\" xmlns:r=\"{RelationshipIds}\">");
            if (Producer == Producer.LibreOffice)
            {
                _ = builder.Append("<fileVersion appName=\"Calc\"/>");
            }

            _ = builder.Append(Date1904 ? "<workbookPr date1904=\"1\"/>" : "<workbookPr/>");
            _ = builder.Append("<sheets>");
            for (int i = 0; i < Sheets.Count; i++)
            {
                string state = Sheets[i].State == "visible" ? string.Empty : $" state=\"{Sheets[i].State}\"";
                _ = builder.Append(CultureInfo.InvariantCulture, $"<sheet name=\"{SecurityElement.Escape(Sheets[i].Name)}\" sheetId=\"{i + 1}\"{state} r:id=\"rId{i + 1}\"/>");
            }

            return builder.Append("</sheets></workbook>").ToString();
        }

        /// <summary>A worksheet part, its text cells added to the shared strings.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <param name="strings">The shared strings so far.</param>
        /// <param name="indexes">Each shared string's index.</param>
        /// <returns>The part.</returns>
        private string Worksheet(RawSheet sheet, List<string> strings, Dictionary<string, int> indexes)
        {
            // --- The producer's preamble: Excel's namespaces and view, LibreOffice's sheet properties ---
            StringBuilder builder = new(Declaration);
            _ = Producer == Producer.Excel
                ? builder.Append(CultureInfo.InvariantCulture, $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{RelationshipIds}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"x14ac\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\">")
                : builder.Append(CultureInfo.InvariantCulture, $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{RelationshipIds}\">");
            if (Producer == Producer.LibreOffice)
            {
                _ = builder.Append("<sheetPr filterMode=\"false\"><pageSetUpPr fitToPage=\"false\"/></sheetPr>");
            }

            if (Producer == Producer.GoogleSheets)
            {
                _ = builder.Append("<sheetPr><outlinePr summaryBelow=\"0\" summaryRight=\"0\"/></sheetPr>");
            }

            int width = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(static row => row.Count);
            bool dimension = sheet.Dimension ?? (Producer != Producer.GoogleSheets);
            if (dimension)
            {
                string reference = sheet.DimensionReference ?? (sheet.Rows.Count == 0 || width == 0 ? "A1" : $"A1:{Letter(width - 1)}{sheet.Rows.Count}");
                _ = builder.Append(CultureInfo.InvariantCulture, $"<dimension ref=\"{reference}\"/>");
            }

            if (Producer == Producer.Excel)
            {
                _ = builder.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews><sheetFormatPr defaultRowHeight=\"15\" x14ac:dyDescent=\"0.25\"/>");
            }

            if (Producer == Producer.LibreOffice && width > 0)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<cols><col min=\"1\" max=\"{width}\" width=\"11.5\" customWidth=\"false\"/></cols>");
            }

            // --- The rows: each cell by reference; text through the shared strings ---
            _ = builder.Append("<sheetData>");
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
                IReadOnlyList<RawCell?> row = sheet.Rows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    if (row[c] is RawCell cell)
                    {
                        _ = builder.Append(Cell(cell, $"{Letter(c)}{r + 1}", strings, indexes));
                    }
                }

                _ = builder.Append("</row>");
            }

            _ = builder.Append("</sheetData>");
            if (Producer == Producer.Excel)
            {
                _ = builder.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
                _ = builder.Append("<extLst><ext uri=\"{00000000-0000-0000-0000-000000000000}\" xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\"><x14:sparklineGroups/></ext></extLst>");
            }

            return builder.Append("</worksheet>").ToString();
        }

        /// <summary>One cell's element.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="reference">Its reference.</param>
        /// <param name="strings">The shared strings so far.</param>
        /// <param name="indexes">Each shared string's index.</param>
        /// <returns>The element.</returns>
        private static string Cell(RawCell cell, string reference, List<string> strings, Dictionary<string, int> indexes)
        {
            StringBuilder builder = new($"<c r=\"{reference}\"");
            if (cell.Type is string type)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $" t=\"{type}\"");
            }

            _ = builder.Append('>');
            if (cell.Formula is string formula)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<f>{SecurityElement.Escape(formula)}</f>");
            }

            if (cell.Type == "s" && cell.Value is string text)
            {
                if (!indexes.TryGetValue(text, out int index))
                {
                    index = strings.Count;
                    indexes[text] = index;
                    strings.Add(text);
                }

                _ = builder.Append(CultureInfo.InvariantCulture, $"<v>{index}</v>");
            }
            else if (cell.Inline is string inline)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<is><t xml:space=\"preserve\">{SecurityElement.Escape(inline)}</t></is>");
            }
            else if (cell.Value is string value)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<v>{SecurityElement.Escape(value)}</v>");
            }

            return builder.Append("</c>").ToString();
        }

        /// <summary>The shared-string part.</summary>
        /// <param name="strings">The strings, in index order.</param>
        /// <returns>The part.</returns>
        private string SharedStrings(List<string> strings)
        {
            StringBuilder builder = new($"{Declaration}<sst xmlns=\"{Main}\" count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">");
            foreach (string text in strings)
            {
                _ = builder.Append(CultureInfo.InvariantCulture, $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(text)}</t></si>");
            }

            return builder.Append("</sst>").ToString();
        }

        /// <summary>A column's letters.</summary>
        /// <param name="index">The zero-based column.</param>
        /// <returns>The letters.</returns>
        private static string Letter(int index)
        {
            string letters = string.Empty;
            for (int value = index + 1; value > 0; value = (value - 1) / 26)
            {
                letters = (char)('A' + ((value - 1) % 26)) + letters;
            }

            return letters;
        }
    }
}
