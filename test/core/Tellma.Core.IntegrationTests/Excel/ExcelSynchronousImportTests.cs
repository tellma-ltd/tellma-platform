// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Excel;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Crud;
using Tellma.Core.Tests.Excel;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>
    ///     The synchronous import against a real tenant database, through the action invoker and
    ///     the real pipeline: editable exports imported back in every mode at every depth, within the
    ///     tenant and into another; the write-once, replacement, renaming and stale-identity cases;
    ///     a reference the caller cannot read; a row the caller cannot write; a row changed after it
    ///     was read, retried, and one changed after it was exported, refused unless the file's stamps
    ///     are ignored; the synchronous cap; and a warning to confirm.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases and their instance.</param>
    [Trait("Category", "Integration")]
    public sealed class ExcelSynchronousImportTests(ExcelImportDatabase fixture) : IClassFixture<ExcelImportDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The first tenant.</summary>
        private const int TenantA = ConnectDatabase.TenantA;

        /// <summary>The second tenant.</summary>
        private const int TenantB = ConnectDatabase.TenantB;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_editable_export_imports_back_by_id_updating_every_row_at_every_depth()
        {
            int first = ExcelImportDatabase.FirstWidget;
            (int userId, string subject) = await WidgetCallerAsync();
            byte[] workbook = await ExportAsync(subject, Widgets, [first, first + 1]);

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Update });

            Assert.Equal((0, 2, 0, 8, 0), (outcome.Inserted, outcome.Updated, outcome.ChildrenInserted, outcome.ChildrenUpdated, outcome.ChildrenDeleted));
            Assert.Equal([new RowRange(2, 3)], outcome.CommittedRowRanges);
            Assert.Empty(outcome.Warnings);
            Assert.Equal(4, await CountAsync($"SELECT COUNT(*) FROM [fixture].[WidgetPartNotes] n JOIN [fixture].[WidgetParts] p ON p.[Id] = n.[WidgetPartId] WHERE p.[WidgetId] = {first};"));
        }

        [Fact]
        public async Task An_insert_adds_new_rows_with_their_parts_and_notes_active_though_the_file_has_no_active_column()
        {
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] template, ImportPlan plan) = await TemplateAsync(userId, subject, Widgets);
            Assert.DoesNotContain(plan.Sheets.SelectMany(static sheet => sheet.Columns), static column => column.Path == "IsActive");
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (plan.Sheets[0].Sheet, [Widget(-1, "I-1", "i1"), Widget(-2, "I-2", "i2")]),
                (plan.Sheets[1].Sheet, [Row(("Widget.Id", -1), ("Id", -10), ("Ordinal", 1)), Row(("Widget.Id", -2), ("Id", -20), ("Ordinal", 1))]),
                (plan.Sheets[2].Sheet, [Row(("WidgetPart.Id", -10), ("Text", "a")), Row(("WidgetPart.Id", -10), ("Text", "b"))]));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook) });

            Assert.Equal((2, 0, 4, 0, 0), (outcome.Inserted, outcome.Updated, outcome.ChildrenInserted, outcome.ChildrenUpdated, outcome.ChildrenDeleted));
            Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] IN ('I-1', 'I-2') AND [IsActive] = 1;"));
            Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM [fixture].[WidgetPartNotes] n JOIN [fixture].[WidgetParts] p ON p.[Id] = n.[WidgetPartId] JOIN [fixture].[Widgets] w ON w.[Id] = p.[WidgetId] WHERE w.[Code] = 'I-1';"));
        }

        [Fact]
        public async Task An_upsert_by_code_updates_the_row_a_code_names_and_inserts_a_row_for_a_new_code()
        {
            int fifth = ExcelImportDatabase.FirstWidget + 4;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] template, ImportPlan plan) = await TemplateAsync(userId, subject, Widgets);
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (plan.Sheets[0].Sheet, [Widget(null, ExcelImportDatabase.CodeOf(fifth), $"s{fifth}", "Upserted"), Widget(null, "U-1", "u1")]));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Upsert, RowKey = "Code" });

            Assert.Equal((1, 1), (outcome.Inserted, outcome.Updated));
            Assert.Equal("Upserted", await fixture.Connect.ScalarAsync<string>(TenantA, $"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {fifth};"));
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'U-1';"));
        }

        [Fact]
        public async Task An_export_of_one_tenant_inserts_into_another_linking_every_depth_by_the_files_identities()
        {
            int first = ExcelImportDatabase.FirstWidget;
            (_, string exporter) = await WidgetCallerAsync();
            byte[] workbook = await ExportAsync(exporter, Widgets, [first, first + 1]);
            (int userId, string subject) = await fixture.SeedCallerAsync(TenantB, (Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook, TenantB) }, TenantB);

            Assert.Equal((2, 0, 8, 0, 0), (outcome.Inserted, outcome.Updated, outcome.ChildrenInserted, outcome.ChildrenUpdated, outcome.ChildrenDeleted));
            Assert.Equal(4, await fixture.Connect.ScalarAsync<int>(TenantB, $"SELECT COUNT(*) FROM [fixture].[WidgetPartNotes] n JOIN [fixture].[WidgetParts] p ON p.[Id] = n.[WidgetPartId] JOIN [fixture].[Widgets] w ON w.[Id] = p.[WidgetId] WHERE w.[Code] = '{ExcelImportDatabase.CodeOf(first)}';"));
        }

        [Fact]
        public async Task An_update_changing_a_write_once_value_is_refused_at_its_cell_and_writes_nothing()
        {
            int sixth = ExcelImportDatabase.FirstWidget + 5;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [sixth]);
            byte[] workbook = WorkbookEdits.WithRows(exported, plan, (plan.Sheets[0].Sheet, [Widget(sixth, ExcelImportDatabase.CodeOf(sixth), "changed", "Renamed")]));

            int fileId = await StageAsync(userId, workbook);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId, Mode = ImportMode.Update }));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportWriteOnceChanged, plan.Sheets[0].Sheet, 2, Letter(plan, 0, "Subject")), (error.Code, error.Sheet, error.Row, error.Column));
            Assert.Empty(refused.CommittedRowRanges);
            Assert.Equal($"Widget {sixth}", await fixture.Connect.ScalarAsync<string>(TenantA, $"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {sixth};"));
        }

        [Fact]
        public async Task A_part_the_file_replaces_is_deleted_with_its_notes()
        {
            int seventh = ExcelImportDatabase.FirstWidget + 6;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [seventh]);
            byte[] workbook = WorkbookEdits.WithRows(
                exported,
                plan,
                (plan.Sheets[0].Sheet, [Widget(seventh, ExcelImportDatabase.CodeOf(seventh), $"s{seventh}")]),
                (plan.Sheets[1].Sheet, [Row(("Widget.Id", seventh), ("Ordinal", 2), ("Notes", "replacement"))]),
                (plan.Sheets[2].Sheet, []));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Update });

            Assert.Equal((1, 1, 0, 1), (outcome.Updated, outcome.ChildrenInserted, outcome.ChildrenUpdated, outcome.ChildrenDeleted));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM [fixture].[WidgetPartNotes] WHERE [Id] = {ExcelImportDatabase.NoteOf(seventh, 1, 1)};"));
            Assert.Equal("replacement", await fixture.Connect.ScalarAsync<string>(TenantA, $"SELECT [Notes] FROM [fixture].[WidgetParts] WHERE [WidgetId] = {seventh};"));
        }

        [Fact]
        public async Task An_update_by_id_renames_a_code()
        {
            int eighth = ExcelImportDatabase.FirstWidget + 7;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [eighth]);
            byte[] workbook = WorkbookEdits.WithRows(exported, plan, (plan.Sheets[0].Sheet, [Widget(eighth, "R-8", $"s{eighth}")]));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Update });

            Assert.Equal(1, outcome.Updated);
            Assert.Equal("R-8", await fixture.Connect.ScalarAsync<string>(TenantA, $"SELECT [Code] FROM [fixture].[Widgets] WHERE [Id] = {eighth};"));
        }

        [Fact]
        public async Task A_copied_row_with_a_stale_id_is_a_duplicate_row_key_at_both_rows()
        {
            int third = ExcelImportDatabase.FirstWidget + 2;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [third]);
            byte[] workbook = WorkbookEdits.WithRows(exported, plan, (plan.Sheets[0].Sheet, [Widget(third, ExcelImportDatabase.CodeOf(third), $"s{third}"), Widget(third, "COPY", "copy")]));

            int fileId = await StageAsync(userId, workbook);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId, Mode = ImportMode.Upsert }));

            Assert.Equal([(ExcelErrorCodes.ImportDuplicateRowKey, 2, "A"), (ExcelErrorCodes.ImportDuplicateRowKey, 3, "A")], refused.Errors.Select(static error => (error.Code, error.Row!.Value, error.Column!)));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'COPY';"));
        }

        [Fact]
        public async Task A_reference_to_a_row_the_caller_cannot_read_is_not_found()
        {
            int first = ExcelImportDatabase.FirstNode;
            // A grant of Save implies reading under its own filter, so both grants hide N2.
            (int userId, string subject) = await fixture.SeedCallerAsync(TenantA, (Nodes, AccessActions.Read, "Code != 'N2'"), (Nodes, AccessActions.Save, "Code != 'N2'"));
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Nodes, [first]);
            byte[] workbook = WorkbookEdits.WithRows(exported, plan, (plan.Sheets[1].Sheet, [Row(("Node.Id", first), ("OtherNode.Code", "N2"))]));

            int fileId = await StageAsync(userId, workbook);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Nodes, new ImportRequest { FileId = fileId, Mode = ImportMode.Update }));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportReferenceNotFound, plan.Sheets[1].Sheet, 2, Letter(plan, 1, "OtherNode.Code")), (error.Code, error.Sheet, error.Row, error.Column));
            Assert.Equal("N2", error.Arguments["value"]);
        }

        [Fact]
        public async Task A_row_the_caller_may_read_but_not_write_fails_the_import_with_the_pipelines_403()
        {
            int fourth = ExcelImportDatabase.FirstWidget + 3;
            (int userId, string subject) = await fixture.SeedCallerAsync(TenantA, (Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, "Code = 'none'"));
            byte[] workbook = await ExportAsync(subject, Widgets, [fourth]);

            _ = await Assert.ThrowsAsync<ForbiddenException>(async () => await ImportAsync(subject, Widgets, new ImportRequest { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Update }));
        }

        [Fact]
        public async Task A_row_changed_after_it_was_read_is_read_again_and_saved_on_one_retry()
        {
            int ninth = ExcelImportDatabase.FirstWidget + 8;
            (int userId, string subject) = await WidgetCallerAsync();
            byte[] workbook = await ExportAsync(subject, Widgets, [ninth]);
            int fileId = await StageAsync(userId, workbook);
            WidgetSaveArm arm = fixture.Probe.Arm(ExcelImportDatabase.CodeOf(ninth), new WidgetSaveArm
            {
                // Another user's edit lands between the import's read of the row and its save.
                Validated = run => run == 1 ? fixture.Connect.ExecuteAsync(TenantA, $"UPDATE [fixture].[Widgets] SET [ModifiedAt] = DATEADD(second, 1, [ModifiedAt]) WHERE [Id] = {ninth};") : Task.CompletedTask,
            });
            using MetricCollector<long> retries = new(fixture.Instance.GetRequiredService<IMeterFactory>(), ExcelTelemetryNames.MeterName, ExcelTelemetryNames.ImportRetries);

            // --- The file's stamp would make the conflict the file's own, refused unretried; ignored, the stamp read is retried ---
            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId, Mode = ImportMode.Update, IgnoreSheetStamps = true });

            Assert.Equal(1, outcome.Updated);
            Assert.Equal(1, retries.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
            Assert.True(arm.Validations >= 2);
        }

        [Fact]
        public async Task A_row_changed_after_the_export_conflicts_at_its_stamp_unless_the_files_stamps_are_ignored()
        {
            int tenth = ExcelImportDatabase.FirstWidget + 9;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] workbook, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [tenth]);
            int fileId = await StageAsync(userId, workbook);
            await fixture.Connect.ExecuteAsync(TenantA, $"UPDATE [fixture].[Widgets] SET [ModifiedAt] = DATEADD(second, 1, [ModifiedAt]) WHERE [Id] = {tenth};");

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId, Mode = ImportMode.Update }));
            ImportError conflict = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportConcurrencyConflict, plan.Sheets[0].Sheet, 2, Letter(plan, 0, "Stamp")), (conflict.Code, conflict.Sheet, conflict.Row, conflict.Column));
            Assert.NotNull(conflict.Arguments["modifiedAt"]);

            ImportOutcome outcome = await ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId, Mode = ImportMode.Update, IgnoreSheetStamps = true });
            Assert.Equal(1, outcome.Updated);
        }

        [Fact]
        public async Task A_file_above_the_synchronous_rows_is_refused_whole_naming_the_background_route()
        {
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] template, ImportPlan plan) = await TemplateAsync(userId, subject, Widgets);
            int rows = ExcelImportDatabase.MaxSynchronousRows + 1;
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [.. Enumerable.Range(1, rows).Select(static row => Widget(-row, $"L-{row}", $"l{row}"))]));

            int fileId = await StageAsync(userId, workbook);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Widgets, new ImportRequest { FileId = fileId }));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportTooLargeForSynchronous, (object?)(long)rows, (object?)(long)ExcelImportDatabase.MaxSynchronousRows), (error.Code, error.Arguments["rows"], error.Arguments["maxRows"]));
            Assert.Contains("import/start", new ResourceManager(CoreStrings.BaseName, Assembly.Load(CoreStrings.Assembly)).GetString("Excel_Import_TooLargeForSynchronous", CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'L-%';"));
        }

        [Fact]
        public async Task A_warning_fails_the_import_as_a_confirmable_error_at_its_row_and_the_confirmed_import_saves()
        {
            int eleventh = ExcelImportDatabase.FirstWidget + 10;
            (int userId, string subject) = await WidgetCallerAsync();
            (byte[] exported, ImportPlan plan) = await ExportPlannedAsync(userId, subject, Widgets, [eleventh]);
            byte[] workbook = WorkbookEdits.WithRows(exported, plan, (plan.Sheets[0].Sheet, [Widget(eleventh, ExcelImportDatabase.CodeOf(eleventh), $"s{eleventh}", ExcelImportDatabase.WarnedName)]));
            ImportRequest request = new() { FileId = await StageAsync(userId, workbook), Mode = ImportMode.Update };

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => ImportAsync(subject, Widgets, request));
            ImportError warning = Assert.Single(refused.Errors);
            Assert.Equal((ExcelImportDatabase.WarningKey, plan.Sheets[0].Sheet, 2), (warning.Code, warning.Sheet, warning.Row));
            Assert.Equal((true, 1), (warning.Arguments[ExcelImports.ConfirmableArgument], warning.Arguments["count"]));

            ImportOutcome outcome = await ImportAsync(subject, Widgets, request, confirmed: [ExcelImportDatabase.WarningKey]);
            Assert.Equal(1, outcome.Updated);
        }

        [Fact]
        public async Task A_tree_inserts_a_new_parent_before_the_child_naming_it_and_looks_up_a_stored_parent()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync(TenantA, (Nodes, AccessActions.Read, null), (Nodes, AccessActions.Save, null));
            (byte[] template, ImportPlan plan) = await TemplateAsync(userId, subject, Nodes);
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (plan.Sheets[0].Sheet, [Row(("Code", "T2"), ("Name", "Tee two"), ("Parent.Code", "T1")), Row(("Code", "T1"), ("Name", "Tee one"), ("Parent.Code", "N1"))]));

            ImportOutcome outcome = await ImportAsync(subject, Nodes, new ImportRequest { FileId = await StageAsync(userId, workbook) });

            Assert.Equal(2, outcome.Inserted);
            Assert.Equal([new RowRange(2, 3)], outcome.CommittedRowRanges);
            Assert.Equal(ExcelImportDatabase.FirstNode, await fixture.Connect.ScalarAsync<int>(TenantA, "SELECT [ParentId] FROM [fixture].[Nodes] WHERE [Code] = 'T1';"));
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM [fixture].[Nodes] c JOIN [fixture].[Nodes] p ON p.[Id] = c.[ParentId] WHERE c.[Code] = 'T2' AND p.[Code] = 'T1' AND c.[IsActive] = 1;"));
        }

        /// <summary>A widget row by path.</summary>
        /// <param name="id">The sheet identity; blank when null.</param>
        /// <param name="code">The code.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="name">The name; the code's when null.</param>
        /// <returns>The row.</returns>
        private static Dictionary<string, object?> Widget(long? id, string code, string subject, string? name = null)
        {
            return Row(("Id", id), ("Code", code), ("Kind", nameof(WidgetKind.Plain)), ("Name", name ?? code), ("Subject", subject));
        }

        /// <summary>A row by path.</summary>
        /// <param name="cells">Each column's path and value.</param>
        /// <returns>The row.</returns>
        private static Dictionary<string, object?> Row(params (string Path, object? Value)[] cells)
        {
            return cells.ToDictionary(static cell => cell.Path, static cell => cell.Value, StringComparer.Ordinal);
        }

        /// <summary>The letters of a plan sheet's column of a path.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="sheet">The sheet's index.</param>
        /// <param name="path">The path.</param>
        /// <returns>The letters.</returns>
        private static string Letter(ImportPlan plan, int sheet, string path)
        {
            return plan.Sheets[sheet].Columns.Single(column => column.Path == path).Column;
        }

        /// <summary>Seeds a caller of the first tenant who may read and save the widgets.</summary>
        /// <returns>The caller's id and subject.</returns>
        private Task<(int UserId, string Subject)> WidgetCallerAsync()
        {
            return fixture.SeedCallerAsync(TenantA, (Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
        }

        /// <summary>A count read from the first tenant.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(string query)
        {
            return fixture.Connect.ScalarAsync<int>(TenantA, query);
        }

        /// <summary>Writes an editable export of rows as a caller of the first tenant, through the action invoker.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <param name="ids">The rows; the template when empty.</param>
        /// <returns>The workbook's bytes.</returns>
        private async Task<byte[]> ExportAsync(string subject, string resource, long[] ids)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: fixture.Instance);
            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(StackAction(scope, resource, "export-for-import"), new ExportForImportRequest { Ids = ids }, Token);
            await using ExportOutcome outcome = Assert.IsType<ExportOutcome>(result);
            using MemoryStream bytes = new();
            await outcome.Workbook.CopyToAsync(bytes, Token);
            return bytes.ToArray();
        }

        /// <summary>Writes an editable export of rows and inspects it, for edits by each column's path.</summary>
        /// <param name="userId">The caller's id.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <param name="ids">The rows; the template when empty.</param>
        /// <returns>The workbook and its plan.</returns>
        private async Task<(byte[] Workbook, ImportPlan Plan)> ExportPlannedAsync(int userId, string subject, string resource, long[] ids)
        {
            byte[] workbook = await ExportAsync(subject, resource, ids);
            int fileId = await StageAsync(userId, workbook);
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: fixture.Instance);
            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(StackAction(scope, resource, "inspect-import"), new InspectImportRequest { FileId = fileId }, Token);
            return (workbook, Assert.IsType<ImportPlan>(result));
        }

        /// <summary>Writes a stack's template and inspects it.</summary>
        /// <param name="userId">The caller's id.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The template and its plan.</returns>
        private Task<(byte[] Workbook, ImportPlan Plan)> TemplateAsync(int userId, string subject, string resource)
        {
            return ExportPlannedAsync(userId, subject, resource, []);
        }

        /// <summary>Imports an upload into a stack through the action invoker, which evaluates the action's securable first.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <param name="request">The request.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="confirmed">The warning keys the caller confirms; none when null.</param>
        /// <returns>The outcome.</returns>
        private async Task<ImportOutcome> ImportAsync(string subject, string resource, ImportRequest request, int tenantId = TenantA, string[]? confirmed = null)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, tenantId: tenantId, services: fixture.Instance);
            if (confirmed is not null)
            {
                scope.ServiceProvider.GetRequiredService<Confirmations>().Seed(confirmed);
            }

            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(StackAction(scope, resource, "import"), request, Token);
            return Assert.IsType<ImportOutcome>(result);
        }

        /// <summary>The registry's own descriptor of one of a stack's API actions.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <param name="name">The action's name.</param>
        /// <returns>The descriptor.</returns>
        private static ApiActionDescriptor StackAction(AsyncServiceScope scope, string resource, string name)
        {
            return scope.ServiceProvider.GetRequiredService<IStackRegistry>().Stacks
                .Single(stack => stack.Resource == resource).Actions
                .OfType<ApiActionDescriptor>()
                .Single(candidate => candidate.Name == name);
        }

        /// <summary>Seeds an import-file blob row as the stage statement writes one, and its bytes in the instance's store.</summary>
        /// <param name="userId">The uploader.</param>
        /// <param name="bytes">The upload's bytes.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The blob's id.</returns>
        private async Task<int> StageAsync(int userId, byte[] bytes, int tenantId = TenantA)
        {
            string storageKey = BlobName.Primary(Import.FileKind, Guid.NewGuid().ToString("N"));
            int id = await fixture.Connect.ScalarAsync<int>(tenantId, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{Import.FileKind}', 'Staged', N'{BlobContentTypes.Xlsx}', N'import.xlsx', {bytes.Length}, N'', SYSUTCDATETIME(), {userId}, NULL, DATEADD(hour, 1, SYSUTCDATETIME()), 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));

            IBlobStore store = fixture.Instance.GetRequiredService<IBlobStore>();
            await store.EnsureTenantAsync(tenantId, Token);
            using MemoryStream content = new(bytes, writable: false);
            await store.WriteAsync(tenantId, [new BlobWrite(storageKey, BlobContentTypes.Xlsx, content, bytes.Length)], Token);
            return id;
        }
    }
}
