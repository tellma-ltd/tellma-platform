// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The entity export writer: a workbook that passes the Office schema, with the parent and
    ///     child sheets in plan order, the bold frozen filterable header, the editable shape's
    ///     validation lists, the hidden protected manifest, the 1900 date system, the row caps, and
    ///     memory that stays flat however many rows stream through. The suite runs alone, since its
    ///     memory test reads the process's heap.
    /// </summary>
    /// <param name="composition">The fixture model, composed once for the class.</param>
    [Collection(SerialSuites.Name)]
    public sealed class WorkbookWriterTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The manifest columns a date column's row is read at: sheet, letter, header, type, role, date encoding and pattern.</summary>
        private static readonly string[] DueColumns = ["A", "C", "D", "F", "G", "K", "L"];

        /// <summary>The manifest columns a parent reference column's row is read at: collection, role and entity.</summary>
        private static readonly string[] ReferenceColumns = ["B", "G", "I"];

        /// <summary>The scratch directory child sheets spool to, one per test.</summary>
        private readonly string _scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"))).FullName;

        /// <summary>The widget's metadata.</summary>
        private EntityMetadata Widgets => composition.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Widget));

        /// <summary>The widget part's metadata.</summary>
        private EntityMetadata Parts => composition.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(WidgetPart));

        [Fact]
        public async Task An_editable_export_passes_the_office_schema_with_its_sheets_in_plan_order_and_the_manifest_last_hidden()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            (byte[] bytes, int rows) = await Write(plan, SampleSource(plan));
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(2, rows);
            Assert.Equal(["Widgets", "Parts", "_tellma"], workbook.Sheets.Select(static s => s.Name!.Value));
            Assert.Equal(SheetStateValues.Hidden, workbook.Sheets[2].State!.Value);
            Assert.Null(workbook.Sheets[0].State);
            Assert.False(workbook.Workbook.WorkbookProperties!.Date1904!.Value);
            Assert.Equal(0u, workbook.Workbook.BookViews!.GetFirstChild<WorkbookView>()!.ActiveTab!.Value);
        }

        [Fact]
        public async Task Each_data_sheet_has_a_bold_header_frozen_below_it_and_a_filter_over_its_rows()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan))).Bytes;
            using WrittenWorkbook workbook = new(bytes);
            Worksheet widgets = workbook.Worksheet("Widgets");
            Pane pane = widgets.Descendants<Pane>().Single();

            Assert.Equal(1d, pane.VerticalSplit!.Value);
            Assert.Null(pane.HorizontalSplit);
            Assert.Equal("A2", pane.TopLeftCell!.Value);
            Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
            Assert.True(widgets.Descendants<SheetView>().Single().TabSelected!.Value);
            Assert.Null(workbook.Worksheet("Parts").Descendants<SheetView>().Single().TabSelected);
            Assert.Equal("A1:H3", widgets.GetFirstChild<AutoFilter>()!.Reference!.Value);
            Assert.Equal("A1:C4", workbook.Worksheet("Parts").GetFirstChild<AutoFilter>()!.Reference!.Value);
            Font header = workbook.Styles.Fonts!.Elements<Font>().ElementAt((int)workbook.StyleOf(workbook.Cell("Widgets", "A1")).FontId!.Value);
            Assert.NotNull(header.GetFirstChild<Bold>());

            // Excel names each sheet's filter range so its sort and filter menus find it.
            DefinedName[] names = [.. workbook.Workbook.DefinedNames!.Elements<DefinedName>()];
            Assert.Equal(["'Widgets'!$A$1:$H$3", "'Parts'!$A$1:$C$4"], names.Select(static n => n.Text));
            Assert.All(names, static n => Assert.Equal("_xlnm._FilterDatabase", n.Name!.Value));
            Assert.Equal([0u, 1u], names.Select(static n => n.LocalSheetId!.Value));
        }

        [Fact]
        public async Task The_cells_read_back_as_the_values_written()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan))).Bytes;
            using WrittenWorkbook workbook = new(bytes);
            IReadOnlyList<IReadOnlyDictionary<string, ExcelRawCell>> rows = workbook.Rows("Widgets");

            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "Code"), rows[0]["B"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Number, "1"), rows[1]["A"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "W-1"), rows[1]["B"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "Fancy"), rows[1]["C"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, " padded "), rows[1]["D"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Number, "12.5"), rows[1]["E"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Number, "46342"), rows[1]["F"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Boolean, "1"), rows[1]["G"]);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "2026-11-16T09:30:00.0000000+00:00"), rows[1]["H"]);

            // A null leaves no cell at all.
            Assert.False(rows[2].ContainsKey("E"));
            Assert.Equal(["Widget", "Ordinal", "Notes"], workbook.Rows("Parts")[0].Values.Select(static c => c.Text));
            Assert.Equal(3, workbook.Rows("Parts").Count - 1);
        }

        [Fact]
        public async Task A_columns_number_format_and_width_ride_its_column_and_its_cells()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan))).Bytes;
            using WrittenWorkbook workbook = new(bytes);
            Column[] columns = [.. workbook.Worksheet("Widgets").Descendants<Column>()];

            Assert.Equal("0.0000", workbook.FormatOf(workbook.Cell("Widgets", "E2")));
            Assert.Equal("[$-0409]m/d/yyyy", workbook.FormatOf(workbook.Cell("Widgets", "F2")));
            Assert.Equal("#49", workbook.FormatOf(workbook.Cell("Widgets", "B2")));
            Assert.Equal(8, columns.Length);
            Assert.True(columns[7].Hidden!.Value);
            Assert.All(columns, static c => Assert.True(c.CustomWidth!.Value));

            // Each distinct format is one custom entry, numbered from 164.
            Assert.Equal(
                workbook.Styles.NumberingFormats!.Elements<NumberingFormat>().Select(static f => f.FormatCode!.Value).Distinct().Count(),
                workbook.Styles.NumberingFormats!.Elements<NumberingFormat>().Count());
            Assert.All(workbook.Styles.NumberingFormats!.Elements<NumberingFormat>(), static f => Assert.True(f.NumberFormatId!.Value >= 164));
        }

        [Fact]
        public async Task An_editable_export_lists_the_choices_and_checks_text_lengths()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan))).Bytes;
            using WrittenWorkbook workbook = new(bytes);
            DataValidation[] validations = [.. workbook.Worksheet("Widgets").Descendants<DataValidation>()];

            DataValidation kind = validations.Single(v => v.SequenceOfReferences!.InnerText == "C2:C1048576");
            DataValidation active = validations.Single(v => v.SequenceOfReferences!.InnerText == "G2:G1048576");
            DataValidation code = validations.Single(v => v.SequenceOfReferences!.InnerText == "B2:B1048576");
            Assert.Equal(DataValidationValues.List, kind.Type!.Value);
            Assert.Equal("\"Plain,Fancy\"", kind.Formula1!.Text);
            Assert.Equal("\"TRUE,FALSE\"", active.Formula1!.Text);
            Assert.Equal(DataValidationValues.TextLength, code.Type!.Value);
            Assert.Equal(DataValidationOperatorValues.LessThanOrEqual, code.Operator!.Value);
            Assert.Equal("50", code.Formula1!.Text);
        }

        [Fact]
        public async Task A_display_export_writes_labels_and_no_validations()
        {
            ExcelWorkbookPlan plan = EditablePlan();
            ExcelWorkbookPlan display = plan with { Shape = ExportKind.Display };

            byte[] bytes = (await Write(display, SampleSource(display))).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Empty(workbook.Worksheet("Widgets").Descendants<DataValidation>());
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "[Fancy]"), workbook.Rows("Widgets")[1]["C"]);
            Assert.Equal("#,##0.0000", workbook.FormatOf(workbook.Cell("Widgets", "E2")));
        }

        [Fact]
        public async Task The_manifest_is_hidden_protected_and_records_the_header_block_and_every_column_by_its_written_sheet()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan))).Bytes;
            using WrittenWorkbook workbook = new(bytes);
            Worksheet manifest = workbook.Worksheet("_tellma");
            IReadOnlyList<IReadOnlyDictionary<string, ExcelRawCell>> rows = workbook.Rows("_tellma");

            Assert.True(manifest.GetFirstChild<SheetProtection>()!.Sheet!.Value);

            // The header block in its order, key in A and value in B.
            Assert.Equal(
                ["Tellma.Manifest", "Shape", "Entity", "Deployment", "Platform", "SourceTenantId", "ExportedAt", "Culture", "Calendar", "TimeZone", "Languages", "SchemaFingerprint", "Truncated"],
                rows.Take(13).Select(static r => r["A"].Text));
            Assert.Equal(["1", "ForImport", Widgets.Name, "deployment-a", "1.0.0-test", "7", "2026-11-16T09:30:00.0000000+00:00"], rows.Take(7).Select(static r => r["B"].Text));
            Assert.Equal(Widgets.SchemaFingerprint, rows[11]["B"].Text);
            Assert.Empty(rows[13]);

            // The column table: its headers, then one row per data column.
            Assert.Equal(ExcelManifest.ColumnTableHeaders, rows[14].Values.Select(static c => c.Text));
            IReadOnlyDictionary<string, ExcelRawCell> due = rows.Single(r => r.TryGetValue("E", out ExcelRawCell path) && path.Text == "DueOn");
            Assert.Equal(["Widgets", "F", "Due on", "Date", "Property", "Serial", "m/d/yyyy"], DueColumns.Select(k => due[k].Text));
            IReadOnlyDictionary<string, ExcelRawCell> parent = rows.Single(r => r.TryGetValue("A", out ExcelRawCell sheet) && sheet.Text == "Parts" && r["C"].Text == "A");
            Assert.Equal(["Parts", "ParentReference", "Widget"], ReferenceColumns.Select(k => parent[k].Text));

            // A parent reference is matched within the workbook, so only a reference records its key kind.
            Assert.False(parent.ContainsKey("J"));
            Assert.Equal(13 + 1 + 1 + 8 + 3, rows.Count);
        }

        [Fact]
        public async Task Sheet_names_are_scrubbed_and_made_unique_and_the_manifest_records_the_names_written()
        {
            ExcelWorkbookPlan plan = EditablePlan();
            ExcelWorkbookPlan renamed = plan with
            {
                Sheets = [plan.Sheets[0] with { Name = "_TELLMA" }, plan.Sheets[1] with { Name = "_tellma" }],
            };

            byte[] bytes = (await Write(renamed, SampleSource(renamed))).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(["_TELLMA (2)", "_tellma (3)", "_tellma"], workbook.Sheets.Select(static s => s.Name!.Value));
            Assert.Contains(workbook.Rows("_tellma"), static r => r.TryGetValue("A", out ExcelRawCell sheet) && sheet.Text == "_tellma (3)");
            Assert.Equal("'_TELLMA (2)'!$A$1:$H$3", workbook.Workbook.DefinedNames!.Elements<DefinedName>().First().Text);
        }

        [Fact]
        public async Task A_right_to_left_request_writes_right_to_left_sheets()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            byte[] bytes = (await Write(plan, SampleSource(plan), Context(ArSa, UmAlQura))).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.True(workbook.Worksheet("Widgets").Descendants<SheetView>().Single().RightToLeft!.Value);
            Assert.Null(workbook.Worksheet("_tellma").Descendants<SheetView>().Single().RightToLeft);
            Assert.Equal("[$-170401]d/m/yyyy", workbook.FormatOf(workbook.Cell("Widgets", "F2")));
        }

        [Fact]
        public async Task A_capped_count_on_the_first_page_is_refused_before_the_destination_opens_or_any_scratch_file_is_made()
        {
            ExcelWorkbookPlan plan = EditablePlan(countCap: 1);
            bool opened = false;

            // The source checks, as it hands over the first page, that nothing is open or spooled yet.
            PagedSource source = SampleSource(plan, firstCount: 2) with
            {
                BeforeFirst = () =>
                {
                    Assert.False(opened);
                    Assert.Empty(Directory.EnumerateFiles(_scratch));
                },
            };
            WorkbookWriter writer = new(new ExcelOptions(), _scratch);

            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(() => writer.WriteAsync(
                plan,
                source,
                Context(),
                () =>
                {
                    opened = true;
                    return Stream.Null;
                },
                TestContext.Current.CancellationToken));

            Assert.Equal(ExcelErrorCodes.ExportRowLimitExceeded, refused.Limit);
            Assert.Equal(2, refused.Actual);
            Assert.Equal(1, refused.Maximum);
            Assert.False(opened);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task The_row_past_the_parent_cap_is_refused_when_no_count_came_first()
        {
            ExcelWorkbookPlan plan = EditablePlan(countCap: null, take: 2);

            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(() => Write(plan, SampleSource(plan)));

            Assert.Equal(2, refused.Actual);
            Assert.Equal(1, refused.Maximum);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task A_child_sheet_is_capped_by_the_options()
        {
            ExcelWorkbookPlan plan = EditablePlan();

            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(
                () => Write(plan, SampleSource(plan), options: new ExcelOptions { MaxExportRows = 2, MaxSynchronousExportRows = 2 }));

            Assert.Equal(3, refused.Actual);
            Assert.Equal(2, refused.Maximum);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task A_child_sheet_must_stand_for_a_dependent_of_the_parent_query()
        {
            ExcelWorkbookPlan plan = EditablePlan();
            ExcelWorkbookPlan stray = plan with { Sheets = [plan.Sheets[0], plan.Sheets[1] with { Query = plan.Sheets[1].Query with { OrderBy = "Ordinal" } }] };
            ExcelWorkbookPlan twice = plan with { Sheets = [plan.Sheets[0], plan.Sheets[1], plan.Sheets[1] with { Name = "Again" }] };

            _ = await Assert.ThrowsAsync<ArgumentException>(() => Write(stray, SampleSource(plan)));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => Write(twice, SampleSource(plan)));
        }

        [Fact]
        public async Task A_row_source_that_does_not_fit_its_query_is_refused()
        {
            ExcelWorkbookPlan plan = EditablePlan();
            ExcelPage noDependents = new([Widget(1, "W-1")], [], Count: null);
            ExcelPage narrowRow = new([["too", "narrow"]], [new ExcelPage([], [], null)], Count: null);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => Write(plan, new PagedSource(plan.Sheets[0].Query, [noDependents])));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => Write(plan, new PagedSource(plan.Sheets[0].Query, [narrowRow])));
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task A_destination_that_fails_releases_every_spool_and_its_first_failure_is_the_one_thrown()
        {
            // Enough rows that the parent sheet reaches the destination while the child sheet spools.
            ExcelWorkbookPlan plan = EditablePlan(countCap: 200_000);
            GeneratedSource source = new(plan.Sheets[0].Query, 20, static _ => { });
            using FailingStream destination = new(budget: 16 * 1024);
            WorkbookWriter writer = new(new ExcelOptions(), _scratch);

            IOException thrown = await Assert.ThrowsAsync<IOException>(
                () => writer.WriteAsync(plan, source, Context(), () => destination, TestContext.Current.CancellationToken));

            Assert.Same(destination.FirstFault, thrown);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task Cancelling_mid_stream_stops_the_write_and_releases_every_spool()
        {
            ExcelWorkbookPlan plan = EditablePlan(countCap: 200_000);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            int spooled = -1;
            GeneratedSource source = new(plan.Sheets[0].Query, 10, page =>
            {
                if (page == 3)
                {
                    spooled = Directory.EnumerateFiles(_scratch).Count();
                    cancel.Cancel();
                }
            });
            using CountingStream sink = new();
            WorkbookWriter writer = new(new ExcelOptions(), _scratch);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(plan, source, Context(), () => sink, cancel.Token));

            // Windows keeps a spool's name until its stream closes; elsewhere the name goes as the file opens.
            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, spooled);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        public async Task Text_xml_cannot_carry_is_escaped_on_its_way_through_the_writer_and_reads_back()
        {
            const string Name = "a\u0001b\uD800c_x0041_d\uFFFFe";
            ExcelWorkbookPlan plan = EditablePlan();

            // The sheet name's lone surrogate and noncharacter are dropped, as the workbook part cannot carry them.
            ExcelWorkbookPlan renamed = plan with { Sheets = [plan.Sheets[0] with { Name = "Wid\uFFFFgets\uD800" }, plan.Sheets[1]] };
            object?[] widget = Widget(1, "W-1");
            widget[3] = Name;
            ExcelPage page = new([widget], [new ExcelPage([], [], null)], null);

            byte[] bytes = (await Write(renamed, new PagedSource(plan.Sheets[0].Query, [page]))).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(["Widgets", "Parts", "_tellma"], workbook.Sheets.Select(static s => s.Name!.Value));
            Assert.Equal("a_x0001_b_xD800_c_x005F_x0041_d_xFFFF_e", workbook.Cell("Widgets", "D2").InnerText);
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, Name), workbook.Rows("Widgets")[1]["D"]);
        }

        [Fact]
        public async Task A_collection_two_levels_down_is_its_own_sheet_fed_by_its_owner_sheets_dependents()
        {
            ExcelWorkbookPlan plan = EditablePlan();
            ExcelQuery notesQuery = new("WidgetPartNote", ["WidgetPart.Ordinal", "Text"], "WidgetPartId,Id", null, null, null, null, null);
            ExcelQuery partsQuery = plan.Sheets[1].Query with { Dependents = [notesQuery] };
            ExcelQuery root = plan.Sheets[0].Query with { Dependents = [partsQuery] };
            ExcelColumnSpec[] noteColumns =
            [
                new ExcelColumnSpec("Part", QueryexType.QxNumeric, null, ["WidgetPart", "Ordinal"])
                {
                    Role = ExcelColumnRole.ParentReference,
                    ReferenceEntity = "WidgetPart",
                    KeyKind = ExcelKeyKind.Natural,
                },
                new ExcelColumnSpec("Text", QueryexType.QxString, null, ["Text"]),
            ];
            ExcelWorkbookPlan deep = plan with
            {
                Sheets =
                [
                    plan.Sheets[0] with { Query = root },
                    plan.Sheets[1] with { Query = partsQuery },
                    new ExcelSheetPlan("Part notes", "Parts.PartNotes", noteColumns, new string?[noteColumns.Length], notesQuery),
                ],
            };

            // Two pages, each part page carrying the notes of its parts as its own dependent page.
            ExcelPage first = new([Widget(1, "W-1")], [new ExcelPage([["W-1", 1, "first"]], [new ExcelPage([[1, "n-1"], [1, "n-2"]], [], null)], null)], null);
            ExcelPage second = new([Widget(2, "W-2")], [new ExcelPage([["W-2", 1, "x"]], [new ExcelPage([[1, "n-3"]], [], null)], null)], null);

            byte[] bytes = (await Write(deep, new PagedSource(root, [first, second]))).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(["Widgets", "Parts", "Part notes", "_tellma"], workbook.Sheets.Select(static s => s.Name!.Value));
            Assert.Equal(["Notes", "first", "x"], workbook.Rows("Parts").Select(static r => r["C"].Text));
            Assert.Equal(["Text", "n-1", "n-2", "n-3"], workbook.Rows("Part notes").Select(static r => r["B"].Text));
            Assert.Contains(workbook.Rows("_tellma"), static r => r.TryGetValue("A", out ExcelRawCell sheet) && sheet.Text == "Part notes" && r["B"].Text == "Parts.PartNotes");
        }

        [Fact]
        public async Task An_editable_date_and_time_in_the_ethiopian_calendar_is_iso_text_and_the_manifest_records_its_pattern()
        {
            ExcelContext context = Context(AmEt, Ethiopian);
            ExcelColumnSpec at = Typed(typeof(DateTime), QueryexType.QxDateTime, name: "At");
            ExcelQuery root = new("Widget", ["At"], "Id", null, null, null, 10, null);
            ExcelWorkbookPlan plan = new(
                Widgets,
                ExportKind.ForImport,
                "Widgets 2026-11-16 0930.xlsx",
                [new ExcelSheetPlan("Widgets", null, [at], [null], root)],
                ExcelManifest.Header(Widgets, ExportKind.ForImport, context, "1.0.0-test"));
            ExcelPage page = new([[new DateTime(2025, 6, 26, 14, 30, 0)]], [], 1);

            byte[] bytes = (await Write(plan, new PagedSource(root, [page]), context)).Bytes;
            using WrittenWorkbook workbook = new(bytes);

            Assert.Empty(workbook.ValidationErrors());
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "2025-06-26T14:30:00"), workbook.Rows("Widgets")[1]["A"]);
            IReadOnlyDictionary<string, ExcelRawCell> column = workbook.Rows("_tellma").Single(static r => r.TryGetValue("E", out ExcelRawCell path) && path.Text == "At");
            Assert.Equal(nameof(ExcelDateEncoding.Text), column["K"].Text);
            Assert.Equal(CellCodec.IsoDateTimePattern, column["L"].Text);
        }

        [Fact]
        public async Task Memory_stays_flat_however_many_rows_stream_through()
        {
            // 60,000 parent rows and as many child rows, a thousand of each per page, written to a
            // stream that cannot seek, as a response body cannot.
            ExcelWorkbookPlan plan = EditablePlan(countCap: 200_000);
            const int Pages = 60;
            long early = 0;
            long late = 0;
            GeneratedSource source = new(plan.Sheets[0].Query, Pages, page =>
            {
                if (page == 10)
                {
                    early = GC.GetTotalMemory(forceFullCollection: true);
                }
                else if (page == Pages - 1)
                {
                    late = GC.GetTotalMemory(forceFullCollection: true);
                }
            });

            using CountingStream sink = new();
            int rows = await new WorkbookWriter(new ExcelOptions(), _scratch).WriteAsync(plan, source, Context(), () => sink, TestContext.Current.CancellationToken);

            Assert.Equal(Pages * 1000, rows);
            Assert.True(sink.Length > 1_000_000);

            // Fifty pages more held nothing more than the first ten did, give or take the allocator's slack.
            Assert.True(late - early < 8 * 1024 * 1024, $"Memory grew by {late - early:N0} bytes over 50,000 rows.");
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.Delete(_scratch, recursive: true);
        }

        /// <summary>A widget row as the editable plan's parent sheet lists it.</summary>
        /// <param name="id">The id.</param>
        /// <param name="code">The code.</param>
        /// <returns>The row.</returns>
        private static object?[] Widget(int id, string code)
        {
            return [id, code, WidgetKind.Fancy, " padded ", 12.5m, new DateOnly(2026, 11, 16), true, Now];
        }

        /// <summary>
        ///     The editable plan of widgets and their parts: the parent sheet's id, code, kind, name,
        ///     price, due date, activity and stamp; the child sheet's parent reference, ordinal and notes.
        /// </summary>
        /// <param name="countCap">The parent query's count cap.</param>
        /// <param name="take">The parent query's row limit.</param>
        /// <returns>The plan.</returns>
        private ExcelWorkbookPlan EditablePlan(int? countCap = 1000, int? take = null)
        {
            EntityMetadata widgets = Widgets;
            EntityMetadata parts = Parts;

            ExcelColumnSpec[] parentColumns =
            [
                Column(widgets, "Id", "Id", QueryexType.QxNumeric) with { Role = ExcelColumnRole.Id },
                Column(widgets, "Code", "Code", QueryexType.QxString),
                Column(widgets, "Kind", "Kind", QueryexType.QxString),
                Column(widgets, "Name", "Name (English)", QueryexType.QxString) with { Language = "en" },
                Column(widgets, "Price", "Price", QueryexType.QxNumeric),
                Column(widgets, "DueOn", "Due on", QueryexType.QxDate),
                Column(widgets, "IsActive", "Active", QueryexType.QxBool),
                new ExcelColumnSpec("Stamp", QueryexType.QxDateTimeOffset, null, null) { Role = ExcelColumnRole.Stamp },
            ];

            ExcelColumnSpec[] childColumns =
            [
                new ExcelColumnSpec("Widget", QueryexType.QxString, null, ["Widget", "Code"])
                {
                    Role = ExcelColumnRole.ParentReference,
                    ReferenceEntity = "Widget",
                    KeyKind = ExcelKeyKind.Natural,
                },
                Column(parts, "Ordinal", "Ordinal", QueryexType.QxNumeric),
                Column(parts, "Notes", "Notes", QueryexType.QxString),
            ];

            ExcelQuery partsQuery = new("WidgetPart", ["Widget.Code", "Ordinal", "Notes"], "WidgetId,Ordinal", null, null, null, null, null);
            ExcelQuery root = new("Widget", ["Id", "Code", "Kind", "Name", "Price", "DueOn", "IsActive", "Stamp"], "Id", null, null, take, countCap, [partsQuery]);
            return new ExcelWorkbookPlan(
                widgets,
                ExportKind.ForImport,
                "Widgets 2026-11-16 0930.xlsx",
                [
                    new ExcelSheetPlan("Widgets", null, parentColumns, new string?[parentColumns.Length], root),
                    new ExcelSheetPlan("Parts", "Parts", childColumns, new string?[childColumns.Length], partsQuery),
                ],
                ExcelManifest.Header(widgets, ExportKind.ForImport, Context(), "1.0.0-test"));
        }

        /// <summary>A column over a property of an entity.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="property">The property's name.</param>
        /// <param name="header">The header.</param>
        /// <param name="type">The Queryex type.</param>
        /// <returns>The column.</returns>
        private static ExcelColumnSpec Column(EntityMetadata entity, string property, string header, QueryexType type)
        {
            PropertyMetadata metadata = entity.Properties.Single(p => p.Name == property);
            return new ExcelColumnSpec(header, type, metadata.Scale, [property]) { Property = metadata };
        }

        /// <summary>The sample rows: two widgets in one page, the first with three parts across the page's dependents.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="firstCount">The capped count the first page carries.</param>
        /// <returns>The source.</returns>
        private static PagedSource SampleSource(ExcelWorkbookPlan plan, int? firstCount = null)
        {
            object?[] second = Widget(2, "W-2");
            second[4] = null;
            ExcelPage parts = new([["W-1", 1, "first"], ["W-1", 2, null], ["W-2", 1, "x"]], [], null);
            ExcelPage page = new([Widget(1, "W-1"), second], [parts], firstCount);
            return new PagedSource(plan.Sheets[0].Query, [page]);
        }

        /// <summary>Writes a workbook to a stream that cannot seek and returns its bytes.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="source">The rows.</param>
        /// <param name="context">The context; the default one when null.</param>
        /// <param name="options">The options; the defaults when null.</param>
        /// <returns>The bytes and the parent rows written.</returns>
        private async Task<(byte[] Bytes, int Rows)> Write(ExcelWorkbookPlan plan, IExcelRowSource source, ExcelContext? context = null, ExcelOptions? options = null)
        {
            using CountingStream destination = new(keep: true);
            int rows = await new WorkbookWriter(options ?? new ExcelOptions(), _scratch).WriteAsync(plan, source, context ?? Context(), () => destination, TestContext.Current.CancellationToken);
            Assert.Empty(Directory.EnumerateFiles(_scratch));
            return (destination.ToArray(), rows);
        }

        /// <summary>A row source over fixed pages of one query.</summary>
        /// <param name="Query">The query the pages answer.</param>
        /// <param name="PageList">The pages.</param>
        private sealed record PagedSource(ExcelQuery Query, IReadOnlyList<ExcelPage> PageList) : IExcelRowSource
        {
            /// <summary>Called as the first page is handed over; null for nothing.</summary>
            public Action? BeforeFirst { get; init; }

            /// <inheritdoc />
            public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery requested, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Assert.Same(Query, requested);
                bool first = true;
                foreach (ExcelPage page in PageList)
                {
                    await Task.Yield();
                    if (first)
                    {
                        BeforeFirst?.Invoke();
                        first = false;
                    }

                    yield return page;
                }
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>A row source that makes its pages as they are pulled: a thousand widgets and a thousand parts each.</summary>
        /// <param name="query">The query the pages answer.</param>
        /// <param name="count">The pages.</param>
        /// <param name="pulled">Called with each page's index before it is made.</param>
        private sealed class GeneratedSource(ExcelQuery query, int count, Action<int> pulled) : IExcelRowSource
        {
            /// <inheritdoc />
            public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery requested, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Assert.Same(query, requested);
                for (int page = 0; page < count; page++)
                {
                    pulled(page);
                    object?[][] widgets = new object?[1000][];
                    object?[][] parts = new object?[1000][];
                    for (int i = 0; i < 1000; i++)
                    {
                        int id = (page * 1000) + i + 1;
                        string code = $"W-{id}";
                        widgets[i] = [id, code, WidgetKind.Plain, $"Widget number {id}", id * 1.25m, new DateOnly(2026, 1, 1).AddDays(id % 365), id % 2 == 0, Now];
                        parts[i] = [code, 1, $"Note {id}"];
                    }

                    await Task.Yield();
                    yield return new ExcelPage(widgets, [new ExcelPage(parts, [], null)], page == 0 ? count * 1000 : null);
                }
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }
}
