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
    ///     The extended Umm al-Qura calendar over its whole range: month lengths against the
    ///     framework's tabular calendar, every day from the Islamic epoch to the last representable
    ///     day, the months of AH 9666 that end after that day, and the refusals past it.
    /// </summary>
    public class UmAlQuraExtendedCalendarTests
    {
        /// <summary>The lengths of the twelve months of AH 9666, a common year of the tabular calendar.</summary>
        private static readonly int[] LengthsOf9666 = [30, 29, 30, 29, 30, 29, 30, 29, 30, 29, 30, 29];

        private static readonly UmAlQuraCalendarSystem System = new();

        private static readonly HijriCalendar Tabular = new() { HijriAdjustment = 0 };

        [Fact]
        public void Outside_the_table_every_month_and_year_has_the_tabular_length()
        {
            Calendar calendar = System.Calendar;
            for (int year = 1; year <= 9666; year++)
            {
                if (year is >= 1318 and <= 1500)
                {
                    continue;
                }

                for (int month = 1; month <= 12; month++)
                {
                    // The framework answers every month that begins on or before AD 9999-12-31.
                    if (year == 9666 && month > 4)
                    {
                        continue;
                    }

                    // Muharram 1501 gives its first day to the table's last.
                    int expected = Tabular.GetDaysInMonth(year, month) - ((year, month) == (1501, 1) ? 1 : 0);
                    Assert.Equal(expected, UmAlQuraExtendedCalendar.DaysIn(year, month));
                }

                int days = Tabular.GetDaysInYear(year) - (year == 1501 ? 1 : 0);
                Assert.Equal(days, calendar.GetDaysInYear(year));
                Assert.Equal(Tabular.IsLeapYear(year) && year != 1501, calendar.IsLeapYear(year));
            }
        }

        [Fact]
        public void Every_day_from_the_islamic_epoch_to_the_last_representable_day_round_trips()
        {
            // Each day decomposes, composes back to itself, and follows the previous one: the next
            // day of its month, or the first of the next month once the previous month's length is
            // reached.
            DateOnly epoch = new(622, 7, 18);
            Assert.True(UmAlQuraExtendedCalendar.TryDecompose(epoch, out int year, out int month, out int day));
            Assert.Equal((1, 1, 1), (year, month, day));
            (int Year, int Month, int Day) previous = (year, month, day);
            for (int dayNumber = epoch.DayNumber + 1; dayNumber <= DateOnly.MaxValue.DayNumber; dayNumber++)
            {
                var date = DateOnly.FromDayNumber(dayNumber);
                Assert.True(UmAlQuraExtendedCalendar.TryDecompose(date, out year, out month, out day));
                Assert.Equal(date, UmAlQuraExtendedCalendar.Compose(year, month, day));

                bool nextDay = (year, month, day) == (previous.Year, previous.Month, previous.Day + 1);
                bool nextMonth = day == 1
                    && previous.Day == UmAlQuraExtendedCalendar.DaysIn(previous.Year, previous.Month)
                    && ((year, month) == (previous.Year, previous.Month + 1) || (year, month) == (previous.Year + 1, 1));
                if (!(nextDay || nextMonth))
                {
                    Assert.Fail($"{date:yyyy-MM-dd} is {year}-{month}-{day} after {previous}.");
                }

                previous = (year, month, day);
            }

            Assert.Equal((9666, 4, 3), previous);
        }

        [Fact]
        public void Every_month_of_ah_9666_has_its_length_although_only_four_begin_before_the_last_representable_day()
        {
            Calendar calendar = System.Calendar;

            Assert.Equal(LengthsOf9666, Enumerable.Range(1, 12).Select(static month => UmAlQuraExtendedCalendar.DaysIn(9666, month)));
            Assert.Equal(LengthsOf9666, Enumerable.Range(1, 12).Select(month => calendar.GetDaysInMonth(9666, month)));
            Assert.Equal(354, calendar.GetDaysInYear(9666));
            Assert.False(calendar.IsLeapYear(9666));
            Assert.Equal(12, calendar.GetMonthsInYear(9666));

            // The months that begin by AD 9999-12-31; Rabi' II 9666 has only three of its days there.
            Assert.Equal(new DateOnly(9999, 10, 1), UmAlQuraExtendedCalendar.Compose(9666, 1, 1));
            Assert.Equal(new DateOnly(9999, 10, 31), UmAlQuraExtendedCalendar.Compose(9666, 2, 1));
            Assert.Equal(new DateOnly(9999, 11, 29), UmAlQuraExtendedCalendar.Compose(9666, 3, 1));
            Assert.Equal(new DateOnly(9999, 12, 29), UmAlQuraExtendedCalendar.Compose(9666, 4, 1));
            Assert.Equal(DateOnly.MaxValue, UmAlQuraExtendedCalendar.Compose(9666, 4, 3));
            Assert.True(UmAlQuraExtendedCalendar.TryDecompose(DateOnly.MaxValue, out int year, out int month, out int day));
            Assert.Equal((9666, 4, 3), (year, month, day));
            Assert.Equal(9666, calendar.GetYear(DateTime.MaxValue));
            Assert.Equal(30 + 29 + 30 + 3, calendar.GetDayOfYear(DateTime.MaxValue));
        }

        [Fact]
        public void The_framework_tabular_calendar_refuses_the_months_of_ah_9666_after_the_last_representable_day()
        {
            // Why the lengths are the tabular rule's own: the framework stops at the month of
            // DateTime.MaxValue, although it answers the year's length and leap rule.
            Assert.Equal(29, Tabular.GetDaysInMonth(9666, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Tabular.GetDaysInMonth(9666, 5));
            Assert.Equal(354, Tabular.GetDaysInYear(9666));
        }

        [Theory]
        [InlineData(9666, 4, 4)]
        [InlineData(9666, 4, 29)]
        [InlineData(9666, 5, 1)]
        [InlineData(9666, 12, 29)]
        public void A_day_after_the_last_representable_day_does_not_compose(int year, int month, int day)
        {
            Calendar calendar = System.Calendar;

            Assert.Null(UmAlQuraExtendedCalendar.Compose(year, month, day));
            Assert.Null(System.TryParse($"{year:D4}-{month:D2}-{day:D2}", CultureInfo.InvariantCulture));
            ArgumentOutOfRangeException toDateTime = Assert.Throws<ArgumentOutOfRangeException>(
                () => calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
            Assert.Equal("day", toDateTime.ParamName);
            Assert.StartsWith("The year, month and day do not name a date the calendar supports.", toDateTime.Message, StringComparison.Ordinal);
            ArgumentOutOfRangeException leapDay = Assert.Throws<ArgumentOutOfRangeException>(
                () => calendar.IsLeapDay(year, month, day));
            Assert.Equal("day", leapDay.ParamName);
        }

        [Fact]
        public void Beyond_ah_9666_no_month_has_a_length()
        {
            Calendar calendar = System.Calendar;

            Assert.Equal(0, UmAlQuraExtendedCalendar.DaysIn(9667, 1));
            Assert.Null(UmAlQuraExtendedCalendar.Compose(9667, 1, 1));
            Assert.Equal("month", Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetDaysInMonth(9667, 1)).ParamName);
            Assert.Equal("year", Assert.Throws<ArgumentOutOfRangeException>(() => calendar.GetDaysInYear(9667)).ParamName);
            Assert.Equal("year", Assert.Throws<ArgumentOutOfRangeException>(() => calendar.IsLeapYear(9667)).ParamName);
        }

        [Fact]
        public void Arithmetic_reaches_the_last_representable_day_and_refuses_past_it()
        {
            Calendar calendar = System.Calendar;
            var last = calendar.ToDateTime(9666, 4, 3, 18, 30, 0, 0);

            Assert.Equal(DateTime.MaxValue.Date.AddHours(18).AddMinutes(30), last);
            Assert.Equal(DateTime.MaxValue.Date, calendar.AddYears(calendar.ToDateTime(9665, 4, 3, 0, 0, 0, 0), 1));
            Assert.Equal(DateTime.MaxValue.Date, calendar.AddMonths(calendar.ToDateTime(9666, 3, 3, 0, 0, 0, 0), 1));
            DateTime back = calendar.AddMonths(last, -1);
            Assert.Equal((9666, 3, 3), (calendar.GetYear(back), calendar.GetMonth(back), calendar.GetDayOfMonth(back)));
            Assert.Equal(last.TimeOfDay, back.TimeOfDay);

            // A result after the last day, including a day clamped to a month that has it but
            // cannot hold it, is refused as outside the range, never as a framework failure.
            AssertOutOfRange("months", () => calendar.AddMonths(last, 1));
            AssertOutOfRange("months", () => calendar.AddMonths(calendar.ToDateTime(9666, 3, 30, 0, 0, 0, 0), 1));
            AssertOutOfRange("years", () => calendar.AddYears(calendar.ToDateTime(9665, 5, 1, 0, 0, 0, 0), 1));
        }

        [Fact]
        public void The_last_representable_day_renders_and_reads_back()
        {
            CultureInfo culture = PinnedCultures.ArSa;
            string text = System.Format(DateOnly.MaxValue, culture, DateStyle.Short);

            Assert.Equal("3/4/9666", text);
            Assert.Equal(DateOnly.MaxValue, System.TryParse(text, culture));
            Assert.Equal(DateOnly.MaxValue, System.TryParse("9666-04-03", CultureInfo.InvariantCulture));
        }

        /// <summary>Asserts that an action is refused as a result outside the calendar's range.</summary>
        /// <param name="parameter">The argument the refusal blames.</param>
        /// <param name="action">The action.</param>
        private static void AssertOutOfRange(string parameter, Func<DateTime> action)
        {
            ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(() => action());
            Assert.Equal(parameter, refusal.ParamName);
            Assert.StartsWith("The result is outside the range the calendar supports.", refusal.Message, StringComparison.Ordinal);
        }
    }
}
