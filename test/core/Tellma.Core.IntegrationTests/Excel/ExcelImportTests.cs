// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Excel;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Crud;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>
    ///     The import intake against a real tenant database, through the real connect prologue,
    ///     runner and evaluator: an editable export, staged as the caller's upload, inspected back in
    ///     one round trip; another user's upload, a committed one and a missing one indistinguishable;
    ///     malformed overrides refused before the upload is resolved; and the delete of an import
    ///     whose job may still run refused, and of one whose job ended allowed, to its creator
    ///     holding no stored grant.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class ExcelImportTests(ExcelRowSourceDatabase fixture) : IClassFixture<ExcelRowSourceDatabase>, IAsyncLifetime
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The instance that serves the tests, with in-memory blobs; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The instance.</summary>
        private ServiceProvider Instance => _instance ?? throw new InvalidOperationException("The instance is not created yet.");

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            _instance = fixture.Connect.CreateInstance(configure: static services => services.AddInMemoryBlobs());
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_instance is not null)
            {
                await _instance.DisposeAsync();
            }
        }

        [Fact]
        public async Task An_editable_export_staged_as_the_callers_upload_inspects_back_same_source_in_one_round_trip()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            byte[] workbook = await ExportAsync(subject, new ExportForImportRequest { Ids = [6, 3] });
            int fileId = await StageAsync(userId, workbook);
            InspectImportRequest request = new() { FileId = fileId };

            // Warmed in a scope of its own, so the measured one finds every cache warm.
            await using (AsyncServiceScope cold = await ScopeAsync(subject))
            {
                _ = await InspectAsync(cold, request);
            }

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            ImportPlan plan = await InspectAsync(scope, request);

            Assert.Equal(1, access.RoundTrips - before);
            Assert.Equal((Widgets, true, true, true), (plan.Entity, plan.ManifestPresent, plan.SameSource, plan.SchemaFingerprintMatches));
            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Child, ImportSheetStatus.Child, ImportSheetStatus.Manifest], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal([2L, 2L, 2L], plan.Sheets.Take(3).Select(static sheet => sheet.Rows));
            Assert.All(plan.Sheets.SelectMany(static sheet => sheet.Columns), static column => Assert.Equal(ImportColumnStatus.Mapped, column.Status));
            Assert.Equal("Id", plan.DefaultRowKey);
            Assert.Empty(plan.Warnings);

            // A synchronous inspection leaves the upload staged.
            Assert.Equal(nameof(BlobState.Staged), await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [State] FROM [core].[Blobs] WHERE [Id] = {fileId};")));
        }

        [Fact]
        public async Task Another_users_upload_a_committed_one_and_a_missing_one_are_all_not_found()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Save, null));
            (int otherId, _) = await fixture.SeedCallerAsync((Widgets, AccessActions.Save, null));
            byte[] workbook = await ExportAsync(subject, new ExportForImportRequest { Ids = [] }, (Widgets, AccessActions.Read, null));
            int theirs = await StageAsync(otherId, workbook);
            int committed = await StageAsync(userId, workbook, state: BlobState.Committed);

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            foreach (int fileId in new[] { theirs, committed, int.MaxValue })
            {
                NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => InspectAsync(scope, new InspectImportRequest { FileId = fileId }));
                Assert.Equal((ExcelImports.BlobResource, fileId.ToString(CultureInfo.InvariantCulture)), (missing.Resource, Assert.Single(missing.Ids)));
            }
        }

        [Fact]
        public async Task A_malformed_override_is_refused_before_the_upload_is_resolved()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Save, null));
            int fileId = await StageAsync(userId, [1, 2, 3]);

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            BadRequestException refused = await Assert.ThrowsAsync<BadRequestException>(() => InspectAsync(scope, new InspectImportRequest
            {
                FileId = fileId,
                SheetMappings = [new("Data", "Nope")],
            }));

            Assert.Equal("sheetMappings[0].collection", refused.Arguments!["path"]);
            Assert.Equal(0, access.RoundTrips - before);
        }

        [Fact]
        public async Task An_upload_that_is_not_a_workbook_is_refused_whole()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Save, null));
            int fileId = await StageAsync(userId, "Code,Name\nW-1,One\n"u8.ToArray());

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => InspectAsync(scope, new InspectImportRequest { FileId = fileId }));

            Assert.Equal(ExcelErrorCodes.ImportMalformedWorkbook, Assert.Single(refused.Errors).Code);
        }

        [Theory]
        [InlineData(nameof(JobStatus.Pending), false)]
        [InlineData(nameof(JobStatus.Running), false)]
        [InlineData(nameof(JobStatus.Held), false)]
        [InlineData(nameof(JobStatus.PendingConfirmation), false)]
        [InlineData(nameof(JobStatus.Succeeded), true)]
        [InlineData(nameof(JobStatus.Failed), true)]
        [InlineData(nameof(JobStatus.Cancelled), true)]
        public async Task An_import_whose_job_may_still_run_cannot_be_deleted_by_its_creator_and_one_whose_job_ended_can(string status, bool ended)
        {
            // The caller holds no stored grant: the imports' own-rows criteria alone let it delete its own.
            (int userId, string subject) = await fixture.SeedCallerAsync();
            string lease = status == nameof(JobStatus.Running) ? "NEWID(), DATEADD(minute, 5, SYSUTCDATETIME())" : "NULL, NULL";
            string confirmations = status == nameof(JobStatus.PendingConfirmation) ? "N'[]'" : "NULL";
            string completedAt = ended ? "SYSUTCDATETIME()" : "NULL";
            int jobId = await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt], [CompletedAt], [LeaseToken], [LeaseExpiresAt], [PendingConfirmationsJson])
                VALUES (@id, N'fixture.probe', '{status}', SYSUTCDATETIME(), {userId}, SYSUTCDATETIME(), {completedAt}, {lease}, {confirmations});
                SELECT @id;
                """));
            int importId = await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Imports];
                INSERT INTO [core].[Imports] ([Id], [Resource], [Mode], [ExpiresAt], [JobId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES (@id, '{Widgets}', 'Insert', DATEADD(day, 7, SYSUTCDATETIME()), {jobId}, SYSUTCDATETIME(), {userId}, SYSUTCDATETIME(), {userId});
                SELECT @id;
                """));

            await using AsyncServiceScope scope = await ScopeAsync(subject);
            if (ended)
            {
                _ = await Imports(scope).DeleteByIdsAsync([importId], cancellationToken: Token);
            }
            else
            {
                // Refused at the target's own position in the request.
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Imports(scope).DeleteByIdsAsync([importId], cancellationToken: Token));
                ValidationError error = Assert.Single(refused.Errors);
                Assert.Equal((ExcelErrorCodes.ImportsJobActive, "Ids[0]"), (error.Code, error.Path.ToString()));
            }

            int remaining = await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Imports] WHERE [Id] = {importId};"));
            Assert.Equal(ended ? 0 : 1, remaining);
        }

        /// <summary>A caller's scope on the instance.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>The scope.</returns>
        private Task<AsyncServiceScope> ScopeAsync(string subject)
        {
            return fixture.Connect.ScopeAsync(subject, services: Instance);
        }

        /// <summary>Writes an editable export of the widgets as a caller, through the action invoker.</summary>
        /// <param name="subject">The caller's subject, who may read the widgets.</param>
        /// <param name="request">The request.</param>
        /// <param name="reader">A permission another caller exports under; null to export as the subject.</param>
        /// <returns>The workbook's bytes.</returns>
        private async Task<byte[]> ExportAsync(string subject, ExportForImportRequest request, (string Resource, string Action, string? Filter)? reader = null)
        {
            string exporter = reader is { } permission ? (await fixture.SeedCallerAsync(permission)).Subject : subject;
            await using AsyncServiceScope scope = await ScopeAsync(exporter);
            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(StackAction(scope, "export-for-import"), request, Token);
            await using ExportOutcome outcome = Assert.IsType<ExportOutcome>(result);
            using MemoryStream bytes = new();
            await outcome.Workbook.CopyToAsync(bytes, Token);
            return bytes.ToArray();
        }

        /// <summary>Inspects an upload against the widgets, through the action invoker, which evaluates the action's securable first.</summary>
        /// <param name="scope">The caller's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The plan.</returns>
        private static async Task<ImportPlan> InspectAsync(AsyncServiceScope scope, InspectImportRequest request)
        {
            object? result = await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(StackAction(scope, "inspect-import"), request, Token);
            return Assert.IsType<ImportPlan>(result);
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

        /// <summary>The imports' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Import, int> Imports(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Import, int>>();
        }

        /// <summary>Seeds an import-file blob row as the stage statement writes one, and its bytes in the instance's store.</summary>
        /// <param name="userId">The uploader.</param>
        /// <param name="bytes">The upload's bytes.</param>
        /// <param name="state">The row's state.</param>
        /// <returns>The blob's id.</returns>
        private async Task<int> StageAsync(int userId, byte[] bytes, BlobState state = BlobState.Staged)
        {
            string storageKey = BlobName.Primary(Import.FileKind, Guid.NewGuid().ToString("N"));
            string committedAt = state == BlobState.Committed ? "SYSUTCDATETIME()" : "NULL";
            string expiresAt = state == BlobState.Committed ? "NULL" : "DATEADD(hour, 1, SYSUTCDATETIME())";
            int id = await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{Import.FileKind}', '{state}', N'{BlobContentTypes.Xlsx}', N'widgets.xlsx', {bytes.Length}, N'', SYSUTCDATETIME(), {userId}, {committedAt}, {expiresAt}, 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));

            IBlobStore store = Instance.GetRequiredService<IBlobStore>();
            await store.EnsureTenantAsync(ConnectDatabase.TenantA, Token);
            using MemoryStream content = new(bytes, writable: false);
            await store.WriteAsync(ConnectDatabase.TenantA, [new BlobWrite(storageKey, BlobContentTypes.Xlsx, content, bytes.Length)], Token);
            return id;
        }
    }
}
