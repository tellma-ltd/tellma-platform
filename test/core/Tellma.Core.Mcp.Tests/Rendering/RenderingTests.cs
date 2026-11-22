// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Mcp.Rendering;
using Tellma.Core.Queryex;

namespace Tellma.Core.Mcp.Tests.Rendering
{
    /// <summary>
    ///     The result renderings: a page of rows as a Markdown table or JSON objects with its footer,
    ///     truncated at a row boundary under the cap; records by id with their collections windowed,
    ///     the first record's header kept whatever its lines weigh; and one collection's window.
    /// </summary>
    public sealed class RenderingTests
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

        [Fact]
        public void A_record_s_collection_is_windowed_with_its_total_and_an_absent_id_is_reported()
        {
            JsonObject result = Result([Record(1, lines: 5)], ids: [1]);

            RenderedText rendered = EntityRendering.Records(result, [1, 7], new ChildWindows(["lines"], Top: 2), 60000);

            using var document = JsonDocument.Parse(rendered.Text[..rendered.Text.IndexOf('\n', StringComparison.Ordinal)]);
            JsonElement record = document.RootElement.GetProperty("entities")[0];
            Assert.Equal(["id", "code", EntityRendering.CollectionsMember, "lines"], record.EnumerateObject().Select(static member => member.Name));
            Assert.Equal(2, record.GetProperty("lines").GetArrayLength());
            Assert.Equal(/*lang=json,strict*/ """{"total":5,"returned":2,"truncated":true}""", record.GetProperty(EntityRendering.CollectionsMember).GetProperty("lines").GetRawText());
            Assert.EndsWith("absent: 7 — no record has these ids, or you may not see them.", rendered.Text, StringComparison.Ordinal);
            Assert.False(rendered.Truncated);
        }

        [Fact]
        public void A_first_record_whose_lines_pass_the_cap_keeps_its_header_and_the_rest_are_named_by_id()
        {
            JsonObject result = Result([Record(1, lines: 400), Record(2, lines: 400)], ids: [1, 2]);

            RenderedText rendered = EntityRendering.Records(result, [1, 2], new ChildWindows(["lines"], Top: 400), 4000);

            Assert.True(rendered.Truncated);
            Assert.True(rendered.Text.Length <= 4000);
            Assert.StartsWith("{\"entities\":[{\"id\":1,\"code\":\"R1\"", rendered.Text, StringComparison.Ordinal);
            Assert.Contains("get the rest by id: 2.", rendered.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_collection_window_pages_from_its_skip()
        {
            JsonObject result = Result([Record(1, lines: 5)], ids: [1]);

            RenderedText rendered = EntityRendering.Collection(result, 1, "lines", skip: 2, top: 2, 60000);

            using var document = JsonDocument.Parse(rendered.Text[..rendered.Text.IndexOf("\n\n", StringComparison.Ordinal)]);
            Assert.Equal((5, 2), (document.RootElement.GetProperty("total").GetInt32(), document.RootElement.GetProperty("skip").GetInt32()));
            Assert.Equal([3, 4], document.RootElement.GetProperty("rows").EnumerateArray().Select(static row => row.GetProperty("n").GetInt32()));
            Assert.EndsWith("More rows follow: page with skip=4.", rendered.Text, StringComparison.Ordinal);
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

        /// <summary>A record with a code and a collection of numbered lines.</summary>
        /// <param name="id">The record's id.</param>
        /// <param name="lines">The lines it holds.</param>
        /// <returns>The record.</returns>
        private static JsonObject Record(long id, int lines)
        {
            return new JsonObject
            {
                ["id"] = id,
                ["code"] = "R" + id,
                ["lines"] = new JsonArray([.. Enumerable.Range(1, lines).Select(static n => (JsonNode)new JsonObject { ["n"] = n, ["memo"] = new string('m', 30) })]),
            };
        }

        /// <summary>An entities result.</summary>
        /// <param name="entities">The records.</param>
        /// <param name="ids">The ids it reports.</param>
        /// <returns>The result.</returns>
        private static JsonObject Result(JsonObject[] entities, long[] ids)
        {
            return new JsonObject
            {
                ["ids"] = new JsonArray([.. ids.Select(static id => (JsonNode)id)]),
                ["entities"] = new JsonArray([.. entities]),
            };
        }
    }
}
