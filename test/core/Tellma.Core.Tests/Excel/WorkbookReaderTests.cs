// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Excel.Corpus;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The workbook reader over well-formed packages of each producer and over hostile ones: the
    ///     package, part, directory and name limits, the structure checks, the shared-string cap and
    ///     spill, the row and sheet text budgets, the skim, and the faults of misaddressed cells, each
    ///     refusing the workbook whole with its reason.
    /// </summary>
    public sealed class WorkbookReaderTests : IDisposable
    {
        /// <summary>How many distinct names a name flood holds: some fifteen times what the name table's bound admits.</summary>
        private const int NameFloodCount = 400_000;

        /// <summary>The most a refused name or node flood may allocate, its package's bytes aside; unbounded, either would take several times this.</summary>
        private const long MaxFloodAllocation = 16L << 20;

        /// <summary>How many 1 KiB blocks a node flood holds: six times the run bound.</summary>
        private const int NodeFloodBlocks = 24 * 1024;

        /// <summary>Bounds roomy enough for every well-formed test package.</summary>
        private static readonly WorkbookLimits Roomy = new(MaxCompressedBytes: 64L << 20, MaxUncompressedBytes: 256L << 20, SharedStringBudget: 64L << 20, MaxSharedStringEntries: 1_000_000);

        /// <summary>The scratch directory spilled strings go to.</summary>
        private readonly string _scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-reader-tests", Guid.NewGuid().ToString("N"))).FullName;

        [Theory]
        [InlineData(nameof(Producer.Excel), 2)]
        [InlineData(nameof(Producer.LibreOffice), 2)]
        [InlineData(nameof(Producer.GoogleSheets), null)]
        [InlineData(nameof(Producer.Strict), 2)]
        public void Each_producers_package_reads_its_sheets_and_cells(string producer, int? dimension)
        {
            SyntheticWorkbook workbook = new(
                Enum.Parse<Producer>(producer),
                new RawSheet("Data", [[RawCell.Text("Code"), RawCell.InlineText("Name"), null, RawCell.Number(1.5)], [RawCell.Text("W-1"), RawCell.Error("#N/A"), RawCell.Uncalculated("A1&B1"), RawCell.Number(-2)]]),
                RawSheet.OfText("Hidden", ["x"]) with { State = "hidden" },
                RawSheet.OfText("Secret", ["y"]) with { State = "veryHidden" });

            using WorkbookReader reader = Open(workbook.Build());

            Assert.Equal(
                [(0, "Data", WorkbookSheetState.Visible), (1, "Hidden", WorkbookSheetState.Hidden), (2, "Secret", WorkbookSheetState.VeryHidden)],
                reader.Sheets.Select(static sheet => (sheet.Position, sheet.Name, sheet.State)));
            Assert.All(reader.Sheets, static sheet => Assert.NotNull(sheet.Part));
            Assert.False(reader.Date1904);

            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);
            Assert.Equal(dimension, sheet.DimensionLastRow);
            List<SheetCell> cells = [];
            Assert.True(sheet.Read(cells, out int first));
            Assert.Equal(1, first);
            Assert.Equal(
                [(0, ExcelCellKind.Text, "Code"), (1, ExcelCellKind.Text, "Name"), (3, ExcelCellKind.Number, "1.5")],
                cells.Select(static cell => (cell.Column, cell.Value.Kind, cell.Value.Text)));

            // The formula without a cached value is blank, so it is not stored.
            Assert.True(sheet.Read(cells, out int second));
            Assert.Equal(2, second);
            Assert.Equal(
                [(0, ExcelCellKind.Text, "W-1"), (1, ExcelCellKind.Error, "#N/A"), (3, ExcelCellKind.Number, "-2")],
                cells.Select(static cell => (cell.Column, cell.Value.Kind, cell.Value.Text)));
            Assert.False(sheet.Read(cells, out _));
        }

        [Fact]
        public void A_macro_enabled_workbook_of_the_1904_date_system_reads()
        {
            using WorkbookReader reader = Open(new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["a"])) { MacroEnabled = true, Date1904 = true }.Build());

            Assert.True(reader.Date1904);
            Assert.Equal("Data", Assert.Single(reader.Sheets).Name);
        }

        [Fact]
        public void Rows_and_cells_without_references_take_the_next_in_sequence()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet("<row><c t=\"inlineStr\"><is><t>a</t></is></c><c t=\"inlineStr\"><is><t>b</t></is></c></row><row r=\"4\"><c><v>1</v></c></row>");

            using WorkbookReader reader = Open(workbook.Build());
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);

            List<SheetCell> cells = [];
            Assert.True(sheet.Read(cells, out int first));
            Assert.Equal(1, first);
            Assert.Equal([0, 1], cells.Select(static cell => cell.Column));
            Assert.True(sheet.Read(cells, out int second));
            Assert.Equal(4, second);
            Assert.Null(sheet.DimensionLastRow);
        }

        [Fact]
        public void A_package_above_the_compressed_limit_is_refused_before_it_is_opened()
        {
            byte[] package = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["a"])).Build();

            ImportException refused = Refuse(package, Roomy with { MaxCompressedBytes = package.Length - 1 });

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportPackageTooLarge, package.Length, package.Length - 1), (error.Code, (long)error.Arguments["bytes"]!, (long)error.Arguments["max"]!));
        }

        [Fact]
        public void A_package_whose_parts_sum_above_the_uncompressed_limit_is_refused_with_the_whole_sum()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/media/padding.xml"] = new string(' ', 200_000);
            byte[] package = workbook.Build();
            using ZipArchive zip = new(new MemoryStream(package), ZipArchiveMode.Read);
            long sum = zip.Entries.Sum(static entry => entry.Length);

            ImportException refused = Refuse(package, Roomy with { MaxUncompressedBytes = 100_000 });

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportPackageTooLarge, sum, 100_000L), (error.Code, (long)error.Arguments["bytes"]!, (long)error.Arguments["max"]!));
        }

        [Fact]
        public void Bytes_that_are_not_a_zip_are_a_malformed_workbook()
        {
            Assert.Equal(ImportFaults.ZipReason, Reason(Refuse(Encoding.UTF8.GetBytes("Code,Name\nW-1,One\n"))));
        }

        [Fact]
        public void An_archive_that_claims_to_span_disks_is_refused_from_its_directory()
        {
            byte[] package = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["a"])).Build();
            int end = package.Length - 22;
            Assert.Equal(0x06054b50u, BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(end)));
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(end + 4), 1);

            Assert.Equal(ImportFaults.ZipReason, Reason(Refuse(package)));
        }

        [Fact]
        public void An_archive_whose_directory_claims_more_entries_than_a_workbook_holds_is_refused()
        {
            byte[] package = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["a"])).Build();
            int end = package.Length - 22;

            // The count at its maximum defers to a zip64 record the archive lacks.
            BinaryPrimitives.WriteUInt16LittleEndian(package.AsSpan(end + 10), ushort.MaxValue);

            Assert.Equal(ImportFaults.ZipReason, Reason(Refuse(package)));
        }

        [Fact]
        public void A_package_without_content_types_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            _ = workbook.Omitted.Add("[Content_Types].xml");

            Assert.Equal(ImportFaults.ContentTypesReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_package_without_its_relationships_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            _ = workbook.Omitted.Add("_rels/.rels");

            Assert.Equal(ImportFaults.RelationshipsReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_package_declaring_a_link_to_another_workbook_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["[Content_Types].xml"] = ContentTypes(
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"" + SyntheticWorkbook.WorkbookContentType + "\"/>"
                + "<Override PartName=\"/xl/externalLinks/externalLink1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml\"/>");

            Assert.Equal(ImportFaults.ExternalLinkReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_package_of_two_workbook_parts_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["[Content_Types].xml"] = ContentTypes(
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"" + SyntheticWorkbook.WorkbookContentType + "\"/>"
                + "<Override PartName=\"/xl/workbook2.xml\" ContentType=\"" + SyntheticWorkbook.MacroWorkbookContentType + "\"/>");

            Assert.Equal(ImportFaults.WorkbookReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_relationship_that_leaves_the_package_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/_rels/workbook.xml.rels"] = Relationships(
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId9\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.test/\" TargetMode=\"External\"/>");

            Assert.Equal(ImportFaults.ExternalRelationshipReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_workbook_relationship_to_an_external_link_part_is_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/externalLinks/externalLink1.xml"] = "<externalLink xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>";
            workbook.Parts["xl/_rels/workbook.xml.rels"] = Relationships(
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink\" Target=\"externalLinks/externalLink1.xml\"/>");

            Assert.Equal(ImportFaults.ExternalLinkReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_part_declaring_a_document_type_is_refused_as_malformed_xml()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<?xml version=\"1.0\"?><!DOCTYPE sst [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;\">]>"
                + "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>&b;</t></si></sst>";

            Assert.Equal(ImportFaults.XmlReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_single_token_longer_than_the_token_bound_is_refused_as_malformed_xml()
        {
            // A run of white space between two elements: no cell text the cell-length check would catch first.
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>a</t></si>"
                + new string(' ', BoundedPartStream.MaxTokenBytes + 1024) + "</sst>";

            Assert.Equal(ImportFaults.XmlReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void Two_sheets_of_one_name_ignoring_case_are_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]), RawSheet.OfText("DATA", ["b"]));

            Assert.Equal(ImportFaults.SheetsReason, Reason(Refuse(workbook.Build())));
        }

        [Fact]
        public void A_shared_string_table_declaring_more_entries_than_the_cap_is_refused_before_it_is_read()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a", "b", "c"]));

            ImportException refused = Refuse(workbook.Build(), Roomy with { MaxSharedStringEntries = 2 });

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportTooManySharedStrings, 3L, 2), (error.Code, (long)error.Arguments["entries"]!, (int)error.Arguments["max"]!));
        }

        [Fact]
        public void A_shared_string_flood_without_a_declared_count_is_counted_whole_and_refused()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + string.Concat(Enumerable.Range(0, 5_000).Select(static i => $"<si><t>{i}</t></si>")) + "</sst>";

            ImportException refused = Refuse(workbook.Build(), Roomy with { MaxSharedStringEntries = 1_000 });

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportTooManySharedStrings, 5_000L, 1_000), (error.Code, (long)error.Arguments["entries"]!, (int)error.Arguments["max"]!));
        }

        [Fact]
        public void Shared_strings_above_the_budget_spill_to_scratch_and_still_read_back()
        {
            string[] texts = [.. Enumerable.Range(0, 300).Select(static i => $"text number {i} " + new string('x', 64))];
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", texts));

            using WorkbookReader reader = Open(workbook.Build(), Roomy with { SharedStringBudget = 4_096 });

            Assert.True(reader.SharedStrings.Spilled);
            Assert.Equal(300, reader.SharedStrings.Count);
            Assert.True(reader.SharedStrings.ChargedBytes > 4_096);
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);
            List<SheetCell> cells = [];
            Assert.True(sheet.Read(cells, out _));
            Assert.Equal(texts, cells.Select(static cell => cell.Value.Text));
        }

        [Fact]
        public void Shared_strings_within_the_budget_stay_in_memory()
        {
            using WorkbookReader reader = Open(new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["a", "b"])).Build());

            Assert.False(reader.SharedStrings.Spilled);
            Assert.Equal(2, reader.SharedStrings.Count);
        }

        [Theory]
        [InlineData("<row r=\"1\"><c r=\"B1\"><v>1</v></c><c r=\"A1\"><v>2</v></c></row>")]
        [InlineData("<row r=\"1\"><c r=\"A2\"><v>1</v></c></row>")]
        [InlineData("<row r=\"2\"><c><v>1</v></c></row><row r=\"1\"><c><v>1</v></c></row>")]
        [InlineData("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>99</v></c></row>")]
        [InlineData("<row r=\"1\"><c r=\"XFE1\"><v>1</v></c></row>")]
        [InlineData("<row r=\"1048577\"><c><v>1</v></c></row>")]
        public void A_misaddressed_row_or_cell_or_a_shared_string_the_table_lacks_is_a_malformed_sheet(string rows)
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet(rows);
            using WorkbookReader reader = Open(workbook.Build());
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);

            ImportException refused = Assert.Throws<ImportException>(() =>
            {
                List<SheetCell> cells = [];
                while (sheet.Read(cells, out _))
                {
                }
            });

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportMalformedWorkbook, "Data", ImportFaults.CellsReason), (error.Code, error.Sheet, (string?)error.Arguments["reason"]));
        }

        [Fact]
        public void A_sheet_part_that_is_not_well_formed_blames_its_sheet()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet("<row r=\"1\"><c><v>1</v></row>");
            using WorkbookReader reader = Open(workbook.Build());
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);

            ImportException refused = Assert.Throws<ImportException>(() => sheet.Read([], out _));

            Assert.Equal(("Data", ImportFaults.XmlReason), (refused.Errors[0].Sheet, Reason(refused)));
        }

        [Fact]
        public void A_sheet_of_distinct_element_names_is_refused_once_its_names_pass_their_bound_and_holds_little_memory()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + NameFlood() + "<sheetData><row r=\"1\"><c><v>1</v></c></row></sheetData></worksheet>";
            using WorkbookReader reader = Open(workbook.Build());

            // The names alone, unbounded, would allocate several times the ceiling below.
            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportException refused = Assert.Throws<ImportException>(() =>
            {
                using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);
                while (sheet.Read([], out _))
                {
                }
            });
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            AssertNamesTooLarge(refused);
            Assert.InRange(allocated, 0, MaxFloodAllocation);
        }

        [Fact]
        public void A_shared_string_table_of_distinct_element_names_is_refused_at_open_and_holds_little_memory()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>a</t></si>" + NameFlood() + "</sst>";
            byte[] package = workbook.Build();

            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportException refused = Refuse(package);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            AssertNamesTooLarge(refused);
            Assert.InRange(allocated, 0, MaxFloodAllocation);
        }

        [Fact]
        public void A_row_whose_text_passes_the_row_budget_is_a_malformed_sheet_and_a_row_within_it_reads()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet(InlineRow(1, 20, 20) + InlineRow(2, 40, 40));
            using WorkbookReader reader = Open(workbook.Build(), Roomy with { RowTextBudget = 100 });
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);
            List<SheetCell> cells = [];

            // Two cells of 20 characters are 80 bytes; two of 40 are 160, past the row's 100.
            Assert.True(sheet.Read(cells, out int row));
            Assert.Equal((1, 2), (row, cells.Count));
            ImportException refused = Assert.Throws<ImportException>(() => sheet.Read(cells, out _));

            AssertCellsRefused(refused);
        }

        [Fact]
        public void The_text_of_every_read_of_every_sheet_is_charged_to_one_workbook_budget()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]), RawSheet.OfText("Other", ["b"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet(InlineRow(1, 20, 20) + InlineRow(2, 20, 20));
            workbook.Parts["xl/worksheets/sheet2.xml"] = Worksheet(InlineRow(1, 20, 20));
            using WorkbookReader reader = Open(workbook.Build(), Roomy with { WorkbookTextBudget = 200 });
            List<SheetCell> cells = [];

            // Each row is 80 bytes: two on the first sheet leave 40, so the second sheet's row passes the budget.
            using (SheetReader first = reader.OpenSheet(reader.Sheets[0]))
            {
                Assert.True(first.Read(cells, out _));
                Assert.True(first.Read(cells, out _));
            }

            using SheetReader second = reader.OpenSheet(reader.Sheets[1]);
            ImportException refused = Assert.Throws<ImportException>(() => second.Read(cells, out _));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportMalformedWorkbook, "Other", ImportFaults.CellsReason), (error.Code, error.Sheet, (string?)error.Arguments["reason"]));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_ucs4_shared_string_part_is_refused_at_open_holding_little_memory(bool bigEndian)
        {
            // A reader decodes the part. Each character of its flood holds a byte, in UTF-8, or a unit,
            // in UTF-16, that reads as '<', so a scan that took the part for either would see the
            // flood as many short runs and let the reader decode it whole.
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            string flood = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(bigEndian ? 0x3C3C : 0x1003C), 2 << 20));
            string sst = $"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>{flood}</t></si></sst>";
            UTF32Encoding ucs4 = new(bigEndian, byteOrderMark: true);
            byte[] package = Replace(workbook.Build(), "xl/sharedStrings.xml", [.. ucs4.GetPreamble(), .. ucs4.GetBytes(sst)]);

            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportException refused = Refuse(package);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(ImportFaults.XmlReason, Reason(refused));
            Assert.InRange(allocated, 0, MaxFloodAllocation);
        }

        [Theory]
        [InlineData("iso-2022-jp", "$B", "<!", "(B")]
        [InlineData("hz-gb-2312", "~{", "<a", "~}")]
        public void A_part_declaring_a_code_page_encoding_is_refused_at_open_holding_little_memory(string encoding, string shiftIn, string pair, string shiftOut)
        {
            // With the code pages registered, as a database client does for the whole process, a
            // reader that switched to the declared encoding would decode each pair of ASCII bytes
            // ('<!', '<a') as one character: an attribute the scan took for many short runs.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            string flood = string.Concat(Enumerable.Repeat(pair, 4 << 20));
            string sst = $"<?xml version=\"1.0\" encoding=\"{encoding}\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" a=\"{shiftIn}{flood}{shiftOut}\"><si><t>a</t></si></sst>";
            byte[] package = Replace(workbook.Build(), "xl/sharedStrings.xml", Encoding.ASCII.GetBytes(sst));

            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportException refused = Refuse(package);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(ImportFaults.XmlReason, Reason(refused));
            Assert.InRange(allocated, 0, MaxFloodAllocation);
        }

        [Theory]
        [InlineData("shared string", ImportFaults.CellsReason)]
        [InlineData("shared string character data", ImportFaults.XmlReason)]
        [InlineData("workbook properties attribute", ImportFaults.XmlReason)]
        [InlineData("row attribute", ImportFaults.XmlReason)]
        [InlineData("cell value", ImportFaults.CellsReason)]
        public void A_node_far_past_the_run_bound_is_refused_holding_little_memory_wherever_its_delimiters_fall(string vector, string reason)
        {
            // Each node is a flood of 1 KiB blocks, each ending in a delimiter its node may hold: a '>'
            // in text and attribute values, a '<' inside a character data section. A text node read in
            // chunks is refused at the longest a cell may hold, before the run bound; the rest at the bound.
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            switch (vector)
            {
                case "shared string":
                    workbook.Parts["xl/sharedStrings.xml"] = $"<sst xmlns=\"{Main}\"><si><t>{Flood('>')}</t></si></sst>";
                    break;
                case "shared string character data":
                    workbook.Parts["xl/sharedStrings.xml"] = $"<sst xmlns=\"{Main}\"><si><t><![CDATA[{Flood('<')}]]></t></si></sst>";
                    break;
                case "workbook properties attribute":
                    workbook.Parts["xl/workbook.xml"] = $"<workbook xmlns=\"{Main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                        + $"<workbookPr codeName=\"{Flood('>')}\"/><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
                    break;
                case "row attribute":
                    workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet($"<row r=\"1\" spans=\"{Flood('>')}\"><c><v>1</v></c></row>");
                    break;
                default:
                    workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet($"<row r=\"1\"><c t=\"str\"><v>{Flood('>')}</v></c></row>");
                    break;
            }

            byte[] package = workbook.Build();

            // Unbounded, the node alone would allocate several times the ceiling: 24 MiB of text is 48 MiB of characters.
            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportException refused = Assert.Throws<ImportException>(() =>
            {
                using WorkbookReader reader = Open(package);
                using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);
                while (sheet.Read([], out _))
                {
                }
            });
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(reason, (string?)Assert.Single(refused.Errors).Arguments["reason"]);
            Assert.InRange(allocated, 0, MaxFloodAllocation);
        }

        [Fact]
        public void A_text_longer_than_a_cell_may_hold_is_refused_before_it_is_read_whole()
        {
            // Within the run bound, but past the longest text one cell may hold.
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet($"<row r=\"1\"><c t=\"str\"><v>{new string('a', XlsxXml.MaxTextLength + 1)}</v></c></row>");
            using WorkbookReader reader = Open(workbook.Build());
            using SheetReader sheet = reader.OpenSheet(reader.Sheets[0]);

            AssertCellsRefused(Assert.Throws<ImportException>(() => sheet.Read([], out _)));
        }

        [Fact]
        public void Sixty_four_sheets_over_one_part_are_refused_at_open_before_any_sheet_is_read()
        {
            // The first sheet's part is large; every sheet's relationship names it.
            RawSheet[] sheets = [.. Enumerable.Range(1, 64).Select(static i => RawSheet.OfText($"S{i}", ["a"]))];
            SyntheticWorkbook workbook = new(Producer.Excel, sheets);
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet(string.Concat(Enumerable.Range(1, 20_000).Select(static row => $"<row r=\"{row}\"><c><v>{row}</v></c></row>")));
            workbook.Parts["xl/_rels/workbook.xml.rels"] = Relationships(
                string.Concat(Enumerable.Range(1, 64).Select(static i => $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"))
                + "<Relationship Id=\"rId65\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>"
                + "<Relationship Id=\"rId66\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            byte[] package = workbook.Build();

            var watch = Stopwatch.StartNew();
            ImportException refused = Refuse(package);
            watch.Stop();

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ImportFaults.RelationshipsReason, "S2"), ((string?)error.Arguments["reason"], error.Sheet));
            Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void A_skim_tells_each_row_filled_as_a_read_would_without_charging_its_text()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<si><t xml:space=\"preserve\"> </t></si><si><t>x</t></si></sst>";
            workbook.Parts["xl/worksheets/sheet1.xml"] = Worksheet(
                "<row r=\"1\"><c t=\"inlineStr\"><is><t xml:space=\"preserve\">   </t></is></c></row>"
                + "<row r=\"2\"><c t=\"inlineStr\"><is><t>a long text past the row budget</t></is></c></row>"
                + "<row r=\"3\"><c t=\"s\"><v>0</v></c></row>"
                + "<row r=\"4\"><c t=\"s\"><v>1</v></c></row>"
                + "<row r=\"5\"><c><v>12</v></c></row>"
                + "<row r=\"6\"><c><f>A5</f></c></row>"
                + "<row r=\"7\"><c t=\"inlineStr\"><is><r><t xml:space=\"preserve\"> </t></r><rPh sb=\"0\" eb=\"1\"><t>ヨミ</t></rPh></is></c></row>"
                + "<row r=\"8\"/>"
                + "<row r=\"9\"><c t=\"str\"><f>A1</f><v xml:space=\"preserve\">  </v></c></row>"
                + "<row r=\"10\"><c><f>A1</f></c><c t=\"b\"><v>0</v></c></row>");
            byte[] package = workbook.Build();
            using WorkbookReader reader = Open(package, Roomy with { RowTextBudget = 8 });

            List<(int Row, bool Filled)> skimmed = [];
            using (SheetReader sheet = reader.OpenSheet(reader.Sheets[0]))
            {
                while (sheet.Skim(out int row, out bool filled))
                {
                    skimmed.Add((row, filled));
                }
            }

            Assert.Equal(
                [(1, false), (2, true), (3, false), (4, true), (5, true), (6, false), (7, false), (8, false), (9, false), (10, true)],
                skimmed);

            // The rows read under a roomy budget agree, and the row the skim passed is refused by a read under its own.
            using WorkbookReader roomy = Open(package);
            using SheetReader read = roomy.OpenSheet(roomy.Sheets[0]);
            List<SheetCell> cells = [];
            List<bool> filledByRead = [];
            while (read.Read(cells, out _))
            {
                filledByRead.Add(cells.Any(static cell => !string.IsNullOrWhiteSpace(cell.Value.Text)));
            }

            Assert.Equal(skimmed.Select(static row => row.Filled), filledByRead);
            using SheetReader budgeted = reader.OpenSheet(reader.Sheets[0]);
            Assert.True(budgeted.Read(cells, out _));
            AssertCellsRefused(Assert.Throws<ImportException>(() => budgeted.Read(cells, out _)));
        }

        [Fact]
        public void Empty_shared_strings_spill_by_their_entry_charge_alone()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText("Data", ["a"]));
            workbook.Parts["xl/sharedStrings.xml"] = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + string.Concat(Enumerable.Repeat("<si/>", 1_000)) + "</sst>";
            byte[] package = workbook.Build();

            // A thousand entries of no text are charged their overhead alone: 16,000 bytes.
            using WorkbookReader spilled = Open(package, Roomy with { SharedStringBudget = 8_000 });
            using WorkbookReader kept = Open(package, Roomy with { SharedStringBudget = 20_000 });

            Assert.Equal((true, 1_000L * SharedStringStore.EntryOverhead), (spilled.SharedStrings.Spilled, spilled.SharedStrings.ChargedBytes));
            Assert.False(kept.SharedStrings.Spilled);
        }

        [Fact]
        public void A_cancelled_shared_string_load_stops_between_entries()
        {
            byte[] part = Encoding.UTF8.GetBytes("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + string.Concat(Enumerable.Range(0, 5_000).Select(static i => $"<si><t>{i}</t></si>")) + "</sst>");
            using CancellationTokenSource source = new();
            source.Cancel();

            _ = Assert.Throws<OperationCanceledException>(() => SharedStringStore.Load(new BoundedPartStream(new MemoryStream(part, writable: false), part.Length), 64L << 20, 1_000_000, _scratch, source.Token).Dispose());
        }

        [Theory]
        [InlineData(100L, 1_000L, true)]
        [InlineData(70_000L, 1_000L, false)]
        [InlineData(100L, 17L << 20, false)]
        public void A_zip64_directory_is_held_to_the_entry_and_directory_bounds(long entries, long directoryBytes, bool accepted)
        {
            // The zip64 end record, its locator, then the end record whose entry count defers to it.
            byte[] archive = new byte[56 + 20 + 22];
            Span<byte> zip64 = archive.AsSpan(0, 56);
            BinaryPrimitives.WriteUInt32LittleEndian(zip64, 0x06064b50);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64[32..], (ulong)entries);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64[40..], (ulong)directoryBytes);
            Span<byte> locator = archive.AsSpan(56, 20);
            BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
            BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], 0);
            Span<byte> end = archive.AsSpan(76, 22);
            BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
            BinaryPrimitives.WriteUInt16LittleEndian(end[8..], ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(end[10..], ushort.MaxValue);

            CheckDirectory(archive, accepted);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void An_end_record_whose_offset_defers_to_a_zip64_record_is_held_to_that_records_bounds(bool locator, bool offsetTrigger)
        {
            // The end record claims one entry of a 47-byte directory; the zip64 record behind it, two million.
            byte[] archive = new byte[56 + 20 + 22];
            Span<byte> zip64 = archive.AsSpan(0, 56);
            BinaryPrimitives.WriteUInt32LittleEndian(zip64, 0x06064b50);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64[24..], 2_000_000);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64[32..], 2_000_000);
            BinaryPrimitives.WriteUInt64LittleEndian(zip64[40..], 47);
            if (locator)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(56), 0x07064b50);
            }

            Span<byte> end = archive.AsSpan(76, 22);
            BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
            BinaryPrimitives.WriteUInt16LittleEndian(end[8..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(end[10..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(end[12..], 47);
            BinaryPrimitives.WriteUInt32LittleEndian(end[16..], offsetTrigger ? uint.MaxValue : 0);

            // With its locator the zip64 record is held to the bounds whatever triggers it; without one,
            // the offset at its maximum names a record that is not there.
            CheckDirectory(archive, accepted: !locator && !offsetTrigger);
        }

        [Fact]
        public void An_end_record_whose_offset_defers_and_whose_zip64_record_is_absent_is_refused()
        {
            byte[] archive = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x06054b50);
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(10), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(16), uint.MaxValue);

            CheckDirectory(archive, accepted: false);
        }

        [Theory]
        [InlineData(1_000L, true)]
        [InlineData(17L << 20, false)]
        public void A_plain_directory_is_held_to_the_directory_bound(long directoryBytes, bool accepted)
        {
            byte[] archive = new byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x06054b50);
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(8), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(10), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(12), (uint)directoryBytes);

            CheckDirectory(archive, accepted);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                Directory.Delete(_scratch, recursive: true);
            }
            catch (IOException)
            {
                // A spilled file still open on a slow host is left for the system's temp cleanup.
            }
        }

        /// <summary>
        ///     A run of distinct empty elements, each a new name for the reader's name table: enough
        ///     that their names, unbounded, would be charged many times the table's bound.
        /// </summary>
        /// <returns>The elements.</returns>
        private static string NameFlood()
        {
            return string.Concat(Enumerable.Range(0, NameFloodCount).Select(static i => $"<n{i}/>"));
        }

        /// <summary>A node's flood: 1 KiB blocks of text, each ending in a delimiter, enough to pass the run bound many times over.</summary>
        /// <param name="delimiter">The delimiter each block ends in.</param>
        /// <returns>The flood.</returns>
        private static string Flood(char delimiter)
        {
            StringBuilder builder = new(NodeFloodBlocks * 1024);
            string block = new string('a', 1023) + delimiter;
            for (int i = 0; i < NodeFloodBlocks; i++)
            {
                _ = builder.Append(block);
            }

            return builder.ToString();
        }

        /// <summary>A row of inline strings, one per length, each of that many characters.</summary>
        /// <param name="row">The row's number.</param>
        /// <param name="lengths">The cells' lengths.</param>
        /// <returns>The row element.</returns>
        private static string InlineRow(int row, params int[] lengths)
        {
            return $"<row r=\"{row}\">" + string.Concat(lengths.Select(static length => $"<c t=\"inlineStr\"><is><t>{new string('x', length)}</t></is></c>")) + "</row>";
        }

        /// <summary>Asserts a refusal is the package-too-large error of the name table's bound.</summary>
        /// <param name="refused">The refusal.</param>
        private static void AssertNamesTooLarge(ImportException refused)
        {
            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportPackageTooLarge, BoundedNameTable.MaxBytes), (error.Code, (long)error.Arguments["max"]!));
            Assert.True((long)error.Arguments["bytes"]! > BoundedNameTable.MaxBytes);
        }

        /// <summary>Asserts a refusal is the malformed-cells error of the test sheet.</summary>
        /// <param name="refused">The refusal.</param>
        private static void AssertCellsRefused(ImportException refused)
        {
            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportMalformedWorkbook, "Data", ImportFaults.CellsReason), (error.Code, error.Sheet, (string?)error.Arguments["reason"]));
        }

        /// <summary>Checks an archive's directory records, expecting them accepted or refused as malformed.</summary>
        /// <param name="archive">The archive's bytes.</param>
        /// <param name="accepted">Whether the check should pass.</param>
        private static void CheckDirectory(byte[] archive, bool accepted)
        {
            using MemoryStream stream = new(archive, writable: false);
            if (accepted)
            {
                ZipDirectory.Check(stream);
                return;
            }

            Assert.Equal(ImportFaults.ZipReason, Reason(Assert.Throws<ImportException>(() => ZipDirectory.Check(stream))));
        }

        /// <summary>A package with one part's bytes replaced, for a part no text encodes.</summary>
        /// <param name="package">The package's bytes.</param>
        /// <param name="name">The part's name.</param>
        /// <param name="bytes">The part's new bytes.</param>
        /// <returns>The new package's bytes.</returns>
        private static byte[] Replace(byte[] package, string name, byte[] bytes)
        {
            using MemoryStream stream = new();
            stream.Write(package);
            using (ZipArchive zip = new(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                zip.GetEntry(name)!.Delete();
                using Stream writer = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
                writer.Write(bytes);
            }

            return stream.ToArray();
        }

        /// <summary>A worksheet part of the given rows.</summary>
        /// <param name="rows">The sheet data's content.</param>
        /// <returns>The part.</returns>
        private static string Worksheet(string rows)
        {
            return $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>{rows}</sheetData></worksheet>";
        }

        /// <summary>A content types part of the defaults and the given overrides.</summary>
        /// <param name="overrides">The overrides.</param>
        /// <returns>The part.</returns>
        private static string ContentTypes(string overrides)
        {
            return "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + overrides + "</Types>";
        }

        /// <summary>A relationships part of the given relationships.</summary>
        /// <param name="relationships">The relationships.</param>
        /// <returns>The part.</returns>
        private static string Relationships(string relationships)
        {
            return $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{relationships}</Relationships>";
        }

        /// <summary>The reason of a refusal's one malformed-workbook error.</summary>
        /// <param name="refused">The refusal.</param>
        /// <returns>The reason.</returns>
        private static string? Reason(ImportException refused)
        {
            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal(ExcelErrorCodes.ImportMalformedWorkbook, error.Code);
            return (string?)error.Arguments["reason"];
        }

        /// <summary>Opens a package.</summary>
        /// <param name="package">The package's bytes.</param>
        /// <param name="limits">The bounds; roomy when null.</param>
        /// <returns>The reader.</returns>
        private WorkbookReader Open(byte[] package, WorkbookLimits? limits = null)
        {
            return WorkbookReader.Open(new MemoryStream(package, writable: false), limits ?? Roomy, _scratch);
        }

        /// <summary>Opens a package that the reader refuses.</summary>
        /// <param name="package">The package's bytes.</param>
        /// <param name="limits">The bounds; roomy when null.</param>
        /// <returns>The refusal.</returns>
        private ImportException Refuse(byte[] package, WorkbookLimits? limits = null)
        {
            return Assert.Throws<ImportException>(() => Open(package, limits).Dispose());
        }
    }
}
