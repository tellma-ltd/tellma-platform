// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The parse and the resolution of an import, over workbooks a user edits from an editable
    ///     export: the uniqueness of row keys within the file, the links of child rows at every depth,
    ///     the per-owner counts, the self-reference order and its cycles, the order's fingerprint, the
    ///     binding of new rows within a chunk and across chunks, the completeness of a child sheet
    ///     over stored children, the coordinate map, and the cap on the errors reported.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ImportSessionTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The nodes' parent sheet.</summary>
        private const string Nodes = "Node";

        /// <summary>The widgets' parent sheet.</summary>
        private const string Widgets = "Contraptions";

        /// <summary>The widgets' parts sheet.</summary>
        private const string Parts = "Contraptions · Components";

        /// <summary>The parts' notes sheet.</summary>
        private const string Notes = "Components · Remarks";

        /// <summary>No lookup rows.</summary>
        private static readonly Dictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> NoLookups = [];

        /// <summary>No committed ids.</summary>
        private static readonly Dictionary<long, long> NoCommitted = [];

        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Fact]
        public async Task Two_rows_with_one_unique_value_are_both_refused_before_any_lookup()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await NodesAsync(scope, Node(code: "A"), Node(code: " A "), Node(code: "B"));

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Node>(workbook, scope.Context));

            Assert.Equal([(Nodes, 2, "D"), (Nodes, 3, "D")], refused.Errors.Select(static error => (error.Sheet!, error.Row!.Value, error.Column!)));
            Assert.All(refused.Errors, static error => Assert.Equal(ExcelErrorCodes.ImportDuplicateRowKey, error.Code));
        }

        [Fact]
        public async Task Rows_are_ordered_parents_first_and_each_new_parent_binds_to_its_children_within_the_chunk()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await NodesAsync(scope, Node(code: "C", parent: "B"), Node(code: "B", parent: "A"), Node(code: "A"));

            ExcelImportSession<Node> session = await _kit.ParseAsync<Node>(workbook, scope.Context);

            Assert.Equal([4, 3, 2], session.SheetRows);
            Assert.Empty(session.LookupsFor(0, 2));
            ExcelChunk<Node> chunk = session.Resolve(0, 2, NoLookups, [], NoCommitted);
            Assert.Empty(chunk.Errors);
            Assert.Equal([("A", -1, null), ("B", -2, -1), ("C", -3, -2)], chunk.Entities.Select(static node => (node.Code, node.Id, node.ParentId)));
            Assert.All(chunk.Entities, static node => Assert.True(node.IsActive));
            Assert.Equal((3, 3, 0), (chunk.Facts.Rows.Count, chunk.Facts.Inserted, chunk.Facts.Updated));
        }

        [Fact]
        public async Task A_cycle_of_new_rows_is_refused_at_each_row_on_it_and_a_row_outside_it_is_not()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await NodesAsync(scope, Node(code: "A", parent: "B"), Node(code: "C"), Node(code: "B", parent: "A"), Node(code: "D", parent: "D"));

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Node>(workbook, scope.Context));

            Assert.Equal([(Nodes, 2, "C"), (Nodes, 4, "C"), (Nodes, 5, "C")], refused.Errors.Select(static error => (error.Sheet!, error.Row!.Value, error.Column!)));
            Assert.All(refused.Errors, static error => Assert.Equal((ExcelErrorCodes.ImportParentCycle, "Parent.Code"), (error.Code, error.Arguments["path"])));
            Assert.Equal([2L, 4L], (long[])refused.Errors[0].Arguments["rows"]!);
            Assert.Equal([5L], (long[])refused.Errors[2].Arguments["rows"]!);
        }

        [Fact]
        public async Task A_later_chunk_binds_a_new_row_of_an_earlier_one_through_its_committed_id_and_without_it_finds_none()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            ImportRequest request = new() { FileId = 1, ColumnMappings = [new ImportColumnMapping(Nodes, "C", "Parent.Id")] };
            byte[] workbook = await NodesAsync(scope, Node(id: -1, code: "A"), Node(id: -2, code: "B", parent: -1L), Node(id: -3, code: "C", parent: -2L));

            ExcelImportSession<Node> session = await _kit.ParseAsync<Node>(workbook, scope.Context, request);

            // --- The first chunk saves A; the rows after it still name A and B ---
            Assert.Equal([2, 3, 4], session.SheetRows);
            Assert.Equal([-2L, -1L], session.ReferencedFrom(1).Order());
            Assert.Equal([-2L], session.ReferencedFrom(2));
            Assert.Empty(session.ReferencedFrom(3));
            ExcelChunk<Node> first = session.Resolve(0, 0, NoLookups, [], NoCommitted);
            first.Entities[0].Id = 500;
            Assert.Equal(new Dictionary<long, long> { [-1] = 500 }, session.LaterReferencedIds(0, 0, first));

            // --- A negative identity is never looked up: the second chunk binds A by its committed id ---
            Assert.Empty(session.LookupsFor(1, 2));
            ExcelChunk<Node> second = session.Resolve(1, 2, NoLookups, [], new Dictionary<long, long> { [-1] = 500 });
            Assert.Empty(second.Errors);
            Assert.Equal([(-1, 500), (-2, -1)], second.Entities.Select(static node => (node.Id, node.ParentId)));

            // --- Without it, the row it named is not found ---
            ExcelChunk<Node> unbound = session.Resolve(1, 2, NoLookups, [], NoCommitted);
            ImportError missing = Assert.Single(unbound.Errors);
            Assert.Equal((ExcelErrorCodes.ImportReferenceNotFound, Nodes, 3, "C"), (missing.Code, missing.Sheet, missing.Row, missing.Column));
        }

        [Fact]
        public async Task The_order_hash_is_the_same_for_the_same_file_and_changes_when_a_column_reads_another_language()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await WidgetsAsync(scope, [Widget(-1, "W-1"), Widget(-2, "W-2")]);

            ExcelImportSession<Widget> first = await _kit.ParseAsync<Widget>(workbook, scope.Context);
            ExcelImportSession<Widget> again = await _kit.ParseAsync<Widget>(workbook, scope.Context);
            ExcelImportSession<Widget> swapped = await _kit.ParseAsync<Widget>(workbook, ImportKit.Tenant(scope, languages: ["ar", "en"]));

            Assert.Equal(first.OrderHash, again.OrderHash);
            Assert.Matches("^[0-9a-f]{64}$", first.OrderHash);
            Assert.NotEqual(first.OrderHash, swapped.OrderHash);
        }

        [Fact]
        public async Task Child_rows_link_to_their_owners_at_depth_two_and_an_orphan_or_a_duplicate_owner_identity_is_refused()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await WidgetsAsync(
                scope,
                [Widget(-1, "W-1"), Widget(-2, "W-2")],
                [Part(-1, -10, 1), Part(-3, -11, 2), Part(null, -12, 3), Part(-2, -10, 4)],
                [Note(-10, "first"), Note(-13, "orphan")]);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Widget>(workbook, scope.Context));

            Assert.Equal(
                [
                    (Parts, 3, "A", ExcelErrorCodes.ImportOrphanChildRow),
                    (Parts, 4, "A", ExcelErrorCodes.ImportOrphanChildRow),
                    (Parts, 2, "B", ExcelErrorCodes.ImportDuplicateRowKey),
                    (Parts, 5, "B", ExcelErrorCodes.ImportDuplicateRowKey),
                    (Notes, 3, "A", ExcelErrorCodes.ImportOrphanChildRow),
                ],
                refused.Errors.Select(static error => (error.Sheet!, error.Row!.Value, error.Column!, error.Code)).OrderBy(static error => error.Item1 == Notes).ThenBy(static error => error.Code == ExcelErrorCodes.ImportDuplicateRowKey).ThenBy(static error => error.Value));
            Assert.Equal([-3L, null, -13L], refused.Errors.Where(static error => error.Code == ExcelErrorCodes.ImportOrphanChildRow).Select(static error => error.Arguments["value"]));
        }

        [Fact]
        public async Task An_owner_with_more_rows_than_a_collection_holds_at_depth_two_or_than_one_save_holds_is_refused()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            int notes = MaxChildrenAttribute.DefaultCount + 1;
            byte[] workbook = await WidgetsAsync(
                scope,
                [Widget(-1, "W-1"), Widget(-2, "W-2")],
                [Part(-1, -10, 1), Part(-2, -20, 2)],
                [.. Enumerable.Range(0, notes).Select(static index => Note(-10, $"n{index}")), Note(-20, "only")]);
            StackLimits limits = StackLimits.Default with { MaxRowsPerSave = notes };

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Widget>(workbook, scope.Context, limits: limits, synchronous: false));

            ImportError tooMany = Assert.Single(refused.Errors, static error => error.Code == ExcelErrorCodes.ImportTooManyChildren);
            Assert.Equal((Notes, MaxChildrenAttribute.DefaultCount + 2, "A"), (tooMany.Sheet, tooMany.Row, tooMany.Column));
            Assert.Equal(("Parts.PartNotes", MaxChildrenAttribute.DefaultCount, notes), (tooMany.Arguments["collection"], tooMany.Arguments["max"], tooMany.Arguments["count"]));
            ImportError tooLarge = Assert.Single(refused.Errors, static error => error.Code == ExcelErrorCodes.ImportTooManyDescendants);
            Assert.Equal((Widgets, 2), (tooLarge.Sheet, tooLarge.Row));
            Assert.Null(tooLarge.Column);
            Assert.Equal((notes + 2, notes), (tooLarge.Arguments["rows"], tooLarge.Arguments["max"]));
        }

        [Fact]
        public async Task A_child_matched_by_id_keeps_a_collection_whose_sheet_is_absent_and_a_replaced_one_is_refused_once()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] full = await WidgetsAsync(scope, [Widget(1, "W-1"), Widget(2, "W-2")], [Part(1, 10, 1), Part(2, 20, 2)]);
            byte[] workbook = WorkbookEdits.WithoutSheet(full, Notes);
            Widget[] stored =
            [
                Stored(1, "W-1", new WidgetPart { Id = 10, WidgetId = 1, Ordinal = 1, PartNotes = [new() { Id = 100, WidgetPartId = 10, Text = "kept" }] }),
                Stored(2, "W-2", new WidgetPart { Id = 20, WidgetId = 2, Ordinal = 2, PartNotes = [new() { Id = 200, WidgetPartId = 20, Text = "kept" }] }),
            ];
            ImportRequest update = new() { FileId = 1, Mode = ImportMode.Update };

            // --- Matched by id, a part keeps its notes: the absent sheet leaves them untouched ---
            ExcelImportSession<Widget> matched = await _kit.ParseAsync<Widget>(workbook, scope.Context, update);
            Assert.Equal([1L, 2L], matched.HydrationIds(0, 1, NoLookups));
            ExcelChunk<Widget> kept = matched.Resolve(0, 1, NoLookups, stored, NoCommitted);
            Assert.Empty(kept.Errors);
            Assert.All(kept.Entities, static widget => Assert.Null(Assert.Single(widget.Parts!).PartNotes));

            // --- Matched by neither its identity nor its natural key, the sheet replaces the parts: it must carry every column and collection ---
            update.ColumnMappings = [new ImportColumnMapping(Parts, "B", null), new ImportColumnMapping(Parts, "C", null)];
            ExcelImportSession<Widget> replaced = await _kit.ParseAsync<Widget>(workbook, scope.Context, update);
            ExcelChunk<Widget> chunk = replaced.Resolve(0, 1, NoLookups, stored, NoCommitted);

            ImportError incomplete = Assert.Single(chunk.Errors);
            Assert.Equal((ExcelErrorCodes.ImportIncompleteChildSheet, Parts), (incomplete.Code, incomplete.Sheet));
            Assert.Null(incomplete.Row);
            Assert.Equal("Parts", incomplete.Arguments["collection"]);
            Assert.Equal(["Ordinal"], (string[])incomplete.Arguments["missingColumns"]!);
            Assert.Equal(["Parts.PartNotes"], (string[])incomplete.Arguments["missingCollections"]!);
        }

        [Fact]
        public async Task An_update_overlays_the_stored_row_and_matches_its_children_by_id_and_the_map_places_every_path()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await WidgetsAsync(scope, [Widget(1, "W-1", name: "Renamed")], [Part(1, 10, 7), Part(1, -1, 9)], [Note(10, "new note")]);
            Widget stored = Stored(1, "W-1", new WidgetPart { Id = 10, WidgetId = 1, Ordinal = 1, Notes = "kept", PartNotes = [new() { Id = 100, WidgetPartId = 10, Text = "old" }] }, new WidgetPart { Id = 11, WidgetId = 1, Ordinal = 2, PartNotes = [] });

            ExcelImportSession<Widget> session = await _kit.ParseAsync<Widget>(workbook, scope.Context, new ImportRequest { FileId = 1, Mode = ImportMode.Update });
            ExcelChunk<Widget> chunk = session.Resolve(0, 0, NoLookups, [stored], NoCommitted);

            // --- The stored row overlaid, the part matched by id kept, the new one added, the absent one dropped ---
            Assert.Empty(chunk.Errors);
            Widget widget = Assert.Single(chunk.Entities);
            Assert.NotSame(stored, widget);
            Assert.Equal((1, "Renamed", "subject-1", ExcelTestKit.Now), (widget.Id, widget.Name, widget.Subject, widget.ModifiedAt));
            Assert.Equal([(10, 7), (0, 9)], widget.Parts!.Select(static part => (part.Id, part.Ordinal)));
            Assert.Equal([(0, "new note")], widget.Parts![0].PartNotes!.Select(static note => (note.Id, note.Text)));
            Assert.Equal((0, 1, 2, 1, 2), (chunk.Facts.Inserted, chunk.Facts.Updated, chunk.Facts.ChildrenInserted, chunk.Facts.ChildrenUpdated, chunk.Facts.ChildrenDeleted));

            // --- Every path the save may report is placed at the cell that produced it ---
            ValidationPath part = ValidationPath.Root.Index(0).Property("Parts").Index(1);
            Assert.Equal(new ExcelCell(Widgets, 2, null), chunk.CoordinateMap[ValidationPath.Root.Index(0)]);
            Assert.Equal(new ExcelCell(Widgets, 2, "E"), chunk.CoordinateMap[ValidationPath.Root.Index(0).Property("Name")]);
            Assert.Equal(new ExcelCell(Parts, 3, "C"), chunk.CoordinateMap[part.Property("Ordinal")]);
            ImportError translated = session.Core.Translate(new ValidationError(ValidationPath.Root.Index(0).Property("Parts").Index(0).Property("PartNotes").Index(0).Property("Text"), "MaxLength", new Dictionary<string, object?> { ["max"] = 1 }), chunk.CoordinateMap);
            Assert.Equal((Notes, 2, "C", "Text", "MaxLength"), (translated.Sheet, translated.Row, translated.Column, translated.Property, translated.Code));
            Assert.NotNull(translated.Header);
            ImportError whole = session.Core.Translate(new ValidationError(ValidationPath.Root, "Whole", new Dictionary<string, object?>()), chunk.CoordinateMap);
            Assert.Null(whole.Sheet);
            Assert.Null(whole.Row);
            Assert.Null(whole.Column);
        }

        [Fact]
        public async Task The_errors_reported_are_capped_in_sheet_row_and_column_order_with_the_total_uncapped()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await NodesAsync(scope, [.. Enumerable.Range(0, 6).Select(static _ => Node(code: "same"))]);

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Node>(workbook, scope.Context, options: new ExcelOptions { MaxReportedErrors = 4 }));

            Assert.Equal(6, refused.TotalErrors);
            Assert.Equal([2, 3, 4, 5], refused.Errors.Select(static error => error.Row!.Value));
            Assert.Empty(refused.CommittedRowRanges);
        }

        [Fact]
        public async Task A_synchronous_import_counts_the_rows_it_reads_whatever_the_sheet_declares_and_refuses_beyond_its_limit()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await NodesAsync(scope, Node(code: "A"), Node(code: "B"), Node(code: "C"));

            ImportException refused = await Assert.ThrowsAsync<ImportException>(() => _kit.ParseAsync<Node>(workbook, scope.Context, options: new ExcelOptions { MaxSynchronousImportRows = 2 }));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal(ExcelErrorCodes.ImportTooLargeForSynchronous, error.Code);
            Assert.Equal((3L, 2L), (error.Arguments["rows"], error.Arguments["maxRows"]));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        /// <summary>A node row.</summary>
        /// <param name="id">The sheet identity; blank when null.</param>
        /// <param name="code">The code.</param>
        /// <param name="parent">The parent's code, or its identity under an override; none when null.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Node(long? id = null, string? code = null, object? parent = null)
        {
            return new(StringComparer.Ordinal)
            {
                ["Id"] = id,
                ["Code"] = code,
                ["Name"] = $"Node {code}",
                ["Parent.Code"] = parent,
            };
        }

        /// <summary>A widget row.</summary>
        /// <param name="id">The sheet identity.</param>
        /// <param name="code">The code.</param>
        /// <param name="name">The English name; the code's when null.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Widget(long id, string code, string? name = null)
        {
            return new(StringComparer.Ordinal)
            {
                ["Id"] = id,
                ["Stamp"] = ExcelTestKit.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["Code"] = code,
                ["Kind"] = nameof(WidgetKind.Plain),
                ["Name"] = name ?? code,
                ["Subject"] = $"subject-{id}",
            };
        }

        /// <summary>A part row.</summary>
        /// <param name="widget">The owner's sheet identity; blank when null.</param>
        /// <param name="id">The part's sheet identity.</param>
        /// <param name="ordinal">The ordinal.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Part(long? widget, long id, int ordinal)
        {
            return new(StringComparer.Ordinal) { ["Widget.Id"] = widget, ["Id"] = id, ["Ordinal"] = ordinal };
        }

        /// <summary>A note row.</summary>
        /// <param name="part">The owner part's sheet identity.</param>
        /// <param name="text">The text.</param>
        /// <returns>The row by path.</returns>
        private static Dictionary<string, object?> Note(long part, string text)
        {
            return new(StringComparer.Ordinal) { ["WidgetPart.Id"] = part, ["Text"] = text };
        }

        /// <summary>A stored widget as a hydration reads it, with its parts.</summary>
        /// <param name="id">The id.</param>
        /// <param name="code">The code.</param>
        /// <param name="parts">The stored parts.</param>
        /// <returns>The widget.</returns>
        private static Widget Stored(int id, string code, params WidgetPart[] parts)
        {
            return new Widget
            {
                Id = id,
                Code = code,
                Kind = WidgetKind.Plain,
                Name = code,
                Subject = $"subject-{id}",
                ModifiedAt = ExcelTestKit.Now.AddDays(-1),
                Parts = [.. parts],
            };
        }

        /// <summary>A node template with its rows.</summary>
        /// <param name="scope">The exporting scope.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The workbook.</returns>
        private async Task<byte[]> NodesAsync(ExportScope scope, params Dictionary<string, object?>[] rows)
        {
            byte[] template = await _kit.ExportAsync<Node>(scope.Context, rows: 0);
            ImportPlan plan = await _kit.InspectAsync<Node>(template, scope.Context);
            return WorkbookEdits.WithRows(template, plan, (Nodes, rows));
        }

        /// <summary>A widget template with its rows on each sheet.</summary>
        /// <param name="scope">The exporting scope.</param>
        /// <param name="widgets">The parent rows.</param>
        /// <param name="parts">The part rows.</param>
        /// <param name="notes">The note rows.</param>
        /// <returns>The workbook.</returns>
        private async Task<byte[]> WidgetsAsync(ExportScope scope, Dictionary<string, object?>[] widgets, Dictionary<string, object?>[]? parts = null, Dictionary<string, object?>[]? notes = null)
        {
            byte[] template = await _kit.ExportAsync<Widget>(scope.Context, rows: 0);
            ImportPlan plan = await _kit.InspectAsync<Widget>(template, scope.Context);
            return WorkbookEdits.WithRows(template, plan, (Widgets, widgets), (Parts, parts ?? []), (Notes, notes ?? []));
        }
    }
}
