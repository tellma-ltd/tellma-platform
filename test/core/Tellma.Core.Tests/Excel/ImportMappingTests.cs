// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Excel.Corpus;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The import plan of editable exports and of hand-made workbooks: sheets bound by the
    ///     manifest, by name and by the fallback; columns by the manifest, the header index and the
    ///     overrides; multilingual columns through the target tenant's slots; the same-source gate on
    ///     identity columns; the faults an override raises; the row keys; the bounds on headers; the
    ///     cancellation; the spill's telemetry; and the sizes against the limits.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ImportMappingTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Fact]
        public async Task A_same_source_export_maps_every_sheet_and_column_back_to_its_path()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context);

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            Assert.Equal(("fixture.Widget", 1, true, true, true), (plan.Entity, plan.ManifestVersion, plan.ManifestPresent, plan.SameSource, plan.SchemaFingerprintMatches));
            Assert.Equal(["Contraptions", "Contraptions · Components", "Components · Remarks", "_tellma"], plan.Sheets.Select(static sheet => sheet.Sheet));
            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Child, ImportSheetStatus.Child, ImportSheetStatus.Manifest], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal(["", "Parts", "Parts.PartNotes", null], plan.Sheets.Select(static sheet => sheet.Collection));
            Assert.Equal([2L, 2L, 2L, 0L], plan.Sheets.Select(static sheet => sheet.Rows));

            ImportPlanSheet parent = plan.Sheets[0];
            Assert.Equal(["Id", "Stamp", "Code", "Kind", "Name", "Name2", "Subject", "Price", "OptionalKind", "DueOn"], parent.Columns.Select(static column => column.Path));
            Assert.Equal([null, null, null, null, "en", "ar", null, null, null, null], parent.Columns.Select(static column => column.Language));
            Assert.All(plan.Sheets.SelectMany(static sheet => sheet.Columns), static column => Assert.Equal(ImportColumnStatus.Mapped, column.Status));
            Assert.Equal(["Widget.Id", "Id", "Ordinal", "Notes"], plan.Sheets[1].Columns.Select(static column => column.Path));
            Assert.Equal(["WidgetPart.Id", "Id", "Text"], plan.Sheets[2].Columns.Select(static column => column.Path));

            Assert.Equal(["Id", "Code", "Subject"], plan.RowKeyCandidates);
            Assert.Equal("Id", plan.DefaultRowKey);
            Assert.Equal(6, plan.TotalRows);
            Assert.False(plan.RequiresBackground);
            Assert.True(plan.AtomicAllowed);
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public async Task A_file_of_languages_en_ar_imports_into_a_tenant_of_ar_en_by_each_columns_language()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context);

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, ImportKit.Tenant(scope, languages: ["ar", "en"]));

            ImportPlanSheet parent = plan.Sheets[0];
            ImportPlanColumn english = parent.Columns[4];
            ImportPlanColumn arabic = parent.Columns[5];
            Assert.Equal(("Name2", "en", ImportColumnStatus.Mapped), (english.Path, english.Language, english.Status));
            Assert.Equal(("Name", "ar", ImportColumnStatus.Mapped), (arabic.Path, arabic.Language, arabic.Status));
            Assert.Empty(plan.Warnings);
        }

        [Fact]
        public async Task Every_plan_column_echoed_back_as_an_override_maps_the_same_across_a_language_permutation()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context);
            ExcelContext target = ImportKit.Tenant(scope, languages: ["ar", "en"]);
            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, target);

            ImportColumnMapping[] echoed = [.. plan.Sheets.SelectMany(static sheet => sheet.Columns.Select(column => new ImportColumnMapping(sheet.Sheet, column.Column, column.Path)))];
            ImportPlan again = await _kit.InspectAsync<Widget>(workbook, target, new InspectImportRequest { FileId = 1, ColumnMappings = echoed });

            Assert.Equal(
                plan.Sheets.SelectMany(static sheet => sheet.Columns).Select(static column => (column.Column, column.Path, column.Status, column.Language)),
                again.Sheets.SelectMany(static sheet => sheet.Columns).Select(static column => (column.Column, column.Path, column.Status, column.Language)));
        }

        [Fact]
        public async Task A_column_in_a_language_the_target_does_not_configure_is_ignored_with_a_warning()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar", "am");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context, request: new ExportForImportRequest { Children = [] });

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, ImportKit.Tenant(scope, languages: ["en", "ar"]));

            ImportPlanColumn amharic = plan.Sheets[0].Columns[6];
            Assert.Equal((null, "am", ImportColumnStatus.Ignored), (amharic.Path, amharic.Language, amharic.Status));
            ImportError warning = Assert.Single(plan.Warnings);
            Assert.Equal((ExcelErrorCodes.ImportUnconfiguredLanguageColumn, "Contraptions", 1, "G"), (warning.Code, warning.Sheet, warning.Row, warning.Column));
            Assert.Equal("am", warning.Arguments["language"]);
        }

        [Fact]
        public async Task A_file_from_another_tenant_keeps_an_identity_only_to_link_a_present_child_sheet_and_ignores_every_stamp()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context);

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, ImportKit.Tenant(scope, tenantId: 8));

            Assert.False(plan.SameSource);
            Assert.Equal(ImportColumnStatus.Mapped, ImportKit.Column(plan.Sheets[0], "Id").Status);
            Assert.Equal(ImportColumnStatus.Ignored, ImportKit.Column(plan.Sheets[0], "Stamp").Status);
            Assert.Equal(ImportColumnStatus.Mapped, ImportKit.Column(plan.Sheets[1], "Id").Status);
            Assert.Equal(ImportColumnStatus.Ignored, ImportKit.Column(plan.Sheets[2], "Id").Status);

            // Each identity column is reported, with where the file came from.
            Assert.Equal(4, plan.Warnings.Count);
            Assert.All(plan.Warnings, static warning =>
            {
                Assert.Equal(ExcelErrorCodes.ImportIdentityColumnIgnored, warning.Code);
                Assert.Equal(ImportMapper.ForeignSourceReason, warning.Arguments["reason"]);
                Assert.Equal("deployment-a", warning.Arguments["deployment"]);
                Assert.Equal(7, warning.Arguments["sourceTenantId"]);
            });
            Assert.Equal(["Id", "Stamp", "Id", "Id"], plan.Warnings.Select(static warning => warning.Arguments["path"]));
            Assert.Equal(["Code", "Subject"], plan.RowKeyCandidates);
            Assert.Equal("Code", plan.DefaultRowKey);
        }

        [Fact]
        public async Task A_file_from_another_deployment_of_the_same_application_is_not_same_source()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(ImportKit.Tenant(scope, deployment: "etpharma-staging"));

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, ImportKit.Tenant(scope, deployment: "etpharma"));

            Assert.False(plan.SameSource);
            Assert.True(plan.SchemaFingerprintMatches);
            Assert.All(plan.Warnings, static warning => Assert.Equal("etpharma-staging", warning.Arguments["deployment"]));
        }

        [Fact]
        public async Task A_sheet_without_a_manifest_is_the_parent_by_the_fallback_and_its_headers_match_the_labels_of_any_candidate_culture()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            string arabic = $"العنوان ({scope.Symbol("ar")})";
            byte[] workbook = new SyntheticWorkbook(
                Producer.Excel,
                RawSheet.OfText("Notes", ["anything"]) with { State = "hidden" },
                RawSheet.OfText("Data", ["Id", "Code", "Title", arabic, "Name2", "price", "Due  on"], ["1", "W-1", "One", "واحد", "x", "2", "3"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            Assert.Equal((null, 0, false, false), (plan.Entity, plan.ManifestVersion, plan.ManifestPresent, plan.SameSource));
            Assert.Equal([ImportSheetStatus.Ignored, ImportSheetStatus.Parent], plan.Sheets.Select(static sheet => sheet.Status));
            ImportPlanSheet data = plan.Sheets[1];
            Assert.Equal(1, data.Rows);
            Assert.Equal(["Id", "Code", "Name", "Name2", "Name2", "Price", "DueOn"], data.Columns.Select(static column => column.Path));
            Assert.Equal([null, null, "en", "ar", "ar", null, null], data.Columns.Select(static column => column.Language));

            // The Arabic title and the technical twin write one slot.
            Assert.Equal(ImportColumnStatus.Duplicate, data.Columns[3].Status);
            Assert.Equal(ImportColumnStatus.Duplicate, data.Columns[4].Status);

            // Without a manifest the identity is never a row key, and with no child sheet it links nothing.
            Assert.Equal(ImportColumnStatus.Ignored, data.Columns[0].Status);
            ImportError warning = Assert.Single(plan.Warnings);
            Assert.Equal((ExcelErrorCodes.ImportIdentityColumnIgnored, ImportMapper.NoManifestReason), (warning.Code, (string?)warning.Arguments["reason"]));
            Assert.Equal(["Code"], plan.RowKeyCandidates);
        }

        [Fact]
        public async Task A_header_of_a_catalogued_language_the_tenant_lacks_is_ignored_with_a_warning()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code", $"Title ({scope.Symbol("am")})"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            Assert.Equal((null, "am", ImportColumnStatus.Ignored), (plan.Sheets[0].Columns[1].Path, plan.Sheets[0].Columns[1].Language, plan.Sheets[0].Columns[1].Status));
            Assert.Equal(ExcelErrorCodes.ImportUnconfiguredLanguageColumn, Assert.Single(plan.Warnings).Code);
        }

        [Fact]
        public async Task An_arabic_reader_matches_the_arabic_labels_and_an_english_reader_matches_them_through_the_tenants_language()
        {
            using ExportScope arabicScope = new(composition.Services, "ar", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(arabicScope.Context, request: new ExportForImportRequest { Children = [] });
            using ExportScope englishScope = new(composition.Services, "en", "en", "ar");
            SyntheticWorkbook stripped = new(Producer.Excel, RawSheet.OfText("Data", ["Id", $"العنوان ({englishScope.Symbol("en")})"]));

            ImportPlan plan = await _kit.InspectAsync<Widget>(stripped.Build(), englishScope.Context);
            ImportPlan full = await _kit.InspectAsync<Widget>(workbook, englishScope.Context);

            Assert.Equal(("Name", "en"), (plan.Sheets[0].Columns[1].Path, plan.Sheets[0].Columns[1].Language));
            Assert.All(full.Sheets[0].Columns, static column => Assert.Equal(ImportColumnStatus.Mapped, column.Status));
        }

        [Fact]
        public async Task Server_owned_and_excluded_paths_are_server_owned_and_two_columns_of_one_path_are_duplicates()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(Producer.LibreOffice, RawSheet.OfText("Contraptions", ["Code", "CreatedAt", "Settings", "Price", "Price (Price)"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            Assert.Equal(ImportSheetStatus.Parent, plan.Sheets[0].Status);
            Assert.Equal(
                [ImportColumnStatus.Mapped, ImportColumnStatus.ServerOwned, ImportColumnStatus.ServerOwned, ImportColumnStatus.Duplicate, ImportColumnStatus.Duplicate],
                plan.Sheets[0].Columns.Select(static column => column.Status));
            Assert.Equal(["Code", "CreatedAt", "Settings", "Price", "Price"], plan.Sheets[0].Columns.Select(static column => column.Path));
        }

        [Fact]
        public async Task An_unmapped_header_is_reported_with_the_nearest_paths()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(Producer.GoogleSheets, RawSheet.OfText("Data", ["Cod", "Pirce", "Something else entirely"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            ImportPlanColumn[] columns = [.. plan.Sheets[0].Columns];
            Assert.All(columns, static column => Assert.Equal((null, ImportColumnStatus.Unmapped), (column.Path, column.Status)));
            Assert.Contains("Code", columns[0].Suggestions);
            Assert.Equal("Price", columns[1].Suggestions[0]);
            Assert.Empty(columns[2].Suggestions);
            Assert.All(columns, static column => Assert.InRange(column.Suggestions.Count, 0, ImportMapper.MaxSuggestions));
        }

        [Fact]
        public async Task An_override_ignores_a_column_or_maps_it_to_a_path_and_wins_over_the_manifest()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context, request: new ExportForImportRequest { Children = [] });

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest
            {
                FileId = 1,
                ColumnMappings = [new("contraptions", "h", Path: null), new("Contraptions", "G", "Price")],
            });

            // The price column is ignored, so the subject column writes the price alone.
            ImportPlanSheet parent = plan.Sheets[0];
            Assert.Equal((null, ImportColumnStatus.Ignored), (parent.Columns[7].Path, parent.Columns[7].Status));
            Assert.Equal(("Price", ImportColumnStatus.Mapped), (parent.Columns[6].Path, parent.Columns[6].Status));
            Assert.Equal(("Code", ImportColumnStatus.Mapped), (parent.Columns[2].Path, parent.Columns[2].Status));
        }

        [Fact]
        public async Task An_override_to_a_path_the_entity_lacks_a_letter_without_a_header_and_a_sheet_the_workbook_lacks_are_refused_together()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code", "Price"]), RawSheet.OfText("Other", ["x"])).Build();

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest
            {
                FileId = 1,
                SheetMappings = [new("Missing", ""), new("Other", Collection: null)],
                ColumnMappings = [new("Data", "B", "Nope.Code"), new("Data", "Z", "Code"), new("Other", "A", "Code")],
            }));

            Assert.Equal(4, refused.TotalErrors);
            (string Code, string? Sheet, int? Row, string? Column)[] expected =
            [
                (ExcelErrorCodes.ImportUnknownSheet, "Missing", null, null),
                (ExcelErrorCodes.ImportUnknownPath, "Data", 1, "B"),
                (ExcelErrorCodes.ImportUnknownColumn, "Data", 1, "Z"),
                (ExcelErrorCodes.ImportUnknownColumn, "Other", 1, "A"),
            ];
            Assert.Equal(expected, refused.Errors.Select(static error => (error.Code, error.Sheet, error.Row, error.Column)));
            Assert.Equal("Nope.Code", refused.Errors[1].Arguments["path"]);
        }

        [Fact]
        public async Task Two_sheets_bound_to_one_target_by_name_and_override_are_both_duplicates()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Contraptions", ["Code"]), RawSheet.OfText("More", ["Code"]), RawSheet.OfText("Parts", ["Ordinal"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest { FileId = 1, SheetMappings = [new("More", "")] });

            Assert.Equal([ImportSheetStatus.Duplicate, ImportSheetStatus.Duplicate, ImportSheetStatus.Child], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal(["", "", "Parts"], plan.Sheets.Select(static sheet => sheet.Collection));
            Assert.Equal(0, plan.TotalRows);
            Assert.Empty(plan.RowKeyCandidates);
        }

        [Fact]
        public async Task A_very_hidden_sheet_binds_only_by_an_override_and_a_sheet_override_to_null_ignores_a_named_sheet()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(
                Producer.Excel,
                RawSheet.OfText("Contraptions", ["Code"]),
                RawSheet.OfText("Parts", ["Ordinal"]) with { State = "veryHidden" },
                RawSheet.OfText("Parts.PartNotes", ["Text"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest { FileId = 1, SheetMappings = [new("Parts.PartNotes", null)] });
            ImportPlan bound = await _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest { FileId = 1, SheetMappings = [new("PARTS", "Parts")] });

            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Ignored, ImportSheetStatus.Ignored], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Child, ImportSheetStatus.Child], bound.Sheets.Select(static sheet => sheet.Status));
        }

        [Fact]
        public async Task A_manifest_of_another_entity_is_a_malformed_workbook()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context, rows: 0);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.InspectAsync<Node>(workbook, scope.Context));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportMalformedWorkbook, SheetNames.Manifest, ImportFaults.EntityReason), (error.Code, error.Sheet, (string?)error.Arguments["reason"]));
        }

        [Fact]
        public async Task The_sizes_decide_whether_the_file_needs_the_background_and_whether_it_may_run_atomically()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context, rows: 3);

            ImportPlan rows = await _kit.InspectAsync<Widget>(workbook, scope.Context, options: new ExcelOptions { MaxSynchronousImportRows = 8 });
            ImportPlan atomic = await _kit.InspectAsync<Widget>(workbook, scope.Context, options: new ExcelOptions { MaxAtomicImportRows = 8 });
            ImportPlan save = await _kit.InspectAsync<Widget>(workbook, scope.Context, limits: StackLimits.Default with { MaxIds = 2 });
            ImportPlan bytes = await _kit.InspectAsync<Widget>(workbook, scope.Context, options: new ExcelOptions { MaxSynchronousImportFileBytes = workbook.Length - 1 });

            Assert.Equal(9, rows.TotalRows);
            Assert.Equal((true, true), (rows.RequiresBackground, rows.AtomicAllowed));
            Assert.Equal((false, false), (atomic.RequiresBackground, atomic.AtomicAllowed));
            Assert.Equal((true, false), (save.RequiresBackground, save.AtomicAllowed));
            Assert.Equal((true, true), (bytes.RequiresBackground, bytes.AtomicAllowed));
        }

        [Fact]
        public async Task A_child_sheets_parent_link_matches_by_its_label_its_path_and_its_bare_foreign_key()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(
                Producer.Excel,
                RawSheet.OfText("Nodes", ["Id", "Code", "Parent node / Id", "Parent.Code"]),
                RawSheet.OfText("Links", ["Node.Id", "Id", "OtherNodeId", "Counterpart / Code"])).Build();

            ImportPlan plan = await _kit.InspectAsync<Node>(workbook, ImportKit.Tenant(scope, tenantId: 7));

            Assert.Equal(["Id", "Code", "Parent.Id", "Parent.Code"], plan.Sheets[0].Columns.Select(static column => column.Path));
            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Child], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal(ImportColumnStatus.Duplicate, plan.Sheets[0].Columns[2].Status);
            Assert.Equal(ImportColumnStatus.Duplicate, plan.Sheets[0].Columns[3].Status);
            Assert.Equal("Node.Id", plan.Sheets[1].Columns[0].Path);
            Assert.Equal(["OtherNode.Id", "OtherNode.Code"], plan.Sheets[1].Columns.Skip(2).Select(static column => column.Path));
        }

        [Fact]
        public async Task A_header_longer_than_any_export_writes_is_cut_and_unmapped_unless_an_override_maps_it()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            string header = $"Price {new string('x', ImportMapper.MaxHeaderLength)}";
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code", header])).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);
            ImportPlan mapped = await _kit.InspectAsync<Widget>(workbook, scope.Context, new InspectImportRequest { FileId = 1, ColumnMappings = [new("Data", "B", "Price")] });

            ImportPlanColumn column = plan.Sheets[0].Columns[1];
            Assert.Equal((header[..ImportMapper.MaxHeaderLength], null, ImportColumnStatus.Unmapped), (column.Header, column.Path, column.Status));
            Assert.Empty(column.Suggestions);
            Assert.Equal(("Price", ImportColumnStatus.Mapped), (mapped.Sheets[0].Columns[1].Path, mapped.Sheets[0].Columns[1].Status));
        }

        [Fact]
        public async Task A_manifest_of_another_schema_fingerprint_is_read_but_does_not_match()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            string?[][] rows = [[ExcelManifest.VersionKey, "1"], [ExcelManifest.EntityKey, "fixture.Widget"], [ExcelManifest.SchemaFingerprintKey, "0000000000000000"]];
            RawSheet manifest = RawSheet.OfText(SheetNames.Manifest, rows) with { State = "veryHidden" };
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code"], ["W-1"]), manifest).Build();

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook, scope.Context);

            Assert.Equal(("fixture.Widget", true, false), (plan.Entity, plan.ManifestPresent, plan.SchemaFingerprintMatches));
        }

        [Fact]
        public async Task A_multilingual_natural_key_mapped_only_through_another_slot_is_a_row_key_candidate()
        {
            // The fixture model has no multilingual natural key, so the widget is described again with its title as one.
            EntityMetadata widget = _kit.Entity<Widget>();
            PropertyMetadata name = Assert.Single(widget.Properties, static property => property.Name == "Name");
            EntityMetadata keyed = new(
                widget.ClrType,
                widget.Name,
                widget.Table,
                widget.TableTypePhysicalName,
                widget.Key,
                widget.Properties,
                [.. widget.NaturalKeys, new NaturalKeyMetadata(name, IsDeclared: true, Order: widget.NaturalKeys.Count)],
                widget.MultilingualGroups,
                widget.UniqueIndexes,
                widget.IsTopLevel,
                widget.IsTemporal,
                widget.IsActivatable,
                widget.IsJobEntity,
                widget.IsCacheable,
                widget.Tree,
                widget.BlobReferences,
                widget.DisplayName,
                () => Activator.CreateInstance(widget.ClrType)!,
                _ => widget.Children,
                _ => widget.References,
                _ => widget.SchemaFingerprint);
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Name2", "Price"], ["واحد", "1"])).Build();

            ImportPlan plan = await _kit.InspectAsync(keyed, workbook, scope.Context);
            ImportPlan unkeyed = await _kit.InspectAsync(widget, workbook, scope.Context);

            Assert.Equal(("Name2", "ar"), (plan.Sheets[0].Columns[0].Path, plan.Sheets[0].Columns[0].Language));
            Assert.Equal(["Name"], plan.RowKeyCandidates);
            Assert.Equal("Name", plan.DefaultRowKey);
            Assert.Empty(unkeyed.RowKeyCandidates);
        }

        [Fact]
        public void A_cancelled_mapping_stops_before_it_reads_a_sheet()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] workbook = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code"], ["W-1"])).Build();
            using var reader = WorkbookReader.Open(new MemoryStream(workbook, writable: false), new WorkbookLimits(1 << 24, 1 << 26, 1 << 24, 100_000), _kit.Scratch, TestContext.Current.CancellationToken);
            ImportMapper mapper = new(_kit.Entity<Widget>(), StackLimits.Default, new ExcelOptions(), scope.Context, new HeaderIndexCache());
            using CancellationTokenSource source = new();
            source.Cancel();

            _ = Assert.Throws<OperationCanceledException>(() => mapper.Map(reader, [], [], source.Token));
        }

        [Fact]
        public async Task A_workbook_whose_shared_strings_spill_is_counted_and_logged_once_and_one_within_the_budget_is_neither()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            string[] texts = [.. Enumerable.Range(0, 300).Select(static i => $"text number {i} " + new string('x', 64))];
            byte[] spilling = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", [["Code"], .. texts.Select(static text => new string?[] { text })])).Build();
            byte[] within = new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code"], ["W-1"])).Build();
            using MetricCollector<long> spilled = new(composition.Services.GetRequiredService<IMeterFactory>(), ExcelTelemetryNames.MeterName, ExcelTelemetryNames.SharedStringsSpilled);
            FakeLoggerProvider logs = new();
            using LoggerFactory loggers = new([logs]);
            ExcelOptions lowered = new() { MaxInMemorySharedStringBytes = 4_096 };

            _ = await _kit.InspectAsync<Widget>(within, scope.Context, options: lowered, loggers: loggers);

            Assert.Empty(spilled.GetMeasurementSnapshot());
            Assert.DoesNotContain(logs.Collector.GetSnapshot(), static record => record.Id.Name == "SharedStringsSpilled");

            _ = await _kit.InspectAsync<Widget>(spilling, scope.Context, options: lowered, loggers: loggers);

            Assert.Equal(1L, Assert.Single(spilled.GetMeasurementSnapshot()).Value);
            FakeLogRecord record = Assert.Single(logs.Collector.GetSnapshot(), static record => record.Id.Name == "SharedStringsSpilled");
            Assert.Equal(LogLevel.Information, record.Level);
            string? bytes = Assert.Single(record.StructuredState!, static pair => pair.Key == "Bytes").Value;
            Assert.True(long.Parse(bytes!, CultureInfo.InvariantCulture) > lowered.MaxInMemorySharedStringBytes);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }
    }
}
