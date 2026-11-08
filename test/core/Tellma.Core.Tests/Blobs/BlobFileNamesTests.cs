// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>File-name sanitisation: display-only names that can never act as paths.</summary>
    public class BlobFileNamesTests
    {
        [Theory]
        [InlineData("invoice.pdf", "invoice.pdf")]
        [InlineData("  invoice.pdf \t", "invoice.pdf")]
        [InlineData("Q3 report (final).xlsx", "Q3 report (final).xlsx")]
        [InlineData(".bashrc", ".bashrc")]
        [InlineData("فاتورة.pdf", "فاتورة.pdf")]
        [InlineData("C:report.pdf", "C:report.pdf")]
        public void An_ordinary_name_is_kept(string input, string expected)
        {
            Assert.Equal(expected, BlobFileNames.Sanitize(input));
        }

        [Theory]
        [InlineData("../../etc/passwd", ".._.._etc_passwd")]
        [InlineData("..\\..\\windows\\win.ini", ".._.._windows_win.ini")]
        [InlineData("/abs/path.txt", "_abs_path.txt")]
        [InlineData("C:\\Users\\me\\a.txt", "C:_Users_me_a.txt")]
        [InlineData("a/b\\c", "a_b_c")]
        public void Path_separators_become_underscores(string input, string expected)
        {
            Assert.Equal(expected, BlobFileNames.Sanitize(input));
        }

        [Theory]
        [InlineData("a\0b.txt", "a_b.txt")]
        [InlineData("a\nb\rc.txt", "a_b_c.txt")]
        [InlineData("tab\there.txt", "tab_here.txt")]
        [InlineData("bell\u0007.txt", "bell_.txt")]
        [InlineData("del\u007F.txt", "del_.txt")]
        [InlineData("c1\u0085.txt", "c1_.txt")]
        public void Control_characters_become_underscores(string input, string expected)
        {
            Assert.Equal(expected, BlobFileNames.Sanitize(input));
        }

        [Theory]
        [InlineData("invoice\u202Efdp.exe", "invoice_fdp.exe")]
        [InlineData("a\u202Ab\u202Cc", "a_b_c")]
        [InlineData("x\u2066y\u2069z", "x_y_z")]
        [InlineData("l\u200Er\u200F", "l_r_")]
        [InlineData("arabic\u061Cmark", "arabic_mark")]
        public void Bidirectional_controls_become_underscores(string input, string expected)
        {
            Assert.Equal(expected, BlobFileNames.Sanitize(input));
        }

        [Theory]
        [InlineData(".", "file")]
        [InlineData("..", "file")]
        [InlineData(" .. ", "file")]
        [InlineData("...", "...")]
        public void A_bare_dot_or_dot_dot_becomes_file(string input, string expected)
        {
            Assert.Equal(expected, BlobFileNames.Sanitize(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public void A_blank_name_is_null(string? input)
        {
            Assert.Null(BlobFileNames.Sanitize(input));
        }

        [Fact]
        public void A_valid_surrogate_pair_survives_and_a_lone_surrogate_does_not()
        {
            Assert.Equal("photo 😀.png", BlobFileNames.Sanitize("photo 😀.png"));
            Assert.Equal("a_b.png", BlobFileNames.Sanitize("a\uD83Db.png"));
            Assert.Equal("a_b.png", BlobFileNames.Sanitize("a\uDE00b.png"));
            Assert.Equal("end_", BlobFileNames.Sanitize("end\uD83D"));
        }

        [Fact]
        public void A_decomposed_name_is_normalised_to_nfc()
        {
            string decomposed = "Cafe\u0301.pdf";

            Assert.Equal("Caf\u00E9.pdf", BlobFileNames.Sanitize(decomposed));
        }

        [Fact]
        public void A_long_name_is_shortened_to_255_keeping_its_extension()
        {
            string input = new string('a', 300) + ".pdf";

            string? sanitized = BlobFileNames.Sanitize(input);

            Assert.NotNull(sanitized);
            Assert.Equal(BlobFileNames.MaxLength, sanitized.Length);
            Assert.EndsWith(".pdf", sanitized, StringComparison.Ordinal);
        }

        [Fact]
        public void A_name_of_exactly_255_is_kept_whole()
        {
            string input = new string('b', 251) + ".csv";

            Assert.Equal(input, BlobFileNames.Sanitize(input));
        }

        [Fact]
        public void A_long_name_without_a_short_extension_is_cut_at_255()
        {
            string input = new string('c', 200) + "." + new string('d', 100);

            string? sanitized = BlobFileNames.Sanitize(input);

            Assert.Equal(input[..BlobFileNames.MaxLength], sanitized);
        }

        [Fact]
        public void Shortening_never_splits_a_surrogate_pair()
        {
            // 127 emoji are 254 code units; one more would make 256, so the 128th is dropped whole.
            string input = string.Concat(Enumerable.Repeat("😀", 130));

            string? sanitized = BlobFileNames.Sanitize(input);

            Assert.NotNull(sanitized);
            Assert.Equal(254, sanitized.Length);
            Assert.True(char.IsLowSurrogate(sanitized[^1]));
        }
    }
}
