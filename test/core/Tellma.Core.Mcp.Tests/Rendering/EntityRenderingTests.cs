// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Mcp.Rendering;

namespace Tellma.Core.Mcp.Tests.Rendering
{
    /// <summary>
    ///     The record renderings: records by id with their collections windowed at every depth before
    ///     any JSON is written, the first record's header kept whatever its lines weigh, the work done
    ///     bounded by the room and never by the rows loaded, the trailing lines always under the cap;
    ///     and one collection's window.
    /// </summary>
    public sealed class EntityRenderingTests
    {
        /// <summary>The shape of a line: its sub-lines.</summary>
        private static readonly RecordShape LineShape = new([new CollectionShape("subs", typeof(Line).GetProperty(nameof(Line.Subs))!, RecordShape.Empty)]);

        /// <summary>The shape of a document: its lines, and theirs.</summary>
        private static readonly RecordShape DocShape = new([new CollectionShape("lines", typeof(Doc).GetProperty(nameof(Doc.Lines))!, LineShape)]);

        [Fact]
        public void A_record_s_collection_is_windowed_with_its_total_and_an_absent_id_is_reported()
        {
            StackRecords records = Records(NewDoc(1, lines: 5));

            RenderedText rendered = EntityRendering.Records(records, [1, 7], DocShape, top: 2, 60000);

            using var document = JsonDocument.Parse(rendered.Text[..rendered.Text.IndexOf('\n', StringComparison.Ordinal)]);
            JsonElement record = document.RootElement.GetProperty("entities")[0];
            Assert.Equal(["id", "code", EntityRendering.CollectionsMember, "lines"], record.EnumerateObject().Select(static member => member.Name));
            Assert.Equal(2, record.GetProperty("lines").GetArrayLength());
            Assert.Equal(/*lang=json,strict*/ """{"total":5,"returned":2,"truncated":true}""", record.GetProperty(EntityRendering.CollectionsMember).GetProperty("lines").GetRawText());
            Assert.EndsWith("absent: 7 — no record has these ids, or you may not see them.", rendered.Text, StringComparison.Ordinal);
            Assert.False(rendered.Truncated);
        }

        [Fact]
        public void A_kept_row_s_own_collections_are_windowed_and_described_too()
        {
            StackRecords records = Records(NewDoc(1, lines: 3, subs: 10));

            RenderedText rendered = EntityRendering.Records(records, [1], DocShape, top: 2, 60000);

            using var document = JsonDocument.Parse(rendered.Text);
            JsonElement line = document.RootElement.GetProperty("entities")[0].GetProperty("lines")[0];
            Assert.Equal(2, line.GetProperty("subs").GetArrayLength());
            Assert.Equal(/*lang=json,strict*/ """{"total":10,"returned":2,"truncated":true}""", line.GetProperty(EntityRendering.CollectionsMember).GetProperty("subs").GetRawText());
        }

        [Fact]
        public void A_first_record_whose_lines_pass_the_cap_keeps_its_header_and_the_rest_are_named_by_id()
        {
            StackRecords records = Records(NewDoc(1, lines: 400), NewDoc(2, lines: 400));

            RenderedText rendered = EntityRendering.Records(records, [1, 2], DocShape, top: 400, 4000);

            Assert.True(rendered.Truncated);
            Assert.True(rendered.Text.Length <= 4000);
            Assert.StartsWith("{\"entities\":[{\"id\":\"1\",\"code\":\"R1\"", rendered.Text, StringComparison.Ordinal);
            Assert.Contains("get the rest by id: 2.", rendered.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_record_holding_ten_thousand_lines_costs_the_rows_its_window_keeps()
        {
            ReadCounter counter = new();
            StackRecords records = Records(NewDoc(1, lines: 10000, counter: counter));

            RenderedText rendered = EntityRendering.Records(records, [1], DocShape, top: 5, 60000);

            // Each kept line's memo is read once, when it is written; no other line is ever written.
            Assert.Equal(5, counter.Reads);
            Assert.Contains("\"lines\":{\"total\":10000,\"returned\":5,\"truncated\":true}", rendered.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_value_past_its_room_stops_being_written_once_the_room_is_spent()
        {
            ReadCounter counter = new();
            List<Line> lines = [.. Enumerable.Range(1, 100000).Select(n => new Line(counter) { N = n, Memo = new string('m', 30) })];

            Assert.Null(BoundedJson.TryWrite(lines, lines.GetType(), 2000));

            // A line writes about fifty characters: the room's bytes and one growth step stop the write within a few hundred lines.
            Assert.InRange(counter.Reads, 1, 400);
            Assert.Equal("[]", BoundedJson.TryWrite(new List<Line>(), typeof(List<Line>), 2));
        }

        [Fact]
        public void The_trailing_lines_never_pass_the_cap_however_many_ids_they_name()
        {
            // --- Fifty records read, fifty ids absent, each id of nineteen digits ---
            long[] requested = [.. Enumerable.Range(0, 100).Select(static index => 1_000_000_000_000_000_000L + index)];
            StackRecords records = Records([.. requested.Take(50).Select(static id => NewDoc(id, lines: 3))]);

            for (int maxChars = 1000; maxChars <= 8000; maxChars += 97)
            {
                RenderedText rendered = EntityRendering.Records(records, requested, DocShape, top: 3, maxChars);

                Assert.True(rendered.Text.Length <= maxChars, string.Create(CultureInfo.InvariantCulture, $"{rendered.Text.Length} > {maxChars}"));
                Assert.Contains("absent: ", rendered.Text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void A_list_of_ids_names_what_fits_and_counts_the_rest()
        {
            Assert.Equal("1, 2, 3", EntityRendering.Ids([1, 2, 3], 100));
            Assert.Equal("1, 2 and 1 more", EntityRendering.Ids([1, 2, 3], 4));
            Assert.Equal("3 ids", EntityRendering.Ids([100, 200, 300], 2));
        }

        [Fact]
        public void A_collection_window_pages_from_its_skip()
        {
            StackRecords records = Records(NewDoc(1, lines: 5));

            RenderedText rendered = EntityRendering.Collection(records, 1, DocShape.Collections[0], skip: 2, top: 2, 60000);

            using var document = JsonDocument.Parse(rendered.Text[..rendered.Text.IndexOf("\n\n", StringComparison.Ordinal)]);
            Assert.Equal((5, 2), (document.RootElement.GetProperty("total").GetInt32(), document.RootElement.GetProperty("skip").GetInt32()));
            Assert.Equal([3, 4], document.RootElement.GetProperty("rows").EnumerateArray().Select(static row => row.GetProperty("n").GetInt32()));
            Assert.EndsWith("More rows follow: page with skip=4.", rendered.Text, StringComparison.Ordinal);
        }

        /// <summary>Records as the service answers them, every one read.</summary>
        /// <param name="docs">The records.</param>
        /// <returns>The records.</returns>
        private static StackRecords Records(params Doc[] docs)
        {
            return new StackRecords([.. docs.Select(static doc => doc.Id)], [.. docs.Select(static doc => new StackRecord(doc.Id, doc))], null, null);
        }

        /// <summary>A document with numbered lines, each with numbered sub-lines.</summary>
        /// <param name="id">The document's id.</param>
        /// <param name="lines">The lines it holds.</param>
        /// <param name="subs">The sub-lines each line holds.</param>
        /// <param name="counter">Counts the reads of the lines' memos.</param>
        /// <returns>The document.</returns>
        private static Doc NewDoc(long id, int lines, int subs = 0, ReadCounter? counter = null)
        {
            ReadCounter reads = counter ?? new ReadCounter();
            return new Doc
            {
                Id = id,
                Code = "R" + id.ToString(CultureInfo.InvariantCulture),
                Lines = [.. Enumerable.Range(1, lines).Select(n => new Line(reads)
                {
                    N = n,
                    Memo = new string('m', 30),
                    Subs = subs == 0 ? null : [.. Enumerable.Range(1, subs).Select(static k => new Sub { K = k })],
                })],
            };
        }
    }

    /// <summary>Counts reads of a value, to show what a rendering wrote.</summary>
    internal sealed class ReadCounter
    {
        /// <summary>The reads.</summary>
        private int _reads;

        /// <summary>The reads so far.</summary>
        public int Reads => _reads;

        /// <summary>Counts a read.</summary>
        public void Count()
        {
            _ = Interlocked.Increment(ref _reads);
        }
    }

    /// <summary>A document: a header and its lines.</summary>
    internal sealed class Doc
    {
        /// <summary>The id.</summary>
        public long Id { get; set; }

        /// <summary>The code.</summary>
        public string? Code { get; set; }

        /// <summary>The lines.</summary>
        public List<Line>? Lines { get; set; }
    }

    /// <summary>A document's line, whose memo counts its reads.</summary>
    /// <param name="counter">Counts the memo's reads.</param>
    internal sealed class Line(ReadCounter counter)
    {
        /// <summary>The line's number.</summary>
        public int N { get; set; }

        /// <summary>The memo; each read is counted.</summary>
        public string? Memo
        {
            get
            {
                counter.Count();
                return field;
            }

            set;
        }

        /// <summary>The sub-lines.</summary>
        public List<Sub>? Subs { get; set; }
    }

    /// <summary>A line's sub-line.</summary>
    internal sealed class Sub
    {
        /// <summary>The sub-line's number.</summary>
        public int K { get; set; }
    }
}
