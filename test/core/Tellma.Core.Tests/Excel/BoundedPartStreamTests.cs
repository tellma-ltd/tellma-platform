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
    ///     The bounds every part is read through: its declared length, and the longest run between
    ///     two markup delimiters, wherever the reads happen to split it.
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
            byte[] part = Encoding.UTF8.GetBytes("<a>" + new string(' ', BoundedPartStream.MaxTokenBytes) + "</a><b>" + new string(' ', BoundedPartStream.MaxTokenBytes) + "</b>");

            Assert.Equal(part.Length, Drain(part, chunk));
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
