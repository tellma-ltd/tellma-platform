// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Blobs;
using Tellma.Core.IntegrationTests.Crud;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>
    ///     The synchronous exports against a real tenant database, through the real connect
    ///     prologue, runner and evaluator: the display and editable workbooks of the widgets read
    ///     through the stack's row source at one round trip per page with every collection riding
    ///     it, the template of an empty id list, a child surface's export under a filtered grant on
    ///     its owner, and an export above the synchronous cap refused at its first page, before any
    ///     file is opened, naming the background route.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class ExcelExportTests(ExcelRowSourceDatabase fixture) : IClassFixture<ExcelRowSourceDatabase>
    {
        /// <summary>The widgets' resource, whose take ceiling is 50.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The widget parts' child surface.</summary>
        private const string Parts = "fixture.WidgetPart";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The display shape ---

        [Fact]
        public async Task A_display_export_by_query_writes_the_rows_the_grid_reads_in_one_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportRequest request = new()
            {
                Filters = ["Id <= 20"],
                OrderBy = "Price desc",
                Columns = [new("Code", "Code"), new("Name", null), new("Price * 2", "Double"), new("Id", null)],
            };

            await WarmAsync(subject, scope => ExportAsync(scope, request));
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (ExportOutcome outcome, int roundTrips) = await MeasureAsync(scope, () => ExportAsync(scope, request));
            await using (outcome)
            {
                // The active widgets of the first twenty, by price descending, in one read.
                Assert.Equal(1, roundTrips);
                Assert.Equal(18, outcome.Rows);
                Assert.EndsWith(".xlsx", outcome.FileName, StringComparison.Ordinal);
                Assert.Equal(0, outcome.Workbook.Position);

                List<Dictionary<string, string>> rows = Sheet(outcome.Workbook, 0);
                Assert.Equal(["Code", "Double", "Id"], [rows[0]["A"], rows[0]["C"], rows[0]["D"]]);
                int[] ids = [.. Enumerable.Range(1, 20).Where(static id => !ExcelRowSourceDatabase.IsInactive(id)).Reverse()];
                Assert.Equal(ids.Select(static id => string.Create(CultureInfo.InvariantCulture, $"X{id:000}")), rows.Skip(1).Select(static row => row["A"]));
                Assert.Equal(ids.Select(static id => $"Widget {id}"), rows.Skip(1).Select(static row => row["B"]));
                Assert.Equal(ids.Select(static id => id * 2m), rows.Skip(1).Select(static row => decimal.Parse(row["C"], CultureInfo.InvariantCulture)));
            }
        }

        [Fact]
        public async Task A_display_export_by_query_reads_one_round_trip_per_page_of_the_take_ceiling()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportRequest request = new() { Filters = ["Id > 1"], Columns = [new("Code", null)] };

            await WarmAsync(subject, scope => ExportAsync(scope, request));
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (ExportOutcome outcome, int roundTrips) = await MeasureAsync(scope, () => ExportAsync(scope, request));
            await using (outcome)
            {
                // Pages of 50, 50 and 7.
                Assert.Equal(3, roundTrips);
                Assert.Equal(107, outcome.Rows);
            }
        }

        [Fact]
        public async Task A_display_export_by_ids_writes_the_readable_named_rows_once_each_in_id_order_when_unordered()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, "Price > 4"));
            ExportRequest request = new() { Ids = [9, 3, 6, 9, 5], Columns = [new("Id", null), new("CreatedBy", "Creator")] };

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            await using ExportOutcome outcome = await ExportAsync(scope, request);

            // The third widget is priced below the caller's grant, so it is not read.
            List<Dictionary<string, string>> rows = Sheet(outcome.Workbook, 0);
            Assert.Equal(["5", "6", "9"], rows.Skip(1).Select(static row => row["A"]));
            Assert.Equal("Creator", rows[0]["B"]);
            Assert.All(rows.Skip(1), static row => Assert.False(string.IsNullOrEmpty(row["B"])));
        }

        // --- The editable shape ---

        [Fact]
        public async Task An_editable_export_by_ids_writes_every_sheet_with_its_collections_riding_one_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportForImportRequest request = new() { Ids = [6, 3] };

            await WarmAsync(subject, scope => ExportForImportAsync(scope, request));
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (ExportOutcome outcome, int roundTrips) = await MeasureAsync(scope, () => ExportForImportAsync(scope, request));
            await using (outcome)
            {
                Assert.Equal(1, roundTrips);
                Assert.Equal(2, outcome.Rows);

                // The parents by id, then each part under its widget, then each note under its part.
                List<Dictionary<string, string>> widgets = Sheet(outcome.Workbook, 0);
                Assert.Equal(["Id", "Stamp"], [widgets[0]["A"], widgets[0]["B"]]);
                Assert.Equal(["3", "6"], widgets.Skip(1).Select(static row => row["A"]));
                Assert.Equal(["X003", "X006"], widgets.Skip(1).Select(static row => row["C"]));
                Assert.All(widgets.Skip(1), static row => Assert.False(string.IsNullOrEmpty(row["B"])));

                List<Dictionary<string, string>> parts = Sheet(outcome.Workbook, 1);
                Assert.Equal([("3", "2003"), ("6", "2006")], parts.Skip(1).Select(static row => (row["A"], row["B"])));
                List<Dictionary<string, string>> notes = Sheet(outcome.Workbook, 2);
                Assert.Equal([("2003", "n3"), ("2006", "n6")], notes.Skip(1).Select(static row => (row["A"], row["C"])));
            }
        }

        [Fact]
        public async Task An_empty_id_list_exports_the_template_of_every_sheet_without_a_row()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            await using ExportOutcome outcome = await ExportForImportAsync(scope, new ExportForImportRequest { Ids = [] });

            Assert.Equal(0, outcome.Rows);
            for (int sheet = 0; sheet < 3; sheet++)
            {
                List<Dictionary<string, string>> rows = Sheet(outcome.Workbook, sheet);
                Assert.Equal("Id", Assert.Single(rows).Values.ElementAt(sheet == 0 ? 0 : 1));
            }
        }

        // --- A child query surface ---

        [Fact]
        public async Task A_child_surface_exports_the_rows_its_query_reads_under_the_owners_filtered_grant_in_one_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, "Price > 60"));
            ExportRequest request = new() { Filters = ["Ordinal = 1"], OrderBy = "Id", Columns = [new("Id", null), new("Widget", null), new("Notes", null)] };

            await WarmAsync(subject, scope => ExportPartsAsync(scope, request));
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (ExportOutcome outcome, int roundTrips) = await MeasureAsync(scope, () => ExportPartsAsync(scope, request));
            await using (outcome)
            {
                // The parts of the widgets priced above 60, each widget written as its display name.
                int[] widgets = [.. Enumerable.Range(61, 60).Where(ExcelRowSourceDatabase.HasPart)];
                Assert.Equal(1, roundTrips);
                List<Dictionary<string, string>> rows = Sheet(outcome.Workbook, 0);
                Assert.Equal(widgets.Select(static id => ExcelRowSourceDatabase.PartOf(id).ToString(CultureInfo.InvariantCulture)), rows.Skip(1).Select(static row => row["A"]));
                Assert.Equal(widgets.Select(static id => $"Widget {id}"), rows.Skip(1).Select(static row => row["B"]));
                Assert.Equal(widgets.Select(static id => $"p{id}"), rows.Skip(1).Select(static row => row["C"]));
            }
        }

        // --- The synchronous cap ---

        [Fact]
        public async Task An_export_above_the_synchronous_cap_is_refused_at_its_first_page_before_any_file_naming_the_background_route()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            // A cap of ten, and a scratch directory that does not exist: opening any file would fail.
            string missing = Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"));
            await using ServiceProvider instance = fixture.Connect.CreateInstance(configure: services =>
            {
                _ = services.PostConfigure<ExcelOptions>(static options => options.MaxSynchronousExportRows = 10);
                _ = services.Replace(ServiceDescriptor.Singleton(new ScratchDirectory(missing)));
            });
            ExportRequest display = new() { Filters = ["Id <= 30"], Columns = [new("Code", null)] };
            ExportForImportRequest editable = new() { Filters = ["Id <= 30"] };

            // Warmed by the same refusal, since any export that writes would need the missing directory.
            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject, services: instance))
            {
                _ = await Assert.ThrowsAsync<LimitExceededException>(() => ExportAsync(cold, display));
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: instance);
            (LimitExceededException refused, int roundTrips) = await MeasureAsync(scope, () => Assert.ThrowsAsync<LimitExceededException>(() => ExportAsync(scope, display)));
            LimitExceededException refusedEditable = await Assert.ThrowsAsync<LimitExceededException>(() => ExportForImportAsync(scope, editable));

            // One read: the first page's capped count is the cap plus one.
            Assert.Equal(1, roundTrips);
            Assert.Equal((ExcelErrorCodes.ExportRowLimitExceeded, 11, 10, "export/start"), (refused.Limit, refused.Actual, refused.Maximum, refused.Remedy));
            Assert.Contains("export/start", refused.Message, StringComparison.Ordinal);
            Assert.Equal("export-for-import/start", refusedEditable.Remedy);
            Assert.False(Directory.Exists(missing));
        }

        /// <summary>Runs an export once in a scope of its own, so the measured one finds every cache warm.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="export">The export.</param>
        /// <returns>A task that completes when the export is written and disposed.</returns>
        private async Task WarmAsync(string subject, Func<AsyncServiceScope, Task<ExportOutcome>> export)
        {
            await using AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject);
            await using ExportOutcome outcome = await export(cold);
        }

        /// <summary>Exports the widgets' display shape through the action invoker, which evaluates the action's securable first.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The workbook.</returns>
        private static Task<ExportOutcome> ExportAsync(AsyncServiceScope scope, ExportRequest request)
        {
            return InvokeAsync(scope, StackAction(scope, "export"), request);
        }

        /// <summary>Exports the widgets' editable shape through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The workbook.</returns>
        private static Task<ExportOutcome> ExportForImportAsync(AsyncServiceScope scope, ExportForImportRequest request)
        {
            return InvokeAsync(scope, StackAction(scope, "export-for-import"), request);
        }

        /// <summary>Exports the widget parts' child surface through the action invoker.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The workbook.</returns>
        private static Task<ExportOutcome> ExportPartsAsync(AsyncServiceScope scope, ExportRequest request)
        {
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().ChildQueries
                .Single(static surface => surface.Resource == Parts).Actions
                .Single(static candidate => candidate.Name == "export");
            return InvokeAsync(scope, action, request);
        }

        /// <summary>The registry's own descriptor of one of the widgets' API actions.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="name">The action's name.</param>
        /// <returns>The descriptor.</returns>
        private static ApiActionDescriptor StackAction(AsyncServiceScope scope, string name)
        {
            return scope.ServiceProvider.GetRequiredService<IStackRegistry>().Stacks
                .Single(static stack => stack.Resource == Widgets).Actions
                .OfType<ApiActionDescriptor>()
                .Single(candidate => candidate.Name == name);
        }

        /// <summary>Invokes a workbook action in a caller's scope.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="action">The action.</param>
        /// <param name="body">The request.</param>
        /// <returns>The workbook.</returns>
        private static async Task<ExportOutcome> InvokeAsync(AsyncServiceScope scope, ApiActionDescriptor action, object body)
        {
            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, body, Token);
            return Assert.IsType<ExportOutcome>(result);
        }

        /// <summary>Runs a read and counts the round trips it took on the scope.</summary>
        /// <typeparam name="T">The read's result.</typeparam>
        /// <param name="scope">The scope.</param>
        /// <param name="read">The read.</param>
        /// <returns>The result and the round trips.</returns>
        private static async Task<(T Result, int RoundTrips)> MeasureAsync<T>(AsyncServiceScope scope, Func<Task<T>> read)
        {
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            T result = await read();
            return (result, access.RoundTrips - before);
        }

        /// <summary>A sheet's rows, header first, each cell's text by its column letter.</summary>
        /// <param name="workbook">The workbook, which is rewound after reading.</param>
        /// <param name="index">The sheet's position in the workbook.</param>
        /// <returns>The rows.</returns>
        private static List<Dictionary<string, string>> Sheet(Stream workbook, int index)
        {
            using MemoryStream copy = new();
            workbook.Position = 0;
            workbook.CopyTo(copy);
            workbook.Position = 0;

            using var document = SpreadsheetDocument.Open(copy, isEditable: false);
            WorkbookPart part = document.WorkbookPart!;
            Sheet sheet = part.Workbook!.Sheets!.Elements<Sheet>().ElementAt(index);
            Worksheet worksheet = ((WorksheetPart)part.GetPartById(sheet.Id!.Value!)).Worksheet!;
            return
            [
                .. worksheet.Descendants<Row>().Select(static row => row.Elements<Cell>().ToDictionary(
                    static cell => new string([.. cell.CellReference!.Value!.TakeWhile(char.IsLetter)]),
                    static cell => cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty,
                    StringComparer.Ordinal)),
            ];
        }
    }
}
