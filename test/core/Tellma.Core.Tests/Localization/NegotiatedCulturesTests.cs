// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The cultures server text is formatted in: named by their pair, Gregorian, Latin digits,
    ///     free of bidirectional controls, and carrying the pinned data on every host.
    /// </summary>
    public class NegotiatedCulturesTests
    {
        /// <summary>Every catalogue pair, as theory data.</summary>
        public static TheoryData<string> Pairs => [.. CultureDataSource.CataloguePairs()];

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Every_catalogue_pair_is_gregorian_read_only_and_named_by_its_pair(string pair)
        {
            CultureInfo culture = Get(pair);

            Assert.Equal(pair, culture.Name);
            Assert.True(culture.IsReadOnly);

            // The calendar dates are formatted in. The culture's own default calendar (Umm al-Qura
            // for ar-SA) is the host's and is never consulted: the platform's calendar systems do
            // every conversion.
            _ = Assert.IsType<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Every_catalogue_pair_carries_exactly_its_pinned_data(string pair)
        {
            // What the culture answers is what the data pins, field by field, whatever this host's
            // ICU would have said. Reading a field cleans it again, so this also holds every
            // pinned value to being unchanged by its own cleaner: a cleaner that starts removing
            // or rewriting something the checked-in data carries fails here on every host, not
            // only where the data can be regenerated.
            var actual = CultureSnapshot.Read(Get(pair));
            CultureSnapshot pinned = CultureDataFile.Pinned.Cultures[pair];

            Assert.All(CultureField.All, field => Assert.Equal(pinned[field.Name], actual[field.Name]));
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void Every_catalogue_pair_formats_with_latin_digits_and_no_bidirectional_control(string pair)
        {
            CultureInfo culture = Get(pair);
            DateTime moment = new(2026, 3, 15, 17, 4, 5);
            string[] rendered =
            [
                (-1234567.891m).ToString("N2", culture),
                1234567.891m.ToString("N0", culture),
                (-0.256).ToString("P1", culture),
                1234.5.ToString("C", culture),
                moment.ToString("d", culture),
                moment.ToString("D", culture),
                moment.ToString("t", culture),
                moment.ToString("T", culture),
                moment.ToString("f", culture),
                moment.ToString("g", culture),
                moment.ToString("Y", culture),
                moment.ToString("M", culture),
            ];

            Assert.All(rendered, static text =>
            {
                Assert.DoesNotContain(text, static c => CalendarText.IsBidiControl(c));
                Assert.DoesNotContain(text, static c => char.IsDigit(c) && !char.IsAsciiDigit(c));
            });
            Assert.Equal(["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"], culture.NumberFormat.NativeDigits);
            Assert.Equal(DigitShapes.None, culture.NumberFormat.DigitSubstitution);
        }

        [Theory]
        [InlineData("ar-SA", "1,234,567.89", "-1,234,567.89", "50%")]
        [InlineData("ar-EG", "1,234,567.89", "-1,234,567.89", "50%")]
        [InlineData("ur-PK", "1,234,567.89", "-1,234,567.89", "50%")]
        [InlineData("hi-IN", "12,34,567.89", "-12,34,567.89", "50%")]
        [InlineData("en-GB", "1,234,567.89", "-1,234,567.89", "50%")]
        [InlineData("en-ZA", "1 234 567,89", "-1 234 567,89", "50%")]
        [InlineData("de-DE", "1.234.567,89", "-1.234.567,89", "50 %")]
        public void Numbers_print_as_pinned(string pair, string positive, string negative, string percent)
        {
            CultureInfo culture = Get(pair);

            // Group separators are no-break spaces where the language groups with a space.
            Assert.Equal(positive, Spaces(1234567.891m.ToString("N2", culture)));
            Assert.Equal(negative, Spaces((-1234567.891m).ToString("N2", culture)));
            Assert.Equal(percent, Spaces(0.5.ToString("P0", culture)));
        }

        [Theory]
        [InlineData("en-GB", "15/03/2026", "Sunday, 15 March 2026", "17:04")]
        [InlineData("en-US", "3/15/2026", "Sunday, March 15, 2026", "5:04 PM")]
        [InlineData("ar-SA", "15/3/2026", "الأحد، 15 مارس 2026", "5:04 م")]
        [InlineData("am-ET", "15/03/2026", "እሑድ 15 ማርች 2026", "5:04 ከሰዓት")]
        [InlineData("om-ET", "15/03/2026", "Dilbata, Bitootessa 15, 2026", "5:04 WB")]
        [InlineData("ti-ER", "15/03/2026", "ሰንበት፣ 15 መጋቢት 2026", "5:04 ድ.ቀ.")]
        [InlineData("tr-TR", "15.03.2026", "15 Mart 2026 Pazar", "17:04")]
        [InlineData("zh-CN", "2026/3/15", "2026年3月15日星期日", "17:04")]
        public void Dates_print_as_pinned(string pair, string shortDate, string longDate, string shortTime)
        {
            CultureInfo culture = Get(pair);
            DateTime moment = new(2026, 3, 15, 17, 4, 5);

            Assert.Equal(shortDate, moment.ToString("d", culture));
            Assert.Equal(longDate, moment.ToString("D", culture));
            Assert.Equal(shortTime, moment.ToString("t", culture));
        }

        [Fact]
        public void The_culture_of_a_pair_is_one_instance_whatever_the_case()
        {
            // The calendar systems remember what they read from a culture by instance.
            Assert.Same(NegotiatedCultures.Get("ar", "SA"), NegotiatedCultures.Get("ar", "SA"));
            Assert.Same(NegotiatedCultures.Get("ar", "SA"), NegotiatedCultures.Get("AR", "sa"));
            Assert.NotSame(NegotiatedCultures.Get("ar", "SA"), NegotiatedCultures.Get("ar", "AE"));
        }

        [Fact]
        public void Resources_fall_back_from_the_pair_to_its_language()
        {
            CultureInfo culture = NegotiatedCultures.Get("ar", "SA");

            Assert.Equal("ar", culture.Parent.Name);
            Assert.Equal("ar", culture.TwoLetterISOLanguageName);
            Assert.True(culture.TextInfo.IsRightToLeft);
        }

        [Theory]
        [InlineData("ru", "RU")]
        [InlineData("fa", "IR")]
        [InlineData("th", "TH")]
        public void A_pair_outside_the_catalogue_takes_the_hosts_latin_digit_gregorian_formats_cleaned(string language, string region)
        {
            // A language a distribution added has no pinned data. Persian and Thai have another
            // calendar by default, and Persian has its own digits.
            CultureInfo culture = NegotiatedCultures.Get(language, region);
            DateTime moment = new(2026, 3, 15, 17, 4, 5);

            Assert.Equal($"{language}-{region}", culture.Name);
            Assert.True(culture.IsReadOnly);
            _ = Assert.IsType<GregorianCalendar>(culture.DateTimeFormat.Calendar, exactMatch: false);
            Assert.Contains("2026", moment.ToString("D", culture), StringComparison.Ordinal);
            Assert.All(
                [1234567.891m.ToString("N2", culture), (-0.5).ToString("P0", culture), moment.ToString("d", culture), moment.ToString("D", culture)],
                static text =>
                {
                    Assert.DoesNotContain(text, static c => CalendarText.IsBidiControl(c));
                    Assert.DoesNotContain(text, static c => char.IsDigit(c) && !char.IsAsciiDigit(c));
                });
        }

        [Fact]
        public void A_pair_the_host_does_not_know_still_yields_a_culture()
        {
            // Negotiation never throws; a made-up language formats with whatever the host gives it.
            CultureInfo culture = NegotiatedCultures.Get("qx", "QQ");

            Assert.True(culture.IsReadOnly);
            Assert.Equal("1234.50", 1234.5m.ToString("F2", culture).Replace(culture.NumberFormat.NumberDecimalSeparator, ".", StringComparison.Ordinal));
        }

        [Fact]
        public void The_calendar_systems_read_a_negotiated_culture_as_it_is()
        {
            // The pinned patterns are already clean and Gregorian, so the calendar systems render
            // the same text the culture itself prints for a numeric date.
            CultureInfo culture = NegotiatedCultures.Get("ar", "SA");
            DateOnly date = new(2026, 3, 15);

            Assert.Equal(date.ToString("d", culture), new GregorianCalendarSystem().Format(date, culture, DateStyle.Short));
        }

        /// <summary>The negotiated culture of a pair.</summary>
        /// <param name="pair">The language and region.</param>
        /// <returns>The culture.</returns>
        private static CultureInfo Get(string pair)
        {
            int hyphen = pair.IndexOf('-', StringComparison.Ordinal);
            return NegotiatedCultures.Get(pair[..hyphen], pair[(hyphen + 1)..]);
        }

        /// <summary>Writes the no-break spaces of a rendering as ordinary ones, so an expected string reads plainly.</summary>
        /// <param name="text">The rendering.</param>
        /// <returns>The text with ordinary spaces.</returns>
        private static string Spaces(string text)
        {
            return text.Replace(' ', ' ').Replace(' ', ' ');
        }
    }
}
