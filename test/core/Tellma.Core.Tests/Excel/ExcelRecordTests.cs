// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The workbook records a stack and the writer exchange: the request context, column specs,
    ///     sheet and workbook plans, and the checkpoint a background import resumes from, with the
    ///     guards each keeps and the JSON the checkpoint is stored as.
    /// </summary>
    /// <param name="composition">The fixture model, composed once for the class.</param>
    public sealed class ExcelRecordTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        /// <summary>A query any sheet plan can carry.</summary>
        private static readonly ExcelQuery Query = new("Widget", ["Code"], null, null, null, null, null, null);

        /// <summary>The widget's metadata.</summary>
        private EntityMetadata Widgets => composition.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Widget));

        [Fact]
        public void A_context_refuses_a_missing_tenant_deployment_or_language()
        {
            ExcelContext context = Context();

            _ = Assert.Throws<ArgumentOutOfRangeException>(() => context with { TenantId = 0 });
            _ = Assert.Throws<ArgumentException>(() => context with { DeploymentId = " " });
            _ = Assert.Throws<ArgumentException>(() => context with { TenantLanguages = [] });
            _ = Assert.Throws<ArgumentNullException>(() => context with { Culture = null! });
            _ = Assert.Throws<ArgumentNullException>(() => context with { Labels = null! });
        }

        [Fact]
        public void A_column_spec_joins_its_path_and_refuses_a_blank_segment_or_an_impossible_scale()
        {
            ExcelColumnSpec column = new("Customer", QueryexType.QxString, null, ["Customer", "Code"]);

            Assert.Equal("Customer.Code", column.PathText);
            Assert.Equal(ExcelColumnRole.Property, column.Role);
            Assert.Null(Computed(QueryexType.QxNumeric).PathText);
            _ = Assert.Throws<ArgumentException>(() => column with { Path = ["Customer", " "] });
            _ = Assert.Throws<ArgumentException>(() => column with { Path = [] });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => column with { Scale = 29 });
            _ = Assert.Throws<ArgumentNullException>(() => column with { Header = null! });
        }

        [Fact]
        public void A_sheet_plan_has_a_number_format_slot_per_column()
        {
            ExcelSheetPlan sheet = new("Widgets", null, [Computed(QueryexType.QxString)], [null], Query);

            _ = Assert.Throws<ArgumentException>(() => sheet with { NumberFormats = [] });
            _ = Assert.Throws<ArgumentException>(() => sheet with { Columns = [] });
            _ = Assert.Throws<ArgumentException>(() => sheet with { Name = "" });
            _ = Assert.Throws<ArgumentException>(() => sheet with { Collection = " " });
        }

        [Fact]
        public void A_workbook_plan_is_an_entity_shape_with_the_parent_sheet_first_and_only_first()
        {
            ExcelSheetPlan parent = new("Widgets", null, [Computed(QueryexType.QxString)], [null], Query);
            ExcelSheetPlan child = parent with { Name = "Parts", Collection = "Parts" };
            ExcelWorkbookPlan plan = new(Widgets, ExportKind.ForImport, "Widgets.xlsx", [parent, child], new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });

            // The manifest keeps the order it was given in.
            Assert.Equal(["b", "a"], plan.Manifest.Keys);
            _ = Assert.Throws<ArgumentException>(() => plan with { Shape = ExportKind.Report });
            _ = Assert.Throws<ArgumentException>(() => plan with { Sheets = [child, parent] });
            _ = Assert.Throws<ArgumentException>(() => plan with { Sheets = [parent, parent] });
            _ = Assert.Throws<ArgumentException>(() => plan with { Sheets = [] });
            _ = Assert.Throws<ArgumentException>(() => plan with { FileName = " " });
        }

        [Fact]
        public void A_checkpoint_stores_its_committed_row_ranges_and_new_ids_as_json_and_reads_them_back()
        {
            ImportCheckpointState state = new(
                NextRow: 102,
                OrderHash: "c0ffee",
                Inserted: 90,
                Updated: 10,
                ChildrenInserted: 4,
                ChildrenUpdated: 0,
                ChildrenDeleted: 1,
                CommittedRowRanges: [new RowRange(2, 101)],
                CommittedNewIds: new Dictionary<long, long> { [5] = 9_007_199_254_740_993 });

            string json = JsonSerializer.Serialize(state, TellmaJson.Options);
            ImportCheckpointState back = JsonSerializer.Deserialize<ImportCheckpointState>(json, TellmaJson.Options)!;

            Assert.Contains("""
                "committedRowRanges":[{"from":2,"to":101}]
                """, json, StringComparison.Ordinal);
            Assert.Equal(state.CommittedRowRanges, back.CommittedRowRanges);
            Assert.Equal(9_007_199_254_740_993, back.CommittedNewIds[5]);
            Assert.Equal((102, "c0ffee", 90, 10, 4, 0, 1), (back.NextRow, back.OrderHash, back.Inserted, back.Updated, back.ChildrenInserted, back.ChildrenUpdated, back.ChildrenDeleted));
        }

        [Fact]
        public void A_checkpoint_refuses_negative_counts_and_copies_what_it_is_given()
        {
            Dictionary<long, long> ids = new() { [1] = 2 };
            ImportCheckpointState state = new(2, "hash", 0, 0, 0, 0, 0, [], ids);
            ids[3] = 4;

            Assert.Single(state.CommittedNewIds);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => state with { NextRow = -1 });
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => state with { ChildrenDeleted = -1 });
            _ = Assert.Throws<ArgumentNullException>(() => state with { OrderHash = null! });
            _ = Assert.Throws<ArgumentException>(() => state with { CommittedRowRanges = [null!] });
        }
    }
}
