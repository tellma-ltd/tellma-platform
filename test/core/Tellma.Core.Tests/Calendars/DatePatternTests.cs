// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Calendars;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     Reading a culture's date pattern: quotes and escapes, the era dropped, the year widened,
    ///     the weekday removed with the punctuation that joins it and nothing more, and the text
    ///     clean-up every culture string goes through.
    /// </summary>
    public class DatePatternTests
    {
        [Theory]
        [InlineData("dddd, d MMMM yyyy", "dddd', 'd' 'MMMM' 'yyyy")]
        [InlineData("d‏/M‏/yyyy g", "d/M/yyyy")]
        [InlineData("dddd، d MMMM yyyy g", "dddd'، 'd' 'MMMM' 'yyyy")]
        [InlineData("dddd, d 'de' MMMM 'de' yyyy", "dddd', 'd' de 'MMMM' de 'yyyy")]
        [InlineData("d. MMMM yy", "d'. 'MMMM' 'yyyy")]
        [InlineData("yyyy年M月d日dddd", "yyyy'年'M'月'd'日'dddd")]
        [InlineData("\\d\\a\\y d \"of\" MMM, y", "'day 'd' of 'MMM', 'yyyy")]
        [InlineData("%d/%M/yyyy", "d/M/yyyy")]
        [InlineData("g yyyy-MM-dd", "yyyy'-'MM'-'dd")]
        [InlineData("d  MMM yyyy", "d' 'MMM' 'yyyy")]
        public void A_pattern_reads_into_tokens_with_its_quirks_normalised(string pattern, string expected)
        {
            Assert.Equal(expected, DatePattern.Parse(pattern).ToString());
        }

        [Theory]
        [InlineData("dddd, d MMMM yyyy", "d' 'MMMM' 'yyyy")]
        [InlineData("dddd d MMMM yyyy", "d' 'MMMM' 'yyyy")]
        [InlineData("yyyy MMMM d, dddd", "yyyy' 'MMMM' 'd")]
        [InlineData("dddd، d MMMM yyyy g", "d' 'MMMM' 'yyyy")]
        [InlineData("yyyy年M月d日dddd", "yyyy'年'M'月'd'日'")]
        [InlineData("dddd, d 'de' MMMM 'de' yyyy", "d' de 'MMMM' de 'yyyy")]
        [InlineData("d MMMM yyyy", "d' 'MMMM' 'yyyy")]
        public void Removing_the_weekday_removes_the_punctuation_that_joins_it_and_keeps_letters(string pattern, string expected)
        {
            Assert.Equal(expected, DatePattern.Parse(pattern).WithoutWeekday().ToString());
        }

        [Fact]
        public void A_pattern_without_a_weekday_gains_one_in_front()
        {
            Assert.Equal("dddd', 'd' 'MMMM' 'yyyy", DatePattern.Parse("d MMMM yyyy").WithWeekday().ToString());
            Assert.Equal("dddd' 'd' 'MMMM' 'yyyy", DatePattern.Parse("dddd d MMMM yyyy").WithWeekday().ToString());
        }

        [Fact]
        public void The_numeric_form_turns_month_names_into_padded_numbers_and_drops_the_weekday()
        {
            DatePattern numeric = DatePattern.Parse("ddd, dd-MMM-yy").ToNumeric();

            Assert.Equal("dd'-'MM'-'yyyy", numeric.ToString());
            Assert.True(numeric.IsComplete);
        }

        [Fact]
        public void The_medium_form_abbreviates_full_month_names_only()
        {
            Assert.Equal("d' 'MMM' 'yyyy", DatePattern.Parse("d MMMM yyyy").WithAbbreviatedMonth().ToString());
            Assert.Equal("d'/'M'/'yyyy", DatePattern.Parse("d'/'M'/'yyyy").WithAbbreviatedMonth().ToString());
        }

        [Theory]
        [InlineData("MMMM yyyy", false)]
        [InlineData("d MMMM", false)]
        [InlineData("dddd", false)]
        [InlineData("d MMMM yyyy", true)]
        public void A_pattern_is_complete_when_it_holds_a_day_a_month_and_a_year(string pattern, bool complete)
        {
            Assert.Equal(complete, DatePattern.Parse(pattern).IsComplete);
        }

        [Fact]
        public void The_field_order_is_the_order_of_first_appearance()
        {
            Assert.Equal([DatePatternTokenKind.Month, DatePatternTokenKind.Day, DatePatternTokenKind.Year], DatePattern.Parse("M/d/yyyy").FieldOrder);
            Assert.Equal([DatePatternTokenKind.Year, DatePatternTokenKind.Month, DatePatternTokenKind.Day], DatePattern.Parse("yyyy/M/d").FieldOrder);
        }

        [Theory]
        [InlineData("a‏b‎c؜d", "abcd")]
        [InlineData("‫x‬⁧y⁩", "xy")]
        [InlineData("h:mm tt", "h:mm tt")]
        [InlineData("1 234 567 ", "1 234 567 ")]
        [InlineData("plain", "plain")]
        public void Cleaning_removes_bidirectional_controls_and_plain_spaces_the_space_variants(string text, string expected)
        {
            Assert.Equal(expected, CalendarText.Clean(text));
        }

        [Theory]
        [InlineData('7', 7)]
        [InlineData('٧', 7)]
        [InlineData('۷', 7)]
        [InlineData('७', 7)]
        [InlineData('a', -1)]
        [InlineData('/', -1)]
        public void Digits_of_any_script_fold_to_their_value(char c, int expected)
        {
            Assert.Equal(expected, CalendarText.DigitValue(c));
        }
    }
}
