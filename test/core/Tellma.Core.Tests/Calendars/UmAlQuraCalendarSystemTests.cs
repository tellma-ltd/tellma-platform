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
    ///     The Umm al-Qura calendar: published dates, the table's bounds, the months of the whole
    ///     table, the tabular Islamic calendar beyond it, and the epoch of the Islamic era, and the framework calendar over the same range.
    /// </summary>
    public class UmAlQuraCalendarSystemTests
    {
        private static readonly UmAlQuraCalendarSystem System = new();

        [Theory]
        // Month starts of the published Umm al-Qura calendar, which the framework's and ICU's tables
        // both reproduce: Ramadan 1445 began on Monday 11 March 2024, Eid al-Fitr 1446 fell on Sunday
        // 30 March 2025, Ramadan 1447 begins on Wednesday 18 February 2026.
        [InlineData("1999-04-17", 1420, 1, 1)]
        [InlineData("2018-09-11", 1440, 1, 1)]
        [InlineData("2024-03-11", 1445, 9, 1)]
        [InlineData("2024-06-16", 1445, 12, 10)]
        [InlineData("2024-07-07", 1446, 1, 1)]
        [InlineData("2025-03-01", 1446, 9, 1)]
        [InlineData("2025-03-30", 1446, 10, 1)]
        [InlineData("2025-06-26", 1447, 1, 1)]
        [InlineData("2026-02-18", 1447, 9, 1)]
        // 1 Muharram 1400, the day of the Grand Mosque seizure: Tuesday 20 November 1979.
        [InlineData("1979-11-20", 1400, 1, 1)]
        public void Published_dates_convert_both_ways(string gregorian, int year, int month, int day)
        {
            var date = DateOnly.ParseExact(gregorian, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.Equal((year, month, day), System.Decompose(date));
            Assert.Equal(date, System.TryParse($"{year:D4}-{month:D2}-{day:D2}", CultureInfo.InvariantCulture));
        }

        [Fact]
        public void The_exact_range_is_the_table_from_1318_to_1500()
        {
            Assert.Equal(new DateOnly(1900, 4, 30), System.MinSupported);
            Assert.Equal(new DateOnly(2077, 11, 16), System.MaxSupported);
            Assert.Equal((1318, 1, 1), System.Decompose(System.MinSupported));
            Assert.Equal((1500, 12, 30), System.Decompose(System.MaxSupported));
            Assert.Equal(CalendarCodes.UmAlQura, System.Code);
            Assert.Equal(12, System.MonthCount);
            Assert.Equal(System.MinSupported, DateOnly.FromDateTime(new UmAlQuraCalendar().MinSupportedDateTime));
            Assert.True(System.Calendar.IsReadOnly);
        }

        [Fact]
        public void The_months_of_the_table_are_contiguous_and_29_or_30_days_long()
        {
            // The 2,196 months from AH 1318 to 1500: each begins the day after the previous ends.
            UmAlQuraCalendar table = new();
            DateOnly expectedStart = System.MinSupported;
            int months = 0;
            for (int year = 1318; year <= 1500; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    var start = DateOnly.FromDateTime(table.ToDateTime(year, month, 1, 0, 0, 0, 0));
                    int length = table.GetDaysInMonth(year, month);

                    Assert.Equal(expectedStart, start);
                    Assert.InRange(length, 29, 30);
                    Assert.Equal((year, month, 1), System.Decompose(start));
                    Assert.Equal((year, month, length), System.Decompose(start.AddDays(length - 1)));
                    expectedStart = start.AddDays(length);
                    months++;
                }
            }

            Assert.Equal(2196, months);
            Assert.Equal(System.MaxSupported.AddDays(1), expectedStart);
        }

        [Fact]
        public void Every_day_of_the_table_reads_back_from_its_numeric_rendering()
        {
            CultureInfo culture = PinnedCultures.ArSa;
            for (DateOnly date = System.MinSupported; date <= System.MaxSupported; date = date.AddDays(1))
            {
                Assert.Equal(date, System.TryParse(System.Format(date, culture, DateStyle.Short), culture));
            }
        }

        [Fact]
        public void Beyond_ah_1500_dates_convert_through_the_tabular_calendar_without_adjustment()
        {
            HijriCalendar tabular = new() { HijriAdjustment = 0 };
            DateOnly after = System.MaxSupported.AddDays(1);

            // The tabular calendar's 1501-01-01 falls on the table's last day, so Muharram 1501
            // begins the day after the table and has the tabular month's other 29 days.
            Assert.Equal((1501, 1, 1), System.Decompose(after));
            Assert.Equal("1/1/1501", System.Format(after, PinnedCultures.ArSa, DateStyle.Short));
            Assert.Equal(after, System.TryParse("1/1/1501", PinnedCultures.ArSa));
            Assert.Equal((1501, 1, 29), System.Decompose(new DateOnly(2077, 12, 15)));
            Assert.Null(System.TryParse("1501-01-30", CultureInfo.InvariantCulture));
            Assert.Equal(29, System.Calendar.GetDaysInMonth(1501, 1));

            // From Safar 1501 every date is the tabular calendar's.
            DateOnly safar = new(2077, 12, 16);
            Assert.Equal((1501, 2, 1), System.Decompose(safar));
            for (DateOnly date = safar; date <= new DateOnly(2100, 12, 31); date = date.AddDays(1))
            {
                var time = date.ToDateTime(TimeOnly.MinValue);
                Assert.Equal((tabular.GetYear(time), tabular.GetMonth(time), tabular.GetDayOfMonth(time)), System.Decompose(date));
            }

            Assert.Equal((9666, 4, 3), System.Decompose(DateOnly.MaxValue));
        }

        [Fact]
        public void Every_day_from_the_islamic_epoch_converts_both_ways_with_consecutive_days()
        {
            // Decompose, the framework calendar and the numeric style agree on every day from
            // AH 1-01-01 to 2100, across both seams: consecutive dates are consecutive days, and each
            // month has the length the calendar reports.
            Calendar calendar = System.Calendar;
            Assert.Equal((1, 1, 1), System.Decompose(new DateOnly(622, 7, 18)));
            (int Year, int Month, int Day) previous = (1, 1, 1);
            for (DateOnly date = new(622, 7, 19); date <= new DateOnly(2100, 12, 31); date = date.AddDays(1))
            {
                (int year, int month, int day) = System.Decompose(date);
                var time = date.ToDateTime(TimeOnly.MinValue);

                Assert.Equal((year, month, day), (calendar.GetYear(time), calendar.GetMonth(time), calendar.GetDayOfMonth(time)));
                Assert.Equal(time, calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
                bool nextDay = (year, month, day) == (previous.Year, previous.Month, previous.Day + 1);
                bool nextMonth = day == 1
                    && previous.Day == calendar.GetDaysInMonth(previous.Year, previous.Month)
                    && ((year, month) == (previous.Year, previous.Month + 1) || (year, month) == (previous.Year + 1, 1));
                Assert.True(nextDay || nextMonth, $"{date:yyyy-MM-dd} is {year}-{month}-{day} after {previous}.");
                previous = (year, month, day);
            }

            Assert.Equal(DateOnly.FromDateTime(calendar.MinSupportedDateTime), new DateOnly(622, 7, 18));
        }

        [Fact]
        public void The_framework_calendar_does_arithmetic_beyond_the_table()
        {
            Calendar calendar = System.Calendar;

            Assert.Equal(1307, calendar.GetYear(new DateTime(1890, 1, 1)));
            var lastMonth = calendar.ToDateTime(1500, 12, 30, 9, 0, 0, 0);
            DateTime muharram = calendar.AddMonths(lastMonth, 1);
            Assert.Equal((1501, 1, 29), (calendar.GetYear(muharram), calendar.GetMonth(muharram), calendar.GetDayOfMonth(muharram)));
            Assert.Equal(lastMonth.TimeOfDay, muharram.TimeOfDay);
            DateTime back = calendar.AddMonths(calendar.ToDateTime(1318, 1, 15, 0, 0, 0, 0), -1);
            Assert.Equal((1317, 12, 15), (calendar.GetYear(back), calendar.GetMonth(back), calendar.GetDayOfMonth(back)));
            DateTime nextYear = calendar.AddYears(calendar.ToDateTime(1500, 1, 1, 0, 0, 0, 0), 1);
            Assert.Equal((1501, 1, 1), (calendar.GetYear(nextYear), calendar.GetMonth(nextYear), calendar.GetDayOfMonth(nextYear)));
            Assert.Equal(new DateTime(2077, 11, 17), nextYear);
            Assert.Equal(new HijriCalendar { HijriAdjustment = 0 }.GetDaysInYear(1501) - 1, calendar.GetDaysInYear(1501));

            Assert.Equal(12, calendar.GetMonthsInYear(1447));
            Assert.Equal([1], calendar.Eras);
            Assert.Equal(1451, calendar.TwoDigitYearMax);
            Assert.Equal(1447, calendar.ToFourDigitYear(47));
            Assert.Throws<InvalidOperationException>(() => calendar.TwoDigitYearMax = 1500);
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetYear(new DateTime(622, 7, 17)));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.ToDateTime(1501, 1, 30, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.AddMonths(calendar.ToDateTime(1, 1, 1, 0, 0, 0, 0), -1));
        }

        [Fact]
        public void Before_ah_1318_dates_convert_through_the_tabular_calendar_and_meet_the_table_without_a_gap()
        {
            DateOnly before = System.MinSupported.AddDays(-1);

            Assert.Equal((1317, 12, 29), System.Decompose(before));
            Assert.Equal(before, System.TryParse("1317-12-29", CultureInfo.InvariantCulture));
            Assert.Null(System.TryParse("1317-12-30", CultureInfo.InvariantCulture));
            Assert.Equal("29 Dhuʻl-Hijjah 1317", System.Format(before, PinnedCultures.EnGb, DateStyle.Long));
        }

        [Fact]
        public void The_islamic_era_begins_on_622_07_18_and_earlier_dates_render_in_the_iso_gregorian_form()
        {
            Assert.Equal((1, 1, 1), System.Decompose(new DateOnly(622, 7, 18)));
            Assert.Throws<ArgumentOutOfRangeException>(() => System.Decompose(new DateOnly(622, 7, 17)));
            Assert.Equal("0622-07-17 gc", System.Format(new DateOnly(622, 7, 17), PinnedCultures.ArSa, DateStyle.Full));
            Assert.Null(System.TryParse(System.Format(new DateOnly(600, 1, 1), PinnedCultures.ArSa, DateStyle.Short), PinnedCultures.ArSa));
        }

        [Theory]
        [InlineData("1447-13-01")]
        [InlineData("1447-00-01")]
        [InlineData("1447-01-31")]
        [InlineData("1446-12-30")]
        [InlineData("9667-01-01")]
        public void A_day_the_calendar_lacks_does_not_read(string text)
        {
            // Dhu al-Hijjah 1446 has 29 days in the table.
            Assert.Null(System.TryParse(text, CultureInfo.InvariantCulture));
        }
    }
}
