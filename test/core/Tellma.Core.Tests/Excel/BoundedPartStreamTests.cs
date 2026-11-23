// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The bounds every part is read through: its declared length, and the longest run from one
    ///     '&lt;' to the next, a markup section counted whole, wherever the reads happen to split it.
    /// </summary>
    public sealed class BoundedPartStreamTests
    {
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
