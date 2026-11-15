// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Calendars;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     The Umm al-Qura months <c>core.CalendarMonths</c> is seeded with: every month .NET supports,
    ///     contiguous and never overlapping, each as <see cref="UmAlQuraCalendar" /> computes it.
    /// </summary>
    public class CalendarMonthsSeedTests
    {
        [Fact]
        public void Every_month_from_1318_to_1500_is_seeded_once_in_order()
        {
            IReadOnlyList<CalendarMonthRow> months = CalendarMonthRowConfiguration.UmAlQuraMonths();

            Assert.Equal(183 * 12, months.Count);
            Assert.All(months, static month => Assert.Equal("uq", month.Calendar));
            Assert.Equal((1318, 1), (months[0].Year, months[0].Month));
            Assert.Equal((1500, 12), (months[^1].Year, months[^1].Month));
            Assert.Equal(months.Count, months.Select(static month => (month.Year, month.Month)).Distinct().Count());
        }

        [Fact]
        public void The_months_are_contiguous_and_each_ends_the_day_before_the_next_begins()
        {
            IReadOnlyList<CalendarMonthRow> months = CalendarMonthRowConfiguration.UmAlQuraMonths();

            for (int index = 1; index < months.Count; index++)
            {
                Assert.Equal(months[index - 1].EndDate, months[index].StartDate);
            }

            Assert.All(months, static month => Assert.InRange(month.EndDate.DayNumber - month.StartDate.DayNumber, 29, 30));
        }

        [Fact]
        public void Each_month_is_the_one_the_calendar_computes()
        {
            UmAlQuraCalendar calendar = new();

            Assert.All(CalendarMonthRowConfiguration.UmAlQuraMonths(), month =>
            {
                Assert.Equal((month.Year, month.Month, 1), Date(calendar, month.StartDate));

                // The day before the exclusive end is the month's last day.
                Assert.Equal((month.Year, month.Month, calendar.GetDaysInMonth(month.Year, month.Month)), Date(calendar, month.EndDate.AddDays(-1)));
            });

            // The range opens and closes where .NET's support does.
            CalendarMonthRow first = CalendarMonthRowConfiguration.UmAlQuraMonths()[0];
            Assert.Equal(DateOnly.FromDateTime(calendar.MinSupportedDateTime), first.StartDate);
            Assert.Equal(DateOnly.FromDateTime(calendar.MaxSupportedDateTime).AddDays(1), CalendarMonthRowConfiguration.UmAlQuraMonths()[^1].EndDate);
        }

        /// <summary>A Gregorian day as the Umm al-Qura calendar names it.</summary>
        /// <param name="calendar">The calendar.</param>
        /// <param name="day">The day.</param>
        /// <returns>Its year, month and day.</returns>
        private static (int Year, int Month, int Day) Date(UmAlQuraCalendar calendar, DateOnly day)
        {
            var time = day.ToDateTime(TimeOnly.MinValue);
            return (calendar.GetYear(time), calendar.GetMonth(time), calendar.GetDayOfMonth(time));
        }
    }
}
