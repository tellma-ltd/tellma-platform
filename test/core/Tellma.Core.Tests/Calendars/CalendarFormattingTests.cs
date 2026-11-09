// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Calendars;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     Rendering and reading over the host's own cultures: properties that hold whatever the
    ///     host's ICU release, the Gregorian rendering checked against the framework's own, month
    ///     names, the numeric style's inverse, and argument checks.
    /// </summary>
    public class CalendarFormattingTests
    {
        /// <summary>The catalogue's languages with one or more of their regions, and a few others.</summary>
        public static readonly TheoryData<string> HostCultures =
        [
            "en-GB", "en-US", "en-AE", "en-IN", "ar-SA", "ar-AE", "ar-EG", "ar-MA", "am-ET", "om-ET", "ti-ET", "ti-ER",
            "so-SO", "sw-KE", "fr-FR", "fr-CA", "es-ES", "es-MX", "pt-BR", "pt-PT", "de-DE", "tr-TR", "ur-PK", "hi-IN",
            "zh-CN", "zh-TW", "ru-RU", "ja-JP", "ko-KR", "fa-IR", "he-IL",
        ];

        private static readonly ICalendarSystem[] Systems = [new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem()];

        /// <summary>A spread of dates: year boundaries, leap days, month ends, the calendars' bounds.</summary>
        private static readonly DateOnly[] Dates =
        [
            new(1900, 4, 30), new(1999, 12, 31), new(2000, 1, 1), new(2000, 2, 29), new(2023, 9, 11), new(2023, 9, 12),
            new(2024, 2, 29), new(2024, 9, 11), new(2025, 6, 26), new(2026, 3, 15), new(2077, 11, 16), new(2077, 11, 17),
        ];

        [Theory]
        [MemberData(nameof(HostCultures))]
        public void Every_rendering_uses_ascii_digits_and_carries_no_bidirectional_control(string name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            foreach (ICalendarSystem system in Systems)
            {
                foreach (DateOnly date in Dates)
                {
                    foreach (DateStyle style in Enum.GetValues<DateStyle>())
                    {
                        string text = system.Format(date, culture, style);
                        string context = $"{system.Code} {date:yyyy-MM-dd} {style} in {name}: '{text}'";

                        Assert.DoesNotContain(text, CalendarText.IsBidiControl);
                        Assert.DoesNotContain(text, c => char.IsDigit(c) && c is not (>= '0' and <= '9'));
                        Assert.False(text != text.Trim() || text.Contains("  ", StringComparison.Ordinal), context);
                        Assert.Contains(system.Decompose(date).Year.ToString("D4", CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
                    }

                    string withTime = system.Format(new DateTimeOffset(date, new TimeOnly(13, 5), TimeSpan.Zero), TimeZoneInfo.Utc, culture, DateStyle.Long);
                    Assert.DoesNotContain(withTime, c => CalendarText.IsBidiControl(c) || CalendarText.IsSpaceVariant(c));
                    Assert.DoesNotContain(withTime, c => char.IsDigit(c) && c is not (>= '0' and <= '9'));
                }
            }
        }

        [Fact]
        public void The_numeric_style_reads_back_in_every_calendar_in_every_predefined_culture()
        {
            // Every specific culture the host's ICU predefines, Bulgarian's trailing " \u0433." and
            // Korean's trailing "." included.
            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                foreach (ICalendarSystem system in Systems)
                {
                    foreach (DateOnly date in Dates)
                    {
                        string text = system.Format(date, culture, DateStyle.Short);

                        Assert.True(date == system.TryParse(text, culture), $"'{text}' ({system.Code}, {culture.Name}) does not read back as {date:yyyy-MM-dd}.");
                    }
                }
            }
        }

        [Fact]
        public void Bulgarian_dates_read_back_past_the_year_word()
        {
            // The pattern is pinned to the one ICU 74 to 78 give bg-BG, so the trailing year word is
            // there whatever the host.
            var bulgarian = (CultureInfo)CultureInfo.GetCultureInfo("bg-BG").Clone();
            bulgarian.DateTimeFormat.ShortDatePattern = "d.MM.yyyy '\u0433'.";
            GregorianCalendarSystem system = new();

            string text = system.Format(new DateOnly(2026, 3, 5), bulgarian, DateStyle.Short);

            Assert.Equal("5.03.2026 \u0433.", text);
            Assert.Equal(new DateOnly(2026, 3, 5), system.TryParse(text, bulgarian));
            Assert.Equal(new DateOnly(2026, 3, 5), system.TryParse("5.03.2026", bulgarian));
        }

        [Fact]
        public void No_long_or_medium_rendering_starts_or_ends_with_the_punctuation_that_joined_its_weekday()
        {
            // Lithuanian's "d., " before a trailing weekday and Romansh's ", ils " after a leading one
            // keep their words but lose the joining comma.
            char[] joining = [',', ';', '\u060C', '\u061B', '\u1363', '\u3001', '\uFF0C', '\uFF1B'];
            GregorianCalendarSystem system = new();
            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                foreach (DateStyle style in (DateStyle[])[DateStyle.Long, DateStyle.Medium])
                {
                    string text = system.Format(new DateOnly(2026, 3, 5), culture, style);

                    Assert.False(
                        text.Length == 0 || joining.Contains(text[0]) || joining.Contains(text[^1]) || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]),
                        $"{style} in {culture.Name} is '{text}'.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(HostCultures))]
        public void Gregorian_renderings_equal_the_frameworks_rendering_of_the_same_pattern(string name)
        {
            // The framework renders a Gregorian date from the derived pattern and the cleaned formats
            // exactly as the platform's own renderer does, genitive month names included.
            var culture = CultureInfo.GetCultureInfo(name);
            var data = CalendarCultureData.For(culture);
            GregorianCalendarSystem system = new();
            foreach (DateOnly date in Dates)
            {
                foreach (DateStyle style in Enum.GetValues<DateStyle>())
                {
                    string expected = date.ToDateTime(TimeOnly.MinValue).ToString(data.PatternOf(style).ToString(), data.Format);

                    Assert.Equal(expected, system.Format(date, culture, style));
                }
            }
        }

        [Fact]
        public void A_culture_whose_own_calendar_is_umm_al_qura_still_renders_gregorian_dates_with_gregorian_names()
        {
            // An ar-SA clone switched to Umm al-Qura: the host's ICU decides ar-SA's default
            // calendar (Umm al-Qura up to ICU 74, Gregorian from ICU 78), so the test sets it.
            var arSa = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
            arSa.DateTimeFormat.Calendar = arSa.OptionalCalendars.OfType<UmAlQuraCalendar>().Single();
            var gregorian = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
            gregorian.DateTimeFormat.Calendar = gregorian.OptionalCalendars.OfType<GregorianCalendar>().First();
            GregorianCalendarSystem system = new();
            DateOnly date = new(2026, 3, 15);

            // The Gregorian rendering carries the culture's Gregorian name of March, never the name
            // of the date's month in its own calendar (Ramadan), whatever names the host's ICU holds.
            string march = gregorian.DateTimeFormat.GetMonthName(3);
            string ownMonth = arSa.DateTimeFormat.GetMonthName(arSa.DateTimeFormat.Calendar.GetMonth(date.ToDateTime(TimeOnly.MinValue)));
            string text = system.Format(date, arSa, DateStyle.Long);
            Assert.NotEqual(march, ownMonth);
            Assert.Contains(march, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ownMonth, text, StringComparison.Ordinal);
            Assert.StartsWith("15 ", text, StringComparison.Ordinal);
            Assert.EndsWith(" 2026", text, StringComparison.Ordinal);
            foreach (DateStyle style in Enum.GetValues<DateStyle>())
            {
                Assert.Equal(system.Format(date, gregorian, style), system.Format(date, arSa, style));
            }

            Assert.Equal(march, system.MonthName(3, arSa, abbreviated: false));
        }

        [Theory]
        [InlineData("uq", 9, "en-GB", "Ramadan")]
        [InlineData("uq", 9, "ar-SA", "رمضان")]
        [InlineData("uq", 9, "am-ET", "ረመዳን")]
        [InlineData("uq", 12, "en-GB", "Dhuʻl-Hijjah")]
        [InlineData("et", 1, "en-GB", "Meskerem")]
        [InlineData("et", 13, "en-GB", "Pagumen")]
        [InlineData("et", 13, "ar-EG", "باجمن")]
        [InlineData("et", 13, "am-ET", "ጳጉሜን")]
        [InlineData("et", 7, "ti-ET", "Megabit")]
        [InlineData("uq", 1, "fr-FR", "Muharram")]
        public void Umm_al_qura_and_ethiopian_month_names_come_from_cores_resources_falling_back_to_english(
            string calendar, int month, string culture, string expected)
        {
            ICalendarSystem system = Systems.Single(s => s.Code == calendar);

            Assert.Equal(expected, system.MonthName(month, CultureInfo.GetCultureInfo(culture), abbreviated: false));
            Assert.Equal(expected, system.MonthName(month, CultureInfo.GetCultureInfo(culture), abbreviated: true));
        }

        [Fact]
        public void A_culture_with_a_unicode_extension_reaches_its_languages_month_names()
        {
            CultureInfo latinDigits = new("ar-SA-u-nu-latn");

            Assert.Equal("رمضان", new UmAlQuraCalendarSystem().MonthName(9, latinDigits, abbreviated: false));
        }

        [Fact]
        public void Gregorian_month_names_come_from_the_culture()
        {
            GregorianCalendarSystem system = new();

            Assert.Equal("September", system.MonthName(9, PinnedCultures.EnGb, abbreviated: false));
            Assert.Equal("Sept", system.MonthName(9, PinnedCultures.EnGb, abbreviated: true));
            Assert.Equal("مارس", system.MonthName(3, PinnedCultures.ArSa, abbreviated: false));
        }

        [Theory]
        [InlineData("gc", 0)]
        [InlineData("gc", 13)]
        [InlineData("uq", 13)]
        [InlineData("et", 0)]
        [InlineData("et", 14)]
        public void A_month_outside_the_calendar_has_no_name(string calendar, int month)
        {
            ICalendarSystem system = Systems.Single(s => s.Code == calendar);

            Assert.Throws<ArgumentOutOfRangeException>(() => system.MonthName(month, CultureInfo.InvariantCulture, abbreviated: false));
        }

        [Theory]
        [InlineData("١٥/٣/٢٠٢٦")]
        [InlineData("15‏/3‏/2026")]
        [InlineData(" 15 / 03 / 2026 ")]
        [InlineData("15-3-2026")]
        [InlineData("2026-03-15")]
        [InlineData("2026-3-15")]
        public void The_numeric_style_reads_any_digits_and_separators(string text)
        {
            Assert.Equal(new DateOnly(2026, 3, 15), new GregorianCalendarSystem().TryParse(text, PinnedCultures.ArSa));
        }

        [Fact]
        public void The_numeric_style_reads_in_the_cultures_order()
        {
            GregorianCalendarSystem system = new();

            Assert.Equal(new DateOnly(2026, 3, 4), system.TryParse("4/3/2026", PinnedCultures.EnGb));
            Assert.Equal(new DateOnly(2026, 4, 3), system.TryParse("4/3/2026", WithShortDate("en-US", "M/d/yyyy")));
            Assert.Equal(new DateOnly(2026, 3, 4), system.TryParse("2026/3/4", WithShortDate("zh-CN", "yyyy/M/d")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("15 Mar 2026")]
        [InlineData("15/03/26")]
        [InlineData("15/03/02026")]
        [InlineData("015/03/2026")]
        [InlineData("15/03")]
        [InlineData("15/03/2026/1")]
        [InlineData("31/02/2026")]
        [InlineData("29/02/2025")]
        [InlineData("00/01/2026")]
        [InlineData("-05/01/2024")]
        [InlineData("+05/01/2024")]
        [InlineData("2024-01-05-")]
        [InlineData("(05)/(01)/(2024)")]
        [InlineData("05/01/2024/")]
        [InlineData("2026-03-15 gc")]
        public void Text_that_is_not_a_numeric_date_of_the_calendar_does_not_read(string? text)
        {
            Assert.Null(new GregorianCalendarSystem().TryParse(text!, PinnedCultures.EnGb));
        }

        [Fact]
        public void A_leap_day_reads_in_a_leap_year()
        {
            Assert.Equal(new DateOnly(2024, 2, 29), new GregorianCalendarSystem().TryParse("29/02/2024", PinnedCultures.EnGb));
        }

        [Fact]
        public void Arguments_are_checked()
        {
            foreach (ICalendarSystem system in Systems)
            {
                DateOnly date = new(2026, 3, 15);
                DateTimeOffset instant = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

                Assert.Throws<ArgumentNullException>(() => system.Format(date, null!, DateStyle.Short));
                Assert.Throws<ArgumentOutOfRangeException>(() => system.Format(date, PinnedCultures.EnGb, (DateStyle)9));
                Assert.Throws<ArgumentNullException>(() => system.Format(instant, null!, PinnedCultures.EnGb, DateStyle.Short));
                Assert.Throws<ArgumentNullException>(() => system.Format(instant, TimeZoneInfo.Utc, null!, DateStyle.Short));
                Assert.Throws<ArgumentNullException>(() => system.MonthName(1, null!, abbreviated: false));
                Assert.Throws<ArgumentNullException>(() => system.TryParse("2026-03-15", null!));
            }
        }

        [Fact]
        public void An_instant_renders_its_date_only_on_request()
        {
            DateTimeOffset instant = new(2026, 3, 15, 13, 5, 0, TimeSpan.FromHours(3));
            TimeZoneInfo utc = TimeZoneInfo.Utc;
            GregorianCalendarSystem system = new();

            Assert.Equal("15/03/2026 10:05", system.Format(instant, utc, PinnedCultures.EnGb, DateStyle.Short));
            Assert.Equal("15/03/2026", system.Format(instant, utc, PinnedCultures.EnGb, DateStyle.Short, includeTime: false));
        }

        [Fact]
        public void The_gregorian_system_covers_every_date_exactly()
        {
            GregorianCalendarSystem system = new();

            Assert.Equal(CalendarCodes.Gregorian, system.Code);
            Assert.Equal(12, system.MonthCount);
            Assert.Equal(DateOnly.MinValue, system.MinSupported);
            Assert.Equal(DateOnly.MaxValue, system.MaxSupported);
            Assert.Equal((2024, 2, 29), system.Decompose(new DateOnly(2024, 2, 29)));
            Assert.Equal((1, 1, 1), system.Decompose(DateOnly.MinValue));
            Assert.Equal((9999, 12, 31), system.Decompose(DateOnly.MaxValue));
            Assert.Equal("0001/01/01", system.Format(DateOnly.MinValue, WithShortDate("ja-JP", "yyyy/MM/dd"), DateStyle.Short));
            Assert.Equal(DateOnly.MaxValue, system.TryParse("9999-12-31", CultureInfo.InvariantCulture));
            Assert.True(system.Calendar.IsReadOnly);
        }

        /// <summary>
        ///     A clone of a culture whose short date pattern and separator are pinned, so an assertion
        ///     on its field order does not move with the host's ICU release.
        /// </summary>
        /// <param name="name">The culture.</param>
        /// <param name="pattern">The short date pattern, with <c>/</c> as the separator.</param>
        /// <returns>The read-only clone.</returns>
        private static CultureInfo WithShortDate(string name, string pattern)
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
            culture.DateTimeFormat.Calendar = culture.OptionalCalendars.OfType<GregorianCalendar>().First();
            culture.DateTimeFormat.DateSeparator = "/";
            culture.DateTimeFormat.ShortDatePattern = pattern;
            return CultureInfo.ReadOnly(culture);
        }
    }
}
