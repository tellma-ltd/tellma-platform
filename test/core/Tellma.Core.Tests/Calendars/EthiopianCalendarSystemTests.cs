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
    ///     The Ethiopian calendar: the conformance vectors the server, the query language and the
    ///     browser share (taken from ICU's <c>ethiopic</c> calendar) and their inverses, the epoch,
    ///     the leap rule, the New Year rule and its century shifts, round trips over the whole range,
    ///     and the bounds.
    /// </summary>
    public class EthiopianCalendarSystemTests
    {
        /// <summary>The Julian day number of <see cref="DateOnly.MinValue" />, Gregorian AD 0001-01-01.</summary>
        private const int JulianDayOfDayNumberZero = 1721426;

        private static readonly EthiopianCalendarSystem System = new();

        [Theory]
        [InlineData("1752-09-14", 1745, 1, 6)]
        [InlineData("1900-01-01", 1892, 4, 23)]
        [InlineData("1971-09-11", 1963, 13, 6)]
        [InlineData("2000-01-01", 1992, 4, 22)]
        [InlineData("2007-09-12", 2000, 1, 1)]
        [InlineData("2011-09-11", 2003, 13, 6)]
        [InlineData("2015-09-12", 2008, 1, 1)]
        [InlineData("2019-09-11", 2011, 13, 6)]
        [InlineData("2020-01-01", 2012, 4, 22)]
        [InlineData("2024-09-11", 2017, 1, 1)]
        [InlineData("2026-09-04", 2018, 12, 29)]
        [InlineData("2500-01-01", 2492, 4, 19)]
        public void The_conformance_vectors_convert_both_ways(string gregorian, int year, int month, int day)
        {
            var date = DateOnly.ParseExact(gregorian, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.Equal((year, month, day), System.Decompose(date));
            Assert.Equal(date, System.TryParse($"{year:D4}-{month:D2}-{day:D2}", CultureInfo.InvariantCulture));
            Assert.Equal(date, System.Calendar.ToDateTime(year, month, day, 0, 0, 0, 0).ToDateOnly());
        }

        [Fact]
        public void The_epoch_is_julian_day_1724221()
        {
            // Meskerem 1, year 1 (Amete Mihret) is Julian day 1,724,221: Gregorian AD 0008-08-27.
            var epoch = DateOnly.FromDayNumber(1724221 - JulianDayOfDayNumberZero);

            Assert.Equal(new DateOnly(8, 8, 27), epoch);
            Assert.Equal((1, 1, 1), System.Decompose(epoch));
            Assert.Equal(epoch.ToDateTime(TimeOnly.MinValue), System.Calendar.MinSupportedDateTime);
        }

        [Fact]
        public void Every_day_round_trips_over_the_whole_representable_range()
        {
            // Every day from the epoch to DateOnly.MaxValue: decomposition and composition are
            // inverses, consecutive days are consecutive Ethiopian days, and every month is 30 days
            // but Pagume, which is 6 days exactly in the years with y % 4 == 3.
            (int Year, int Month, int Day) previous = (1, 1, 0);
            for (int dayNumber = EthiopianCalendar.EpochDayNumber; dayNumber <= DateOnly.MaxValue.DayNumber; dayNumber++)
            {
                (int year, int month, int day) = EthiopianCalendar.FromDayNumber(dayNumber);
                Assert.Equal(dayNumber, EthiopianCalendar.ToDayNumber(year, month, day));

                bool nextDay = (year, month, day) == (previous.Year, previous.Month, previous.Day + 1);
                bool nextMonth = day == 1 && previous.Day == EthiopianCalendar.DaysIn(previous.Year, previous.Month)
                    && ((year, month) == (previous.Year, previous.Month + 1) || (year, month) == (previous.Year + 1, 1));
                Assert.True(nextDay || nextMonth, $"Day number {dayNumber} is {year}-{month}-{day} after {previous}.");
                previous = (year, month, day);
            }
        }

        [Fact]
        public void Every_day_of_the_supported_range_reads_back_from_its_numeric_rendering()
        {
            CultureInfo culture = PinnedCultures.EnGb;
            for (DateOnly date = System.MinSupported; date <= System.MaxSupported; date = date.AddDays(1))
            {
                Assert.Equal(date, System.TryParse(System.Format(date, culture, DateStyle.Short), culture));
            }
        }

        [Fact]
        public void Pagume_has_six_days_exactly_when_the_year_mod_four_is_three()
        {
            for (int year = 1745; year <= 2492; year++)
            {
                int days = (int)(EthiopianCalendar.ToDayNumber(year + 1, 1, 1) - EthiopianCalendar.ToDayNumber(year, 1, 1));
                Assert.Equal(year % 4 == 3 ? 366 : 365, days);
                Assert.Equal(year % 4 == 3, System.Calendar.IsLeapYear(year));
                Assert.Equal(year % 4 == 3 ? 6 : 5, System.Calendar.GetDaysInMonth(year, 13));
            }
        }

        [Fact]
        public void New_year_falls_on_11_september_or_on_12_september_before_a_gregorian_leap_year()
        {
            // From March 1900 to February 2100, Meskerem 1 falls on 11 September, or on 12 September
            // in the Gregorian year before a leap year. 2100 counts as one here: the Ethiopian leap
            // day falls every fourth year regardless, and the leap day 2100 skips only moves New Year
            // from September 2100 on.
            for (int gregorianYear = 1900; gregorianYear <= 2099; gregorianYear++)
            {
                int expectedDay = (gregorianYear + 1) % 4 == 0 ? 12 : 11;
                (int year, int month, int day) = System.Decompose(new DateOnly(gregorianYear, 9, expectedDay));
                Assert.Equal((gregorianYear - 7, 1, 1), (year, month, day));
            }
        }

        [Theory]
        [InlineData(1745, "1752-09-09")]
        [InlineData(1790, "1797-09-09")]
        [InlineData(1791, "1798-09-09")]
        [InlineData(1792, "1799-09-10")]
        [InlineData(1793, "1800-09-10")]
        [InlineData(1796, "1803-09-11")]
        [InlineData(1797, "1804-09-10")]
        [InlineData(1896, "1903-09-12")]
        [InlineData(1897, "1904-09-11")]
        [InlineData(2096, "2103-09-13")]
        [InlineData(2097, "2104-09-12")]
        [InlineData(2492, "2499-09-15")]
        public void New_year_moves_a_day_later_after_each_gregorian_century_year_not_divisible_by_400(int year, string gregorian)
        {
            // ICU's ethiopic calendar: 9 or 10 September in the 1700s, 10 or 11 in the 1800s,
            // 11 or 12 in the 1900s and 2000s, 12 or 13 in the 2100s; 1 Meskerem 2492 is 15 September.
            var date = DateOnly.ParseExact(gregorian, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.Equal((year, 1, 1), System.Decompose(date));
        }

        [Fact]
        public void The_supported_range_is_1752_09_14_to_2500_01_01()
        {
            Assert.Equal(new DateOnly(1752, 9, 14), System.MinSupported);
            Assert.Equal(new DateOnly(2500, 1, 1), System.MaxSupported);
            Assert.Equal(CalendarCodes.Ethiopian, System.Code);
            Assert.Equal(13, System.MonthCount);
        }

        [Fact]
        public void Dates_outside_the_supported_range_still_convert_exactly()
        {
            Assert.Equal((1745, 1, 5), System.Decompose(new DateOnly(1752, 9, 13)));
            Assert.Equal((2492, 4, 20), System.Decompose(new DateOnly(2500, 1, 2)));
            Assert.Equal((9992, 2, 21), System.Decompose(DateOnly.MaxValue));
        }

        [Fact]
        public void A_date_before_the_epoch_cannot_be_decomposed_and_renders_in_the_iso_gregorian_form()
        {
            DateOnly beforeEpoch = new(8, 8, 26);

            Assert.Throws<ArgumentOutOfRangeException>(() => System.Decompose(beforeEpoch));
            Assert.Equal("0008-08-26", System.Format(beforeEpoch, PinnedCultures.EnGb, DateStyle.Long));
            Assert.Equal("0001-01-01", System.Format(DateOnly.MinValue, PinnedCultures.EnGb, DateStyle.Short));
        }

        [Theory]
        [InlineData("2015-13-06", "2023-09-11")]
        [InlineData("2016-13-05", "2024-09-10")]
        public void Pagume_reads_back_up_to_its_last_day(string text, string gregorian)
        {
            Assert.Equal(DateOnly.ParseExact(gregorian, "yyyy-MM-dd", CultureInfo.InvariantCulture), System.TryParse(text, CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData("2016-13-06")]
        [InlineData("2015-13-07")]
        [InlineData("2015-14-01")]
        [InlineData("2015-01-31")]
        [InlineData("2015-00-01")]
        [InlineData("0000-01-01")]
        public void A_day_the_calendar_lacks_does_not_read(string text)
        {
            Assert.Null(System.TryParse(text, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void The_framework_calendar_adds_months_across_pagume_and_years_clamping_the_day()
        {
            Calendar calendar = System.Calendar;
            var nehasse30 = calendar.ToDateTime(2015, 12, 30, 8, 15, 0, 0);

            // Nehasse 30 + 1 month is Pagume, which has only 6 days in 2015.
            DateTime pagume = calendar.AddMonths(nehasse30, 1);
            Assert.Equal((2015, 13, 6), (calendar.GetYear(pagume), calendar.GetMonth(pagume), calendar.GetDayOfMonth(pagume)));
            Assert.Equal(nehasse30.TimeOfDay, pagume.TimeOfDay);

            // Pagume + 1 month is Meskerem of the next year; going back 14 months crosses a year.
            DateTime meskerem = calendar.AddMonths(pagume, 1);
            Assert.Equal((2016, 1, 6), (calendar.GetYear(meskerem), calendar.GetMonth(meskerem), calendar.GetDayOfMonth(meskerem)));
            DateTime back = calendar.AddMonths(meskerem, -14);
            Assert.Equal((2014, 13, 5), (calendar.GetYear(back), calendar.GetMonth(back), calendar.GetDayOfMonth(back)));

            // Pagume 6 a year later is Pagume 5: 2016 is not a leap year.
            DateTime nextYear = calendar.AddYears(pagume, 1);
            Assert.Equal((2016, 13, 5), (calendar.GetYear(nextYear), calendar.GetMonth(nextYear), calendar.GetDayOfMonth(nextYear)));
        }

        [Fact]
        public void The_framework_calendar_answers_its_structure()
        {
            Calendar calendar = System.Calendar;

            Assert.True(calendar.IsReadOnly);
            Assert.Equal([1], calendar.Eras);
            Assert.Equal(13, calendar.GetMonthsInYear(2017));
            Assert.Equal(366, calendar.GetDaysInYear(2015));
            Assert.Equal(365, calendar.GetDaysInYear(2016));
            Assert.True(calendar.IsLeapDay(2015, 13, 6));
            Assert.False(calendar.IsLeapDay(2015, 13, 5));
            Assert.False(calendar.IsLeapMonth(2015, 13));
            Assert.Equal(1, calendar.GetEra(new DateTime(2024, 9, 11)));
            Assert.Equal(DayOfWeek.Wednesday, calendar.GetDayOfWeek(new DateTime(2024, 9, 11)));
            Assert.Equal(366, calendar.GetDayOfYear(new DateTime(2023, 9, 11)));
            Assert.Equal(DateTime.MaxValue, calendar.MaxSupportedDateTime);
        }

        [Fact]
        public void The_framework_calendar_refuses_what_the_calendar_lacks()
        {
            Calendar calendar = System.Calendar;

            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.ToDateTime(2016, 13, 6, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.ToDateTime(2016, 14, 1, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.ToDateTime(0, 1, 1, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.ToDateTime(2016, 1, 1, 24, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetDaysInMonth(2016, 1, era: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetYear(new DateTime(8, 8, 26)));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.AddMonths(calendar.ToDateTime(1, 1, 1, 0, 0, 0, 0), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => calendar.AddYears(DateTime.MaxValue, 1));
        }
    }

    /// <summary>Converts a framework time to its date.</summary>
    internal static class DateTimeExtensions
    {
        /// <summary>The time's date.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The date.</returns>
        public static DateOnly ToDateOnly(this DateTime time)
        {
            return DateOnly.FromDateTime(time);
        }
    }
}
