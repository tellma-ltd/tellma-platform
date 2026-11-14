// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.Tests.Semantics
{
    /// <summary>
    ///     The second implementation's calendars, checked against sources of their own.
    /// </summary>
    /// <remarks>
    ///     The differential runs compare the engine with these readings, so a reading that shared a
    ///     mistake with the engine would hide it. The Ethiopian reading is held to ICU's vectors, and
    ///     the Umm al-Qura reading to the month table the server is given.
    /// </remarks>
    public sealed class ReferenceCalendarTests
    {
        /// <summary>Every ICU vector reads as the date ICU gives, and converts back to its Gregorian date.</summary>
        [Fact]
        public void Ethiopian_AgreesWithIcuOnEveryVector()
        {
            Assert.True(EthiopicVectors.All.Count > 1000, "the vectors were not all read");

            foreach (EthiopicVector vector in EthiopicVectors.All)
            {
                string where = vector.Gregorian.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.True(
                    ReferenceCalendars.ToCalendar("et", vector.Gregorian) == vector.Ethiopian,
                    where + " reads as " + ReferenceCalendars.ToCalendar("et", vector.Gregorian) + ", not " + vector.Ethiopian);
                Assert.Equal(vector.Gregorian, ReferenceCalendars.ToGregorian("et", vector.Ethiopian));
            }
        }

        /// <summary>Every day of two centuries converts to an Ethiopian date and back to itself, one day after the last.</summary>
        [Fact]
        public void Ethiopian_RoundTripsEveryDay()
        {
            CalendarDate? previous = null;
            for (DateOnly day = new(1900, 1, 1); day <= new DateOnly(2100, 12, 31); day = day.AddDays(1))
            {
                CalendarDate parts = ReferenceCalendars.ToCalendar("et", day)!.Value;
                Assert.Equal(day, ReferenceCalendars.ToGregorian("et", parts));

                // Each day follows the one before it: the next day of the month, or the first of the
                // next month, or the first of the next year after the thirteenth month.
                if (previous is CalendarDate before)
                {
                    bool next = parts == before with { Day = before.Day + 1 }
                        || (parts.Day == 1 && parts.Year == before.Year && parts.Month == before.Month + 1)
                        || (parts == new CalendarDate(before.Year + 1, 1, 1) && before.Month == 13);
                    Assert.True(next, before + " is followed by " + parts);
                }

                previous = parts;
            }
        }

        /// <summary>Every day the month table covers reads as the month whose span holds it.</summary>
        [Fact]
        public void UmAlQura_AgreesWithTheMonthTable()
        {
            foreach (CalendarMonth month in CalendarMonths.All)
            {
                for (DateOnly day = month.StartDate; day < month.EndDate; day = day.AddDays(1))
                {
                    CalendarDate expected = new(month.Year, month.Month, day.DayNumber - month.StartDate.DayNumber + 1);
                    Assert.Equal(expected, ReferenceCalendars.ToCalendar("uq", day));
                    Assert.Equal(day, ReferenceCalendars.ToGregorian("uq", expected));
                }
            }
        }

        /// <summary>A day the month table does not cover has no Umm al-Qura date, at either end.</summary>
        [Fact]
        public void UmAlQura_HasNoDateOutsideTheMonthTable()
        {
            DateOnly first = CalendarMonths.All[0].StartDate;
            DateOnly end = CalendarMonths.All[^1].EndDate;

            Assert.Null(ReferenceCalendars.ToCalendar("uq", first.AddDays(-1)));
            Assert.Null(ReferenceCalendars.ToCalendar("uq", end));
            Assert.Null(ReferenceCalendars.ToGregorian("uq", new CalendarDate(CalendarMonths.FirstYear - 1, 12, 1)));
            Assert.Null(ReferenceCalendars.ToGregorian("uq", new CalendarDate(CalendarMonths.LastYear + 1, 1, 1)));
        }

        /// <summary>The month table tiles its span: each month begins the day the one before it ends.</summary>
        [Fact]
        public void TheMonthTable_HasNoGapsOrOverlaps()
        {
            Assert.Equal((CalendarMonths.LastYear - CalendarMonths.FirstYear + 1) * 12, CalendarMonths.All.Count);

            for (int index = 1; index < CalendarMonths.All.Count; index++)
            {
                Assert.Equal(CalendarMonths.All[index - 1].EndDate, CalendarMonths.All[index].StartDate);
            }
        }
    }
}
