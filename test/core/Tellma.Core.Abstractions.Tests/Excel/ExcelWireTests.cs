// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Tests.Excel
{
    /// <summary>
    ///     The workbook features' wire records: the guards an import plan and outcome keep, and the
    ///     JSON the platform's options write for them.
    /// </summary>
    public class ExcelWireTests
    {
        /// <summary>No arguments.</summary>
        private static readonly Dictionary<string, object?> None = [];

        /// <summary>The platform's JSON options.</summary>
        private static JsonSerializerOptions Options => TellmaJson.Options;

        [Fact]
        public void A_row_range_is_written_as_from_and_to()
        {
            string json = JsonSerializer.Serialize<IReadOnlyList<RowRange>>([new RowRange(2, 101)], Options);

            Assert.Equal(/*lang=json,strict*/ """[{"from":2,"to":101}]""", json);
            Assert.Equal([new RowRange(2, 101)], JsonSerializer.Deserialize<RowRange[]>(json, Options)!);
        }

        [Fact]
        public void An_import_outcome_round_trips_with_its_committed_row_ranges()
        {
            ImportOutcome outcome = new(3, 1, 4, 0, 2, [new RowRange(2, 101), new RowRange(103, 103)], [], Stopped: true);

            string json = JsonSerializer.Serialize(outcome, Options);
            ImportOutcome back = JsonSerializer.Deserialize<ImportOutcome>(json, Options)!;

            Assert.Contains("""
                "committedRowRanges":[{"from":2,"to":101},{"from":103,"to":103}]
                """, json, StringComparison.Ordinal);
            Assert.Equal(outcome.CommittedRowRanges, back.CommittedRowRanges);
            Assert.Equal((3, 1, 4, 0, 2, true), (back.Inserted, back.Updated, back.ChildrenInserted, back.ChildrenUpdated, back.ChildrenDeleted, back.Stopped));
        }

        [Fact]
        public void An_import_failures_committed_row_ranges_are_written_the_same_way()
        {
            ImportException failure = new([new ImportError("Widgets", 104, "B", "Name", "Name", "Required", None)], 1, [new RowRange(2, 101)]);

            Assert.Equal(/*lang=json,strict*/ """[{"from":2,"to":101}]""", JsonSerializer.Serialize(failure.CommittedRowRanges, Options));
        }

        [Fact]
        public void An_import_outcome_refuses_negative_counts_and_null_entries()
        {
            ImportOutcome outcome = new(0, 0, 0, 0, 0, [], []);

            Assert.False(outcome.Stopped);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ImportOutcome(-1, 0, 0, 0, 0, [], []));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => outcome with { ChildrenDeleted = -1 });
            _ = Assert.Throws<ArgumentNullException>(() => new ImportOutcome(0, 0, 0, 0, 0, null!, []));
            _ = Assert.Throws<ArgumentException>(() => new ImportOutcome(0, 0, 0, 0, 0, [], [null!]));
        }

        [Fact]
        public void An_import_plan_lists_its_sheets_and_columns_and_refuses_what_cannot_be()
        {
            ImportPlanColumn column = new("B", "Name", "Name", ImportColumnStatus.Mapped, "en", ["Name"]);
            ImportPlanSheet sheet = new("Widgets", null, ImportSheetStatus.Parent, 10, [column]);
            ImportPlan plan = new("Widget", 1, true, true, true, [sheet], ["Code"], "Code", 10, false, true, []);

            Assert.Equal(column, Assert.Single(Assert.Single(plan.Sheets).Columns));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => plan with { TotalRows = -1 });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => plan with { ManifestVersion = -1 });
            _ = Assert.Throws<ArgumentException>(() => plan with { Sheets = [null!] });
            _ = Assert.Throws<ArgumentException>(() => sheet with { Sheet = " " });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => sheet with { Rows = -1 });
            _ = Assert.Throws<ArgumentException>(() => column with { Column = "" });
            _ = Assert.Throws<ArgumentNullException>(() => column with { Header = null! });

            // A blank header is a header the workbook has; only a missing one is refused.
            Assert.Equal(string.Empty, (column with { Header = string.Empty }).Header);
        }

        [Fact]
        public void An_import_plan_writes_its_statuses_by_name_and_its_row_counts_as_strings()
        {
            ImportPlanSheet sheet = new("Parts", "Parts", ImportSheetStatus.Child, 2, [new("A", "Id", "Id", ImportColumnStatus.ServerOwned, null, [])]);

            string json = JsonSerializer.Serialize(sheet, Options);

            Assert.Contains("\"status\":\"Child\"", json, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"ServerOwned\"", json, StringComparison.Ordinal);
            Assert.Contains("\"rows\":\"2\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_start_import_request_reads_the_import_request_and_its_atomic_flag()
        {
            const string Json = """
                {"fileId":7,"mode":"Upsert","rowKey":"Code","sheetMappings":[{"sheet":"Widgets","collection":null}],
                 "columnMappings":[{"sheet":"Widgets","column":"C","path":null}],"ignoreUnmappedColumns":true,"atomic":true}
                """;

            StartImportRequest request = JsonSerializer.Deserialize<StartImportRequest>(Json, Options)!;

            Assert.Equal(7, request.FileId);
            Assert.Equal(ImportMode.Upsert, request.Mode);
            Assert.True(request.Atomic);
            Assert.True(request.IgnoreUnmappedColumns);
            Assert.Equal(new ImportColumnMapping("Widgets", "C", null), Assert.Single(request.ColumnMappings));
        }
    }
}
