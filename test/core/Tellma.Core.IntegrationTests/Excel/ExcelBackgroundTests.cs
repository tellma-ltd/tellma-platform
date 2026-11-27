// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Excel;
using Tellma.Core.IntegrationTests.Crud;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Tests.Excel;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Excel.ExcelJobsDatabase;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>
    ///     The background exports and imports through the jobs' worker: an export's row inserted with
    ///     its committed file and its notice, a denied read, the start routes of a read-only tenant;
    ///     a child surface's export and a start refused before it queues; an import's chunks, its
    ///     checkpoint across a crash, a host stop, a lost lease and a smaller chunk size, a chunk's
    ///     errors, refusals and warnings, a stop requested mid-chunk or while waiting, a changed
    ///     file, the atomic mode, mutually linked new rows, and a background refusal of the
    ///     caller's access.
    /// </summary>
    /// <param name="fixture">The database.</param>
    [Trait("Category", "Integration")]
    public sealed class ExcelBackgroundTests(ExcelJobsDatabase fixture) : IClassFixture<ExcelJobsDatabase>, IAsyncLifetime
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The widget parts' child query surface.</summary>
        private const string Parts = "fixture.WidgetPart";

        /// <summary>The first id the seeded widgets take.</summary>
        private const int FirstWidget = 9001;

        /// <summary>The test's cancellation token.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            return new ValueTask(fixture.ResetAsync());
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task An_export_start_runs_in_the_background_inserting_an_export_row_that_owns_its_committed_file_and_notifies_its_requester()
        {
            await SeedWidgetsAsync(3);
            using MetricCollector<long> exported = Collector(ExcelTelemetryNames.ExportRows);

            // --- The start enqueues a labelled job and no row ---
            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest
            {
                Columns = [new ExportColumn("Code", Header: null), new ExportColumn("Name", Header: null)],
                OrderBy = "Code",
            }));
            Assert.Null(accepted.ResourceId);
            JobRow queued = await fixture.JobAsync(accepted.JobId);
            Assert.Equal((nameof(JobStatus.Pending), ExcelJobs.ExportLabel), (queued.Status, (string?)queued.Columns["LabelKey"]));
            Assert.Contains(Widgets, (string?)queued.Columns["LabelArgumentsJson"], StringComparison.Ordinal);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Exports];"));

            // --- The run: one export row, its file committed with it ---
            PolledJob polled = Assert.Single(await fixture.PollAsync());
            Assert.Equal(accepted.JobId, polled.JobId);
            Ended(polled, JobStatus.Succeeded);
            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Exports]", Invariant($"[JobId] = {accepted.JobId}"));
            Assert.Equal((Widgets, nameof(ExportKind.Display), 3), ((string)row["Resource"]!, (string)row["Kind"]!, (int)row["RowCount"]!));
            int fileId = (int)row["FileId"]!;
            Dictionary<string, object?> blob = await fixture.RowAsync("[core].[Blobs]", Invariant($"[Id] = {fileId}"));
            Assert.Equal((Export.FileKind, nameof(BlobState.Committed), BlobContentTypes.Xlsx), ((string)blob["Kind"]!, (string)blob["State"]!, (string)blob["ContentType"]!));
            Assert.Equal((string)row["FileName"]!, (string)blob["FileName"]!);
            Assert.Equal("PK"u8.ToArray(), (await fixture.BlobBytesAsync(fileId))[..2]);
            JsonElement request = JsonSerializer.Deserialize<JsonElement>((string)row["RequestJson"]!);
            Assert.Equal("Code", request.GetProperty("orderBy").GetString());

            // --- The requester's notice, with the file it links ---
            NotificationRequest notice = Assert.Single(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ExportReadyType);
            Assert.Equal([ImporterId], notice.RecipientIds);
            Assert.Equal((CoreResources.Export, (int)row["Id"]!), (notice.TargetResource, notice.TargetId));
            JsonElement arguments = JsonSerializer.SerializeToElement(notice.Arguments, TellmaJson.Options);
            Assert.Equal((fileId, 3), (arguments.GetProperty("fileId").GetInt32(), arguments.GetProperty("rowCount").GetInt32()));

            // --- The success completed the progress, and the rows were counted as a background export's ---
            Assert.Equal((byte?)100, (await fixture.JobAsync(accepted.JobId)).ProgressPercent);
            CollectedMeasurement<long> rows = Assert.Single(exported.GetMeasurementSnapshot());
            Assert.Equal((3L, true), (rows.Value, (bool)rows.Tags[ExcelTelemetryNames.BackgroundTag]!));
        }

        [Fact]
        public async Task An_export_for_import_start_writes_the_editable_shape_of_the_rows_it_names()
        {
            await SeedWidgetsAsync(3);

            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Widgets, "export-for-import/start", new ExportForImportRequest { Ids = [FirstWidget, FirstWidget + 2] }));
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);

            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Exports]", Invariant($"[JobId] = {accepted.JobId}"));
            Assert.Equal((nameof(ExportKind.ForImport), 2), ((string)row["Kind"]!, (int)row["RowCount"]!));

            // The workbook is one the import reads back: the plan finds the parent sheet's two rows.
            byte[] workbook = await fixture.BlobBytesAsync((int)row["FileId"]!);
            ImportPlan plan = Assert.IsType<ImportPlan>(await InvokeAsync(ImporterId, Widgets, "inspect-import", new InspectImportRequest { FileId = await fixture.StageAsync(ImporterId, workbook) }));
            Assert.Equal(2, plan.Sheets[0].Rows);
        }

        [Fact]
        public async Task A_background_export_whose_requester_lost_the_read_fails_with_the_denial_and_writes_no_row()
        {
            await SeedWidgetsAsync(1);
            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(RevokedId, Widgets, "export/start", new ExportRequest { Columns = [new ExportColumn("Code", Header: null)] }));

            // --- The read is revoked while the job waits ---
            await fixture.RevokeAsync(RevokedId);
            PolledJob polled = Assert.Single(await fixture.PollAsync());

            Ended(polled, JobStatus.Failed, "forbidden");
            JobRow failed = await fixture.JobAsync(accepted.JobId);
            Assert.Equal(1, failed.Attempts);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Exports];"));
            Assert.DoesNotContain(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ExportReadyType);
        }

        [Fact]
        public async Task A_read_only_tenant_starts_a_background_export_and_refuses_a_background_import()
        {
            await SeedWidgetsAsync(1);
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "RO-1")]));
            int fileId = await fixture.StageAsync(ImporterId, workbook);

            JobAccepted export = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest { Columns = [new ExportColumn("Code", Header: null)] }, TenantState.ReadOnly));
            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => InvokeAsync(ImporterId, Widgets, "import/start", new StartImportRequest { FileId = fileId }, TenantState.ReadOnly));

            Assert.Equal(nameof(JobStatus.Pending), (await fixture.JobAsync(export.JobId)).Status);
            Assert.Equal(TenantProblemCodes.ReadOnly, refused.Code);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Imports];"));

            // --- The worker runs the export on the read-only tenant: its row and file persist through the job frame ---
            fixture.Tenant.State = TenantState.ReadOnly;
            try
            {
                Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);
            }
            finally
            {
                fixture.Tenant.State = TenantState.Active;
            }

            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Exports]", Invariant($"[JobId] = {export.JobId}"));
            Assert.Equal(1, (int)row["RowCount"]!);
            Assert.Equal(nameof(BlobState.Committed), await fixture.TextAsync(Invariant($"SELECT [State] FROM [core].[Blobs] WHERE [Id] = {(int)row["FileId"]!};")));
            _ = Assert.Single(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ExportReadyType);
        }

        [Fact]
        public async Task A_chunked_import_commits_each_chunk_with_its_checkpoint_records_the_outcome_on_its_row_and_notifies_its_requester()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (plan.Sheets[0].Sheet, [Widget(-1, "C-1"), Widget(-2, "C-2"), Widget(-3, "C-3"), Widget(-4, "C-4"), Widget(-5, "C-5")]),
                (plan.Sheets[1].Sheet, [Row(("Widget.Id", -1), ("Id", -10), ("Ordinal", 1)), Row(("Widget.Id", -5), ("Id", -50), ("Ordinal", 1))]));

            // --- The start inserts the row, which owns the upload, with its job ---
            using MetricCollector<long> imported = Collector(ExcelTelemetryNames.ImportRows);
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            Dictionary<string, object?> started = await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"));
            Assert.Equal((accepted.JobId, null), ((int)started["JobId"]!, started["ResultJson"]));
            Assert.Equal(nameof(BlobState.Committed), await fixture.TextAsync(Invariant($"SELECT [State] FROM [core].[Blobs] WHERE [Id] = {(int)started["FileId"]!};")));
            Assert.Equal(ExcelJobs.ImportLabel, (string?)(await fixture.JobAsync(accepted.JobId)).Columns["LabelKey"]);

            PolledJob polled = Assert.Single(await fixture.PollAsync());

            // --- Three chunks of two, two and one parent rows, each committed with its checkpoint ---
            Ended(polled, JobStatus.Succeeded);
            Assert.Equal([2, 4, 5], Committed().Select(static record => int.Parse(record.GetStructuredStateValue("NextRow")!, CultureInfo.InvariantCulture)));
            ImportCheckpointState checkpoint = State(await fixture.JobAsync(accepted.JobId));
            Assert.Equal((5, 5, 0, 2), (checkpoint.NextRow, checkpoint.Inserted, checkpoint.Updated, checkpoint.ChildrenInserted));
            Assert.Empty(checkpoint.CommittedNewIds);

            // --- The outcome on the row, and the requester's notice ---
            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"));
            Assert.Equal((5, 0), ((int)row["RowCount"]!, (int)row["ErrorCount"]!));
            ImportOutcome outcome = Outcome(row);
            Assert.Equal((5, 0, 2, false), (outcome.Inserted, outcome.Updated, outcome.ChildrenInserted, outcome.Stopped));
            Assert.Equal([new RowRange(2, 6)], outcome.CommittedRowRanges);
            Assert.Equal(5, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'C-%';"));
            NotificationRequest notice = Assert.Single(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ImportCompletedType);
            Assert.Equal((CoreResources.Import, accepted.ResourceId), (notice.TargetResource, notice.TargetId));
            JsonElement arguments = JsonSerializer.SerializeToElement(notice.Arguments, TellmaJson.Options);
            Assert.Equal(("import.xlsx", 5, 0), (arguments.GetProperty("fileName").GetString(), arguments.GetProperty("rowCount").GetInt32(), arguments.GetProperty("errorCount").GetInt32()));
            CollectedMeasurement<long> rows = Assert.Single(imported.GetMeasurementSnapshot());
            Assert.Equal((5L, true), (rows.Value, (bool)rows.Tags[ExcelTelemetryNames.BackgroundTag]!));
        }

        [Fact]
        public async Task A_node_named_by_negative_id_in_an_earlier_chunk_binds_through_the_checkpoint_after_a_crash_without_saving_a_row_twice()
        {
            fixture.Options.ImportChunkRows = 1;
            (byte[] template, ImportPlan plan) = await TemplateAsync(Nodes);
            string parent = plan.Sheets[0].Columns.Single(static column => column.Path == "Parent.Code").Column;
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Node(-1, "NA", null), Node(-2, "NB", -1L), Node(-3, "NC", -2L)]));
            StartImportRequest request = new() { ColumnMappings = [new ImportColumnMapping(plan.Sheets[0].Sheet, parent, "Parent.Id")] };

            using MetricCollector<long> chunks = Collector(ExcelTelemetryNames.ImportChunks);
            using MetricCollector<long> resumes = Collector(ExcelTelemetryNames.ImportResumed);

            // --- The second chunk's first save crashes the attempt ---
            int crashes = 0;
            fixture.NodeHook.Validating = nodes => nodes.Any(static node => node.Code == "NB") && Interlocked.Increment(ref crashes) == 1
                ? throw new InvalidOperationException("The host crashed.")
                : Task.CompletedTask;
            JobAccepted accepted = await StartImportAsync(workbook, request, Nodes);
            _ = Assert.Single(await fixture.PollAsync());

            JobRow retried = await fixture.JobAsync(accepted.JobId);
            Assert.Equal((nameof(JobStatus.Pending), 1), (retried.Status, retried.Attempts));
            ImportCheckpointState checkpoint = State(retried);
            int a = await fixture.CountAsync("SELECT [Id] FROM [fixture].[Nodes] WHERE [Code] = 'NA';");
            Assert.Equal((1, 1), (checkpoint.NextRow, checkpoint.Inserted));
            Assert.Equal(new Dictionary<long, long> { [-1] = a }, checkpoint.CommittedNewIds);

            // --- The next attempt resumes at the second row and binds NB to NA through the checkpoint ---
            await fixture.DueNowAsync(accepted.JobId);
            PolledJob resumed = Assert.Single(await fixture.PollAsync());

            Ended(resumed, JobStatus.Succeeded);
            Assert.Equal(3, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Nodes];"));
            Assert.Equal(a, await fixture.CountAsync("SELECT [ParentId] FROM [fixture].[Nodes] WHERE [Code] = 'NB';"));
            Assert.Equal(1, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Nodes] c JOIN [fixture].[Nodes] b ON b.[Id] = c.[ParentId] WHERE c.[Code] = 'NC' AND b.[Code] = 'NB';"));
            ImportOutcome outcome = Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")));
            Assert.Equal(3, outcome.Inserted);
            Assert.Equal([new RowRange(2, 4)], outcome.CommittedRowRanges);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "ImportResumed" && record.GetStructuredStateValue("NextRow") == "1");

            // --- Three chunks across the two attempts, counted once as the import ended; one resume ---
            Assert.Equal(3L, Assert.Single(chunks.GetMeasurementSnapshot()).Value);
            Assert.Equal(1L, Assert.Single(resumes.GetMeasurementSnapshot()).Value);
        }

        [Fact]
        public async Task A_node_named_by_code_in_an_earlier_chunk_is_found_by_the_later_chunks_lookup()
        {
            fixture.Options.ImportChunkRows = 1;
            (byte[] template, ImportPlan plan) = await TemplateAsync(Nodes);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Node(null, "KC", "KB"), Node(null, "KB", "KA"), Node(null, "KA", null)]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest(), Nodes);
            PolledJob polled = Assert.Single(await fixture.PollAsync());

            // The order saves KA, then KB, then KC, each in its own chunk.
            Ended(polled, JobStatus.Succeeded);
            Assert.Equal(3, Committed().Count);
            Assert.Equal(1, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Nodes] c JOIN [fixture].[Nodes] b ON b.[Id] = c.[ParentId] JOIN [fixture].[Nodes] a ON a.[Id] = b.[ParentId] WHERE c.[Code] = 'KC' AND b.[Code] = 'KB' AND a.[Code] = 'KA';"));
            Assert.Empty(State(await fixture.JobAsync(accepted.JobId)).CommittedNewIds);
        }

        [Fact]
        public async Task A_chunks_errors_stop_the_import_keeping_the_earlier_chunks_and_reporting_their_rows()
        {
            await SeedWidgetsAsync(1);
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            string taken = CodeOf(FirstWidget);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "E-1"), Widget(null, "E-2"), Widget(null, taken, subject: "fresh"), Widget(null, "E-4")]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            PolledJob polled = Assert.Single(await fixture.PollAsync());

            // --- The job failed; the first chunk stays ---
            Ended(polled, JobStatus.Failed, ExcelErrorCodes.ImportFailed);
            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] IN ('E-1', 'E-2');"));
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'E-4';"));

            // --- The errors at the second chunk's row, beside the first chunk's rows ---
            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"));
            using var result = JsonDocument.Parse((string)row["ResultJson"]!);
            JsonElement error = Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal((plan.Sheets[0].Sheet, 4), (error.GetProperty("sheet").GetString(), error.GetProperty("row").GetInt32()));
            Assert.Equal(1, result.RootElement.GetProperty("totalErrors").GetInt32());
            Assert.Equal(
                [(2, 3)],
                result.RootElement.GetProperty("committedRowRanges").EnumerateArray().Select(static range => (range.GetProperty("from").GetInt32(), range.GetProperty("to").GetInt32())));
            Assert.Equal((4, 1), ((int)row["RowCount"]!, (int)row["ErrorCount"]!));
            Assert.DoesNotContain(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ImportCompletedType);
        }

        [Fact]
        public async Task A_warning_in_the_second_chunk_leaves_the_job_waiting_at_its_row_and_its_confirmation_resumes_the_import()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "P-1"), Widget(null, "P-2"), Widget(null, "P-3", name: WarnedName), Widget(null, "P-4")]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            PolledJob waiting = Assert.Single(await fixture.PollAsync());

            // --- The second chunk rolled back; the job waits on its warning, placed at its row ---
            Ended(waiting, JobStatus.PendingConfirmation);
            JobRow pending = await fixture.JobAsync(accepted.JobId);
            using (var confirmations = JsonDocument.Parse(pending.PendingConfirmationsJson!))
            {
                string text = confirmations.RootElement.GetRawText();
                Assert.Contains(WarningKey, text, StringComparison.Ordinal);
                Assert.Contains("\"row\":4", text, StringComparison.Ordinal);
                Assert.Contains(plan.Sheets[0].Sheet, text, StringComparison.Ordinal);
            }

            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'P-%';"));
            Assert.Null((await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]);

            // --- The initiator confirms: the import continues at the second chunk ---
            await fixture.SetJobAsync(accepted.JobId, Invariant($"[Status] = 'Pending', [ConfirmedKeys] = N'{WarningKey}', [PendingConfirmationsJson] = NULL, [Attempts] = 0, [DueAt] = DATEADD(second, -1, SYSUTCDATETIME())"));
            PolledJob confirmed = Assert.Single(await fixture.PollAsync());

            Ended(confirmed, JobStatus.Succeeded);
            Assert.Equal(4, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'P-%';"));
            Assert.Equal(4, Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"))).Inserted);
        }

        [Fact]
        public async Task A_cancel_of_a_retried_import_records_the_committed_chunks_as_a_stopped_outcome()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "S-1"), Widget(null, "S-2"), Widget(null, "S-3"), Widget(null, "S-4")]));
            _ = fixture.Probe.Arm("S-3", new WidgetSaveArm { Validated = static _ => throw new InvalidOperationException("The host crashed.") });

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            _ = Assert.Single(await fixture.PollAsync());
            Assert.Equal(nameof(JobStatus.Pending), (await fixture.JobAsync(accepted.JobId)).Status);

            // --- The requester cancels while the job waits for its retry ---
            await fixture.SetJobAsync(accepted.JobId, "[CancelRequestedAt] = SYSUTCDATETIME(), [DueAt] = DATEADD(second, -1, SYSUTCDATETIME())");
            PolledJob stopped = Assert.Single(await fixture.PollAsync());

            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), (stopped.Status, stopped.ErrorCode));
            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"));
            ImportOutcome outcome = Outcome(row);
            Assert.Equal((2, true), (outcome.Inserted, outcome.Stopped));
            Assert.Equal([new RowRange(2, 3)], outcome.CommittedRowRanges);
            Assert.Equal(0, (int)row["ErrorCount"]!);
            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'S-%';"));
            Assert.DoesNotContain(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ImportCompletedType);
        }

        [Fact]
        public async Task A_host_stop_during_a_chunk_rolls_it_back_and_the_next_attempt_resumes_before_it()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "H-1"), Widget(null, "H-2"), Widget(null, "H-3"), Widget(null, "H-4")]));
            using var host = CancellationTokenSource.CreateLinkedTokenSource(Token);
            _ = fixture.Probe.Arm("H-3", new WidgetSaveArm { Validated = run => run == 1 ? host.CancelAsync() : Task.CompletedTask });

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            _ = await fixture.PollStoppingAsync(host.Token);

            // --- The stop sent the job back with the first chunk committed and the second rolled back ---
            JobRow stopped = await fixture.JobAsync(accepted.JobId);
            Assert.Equal(nameof(JobStatus.Pending), stopped.Status);
            Assert.Equal(2, State(stopped).NextRow);
            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'H-%';"));

            await fixture.DueNowAsync(accepted.JobId);
            PolledJob resumed = Assert.Single(await fixture.PollAsync());

            Ended(resumed, JobStatus.Succeeded);
            Assert.Equal(4, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'H-%';"));
            Assert.Equal(4, Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"))).Inserted);
        }

        [Fact]
        public async Task An_import_interrupted_on_every_attempt_fails_once_its_attempts_are_spent_keeping_what_it_committed()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "X-1"), Widget(null, "X-2"), Widget(null, "X-3")]));

            // --- Each attempt's host stops while the second chunk saves ---
            CancellationTokenSource? host = null;
            _ = fixture.Probe.Arm("X-3", new WidgetSaveArm { Validated = _ => host!.CancelAsync() });
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
                host = stopping;
                await fixture.DueNowAsync(accepted.JobId);
                _ = Assert.Single(await fixture.PollStoppingAsync(stopping.Token));
            }

            // --- The third interruption spent the attempts: failed as exhausted, the first chunk kept ---
            JobRow failed = await fixture.JobAsync(accepted.JobId);
            Assert.Equal((nameof(JobStatus.Failed), 3, JobErrorCodes.AttemptsExhausted), (failed.Status, failed.Attempts, failed.ErrorCode));
            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'X-%';"));
            Assert.Null((await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]);
        }

        [Fact]
        public async Task A_resumed_import_whose_file_no_longer_orders_its_rows_as_its_checkpoint_did_fails_without_saving()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "M-1"), Widget(null, "M-2"), Widget(null, "M-3")]));
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            ImportCheckpointState foreign = new(1, "not-this-order", 1, 0, 0, 0, 0, [new RowRange(2, 2)], new Dictionary<long, long>());
            await fixture.SetJobAsync(accepted.JobId, $"[StateJson] = {JobRunDatabase.Literal(JsonSerializer.Serialize(foreign, TellmaJson.Options))}");

            PolledJob polled = Assert.Single(await fixture.PollAsync());

            Ended(polled, JobStatus.Failed, ExcelErrorCodes.ImportFailed);
            using var result = JsonDocument.Parse((string)(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]!);
            JsonElement error = Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal((ExcelErrorCodes.ImportResumeMismatch, "not-this-order"), (error.GetProperty("code").GetString(), error.GetProperty("arguments").GetProperty("expected").GetString()));
            Assert.Equal(2, result.RootElement.GetProperty("committedRowRanges")[0].GetProperty("from").GetInt32());
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'M-%';"));
        }

        [Fact]
        public async Task An_atomic_import_saves_the_whole_file_in_one_transaction_and_one_above_its_ceiling_saves_nothing()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "A-1"), Widget(null, "A-2"), Widget(null, "A-3"), Widget(null, "A-4"), Widget(null, "A-5")]));

            // --- Five rows, though a chunk holds two: one chunk ---
            _ = await StartImportAsync(workbook, new StartImportRequest { Atomic = true });
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);
            Assert.Single(Committed());
            Assert.Equal(5, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'A-%';"));

            // --- Above the atomic ceiling: refused before any chunk ---
            fixture.Options.MaxAtomicImportRows = 4;
            byte[] larger = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "B-1"), Widget(null, "B-2"), Widget(null, "B-3"), Widget(null, "B-4"), Widget(null, "B-5")]));
            JobAccepted refused = await StartImportAsync(larger, new StartImportRequest { Atomic = true });
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Failed, ExcelErrorCodes.ImportFailed);

            using var result = JsonDocument.Parse((string)(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {refused.ResourceId}")))["ResultJson"]!);
            JsonElement error = Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray());
            // The counts are 64-bit, so they travel as strings.
            Assert.Equal((ExcelErrorCodes.ImportTooLargeForAtomic, "5", "4"), (error.GetProperty("code").GetString(), error.GetProperty("arguments").GetProperty("rows").GetString(), error.GetProperty("arguments").GetProperty("maxRows").GetString()));
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'B-%';"));
        }

        [Fact]
        public async Task A_row_the_caller_may_not_save_stops_the_background_import_with_one_error_placed_nowhere()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "OK-1"), Widget(null, "NO-1")]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest(), user: FilteredId);
            PolledJob polled = Assert.Single(await fixture.PollAsync());

            Ended(polled, JobStatus.Failed, ExcelErrorCodes.ImportFailed);
            using var result = JsonDocument.Parse((string)(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]!);
            JsonElement error = Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("forbidden", error.GetProperty("code").GetString());
            Assert.False(error.TryGetProperty("sheet", out _) || error.TryGetProperty("row", out _) || error.TryGetProperty("column", out _));
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] IN ('OK-1', 'NO-1');"));
        }

        [Fact]
        public async Task A_row_refused_in_the_second_chunk_rolls_back_its_checkpoint_and_reports_only_the_first_chunks_rows()
        {
            fixture.Options.ImportChunkRows = 1;
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "OK-1"), Widget(null, "NO-1")]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest(), user: FilteredId);
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Failed, ExcelErrorCodes.ImportFailed);

            // --- The first chunk stays with its checkpoint; the second's checkpoint went with its rows ---
            Assert.Equal(1, State(await fixture.JobAsync(accepted.JobId)).NextRow);
            Assert.Equal((1, 0), (await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'OK-1';"), await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'NO-1';")));
            using var result = JsonDocument.Parse((string)(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]!);
            Assert.Equal("forbidden", Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray()).GetProperty("code").GetString());
            Assert.Equal(
                [(2, 2)],
                result.RootElement.GetProperty("committedRowRanges").EnumerateArray().Select(static range => (range.GetProperty("from").GetInt32(), range.GetProperty("to").GetInt32())));
        }

        [Fact]
        public async Task A_cancel_while_the_third_chunk_saves_lets_it_commit_then_records_the_three_chunks_as_a_stopped_outcome()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [.. Enumerable.Range(1, 8).Select(static index => Widget(null, Invariant($"K-{index}")))]));
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            using MetricCollector<long> chunks = Collector(ExcelTelemetryNames.ImportChunks);

            // --- The requester cancels while the third chunk validates, and the handler reads it at once ---
            _ = fixture.Probe.Arm("K-5", new WidgetSaveArm
            {
                Validated = async run =>
                {
                    if (run == 1)
                    {
                        await fixture.SetJobAsync(accepted.JobId, "[CancelRequestedAt] = SYSUTCDATETIME()");
                        await fixture.RenewAsync();
                    }
                },
            });
            PolledJob stopped = Assert.Single(await fixture.PollAsync());

            // --- The third chunk committed; the fourth never ran ---
            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), (stopped.Status, stopped.ErrorCode));
            Assert.Equal([2, 4, 6], Committed().Select(static record => int.Parse(record.GetStructuredStateValue("NextRow")!, CultureInfo.InvariantCulture)));
            Assert.Equal(6, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'K-%';"));
            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"));
            ImportOutcome outcome = Outcome(row);
            Assert.Equal((6, true), (outcome.Inserted, outcome.Stopped));
            Assert.Equal([new RowRange(2, 7)], outcome.CommittedRowRanges);
            Assert.Equal(8, (int)row["RowCount"]!);
            Assert.DoesNotContain(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ImportCompletedType);
            Assert.Equal(3L, Assert.Single(chunks.GetMeasurementSnapshot()).Value);
        }

        [Fact]
        public async Task A_cancel_of_an_import_waiting_on_a_warning_records_the_committed_chunks_as_a_stopped_outcome()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "Q-1"), Widget(null, "Q-2"), Widget(null, "Q-3", name: WarnedName), Widget(null, "Q-4")]));
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.PendingConfirmation);

            // --- The cancel queues the waiting job flagged, as the jobs' cancel action does, so its claim records the stop ---
            await fixture.SetJobAsync(accepted.JobId, "[Status] = 'Pending', [DueAt] = SYSUTCDATETIME(), [CancelRequestedAt] = SYSUTCDATETIME(), [PendingConfirmationsJson] = NULL");
            PolledJob stopped = Assert.Single(await fixture.PollAsync());

            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), (stopped.Status, stopped.ErrorCode));
            ImportOutcome outcome = Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")));
            Assert.Equal((2, true), (outcome.Inserted, outcome.Stopped));
            Assert.Equal([new RowRange(2, 3)], outcome.CommittedRowRanges);
            Assert.Equal(2, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'Q-%';"));
        }

        [Fact]
        public async Task A_cancel_landing_in_a_chunk_that_then_warns_records_the_earlier_chunks_as_a_stopped_outcome()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "CW-1"), Widget(null, "CW-2"), Widget(null, "CW-3", name: WarnedName), Widget(null, "CW-4")]));
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());

            // --- The requester cancels while the second chunk validates; the chunk then asks for confirmation ---
            _ = fixture.Probe.Arm("CW-3", new WidgetSaveArm
            {
                Validated = async run =>
                {
                    if (run == 1)
                    {
                        await fixture.SetJobAsync(accepted.JobId, "[CancelRequestedAt] = SYSUTCDATETIME()");
                        await fixture.RenewAsync();
                    }
                },
            });
            PolledJob stopped = Assert.Single(await fixture.PollAsync());

            // --- The stop wins over the wait: cancelled, with the first chunk recorded ---
            Assert.Equal((JobStatus.Cancelled, JobErrorCodes.Cancelled), (stopped.Status, stopped.ErrorCode));
            ImportOutcome outcome = Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")));
            Assert.Equal((2, true), (outcome.Inserted, outcome.Stopped));
            Assert.Equal([new RowRange(2, 3)], outcome.CommittedRowRanges);
            Assert.Null((await fixture.JobAsync(accepted.JobId)).PendingConfirmationsJson);
        }

        [Fact]
        public async Task A_lease_lost_during_the_third_chunk_rolls_it_back_with_its_checkpoint_and_the_next_claim_resumes_before_it()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [.. Enumerable.Range(1, 6).Select(static index => Widget(null, Invariant($"L-{index}")))]));
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());

            // --- Another worker takes the lease while the third chunk validates ---
            _ = fixture.Probe.Arm("L-5", new WidgetSaveArm { Validated = run => run == 1 ? fixture.SetJobAsync(accepted.JobId, "[LeaseToken] = NEWID()") : Task.CompletedTask });
            PolledJob lost = Assert.Single(await fixture.PollAsync());

            // --- The fence refused the chunk: the attempt was taken back, not failed, and recorded nothing ---
            Assert.Equal(PolledJobEnd.LostLease, lost.End);
            JobRow taken = await fixture.JobAsync(accepted.JobId);
            Assert.Equal((nameof(JobStatus.Running), 4), (taken.Status, State(taken).NextRow));
            Assert.Equal(4, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'L-%';"));
            Assert.Null((await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")))["ResultJson"]);
            Assert.DoesNotContain(fixture.Log.GetSnapshot(), static record => record.Id.Name == "ImportFailed");

            // --- The lease runs out and the next claim resumes at the third chunk ---
            await fixture.SetJobAsync(accepted.JobId, "[LeaseExpiresAt] = DATEADD(second, -1, SYSUTCDATETIME()), [DueAt] = DATEADD(second, -1, SYSUTCDATETIME())");
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);
            Assert.Equal(6, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'L-%';"));
            Assert.Equal(6, Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}"))).Inserted);
            Assert.Contains(fixture.Log.GetSnapshot(), static record => record.Id.Name == "ImportResumed" && record.GetStructuredStateValue("NextRow") == "4");
        }

        [Fact]
        public async Task A_resume_under_a_smaller_chunk_size_commits_every_remaining_row_once()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [.. Enumerable.Range(1, 5).Select(static index => Widget(null, Invariant($"R-{index}")))]));
            _ = fixture.Probe.Arm("R-3", new WidgetSaveArm { Validated = static run => run == 1 ? throw new InvalidOperationException("The host crashed.") : Task.CompletedTask });
            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest());
            _ = Assert.Single(await fixture.PollAsync());
            Assert.Equal(2, State(await fixture.JobAsync(accepted.JobId)).NextRow);

            // --- The next attempt runs under chunks of one row ---
            fixture.Options.ImportChunkRows = 1;
            await fixture.DueNowAsync(accepted.JobId);
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);

            Assert.Equal([2, 3, 4, 5], Committed().Select(static record => int.Parse(record.GetStructuredStateValue("NextRow")!, CultureInfo.InvariantCulture)));
            Assert.Equal(5, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'R-%';"));
            ImportOutcome outcome = Outcome(await fixture.RowAsync("[core].[Imports]", Invariant($"[Id] = {accepted.ResourceId}")));
            Assert.Equal(5, outcome.Inserted);
            Assert.Equal([new RowRange(2, 6)], outcome.CommittedRowRanges);
        }

        [Fact]
        public async Task An_atomic_import_that_hits_a_warning_commits_nothing_and_its_confirmation_restarts_it_from_the_first_row()
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Widgets);
            byte[] workbook = WorkbookEdits.WithRows(template, plan, (plan.Sheets[0].Sheet, [Widget(null, "AW-1"), Widget(null, "AW-2"), Widget(null, "AW-3", name: WarnedName)]));

            JobAccepted accepted = await StartImportAsync(workbook, new StartImportRequest { Atomic = true });
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.PendingConfirmation);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'AW-%';"));
            Assert.Null((await fixture.JobAsync(accepted.JobId)).StateJson);

            await fixture.SetJobAsync(accepted.JobId, Invariant($"[Status] = 'Pending', [ConfirmedKeys] = N'{WarningKey}', [PendingConfirmationsJson] = NULL, [Attempts] = 0, [DueAt] = DATEADD(second, -1, SYSUTCDATETIME())"));
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);

            Assert.Single(Committed());
            Assert.Equal(3, await fixture.CountAsync("SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] LIKE 'AW-%';"));
            Assert.DoesNotContain(fixture.Log.GetSnapshot(), static record => record.Id.Name == "ImportResumed");
        }

        [Fact]
        public async Task Mutually_linked_new_nodes_import_synchronously_and_in_one_background_chunk()
        {
            (byte[] workbook, ImportColumnMapping[] mappings) = await LinkedNodesAsync("LA", "LB");
            object? result = await InvokeAsync(ImporterId, Nodes, "import", new ImportRequest { FileId = await fixture.StageAsync(ImporterId, workbook), ColumnMappings = mappings });
            Assert.Equal((2, 2), (Assert.IsType<ImportOutcome>(result).Inserted, ((ImportOutcome)result).ChildrenInserted));
            Assert.Equal(2, await LinkedAsync("LA", "LB"));

            // --- In the background, under chunks of one row, the pair stays in one chunk ---
            fixture.Options.ImportChunkRows = 1;
            (byte[] background, ImportColumnMapping[] backgroundMappings) = await LinkedNodesAsync("LC", "LD");
            JobAccepted accepted = await StartImportAsync(background, new StartImportRequest { ColumnMappings = backgroundMappings }, Nodes);
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);

            Assert.Equal("2", Assert.Single(Committed()).GetStructuredStateValue("NextRow"));
            Assert.Equal(2, await LinkedAsync("LC", "LD"));
            Assert.Empty(State(await fixture.JobAsync(accepted.JobId)).CommittedNewIds);
        }

        [Fact]
        public async Task A_child_surface_export_start_runs_in_the_background_inserting_an_export_row_of_the_child()
        {
            await SeedWidgetsAsync(1);
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"INSERT INTO [fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes]) VALUES (1, {FirstWidget}, 1, N'a'), (2, {FirstWidget}, 2, N'b');"));

            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Parts, "export/start", new ExportRequest { Columns = [new ExportColumn("Ordinal", Header: null)], OrderBy = "Ordinal" }));
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Succeeded);

            Dictionary<string, object?> row = await fixture.RowAsync("[core].[Exports]", Invariant($"[JobId] = {accepted.JobId}"));
            Assert.Equal((Parts, nameof(ExportKind.Display), 2), ((string)row["Resource"]!, (string)row["Kind"]!, (int)row["RowCount"]!));
        }

        [Fact]
        public async Task An_export_start_whose_request_does_not_plan_is_refused_before_any_job_is_queued()
        {
            _ = await Assert.ThrowsAsync<InvalidQueryException>(() => InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest { Columns = [new ExportColumn("Code +", Header: null)] }));
            InvalidQueryException unknown = await Assert.ThrowsAsync<InvalidQueryException>(
                () => InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest { Columns = [new ExportColumn("Code", Header: null)], NamedFilters = ["NoSuchFilter"] }));
            Assert.Equal(ValidationCodes.QueryUnknownNamedFilter, Assert.Single(unknown.Diagnostics).Code);
            _ = await Assert.ThrowsAsync<BadRequestException>(() => InvokeAsync(ImporterId, Widgets, "export-for-import/start", new ExportForImportRequest { Ids = [], Columns = ["NoSuchColumn"] }));

            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_background_export_whose_filter_does_not_compile_fails_once_with_the_diagnostics_code()
        {
            await SeedWidgetsAsync(1);
            ExportRequest request = new() { Columns = [new ExportColumn("Code", Header: null)], Filters = ["Code = = 'X'"] };

            // --- The synchronous route refuses the filter; the start cannot, since filters compile only in the run ---
            InvalidQueryException synchronous = await Assert.ThrowsAsync<InvalidQueryException>(() => InvokeAsync(ImporterId, Widgets, "export", request));
            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Widgets, "export/start", request));

            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Failed, synchronous.Diagnostics[0].Code);
            Assert.Equal(1, (await fixture.JobAsync(accepted.JobId)).Attempts);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Exports];"));
        }

        [Fact]
        public async Task An_export_start_above_its_search_or_ids_ceiling_is_refused_before_any_job_is_queued()
        {
            LimitExceededException search = await Assert.ThrowsAsync<LimitExceededException>(
                () => InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest { Columns = [new ExportColumn("Code", Header: null)], Search = new string('a', 5_000) }));
            LimitExceededException ids = await Assert.ThrowsAsync<LimitExceededException>(
                () => InvokeAsync(ImporterId, Widgets, "export-for-import/start", new ExportForImportRequest { Ids = [.. Enumerable.Range(1, 50_000).Select(static id => (long)id)] }));
            LimitExceededException childSearch = await Assert.ThrowsAsync<LimitExceededException>(
                () => InvokeAsync(ImporterId, Parts, "export/start", new ExportRequest { Columns = [new ExportColumn("Ordinal", Header: null)], Search = new string('a', 5_000) }));

            Assert.Equal((LimitNames.MaxSearchLength, LimitNames.MaxIds, LimitNames.MaxSearchLength), (search.Limit, ids.Limit, childSearch.Limit));
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_background_export_above_the_file_kinds_size_fails_once_as_too_large_and_writes_no_row()
        {
            // --- Rows of text that compresses poorly, so the workbook passes the narrowed size ---
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"""
                WITH [n] AS (SELECT TOP (2000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [i] FROM sys.all_objects AS [a] CROSS JOIN sys.all_objects AS [b])
                INSERT INTO [fixture].[Widgets] ([Id], [Code], [Kind], [Name], [Subject], [Price], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                SELECT 20000 + [i], CONCAT('T', [i]), 'Plain', CONCAT(NEWID(), NEWID(), NEWID(), NEWID(), NEWID(), NEWID()), CONCAT('t', [i]), 1, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId} FROM [n];
                """));

            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(ImporterId, Widgets, "export/start", new ExportRequest
            {
                Columns = [new ExportColumn("Code", Header: null), new ExportColumn("Name", Header: null)],
                OrderBy = "Code",
            }));
            Ended(Assert.Single(await fixture.PollAsync()), JobStatus.Failed, BlobRejectedException.TooLarge);

            Assert.Equal(1, (await fixture.JobAsync(accepted.JobId)).Attempts);
            Assert.Equal(0, await fixture.CountAsync("SELECT COUNT(*) FROM [core].[Exports];"));
            Assert.DoesNotContain(fixture.Notices.Raised, static notice => notice.Type == ExcelJobs.ExportReadyType);
        }

        /// <summary>Listens to one of the codec's instruments from now on.</summary>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector, which the caller disposes.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), ExcelTelemetryNames.MeterName, instrument);
        }

        /// <summary>A node workbook of two new nodes whose link rows name each other by sheet identity.</summary>
        /// <param name="first">The first node's code.</param>
        /// <param name="second">The second node's code.</param>
        /// <returns>The workbook, and the mapping that reads the link sheet's reference as a sheet identity.</returns>
        private async Task<(byte[] Workbook, ImportColumnMapping[] Mappings)> LinkedNodesAsync(string first, string second)
        {
            (byte[] template, ImportPlan plan) = await TemplateAsync(Nodes);
            ImportPlanSheet links = plan.Sheets.Single(static sheet => sheet.Collection == "Links");
            string other = links.Columns.Single(static column => column.Path == "OtherNode.Code").Column;
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (plan.Sheets[0].Sheet, [Node(-1, first, null), Node(-2, second, null)]),
                (links.Sheet, [Row(("Node.Id", -1L), ("OtherNode.Code", -2L)), Row(("Node.Id", -2L), ("OtherNode.Code", -1L))]));
            return (workbook, [new ImportColumnMapping(links.Sheet, other, "OtherNode.Id")]);
        }

        /// <summary>Counts the stored links between two nodes, in either direction.</summary>
        /// <param name="first">The first node's code.</param>
        /// <param name="second">The second node's code.</param>
        /// <returns>The count.</returns>
        private Task<int> LinkedAsync(string first, string second)
        {
            return fixture.CountAsync(Invariant($"SELECT COUNT(*) FROM [fixture].[Links] l JOIN [fixture].[Nodes] n ON n.[Id] = l.[NodeId] JOIN [fixture].[Nodes] o ON o.[Id] = l.[OtherNodeId] WHERE (n.[Code] = '{first}' AND o.[Code] = '{second}') OR (n.[Code] = '{second}' AND o.[Code] = '{first}');"));
        }

        /// <summary>Asserts how a polled job ended, naming what its handler threw when it ended otherwise.</summary>
        /// <param name="polled">The job.</param>
        /// <param name="status">The status it should end in.</param>
        /// <param name="code">The error code it should record; none when null.</param>
        private void Ended(PolledJob polled, JobStatus status, string? code = null)
        {
            string thrown = string.Join(
                Environment.NewLine,
                fixture.Log.GetSnapshot().Where(static record => record.Exception is not null).Select(static record => record.Exception!.ToString()));
            Assert.True(polled.Status == status && polled.ErrorCode == code, $"The job ended {polled.Status} ({polled.ErrorCode}). {thrown}");
        }

        /// <summary>A text formatted invariantly.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A seeded widget's code.</summary>
        /// <param name="id">The widget's id.</param>
        /// <returns>The code.</returns>
        private static string CodeOf(int id)
        {
            return Invariant($"X{id}");
        }

        /// <summary>A widget row of the parent sheet.</summary>
        /// <param name="id">The sheet identity; blank when null.</param>
        /// <param name="code">The code.</param>
        /// <param name="name">The name; the code's when null.</param>
        /// <param name="subject">The subject; one of the code's when null.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Widget(long? id, string code, string? name = null, string? subject = null)
        {
            return Row(("Id", id), ("Code", code), ("Kind", nameof(WidgetKind.Plain)), ("Name", name ?? code), ("Subject", subject ?? $"s-{code}"));
        }

        /// <summary>A node row of the parent sheet.</summary>
        /// <param name="id">The sheet identity; blank when null.</param>
        /// <param name="code">The code.</param>
        /// <param name="parent">The parent's code, or its identity under an override; none when null.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Node(long? id, string code, object? parent)
        {
            return Row(("Id", id), ("Code", code), ("Name", $"Node {code}"), ("Parent.Code", parent));
        }

        /// <summary>A row by path.</summary>
        /// <param name="cells">Each cell's path and value.</param>
        /// <returns>The row.</returns>
        private static Dictionary<string, object?> Row(params (string Path, object? Value)[] cells)
        {
            return cells.ToDictionary(static cell => cell.Path, static cell => cell.Value, StringComparer.Ordinal);
        }

        /// <summary>A job's checkpoint.</summary>
        /// <param name="job">The job.</param>
        /// <returns>The checkpoint.</returns>
        private static ImportCheckpointState State(JobRow job)
        {
            return JsonSerializer.Deserialize<ImportCheckpointState>(job.StateJson ?? throw new InvalidOperationException("The job has no checkpoint."), TellmaJson.Options)!;
        }

        /// <summary>An Import row's outcome.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The outcome.</returns>
        private static ImportOutcome Outcome(Dictionary<string, object?> row)
        {
            return JsonSerializer.Deserialize<ImportOutcome>((string)row["ResultJson"]!, TellmaJson.Options)!;
        }

        /// <summary>The chunk commits the imports logged.</summary>
        /// <returns>The records, in order.</returns>
        private List<FakeLogRecord> Committed()
        {
            return [.. fixture.Log.GetSnapshot().Where(static record => record.Id.Name == "ImportChunkCommitted")];
        }

        /// <summary>Seeds widgets from the first id, each coded by its id.</summary>
        /// <param name="count">How many.</param>
        /// <returns>A task that completes once they are stored.</returns>
        private Task SeedWidgetsAsync(int count)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                DataQueryDatabase.Stamped(
                    "[fixture].[Widgets]",
                    "[Id], [Code], [Kind], [Name], [Subject], [Price]",
                    [.. Enumerable.Range(FirstWidget, count).Select(static id => Invariant($"({id}, '{CodeOf(id)}', 'Plain', N'Widget {id}', N's{id}', 1)"))]));
        }

        /// <summary>Invokes a stack's action as a member.</summary>
        /// <param name="user">The member.</param>
        /// <param name="resource">The stack.</param>
        /// <param name="name">The action's name.</param>
        /// <param name="body">The body.</param>
        /// <param name="state">The tenant's state the request sees; its own when null.</param>
        /// <returns>The action's result.</returns>
        private async Task<object?> InvokeAsync(int user, string resource, string name, object body, TenantState? state = null)
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(user, state);

            // A stack's action, else a child query surface's.
            IStackRegistry registry = scope.ServiceProvider.GetRequiredService<IStackRegistry>();
            IEnumerable<ApiActionDescriptor> actions = registry.Stacks.SingleOrDefault(stack => stack.Resource == resource)?.Actions.OfType<ApiActionDescriptor>()
                ?? registry.ChildQueries.Single(surface => surface.Resource == resource).Actions;
            ApiActionDescriptor action = actions.Single(candidate => candidate.Name == name);
            return await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, body, Token);
        }

        /// <summary>An empty editable export of a stack, with the plan of its import.</summary>
        /// <param name="resource">The stack.</param>
        /// <returns>The template and its plan.</returns>
        private async Task<(byte[] Template, ImportPlan Plan)> TemplateAsync(string resource)
        {
            object? result = await InvokeAsync(ImporterId, resource, "export-for-import", new ExportForImportRequest { Ids = [] });
            await using ExportOutcome outcome = Assert.IsType<ExportOutcome>(result);
            using MemoryStream bytes = new();
            await outcome.Workbook.CopyToAsync(bytes, Token);
            byte[] template = bytes.ToArray();
            ImportPlan plan = Assert.IsType<ImportPlan>(await InvokeAsync(ImporterId, resource, "inspect-import", new InspectImportRequest { FileId = await fixture.StageAsync(ImporterId, template) }));
            return (template, plan);
        }

        /// <summary>Stages a workbook and starts its background import.</summary>
        /// <param name="workbook">The workbook.</param>
        /// <param name="request">The request, whose file the upload becomes.</param>
        /// <param name="resource">The stack; the widgets when null.</param>
        /// <param name="user">The member; the importer when null.</param>
        /// <returns>The job and the Import row.</returns>
        private async Task<JobAccepted> StartImportAsync(byte[] workbook, StartImportRequest request, string resource = Widgets, int user = ImporterId)
        {
            request.FileId = await fixture.StageAsync(user, workbook);
            JobAccepted accepted = Assert.IsType<JobAccepted>(await InvokeAsync(user, resource, "import/start", request));
            Assert.NotNull(accepted.ResourceId);
            return accepted;
        }
    }
}
