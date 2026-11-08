// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The Ethiopian calendar as a framework <see cref="Calendar" />, for date arithmetic: twelve
    ///     months of 30 days and a thirteenth, Pagume, of 5 days, or 6 in a year <c>y</c> with
    ///     <c>y % 4 == 3</c>; a single era (Amete Mihret) whose year 1 begins on Gregorian
    ///     AD 0008-08-27.
    /// </summary>
    /// <remarks>
    ///     The conversion is the Julian-day-number arithmetic ICU's <c>ethiopic</c> calendar uses, so
    ///     the server, the query language and the browser agree on every day. It is exact for every
    ///     date from the epoch to <see cref="DateTime.MaxValue" />.
    /// </remarks>
    internal sealed class EthiopianCalendar : Calendar
    {
        /// <summary>The era of every date: Amete Mihret.</summary>
        public const int AmeteMihret = 1;

        /// <summary>The <see cref="DateOnly.DayNumber" /> of Meskerem 1, year 1: Gregorian AD 0008-08-27.</summary>
        public const int EpochDayNumber = 2795;

        /// <summary>The days in four years: three of 365 and one of 366.</summary>
        private const int DaysPerCycle = 1461;

        /// <summary>The largest month offset <see cref="AddMonths" /> accepts, as the framework's own calendars do.</summary>
        private const int MaxMonthOffset = 120000;

        /// <summary>The default of <see cref="TwoDigitYearMax" />.</summary>
        private const int DefaultTwoDigitYearMax = 2041;

        /// <summary>The last representable day, as a day number.</summary>
        private static readonly int MaxDayNumber = DateOnly.MaxValue.DayNumber;

        /// <summary>The last representable year.</summary>
        private static readonly int MaxYear = FromDayNumber(MaxDayNumber).Year;

        /// <summary>The year whose two-digit form is the largest that maps into the current century.</summary>
        private int _twoDigitYearMax = DefaultTwoDigitYearMax;

        /// <inheritdoc />
        /// <remarks>2041 by default: the Ethiopian year of Gregorian 2049, the framework Gregorian calendar's default.</remarks>
        public override int TwoDigitYearMax
        {
            get => _twoDigitYearMax;

            set
            {
                if (IsReadOnly)
                {
                    throw new InvalidOperationException("The calendar is read-only.");
                }

                ArgumentOutOfRangeException.ThrowIfLessThan(value, 99);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxYear);
                _twoDigitYearMax = value;
            }
        }

        /// <inheritdoc />
        public override DateTime MinSupportedDateTime => DateOnly.FromDayNumber(EpochDayNumber).ToDateTime(TimeOnly.MinValue);

        /// <inheritdoc />
        public override DateTime MaxSupportedDateTime => DateTime.MaxValue;

        /// <inheritdoc />
        public override CalendarAlgorithmType AlgorithmType => CalendarAlgorithmType.SolarCalendar;

        /// <inheritdoc />
        public override int[] Eras => [AmeteMihret];

        /// <summary>Whether a year has a sixth day of Pagume.</summary>
        /// <param name="year">The year.</param>
        /// <returns>True when <c>year % 4 == 3</c>.</returns>
        public static bool IsLeap(int year)
        {
            return year % 4 == 3;
        }

        /// <summary>The number of days in a month.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month, 1 to 13.</param>
        /// <returns>30 for months 1 to 12; 5 or 6 for Pagume.</returns>
        public static int DaysIn(int year, int month)
        {
            return month < 13 ? 30 : IsLeap(year) ? 6 : 5;
        }

        /// <summary>Converts a day number to the Ethiopian year, month and day.</summary>
        /// <param name="dayNumber">The <see cref="DateOnly.DayNumber" />, at least <see cref="EpochDayNumber" />.</param>
        /// <returns>The year, month and day.</returns>
        public static (int Year, int Month, int Day) FromDayNumber(int dayNumber)
        {
            // Days since the start of the (proleptic) year 0, in four-year cycles whose fourth year
            // is the leap one: r / 365 counts whole years, less one on the cycle's last day, which
            // closes the leap year rather than opening the next.
            int sinceYearZero = dayNumber - EpochDayNumber + 365;
            int cycles = sinceYearZero / DaysPerCycle;
            int remainder = sinceYearZero % DaysPerCycle;
            int year = (4 * cycles) + (remainder / 365) - (remainder / (DaysPerCycle - 1));
            int dayOfYear = remainder == DaysPerCycle - 1 ? 365 : remainder % 365;
            return (year, (dayOfYear / 30) + 1, (dayOfYear % 30) + 1);
        }

        /// <summary>Converts an Ethiopian year, month and day to a day number.</summary>
        /// <param name="year">The year, at least 1.</param>
        /// <param name="month">The month, 1 to 13.</param>
        /// <param name="day">The day, 1 to the month's length.</param>
        /// <returns>The <see cref="DateOnly.DayNumber" />; it may exceed the last representable day.</returns>
        public static long ToDayNumber(int year, int month, int day)
        {
            return EpochDayNumber + (365L * (year - 1)) + (year / 4) + (30 * (month - 1)) + (day - 1);
        }

        /// <summary>Whether a year, month and day name a representable Ethiopian date.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day.</param>
        /// <returns>True when they do.</returns>
        public static bool IsValid(int year, int month, int day)
        {
            return year >= 1 && year <= MaxYear
                && month is >= 1 and <= 13
                && day >= 1 && day <= DaysIn(year, month)
                && ToDayNumber(year, month, day) <= MaxDayNumber;
        }

        /// <inheritdoc />
        public override DateTime AddMonths(DateTime time, int months)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(months, -MaxMonthOffset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(months, MaxMonthOffset);
            (int year, int month, int day) = Decompose(time);

            // Months counted from year 0, divided back with floor division so a step backwards across
            // the epoch lands below year 1 (and is refused) rather than wrapping.
            long index = (13L * year) + (month - 1) + months;
            long targetYear = Math.DivRem(index, 13, out long monthOffset);
            if (monthOffset < 0)
            {
                targetYear--;
                monthOffset += 13;
            }

            int targetMonth = (int)monthOffset + 1;
            return targetYear < 1
                ? throw new ArgumentOutOfRangeException(nameof(months), months, "The result is outside the range the calendar supports.")
                : Compose((int)targetYear, targetMonth, Math.Min(day, DaysIn((int)targetYear, targetMonth)), time, nameof(months));
        }

        /// <inheritdoc />
        public override DateTime AddYears(DateTime time, int years)
        {
            (int year, int month, int day) = Decompose(time);
            long targetYear = (long)year + years;
            return targetYear is < 1 or > int.MaxValue
                ? throw new ArgumentOutOfRangeException(nameof(years), years, "The result is outside the range the calendar supports.")
                : Compose((int)targetYear, month, Math.Min(day, DaysIn((int)targetYear, month)), time, nameof(years));
        }

        /// <inheritdoc />
        public override int GetDayOfMonth(DateTime time)
        {
            return Decompose(time).Day;
        }

        /// <inheritdoc />
        public override DayOfWeek GetDayOfWeek(DateTime time)
        {
            CheckRange(time);
            return time.DayOfWeek;
        }

        /// <inheritdoc />
        public override int GetDayOfYear(DateTime time)
        {
            (_, int month, int day) = Decompose(time);
            return (30 * (month - 1)) + day;
        }

        /// <inheritdoc />
        public override int GetDaysInMonth(int year, int month, int era)
        {
            CheckYear(year, era);
            ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 13);
            return DaysIn(year, month);
        }

        /// <inheritdoc />
        public override int GetDaysInYear(int year, int era)
        {
            CheckYear(year, era);
            return IsLeap(year) ? 366 : 365;
        }

        /// <inheritdoc />
        public override int GetEra(DateTime time)
        {
            CheckRange(time);
            return AmeteMihret;
        }

        /// <inheritdoc />
        public override int GetMonth(DateTime time)
        {
            return Decompose(time).Month;
        }

        /// <inheritdoc />
        public override int GetMonthsInYear(int year, int era)
        {
            CheckYear(year, era);
            return 13;
        }

        /// <inheritdoc />
        public override int GetYear(DateTime time)
        {
            return Decompose(time).Year;
        }

        /// <inheritdoc />
        public override bool IsLeapDay(int year, int month, int day, int era)
        {
            CheckDate(year, month, day, era);
            return month == 13 && day == 6;
        }

        /// <inheritdoc />
        public override bool IsLeapMonth(int year, int month, int era)
        {
            CheckYear(year, era);
            ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 13);
            return false;
        }

        /// <inheritdoc />
        public override bool IsLeapYear(int year, int era)
        {
            CheckYear(year, era);
            return IsLeap(year);
        }

        /// <inheritdoc />
        public override DateTime ToDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int era)
        {
            CheckDate(year, month, day, era);
            TimeSpan timeOfDay = hour is >= 0 and < 24 && minute is >= 0 and < 60 && second is >= 0 and < 60 && millisecond is >= 0 and < 1000
                ? new TimeSpan(0, hour, minute, second, millisecond)
                : throw new ArgumentOutOfRangeException(null, "The time of day is not valid.");
            return DateOnly.FromDayNumber((int)ToDayNumber(year, month, day)).ToDateTime(TimeOnly.MinValue) + timeOfDay;
        }

        /// <summary>Decomposes a time, refusing one before the epoch.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The year, month and day of its date.</returns>
        private static (int Year, int Month, int Day) Decompose(DateTime time)
        {
            CheckRange(time);
            return FromDayNumber(DateOnly.FromDateTime(time).DayNumber);
        }

        /// <summary>Builds the time of a date, keeping another time's time of day.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day, already clamped to the month.</param>
        /// <param name="time">The time whose time of day to keep.</param>
        /// <param name="parameter">The argument blamed when the date is out of range.</param>
        /// <returns>The time.</returns>
        private static DateTime Compose(int year, int month, int day, DateTime time, string parameter)
        {
            return IsValid(year, month, day)
                ? DateOnly.FromDayNumber((int)ToDayNumber(year, month, day)).ToDateTime(TimeOnly.MinValue) + time.TimeOfDay
                : throw new ArgumentOutOfRangeException(parameter, "The result is outside the range the calendar supports.");
        }

        /// <summary>Refuses a time before the epoch.</summary>
        /// <param name="time">The time.</param>
        private static void CheckRange(DateTime time)
        {
            if (DateOnly.FromDateTime(time).DayNumber < EpochDayNumber)
            {
                throw new ArgumentOutOfRangeException(nameof(time), time, "The date precedes the first day of the Ethiopian calendar (AD 0008-08-27).");
            }
        }

        /// <summary>Refuses a year or era the calendar does not have.</summary>
        /// <param name="year">The year.</param>
        /// <param name="era">The era: <see cref="Calendar.CurrentEra" /> or <see cref="AmeteMihret" />.</param>
        private static void CheckYear(int year, int era)
        {
            if (era is not (CurrentEra or AmeteMihret))
            {
                throw new ArgumentOutOfRangeException(nameof(era), era, "The Ethiopian calendar has a single era.");
            }

            if (year < 1 || year > MaxYear)
            {
                throw new ArgumentOutOfRangeException(nameof(year), year, "The year is outside the range the calendar supports.");
            }
        }

        /// <summary>Refuses a date the calendar does not have.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day.</param>
        /// <param name="era">The era.</param>
        private static void CheckDate(int year, int month, int day, int era)
        {
            CheckYear(year, era);
            if (!IsValid(year, month, day))
            {
                throw new ArgumentOutOfRangeException(nameof(day), day, "The year, month and day do not name a date the calendar supports.");
            }
        }
    }
}
