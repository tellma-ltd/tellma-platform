// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Text;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The bounds every part is read through: its declared length, the longest run from one
    ///     '&lt;' to the next, a markup section counted whole, wherever the reads happen to split it,
    ///     and the refusal of U+0000, which every UCS-4 part holds.
    /// </summary>
    public sealed class BoundedPartStreamTests
    {
        /// <summary>The encodings a part's markup is found in: UTF-8, and UTF-16 of either byte order, marked or not.</summary>
        public static readonly TheoryData<string> Encodings = ["utf-8", "utf-16le", "utf-16le bom", "utf-16be", "utf-16be bom"];

        /// <summary>
        ///     The byte orders an XML reader decodes UCS-4 in, each named by where the big-endian bytes
        ///     1 to 4 of a unit lie, marked or not, and read whole or a byte at a time.
        /// </summary>
        public static readonly TheoryData<string, bool, int> Ucs4Orders = new()
        {
            { "1234", false, 4096 }, { "1234", true, 4096 }, { "4321", false, 4096 }, { "4321", true, 4096 },
            { "2143", false, 4096 }, { "3412", false, 4096 }, { "1234", true, 1 }, { "4321", true, 1 },
        };

        /// <summary>
        ///     Each section's fillers dense in its terminator's units without forming it, each in UTF-8
        ///     and in UTF-16 of either byte order, read a byte at a time, in reads that split units and
        ///     terminators at every offset, and in reads that engage the vector search.
        /// </summary>
        public static readonly TheoryData<string, string, string, string, int> MarkDenseFloods = MarkDense();

        /// <summary>Each kind of section, in UTF-8 and in UTF-16 of either byte order, in reads that engage the vector search.</summary>
        public static readonly TheoryData<string, string, string, int> SectionsByEncoding = new()
        {
            { "utf-8", "<![CDATA[", "]]>", 4096 }, { "utf-8", "<!--", "-->", 4096 }, { "utf-8", "<?pi ", "?>", 4096 },
            { "utf-16le", "<![CDATA[", "]]>", 4096 }, { "utf-16le", "<!--", "-->", 65536 }, { "utf-16le", "<?pi ", "?>", 4097 },
            { "utf-16be bom", "<![CDATA[", "]]>", 4097 }, { "utf-16be bom", "<!--", "-->", 4096 }, { "utf-16be bom", "<?pi ", "?>", 65536 },
        };

        [Theory]
        [InlineData(4096)]
        [InlineData(BoundedPartStream.MaxTokenBytes + 4096)]
        public void A_run_over_the_bound_is_refused_however_the_reads_split_it(int chunk)
        {
            // The run ends one chunk after it started, inside a chunk of further markup.
            byte[] part = Encoding.UTF8.GetBytes("<a>" + new string(' ', BoundedPartStream.MaxTokenBytes + 1) + "</a><b/>");

            ImportException refused = Assert.Throws<ImportException>(() => Drain(part, chunk));

            Assert.Equal(ImportFaults.XmlReason, Assert.Single(refused.Errors).Arguments["reason"]);
        }

        [Fact]
        public void A_run_over_the_bound_between_two_delimiters_of_one_read_is_refused()
        {
            byte[] part = Encoding.UTF8.GetBytes("<a>" + new string(' ', BoundedPartStream.MaxTokenBytes + 1) + "</a>");

            _ = Assert.Throws<ImportException>(() => Drain(part, part.Length));
        }

        [Theory]
        [InlineData(4096)]
        [InlineData(BoundedPartStream.MaxTokenBytes + 4096)]
        public void A_run_at_the_bound_is_read(int chunk)
        {
            // A run counts from its '<': the tag's three bytes and the text after them reach the bound exactly.
            byte[] part = Encoding.UTF8.GetBytes("<a>" + new string(' ', BoundedPartStream.MaxTokenBytes - 3) + "</a><b>" + new string(' ', BoundedPartStream.MaxTokenBytes - 3) + "</b>");

            Assert.Equal(part.Length, Drain(part, chunk));
        }

        [Theory]
        [InlineData(4096)]
        [InlineData(BoundedPartStream.MaxTokenBytes + 4096)]
        public void A_greater_than_sign_in_text_or_an_attribute_ends_no_run(int chunk)
        {
            string flood = string.Concat(Enumerable.Repeat(new string('a', 1023) + ">", (BoundedPartStream.MaxTokenBytes / 1024) + 1));

            _ = Assert.Throws<ImportException>(() => Drain(Encoding.UTF8.GetBytes($"<a>{flood}</a>"), chunk));
            _ = Assert.Throws<ImportException>(() => Drain(Encoding.UTF8.GetBytes($"<a b=\"{flood}\"/>"), chunk));
        }

        [Theory]
        [InlineData("<![CDATA[", "]]>")]
        [InlineData("<!--", "-->")]
        [InlineData("<?pi ", "?>")]
        public void A_section_is_one_run_from_its_opener_to_its_terminator_whatever_it_holds(string opener, string terminator)
        {
            string flood = string.Concat(Enumerable.Repeat(new string('a', 1023) + "<", (BoundedPartStream.MaxTokenBytes / 1024) + 1));
            string small = string.Concat(Enumerable.Repeat(new string('a', 1023) + "<", 64));

            _ = Assert.Throws<ImportException>(() => Drain(Encoding.UTF8.GetBytes($"<a>{opener}{flood}{terminator}</a>"), 4096));

            // A section within the bound closes, and the runs after it are counted afresh.
            byte[] closed = Encoding.UTF8.GetBytes($"<a>{opener}{small}{terminator}</a><b>" + new string(' ', BoundedPartStream.MaxTokenBytes - 3) + "</b>");
            Assert.Equal(closed.Length, Drain(closed, 4096));
        }

        [Fact]
        public void An_opener_that_breaks_off_is_a_tag_whose_next_less_than_sign_starts_a_run()
        {
            byte[] part = Encoding.UTF8.GetBytes("<a><![CDAT<b>" + new string(' ', BoundedPartStream.MaxTokenBytes - 3) + "</b></a>");

            Assert.Equal(part.Length, Drain(part, 4096));
        }

        [Fact]
        public void A_utf16_run_is_bounded_in_bytes()
        {
            byte[] part = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<a>" + new string(' ', (BoundedPartStream.MaxTokenBytes / 2) + 1) + "</a>")];

            _ = Assert.Throws<ImportException>(() => Drain(part, 4096));
        }

        [Fact]
        public void A_part_longer_than_its_declared_length_is_refused()
        {
            byte[] part = Encoding.UTF8.GetBytes("<a></a>");

            ImportException refused = Assert.Throws<ImportException>(() => Drain(part, 4096, declared: part.Length - 1));

            Assert.Equal(ExcelErrorCodes.ImportPackageTooLarge, Assert.Single(refused.Errors).Code);
        }

        [Theory]
        [MemberData(nameof(Ucs4Orders))]
        public void A_ucs4_part_is_refused_in_every_byte_order_with_or_without_its_mark(string order, bool marked, int chunk)
        {
            // Every UCS-4 unit of an ASCII character holds three NUL bytes, whichever way it is laid out.
            byte[] part = Ucs4("<a>" + new string('x', 64) + "</a>", order, marked);

            ImportException refused = Assert.Throws<ImportException>(() => Drain(part, chunk));

            ImportError error = Assert.Single(refused.Errors);
            Assert.Equal((ExcelErrorCodes.ImportMalformedWorkbook, ImportFaults.XmlReason), (error.Code, (string?)error.Arguments["reason"]));
        }

        [Theory]
        [MemberData(nameof(Encodings))]
        public void A_nul_in_content_or_in_a_section_is_refused_in_every_encoding(string encoding)
        {
            foreach (string text in (string[])["<a>x\0y</a>", "<a><![CDATA[x\0y]]></a>", "<a><!--x\0y--></a>", "<a><?pi x\0y?></a>", "<a b=\"\0\"/>"])
            {
                ImportException refused = Assert.Throws<ImportException>(() => Drain(Encode(text, encoding), 4096));

                Assert.Equal(ImportFaults.XmlReason, Assert.Single(refused.Errors).Arguments["reason"]);
            }
        }

        [Theory]
        [MemberData(nameof(Encodings))]
        public void A_part_without_a_nul_is_read_whole_in_every_encoding_its_zero_bytes_in_utf16_included(string encoding)
        {
            byte[] part = Encode("<a b=\"c\"><![CDATA[<x>]]><!-- - --><?pi ? ?>\u0100\u3c00\u003c/a>", encoding);

            Assert.Equal(part.Length, Drain(part, 3));
        }

        [Theory]
        [InlineData("<![CDATA[", ']', "]]>")]
        [InlineData("<!--", '-', "-->")]
        [InlineData("<?pi ", '?', "?>")]
        public void A_flood_of_a_sections_terminator_mark_is_one_run_refused_past_the_bound(string opener, char mark, string terminator)
        {
            byte[] part = Encoding.UTF8.GetBytes($"<a>{opener}{new string(mark, BoundedPartStream.MaxTokenBytes)}{terminator}</a>");

            ImportException refused = Assert.Throws<ImportException>(() => Drain(part, 4096));

            Assert.Equal(ImportFaults.XmlReason, Assert.Single(refused.Errors).Arguments["reason"]);
        }

        [Theory]
        [InlineData("utf-8", 1)]
        [InlineData("utf-8", 3)]
        [InlineData("utf-8", 4096)]
        [InlineData("utf-16le", 3)]
        [InlineData("utf-16be bom", 4096)]
        public void A_terminator_after_a_run_of_its_marks_closes_its_section_wherever_the_reads_split_it(string encoding, int chunk)
        {
            // Each section closes on its terminator after a run of marks, a lone mark or a short run
            // before it; the run after them is counted afresh, so it fits the bound only if each closed.
            int width = encoding == "utf-8" ? 1 : 2;
            string sections = "<a><![CDATA[x]a]]a]>]]]]>]]></a><!-- -x-a--b---><?pi ?a??>";
            string text = sections + "<b>" + new string(' ', (BoundedPartStream.MaxTokenBytes / width) - 3) + "</b>";
            byte[] part = Encode(text, encoding);

            Assert.Equal(part.Length, Drain(part, chunk));
        }

        [Theory]
        [InlineData("utf-8", 4096)]
        [InlineData("utf-8", 7)]
        [InlineData("utf-16le", 4096)]
        [InlineData("utf-16be", 7)]
        public void A_terminator_is_found_at_every_place_a_vector_or_a_read_may_put_it(string encoding, int chunk)
        {
            // Each section closes only if its terminator is found; a run at the bound follows it.
            int width = encoding == "utf-8" ? 1 : 2;
            string filler = "]a>]]a-->-a?a";
            string sections = string.Concat(Enumerable.Range(0, 96).Select(at =>
                $"<a><![CDATA[{Fill(filler, at)}]]><!--{Fill("-a>", at)}--><?pi {Fill("?a", at)}?></a>"));
            byte[] part = Encode(sections + "<b>" + new string(' ', (BoundedPartStream.MaxTokenBytes / width) - 3) + "</b>", encoding);

            Assert.Equal(part.Length, Drain(part, chunk));
        }

        [Theory]
        [MemberData(nameof(MarkDenseFloods))]
        public void A_flood_of_marks_and_less_than_signs_in_one_section_is_one_run_refused_past_the_bound(string encoding, string opener, string filler, string terminator, int chunk)
        {
            // Each '<' inside the section is its text; were the section closed early, at a place
            // that is no terminator, the '<' after it would start a run and the part would pass.
            int width = encoding == "utf-8" ? 1 : 2;
            string flood = Fill(filler + "<", (BoundedPartStream.MaxTokenBytes / width) + 64);
            byte[] part = Encode($"<a>{opener}{flood}{terminator}</a>", encoding);

            ImportException refused = Assert.Throws<ImportException>(() => Drain(part, chunk));

            Assert.Equal(ImportFaults.XmlReason, Assert.Single(refused.Errors).Arguments["reason"]);
        }

        [Theory]
        [MemberData(nameof(SectionsByEncoding))]
        public void A_section_is_counted_whole_to_its_terminator_however_many_less_than_signs_precede_it(string encoding, string opener, string terminator, int chunk)
        {
            // Half the bound of section, ending on a block of '<', then half the bound of text: one
            // run past the bound. Were the section closed anywhere short of its terminator, a '<' of
            // the block would start a run of little more than the text, and the part would pass.
            // The padding moves the terminator to other places within a vector.
            int width = encoding == "utf-8" ? 1 : 2;
            int half = BoundedPartStream.MaxTokenBytes / width / 2;
            foreach (int padding in (int[])[0, 5, 10, 17])
            {
                string section = opener + new string('a', half - padding) + new string('<', 64) + terminator;
                byte[] part = Encode($"<a>{section}{new string('a', half)}</a>", encoding);

                ImportException refused = Assert.Throws<ImportException>(() => Drain(part, chunk));

                Assert.Equal(ImportFaults.XmlReason, Assert.Single(refused.Errors).Arguments["reason"]);
            }
        }

        [Theory]
        [InlineData("utf-8", "<a><![CDATA[", "a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]]a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]a>", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", ">", "]]></a>")]
        [InlineData("utf-8", "<a><!--", "-a", "--></a>")]
        [InlineData("utf-8", "<a><!--", "-a>", "--></a>")]
        [InlineData("utf-8", "<a><?pi ", "?a", "?></a>")]
        [InlineData("utf-16le", "<a>", "a", "</a>")]
        [InlineData("utf-16le", "<a><![CDATA[", "]a", "]]></a>")]
        [InlineData("utf-16le", "<a><![CDATA[", "]a>", "]]></a>")]
        [InlineData("utf-16be", "<a>", "a", "</a>")]
        [InlineData("utf-16be", "<a><![CDATA[", "]a", "]]></a>")]
        [InlineData("utf-16be", "<a><!--", "-a>", "--></a>")]
        public void A_part_takes_a_few_units_one_at_a_time_per_opener_and_per_read_however_long_its_nodes(string encoding, string opener, string filler, string terminator)
        {
            // 16 nodes of 1 Mi units each, read in 64 KiB reads. Only an opener's units, and the unit
            // after a run of marks a read splits, are taken one at a time; the rest, whatever it holds,
            // is skipped a vector at a time, so the count does not grow with the nodes' length.
            string body = Fill(filler, 1 << 20);
            string text = string.Concat(Enumerable.Repeat(opener + body + terminator, 16));
            byte[] part = Encode(text, encoding);
            int chunk = 64 * 1024;
            long openers = text.Count(static character => character == '<');
            long reads = (part.Length / chunk) + 1;

            using BoundedPartStream stream = new(new MemoryStream(part, writable: false), part.Length);
            stream.CopyTo(Stream.Null, chunk);

            Assert.InRange(stream.UnitsTakenSingly, 0, 12 * (openers + reads));
        }

        [Theory]
        [Trait("Category", "Nightly")]
        [InlineData("utf-8", "<a><![CDATA[", "a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]]a", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", "]a>", "]]></a>")]
        [InlineData("utf-8", "<a><![CDATA[", ">", "]]></a>")]
        [InlineData("utf-8", "<a><!--", "-a", "--></a>")]
        [InlineData("utf-8", "<a><?pi ", "?a", "?></a>")]
        [InlineData("utf-16le", "<a>", "a", "</a>")]
        [InlineData("utf-16le", "<a><![CDATA[", "]a", "]]></a>")]
        [InlineData("utf-16be", "<a>", "a", "</a>")]
        [InlineData("utf-16be", "<a><![CDATA[", "]a", "]]></a>")]
        public void A_part_costs_about_what_the_same_bytes_of_utf8_text_cost_to_scan_whatever_its_encoding_and_markup(string encoding, string opener, string filler, string terminator)
        {
            // Nightly: a timing check, with ten times the baseline's cost as its allowance for a busy
            // host. It pins what counting units cannot: that the terminator search, too, does not slow
            // on fillers dense in places that begin and end like a terminator. 64 MiB of runs within the bound, each a node of 1 Mi characters, against the same bytes of
            // UTF-8 text. A section's filler may be dense in its terminator's marks without forming it;
            // scanned a unit at a time, such a section or any UTF-16 part would cost many times as much.
            string body = string.Concat(Enumerable.Repeat(filler, (1 << 20) / filler.Length));
            byte[] baseline = Encode(string.Concat(Enumerable.Repeat($"<a>{new string('a', 1 << 20)}</a>", 64)), "utf-8");
            byte[] part = Encode(string.Concat(Enumerable.Repeat(opener + body + terminator, encoding == "utf-8" ? 64 : 32)), encoding);

            // The fastest of several drains of each, interleaved, so a busy host slows both alike.
            TimeSpan baselineTime = TimeSpan.MaxValue;
            TimeSpan partTime = TimeSpan.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                baselineTime = Min(baselineTime, Time(baseline));
                partTime = Min(partTime, Time(part));
            }

            Assert.InRange(partTime, TimeSpan.Zero, (baselineTime * 10) + TimeSpan.FromMilliseconds(25));
        }

        /// <summary>Drains a part in 64 KiB reads and times it.</summary>
        /// <param name="part">The part's bytes.</param>
        /// <returns>The time the drain took.</returns>
        private static TimeSpan Time(byte[] part)
        {
            long started = Stopwatch.GetTimestamp();
            _ = Drain(part, 64 * 1024);
            return Stopwatch.GetElapsedTime(started);
        }

        /// <summary>A filler of a length, its pattern repeated and cut.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="length">The length.</param>
        /// <returns>The filler.</returns>
        private static string Fill(string pattern, int length)
        {
            return string.Concat(Enumerable.Repeat(pattern, (length / pattern.Length) + 1))[..length];
        }

        /// <summary>The floods of each section's mark-dense fillers, in each encoding and read size.</summary>
        /// <returns>The theory's data.</returns>
        private static TheoryData<string, string, string, string, int> MarkDense()
        {
            TheoryData<string, string, string, string, int> floods = [];
            (string Opener, string Terminator, string[] Fillers)[] sections =
            [
                ("<![CDATA[", "]]>", ["]a>", "]]a", "]>", "]"]),
                ("<!--", "-->", ["-a>", "->", "--a"]),
                ("<?pi ", "?>", ["?a", "?"]),
            ];
            foreach (string encoding in (string[])["utf-8", "utf-16le", "utf-16be bom"])
            {
                foreach ((string opener, string terminator, string[] fillers) in sections)
                {
                    foreach (string filler in fillers)
                    {
                        foreach (int chunk in (int[])[1, 7, 4096, 4097])
                        {
                            floods.Add(encoding, opener, filler, terminator, chunk);
                        }
                    }
                }
            }

            return floods;
        }

        /// <summary>The lesser of two times.</summary>
        /// <param name="left">One time.</param>
        /// <param name="right">The other.</param>
        /// <returns>The lesser.</returns>
        private static TimeSpan Min(TimeSpan left, TimeSpan right)
        {
            return left < right ? left : right;
        }

        /// <summary>A text in one of the encodings a part's markup is found in.</summary>
        /// <param name="text">The text.</param>
        /// <param name="encoding">UTF-8, or UTF-16 of a byte order, with "bom" when marked.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Encode(string text, string encoding)
        {
            Encoding chosen = encoding.StartsWith("utf-16be", StringComparison.Ordinal) ? Encoding.BigEndianUnicode
                : encoding.StartsWith("utf-16le", StringComparison.Ordinal) ? Encoding.Unicode
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            byte[] bytes = chosen.GetBytes(text);
            return encoding.EndsWith("bom", StringComparison.Ordinal) ? [.. chosen.GetPreamble(), .. bytes] : bytes;
        }

        /// <summary>A text in UCS-4, each unit's big-endian bytes laid out in an order.</summary>
        /// <param name="text">The text.</param>
        /// <param name="order">Where bytes 1 to 4 of each big-endian unit lie, such as "4321" for little-endian.</param>
        /// <param name="marked">Whether a byte order mark leads.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Ucs4(string text, string order, bool marked)
        {
            byte[] bigEndian = new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes((marked ? "\uFEFF" : string.Empty) + text);
            byte[] laid = new byte[bigEndian.Length];
            for (int unit = 0; unit < bigEndian.Length; unit += 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    laid[unit + i] = bigEndian[unit + order[i] - '1'];
                }
            }

            return laid;
        }

        /// <summary>Reads a part through the bounds to its end.</summary>
        /// <param name="part">The part's bytes.</param>
        /// <param name="chunk">The size of each read.</param>
        /// <param name="declared">The declared length; the part's own when null.</param>
        /// <returns>The bytes read.</returns>
        private static long Drain(byte[] part, int chunk, long? declared = null)
        {
            using BoundedPartStream stream = new(new MemoryStream(part, writable: false), declared ?? part.Length);
            byte[] buffer = new byte[chunk];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
            }

            return total;
        }
    }
}
