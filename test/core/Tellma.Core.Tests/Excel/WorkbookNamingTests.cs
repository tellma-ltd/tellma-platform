// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The names and escapes a workbook carries: sheet names Excel accepts and keeps unique, file
    ///     names from a stem and the request's local time, column letters, serial dates and the
    ///     escapes of characters XML cannot hold.
    /// </summary>
    public class WorkbookNamingTests
    {
        [Theory]
        [InlineData("Widgets", "Widgets")]
        [InlineData("Q1/Q2: [draft]?*", "Q1Q2 draft")]
        [InlineData("'quoted'", "quoted")]
        [InlineData("tab\there", "tabhere")]
        [InlineData("///", "Sheet")]
        [InlineData("A very long sheet name that Excel would refuse", "A very long sheet name that Exc")]
        public void A_sheet_name_is_scrubbed_of_what_excel_refuses_and_cut_to_31_characters(string name, string expected)
        {
            Assert.Equal(expected, SheetNames.Scrub(name));
        }

        [Fact]
        public void A_cut_never_splits_a_surrogate_pair()
        {
            string name = new string('a', 30) + "😀";

            Assert.Equal(new string('a', 30), SheetNames.Scrub(name));
        }

        [Fact]
        public void Sheet_names_are_unique_ignoring_case_and_never_history()
        {
            SheetNames names = new();

            Assert.Equal("Parts", names.Take("Parts"));
            Assert.Equal("parts (2)", names.Take("parts"));
            Assert.Equal("Parts (3)", names.Take("Parts"));
            Assert.Equal("history (2)", names.Take("history"));
        }

        [Fact]
        public void A_suffix_fits_within_31_characters()
        {
            SheetNames names = new();
            string name = new('x', 31);

            _ = names.Take(name);

            Assert.Equal(new string('x', 27) + " (2)", names.Take(name));
        }

        [Fact]
        public void A_file_name_is_the_stems_ascii_form_and_the_local_time()
        {
            DateTimeOffset now = new(2026, 11, 16, 22, 45, 0, TimeSpan.Zero);
            var riyadh = TimeZoneInfo.CreateCustomTimeZone("Test/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");

            Assert.Equal("Widgets 2026-11-17 0145.xlsx", ExcelFileNames.Compose("Widgets", "report", now, riyadh));
            Assert.Equal("Sales by region 2026-11-16 2245.xlsx", ExcelFileNames.Compose("  Sales  by / region!  ", "report", now, TimeZoneInfo.Utc));
        }

        [Fact]
        public void A_stem_without_an_ascii_form_falls_back()
        {
            DateTimeOffset now = new(2026, 11, 16, 9, 5, 0, TimeSpan.Zero);

            Assert.Equal("report 2026-11-16 0905.xlsx", ExcelFileNames.Compose("تقرير المبيعات", ExcelFileNames.ReportStem, now, TimeZoneInfo.Utc));
            _ = Assert.Throws<ArgumentException>(() => ExcelFileNames.Compose("تقرير", "تقرير", now, TimeZoneInfo.Utc));
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        [InlineData(16_383, "XFD")]
        public void Column_letters_count_in_bijective_base_26(int index, string letters)
        {
            Assert.Equal(letters, ColumnLetters.Of(index));
            Assert.True(ColumnLetters.TryParse(letters.ToLowerInvariant(), out int parsed));
            Assert.Equal(index, parsed);
        }

        [Fact]
        public void Column_letters_beyond_the_worksheet_are_refused()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => ColumnLetters.Of(16_384));
            Assert.False(ColumnLetters.TryParse("XFE", out _));
            Assert.False(ColumnLetters.TryParse("A1", out _));
            Assert.True(ColumnLetters.TryParseReference("ab12", out int column, out int row));
            Assert.Equal((27, 12), (column, row));
            Assert.False(ColumnLetters.TryParseReference("A1048577", out _, out _));
            Assert.Equal("$A$1:$C$9", ColumnLetters.AbsoluteRange(0, 1, 2, 9));
        }

        [Fact]
        public void A_serial_counts_days_from_the_1900_epoch_and_starts_on_1_march_1900()
        {
            Assert.True(ExcelSerial.TryFrom(new DateOnly(1900, 3, 1), out int first));
            Assert.Equal(61, first);
            Assert.False(ExcelSerial.TryFrom(new DateOnly(1900, 2, 28), out int _));
            Assert.True(ExcelSerial.TryFrom(new DateTime(2026, 11, 16, 6, 0, 0), out double withTime));
            Assert.Equal(46342.25, withTime);
            Assert.Equal(0.5, ExcelSerial.From(new TimeOnly(12, 0)));
        }

        [Fact]
        public void A_1904_serial_shifts_by_1462_days()
        {
            Assert.True(ExcelSerial.TryReadDate(44880, date1904: true, out DateOnly date));
            Assert.Equal(new DateOnly(2026, 11, 16), date);
            Assert.False(ExcelSerial.TryReadDate(60, date1904: false, out _));
            Assert.False(ExcelSerial.TryReadDate(double.NaN, date1904: false, out _));
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("bell\u0007", "bell_x0007_")]
        [InlineData("a_x0041_b", "a_x005F_x0041_b")]
        [InlineData("pair😀", "pair😀")]
        [InlineData("tab\tline\nfeed\r", "tab\tline\nfeed\r")]
        public void Text_xml_cannot_hold_is_escaped_and_reads_back(string text, string escaped)
        {
            Assert.Equal(escaped, XmlStrings.Escape(text));
            Assert.Equal(text, XmlStrings.Unescape(escaped));
        }

        [Fact]
        public void A_lone_surrogate_and_a_noncharacter_are_escaped_and_read_back()
        {
            string text = string.Concat("lone", ((char)0xD800).ToString(), "end", ((char)0xFFFE).ToString());

            Assert.Equal("lone_xD800_end_xFFFE_", XmlStrings.Escape(text));
            Assert.Equal(text, XmlStrings.Unescape("lone_xD800_end_xFFFE_"));
        }

        [Fact]
        public void Text_needing_no_escape_is_returned_as_it_is()
        {
            const string Underscored = "nothing to escape_x";
            const string Plain = "nothing to escape";

            Assert.Same(Underscored, XmlStrings.Escape(Underscored));
            Assert.Same(Plain, XmlStrings.Unescape(Plain));
        }
    }
}
