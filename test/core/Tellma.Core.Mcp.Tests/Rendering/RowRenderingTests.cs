// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Mcp.Rendering;
using Tellma.Core.Queryex;

namespace Tellma.Core.Mcp.Tests.Rendering
{
    /// <summary>
    ///     The row renderings: a page of rows as a Markdown table or JSON objects with its footer,
    ///     truncated at a row boundary under the cap.
    /// </summary>
    public sealed class RowRenderingTests
    {
        [Fact]
        public void A_table_escapes_its_cells_leaves_an_absent_value_empty_and_reports_the_count()
        {
            RowPage page = Page(count: 12, ("A|1", 1.50m), ("line\nbreak", null));

            RenderedText rendered = RowRendering.Render(page, QueryFormat.Table, new RowPaging(0, 50, Counted: true), 60000);

            Assert.False(rendered.Truncated);
            Assert.Equal(
                "| Code | Amount |\n| --- | --- |\n| A\\|1 | 1.50 |\n| line break |  |\n\nreturned: 2 rows from skip 0\ncount: 12, countCapped: false",
                rendered.Text);
        }

        [Fact]
        public void Objects_are_keyed_by_column_and_a_full_page_says_more_may_follow()
        {
            RowPage page = Page(count: null, ("A", 1m), ("B", null));

            RenderedText rendered = RowRendering.Render(page, QueryFormat.Objects, new RowPaging(10, 2, Counted: false), 60000);

            string json = rendered.Text[..rendered.Text.IndexOf("\n\n", StringComparison.Ordinal)];
            JsonArray rows = JsonNode.Parse(json)!.AsArray();
            Assert.Equal("A", rows[0]!["Code"]!.GetValue<string>());
            Assert.Equal(2, rows.Count);
            Assert.EndsWith("returned: 2 rows from skip 10\nThe page is full, so more rows may follow: page with skip=12.", rendered.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_page_past_the_cap_stops_at_a_row_boundary_with_the_skip_that_pages_on()
        {
            (string, decimal?)[] many = [.. Enumerable.Range(0, 200).Select(static index => (new string('x', 40) + index, (decimal?)index))];
            RowPage page = Page(count: null, many);

            RenderedText rendered = RowRendering.Render(page, QueryFormat.Table, new RowPaging(0, 200, Counted: false), 3000);

            Assert.True(rendered.Truncated);
            Assert.True(rendered.Text.Length <= 3000, rendered.Text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            int returned = rendered.Text.Split('\n').Count(static line => line.StartsWith("| x", StringComparison.Ordinal));
            Assert.Contains($"truncated: true — the result reached its size limit after {returned} of 200 rows; {RowRendering.TruncatedGuidance}={returned}.", rendered.Text, StringComparison.Ordinal);
        }

        /// <summary>A page of two columns, a string code and a decimal amount.</summary>
        /// <param name="count">The page's count, if counted.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The page.</returns>
        private static RowPage Page(int? count, params (string Code, decimal? Amount)[] rows)
        {
            QueryColumn[] columns =
            [
                new("Code", QueryexType.QxString, null, null, Nullable: true, ["Code"], GroupingKey: false),
                new("Amount", QueryexType.QxNumeric, 2, 19, Nullable: true, ["Amount"], GroupingKey: false),
            ];
            using DataTable table = new();
            _ = table.Columns.Add("Code", typeof(string));
            _ = table.Columns.Add("Amount", typeof(decimal));
            foreach ((string code, decimal? amount) in rows)
            {
                _ = table.Rows.Add(code, amount is decimal value ? value : DBNull.Value);
            }

            QueryRowSet.Builder builder = new(columns, [typeof(string), typeof(decimal)], rows.Length);
            using DataTableReader reader = table.CreateDataReader();
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return new RowPage(builder.Build(), count, null, count is null ? null : 10000);
        }
    }
}
