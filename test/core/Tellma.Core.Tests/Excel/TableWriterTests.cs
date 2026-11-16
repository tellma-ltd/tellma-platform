// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Spreadsheet;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The report table writer: one sheet with several bold header rows, the pane frozen below
    ///     them and after the frozen columns, each row style's font, fill and border, the first
    ///     column's indent, right-to-left sheets, and no manifest.
    /// </summary>
    public class TableWriterTests
    {
        [Fact]
        public async Task A_table_passes_the_office_schema_as_one_sheet_without_a_manifest()
        {
            using WrittenWorkbook workbook = new(await Write(Spec(), Rows()));

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(["Sales by region"], workbook.Sheets.Select(static s => s.Name!.Value));
            Assert.Null(workbook.Sheets[0].State);
            Assert.False(workbook.Workbook.WorkbookProperties!.Date1904!.Value);
        }

        [Fact]
        public async Task Every_header_row_is_bold_and_the_filter_sits_on_the_last()
        {
            using WrittenWorkbook workbook = new(await Write(Spec(), Rows()));
            IReadOnlyList<IReadOnlyDictionary<string, ExcelRawCell>> rows = workbook.Rows("Sales by region");

            Assert.Equal(["Region", "2026"], rows[0].Values.Select(static c => c.Text));
            Assert.Equal(["Region", "Q1", "Q2"], rows[1].Values.Select(static c => c.Text));
            Assert.True(IsBold(workbook, workbook.Cell("Sales by region", "B1")));
            Assert.True(IsBold(workbook, workbook.Cell("Sales by region", "C2")));
            Assert.Equal("A2:C8", workbook.Worksheet("Sales by region").GetFirstChild<AutoFilter>()!.Reference!.Value);
            Assert.Equal("'Sales by region'!$A$2:$C$8", workbook.Workbook.DefinedNames!.GetFirstChild<DefinedName>()!.Text);
        }

        [Fact]
        public async Task The_pane_freezes_below_the_headers_and_after_the_frozen_columns()
        {
            using WrittenWorkbook workbook = new(await Write(Spec(), Rows()));
            Pane pane = workbook.Worksheet("Sales by region").Descendants<Pane>().Single();

            Assert.Equal(2d, pane.VerticalSplit!.Value);
            Assert.Equal(1d, pane.HorizontalSplit!.Value);
            Assert.Equal("B3", pane.TopLeftCell!.Value);
            Assert.Equal(PaneValues.BottomRight, pane.ActivePane!.Value);
        }

        [Fact]
        public async Task Without_frozen_columns_only_the_headers_freeze()
        {
            using WrittenWorkbook workbook = new(await Write(Spec() with { FrozenColumns = 0 }, Rows()));
            Pane pane = workbook.Worksheet("Sales by region").Descendants<Pane>().Single();

            Assert.Null(pane.HorizontalSplit);
            Assert.Equal("A3", pane.TopLeftCell!.Value);
            Assert.Equal(PaneValues.BottomLeft, pane.ActivePane!.Value);
        }

        [Fact]
        public async Task Each_row_style_has_its_font_fill_and_border_on_every_cell()
        {
            using WrittenWorkbook workbook = new(await Write(Spec(), Rows()));

            // Rows 3 to 8: normal, phantom, first, second and third subtotal, total.
            Assert.Equal(("regular", false, false), Look(workbook, "C3"));
            Assert.Equal(("italic", false, false), Look(workbook, "C4"));
            Assert.Equal(("bold", true, false), Look(workbook, "C5"));
            Assert.Equal(("bold", false, false), Look(workbook, "C6"));
            Assert.Equal(("regular", false, false), Look(workbook, "C7"));
            Assert.Equal(("bold", false, true), Look(workbook, "C8"));

            // A styled row bands its blank cells too; a normal row leaves them out.
            Assert.Equal(("bold", true, false), Look(workbook, "B5"));
            Assert.DoesNotContain(workbook.Worksheet("Sales by region").Descendants<Cell>(), static c => c.CellReference == "B3");
        }

        [Fact]
        public async Task The_first_column_carries_the_rows_indent_from_the_edge_text_starts_at()
        {
            using WrittenWorkbook left = new(await Write(Spec(), Rows()));
            using WrittenWorkbook right = new(await Write(Spec(), Rows(), Context(ArSa)));

            Alignment indented = left.StyleOf(left.Cell("Sales by region", "A4")).Alignment!;
            Assert.Equal(2u, indented.Indent!.Value);
            Assert.Equal(HorizontalAlignmentValues.Left, indented.Horizontal!.Value);
            Assert.Null(left.StyleOf(left.Cell("Sales by region", "C4")).Alignment);
            Assert.Equal(HorizontalAlignmentValues.Right, right.StyleOf(right.Cell("Sales by region", "A4")).Alignment!.Horizontal!.Value);
            Assert.True(right.Worksheet("Sales by region").Descendants<SheetView>().Single().RightToLeft!.Value);
            Assert.Empty(right.ValidationErrors());
        }

        [Fact]
        public async Task A_columns_format_from_the_spec_rides_its_cells()
        {
            using WrittenWorkbook workbook = new(await Write(Spec(), Rows()));

            Assert.Equal("#4", workbook.FormatOf(workbook.Cell("Sales by region", "C3")));
            Assert.Equal("0.0%", workbook.FormatOf(workbook.Cell("Sales by region", "B4")));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Number, "1250.5"), workbook.Rows("Sales by region")[2]["C"]);
        }

        [Fact]
        public async Task A_row_not_parallel_to_the_columns_is_refused()
        {
            _ = await Assert.ThrowsAsync<ArgumentException>(() => Write(Spec(), Of(new ExcelTableRow(["East", 1m], ExcelRowStyle.Normal, 0))));
        }

        [Fact]
        public async Task The_file_name_comes_from_the_stem_or_falls_back_to_report()
        {
            ExcelTableSpec spec = Spec();

            Assert.Equal("Sales by region 2026-11-16 0930.xlsx", ExcelFileNames.Compose(spec.FileNameStem, ExcelFileNames.ReportStem, Now, TimeZoneInfo.Utc));
            Assert.Equal("report 2026-11-16 0930.xlsx", ExcelFileNames.Compose((spec with { FileNameStem = "المبيعات" }).FileNameStem, ExcelFileNames.ReportStem, Now, TimeZoneInfo.Utc));
            using CountingStream sink = new();
            Assert.Equal(0, await WorkbookWriter.WriteTableAsync(spec, Of(), Context(), sink, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void A_table_spec_refuses_what_cannot_be_written()
        {
            ExcelTableSpec spec = Spec();

            _ = Assert.Throws<ArgumentException>(() => spec with { HeaderRows = [] });
            _ = Assert.Throws<ArgumentException>(() => spec with { HeaderRows = [["a", "b", "c", "d"]] });
            _ = Assert.Throws<ArgumentException>(() => spec with { NumberFormats = [null] });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => spec with { FrozenColumns = 4 });
            _ = Assert.Throws<ArgumentException>(() => spec with { SheetName = " " });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelTableRow([], ExcelRowStyle.Normal, ExcelTableRow.MaxIndent + 1));
        }

        /// <summary>The sales table: two header rows, a region column frozen, a share and an amount.</summary>
        /// <returns>The spec.</returns>
        private static ExcelTableSpec Spec()
        {
            return new ExcelTableSpec(
                "Sales by region",
                "Sales by region",
                [["Region", "2026"], ["Region", "Q1", "Q2"]],
                [Computed(QueryexType.QxString, "Region"), Computed(QueryexType.QxNumeric, "Q1"), Computed(QueryexType.QxNumeric, "Q2", scale: 2)],
                [null, "0.0%", "#,##0.00"],
                FrozenColumns: 1);
        }

        /// <summary>One row of each style, the phantom row indented.</summary>
        /// <returns>The rows.</returns>
        private static IAsyncEnumerable<ExcelTableRow> Rows()
        {
            return Of(
                new ExcelTableRow(["East", null, 1250.5m], ExcelRowStyle.Normal, 0),
                new ExcelTableRow(["(none)", 0.25m, 3m], ExcelRowStyle.Phantom, 2),
                new ExcelTableRow(["Subtotal", null, 10m], ExcelRowStyle.Subtotal1, 0),
                new ExcelTableRow(["Subtotal", 0.5m, 20m], ExcelRowStyle.Subtotal2, 1),
                new ExcelTableRow(["Subtotal", 0.5m, 30m], ExcelRowStyle.Subtotal3, 2),
                new ExcelTableRow(["Total", 1m, 60m], ExcelRowStyle.Total, 0));
        }

        /// <summary>Rows as an asynchronous sequence.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The sequence.</returns>
        private static async IAsyncEnumerable<ExcelTableRow> Of(params ExcelTableRow[] rows)
        {
            foreach (ExcelTableRow row in rows)
            {
                await Task.Yield();
                yield return row;
            }
        }

        /// <summary>Writes a table and returns its bytes.</summary>
        /// <param name="spec">The spec.</param>
        /// <param name="rows">The rows.</param>
        /// <param name="context">The context; the default one when null.</param>
        /// <returns>The bytes.</returns>
        private static async Task<byte[]> Write(ExcelTableSpec spec, IAsyncEnumerable<ExcelTableRow> rows, ExcelContext? context = null)
        {
            using CountingStream destination = new(keep: true);
            _ = await WorkbookWriter.WriteTableAsync(spec, rows, context ?? Context(), destination, TestContext.Current.CancellationToken);
            return destination.ToArray();
        }

        /// <summary>Whether a cell's font is bold.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="cell">The cell.</param>
        /// <returns>True when it is.</returns>
        private static bool IsBold(WrittenWorkbook workbook, Cell cell)
        {
            return Font(workbook, cell).GetFirstChild<Bold>() is not null;
        }

        /// <summary>A cell's font.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="cell">The cell.</param>
        /// <returns>The font.</returns>
        private static Font Font(WrittenWorkbook workbook, Cell cell)
        {
            return workbook.Styles.Fonts!.Elements<Font>().ElementAt((int)workbook.StyleOf(cell).FontId!.Value);
        }

        /// <summary>How a cell looks: its font's weight or slant, whether it is shaded, whether it has a double top border.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="reference">The cell's reference.</param>
        /// <returns>The look.</returns>
        private static (string Font, bool Shaded, bool DoubleTop) Look(WrittenWorkbook workbook, string reference)
        {
            Cell cell = workbook.Cell("Sales by region", reference);
            CellFormat format = workbook.StyleOf(cell);
            Font font = Font(workbook, cell);
            Fill fill = workbook.Styles.Fills!.Elements<Fill>().ElementAt((int)format.FillId!.Value);
            Border border = workbook.Styles.Borders!.Elements<Border>().ElementAt((int)format.BorderId!.Value);
            string weight = font.GetFirstChild<Bold>() is not null ? "bold" : font.GetFirstChild<Italic>() is not null ? "italic" : "regular";
            return (weight, fill.PatternFill!.PatternType!.Value == PatternValues.Solid, border.TopBorder?.Style?.Value == BorderStyleValues.Double);
        }
    }
}
