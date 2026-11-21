// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The Umm al-Qura calendar extended beyond its table, as a framework <see cref="Calendar" />:
    ///     the framework's <see cref="UmAlQuraCalendar" /> table for AD 1900-04-30 to 2077-11-16
    ///     (AH 1318 to 1500), and the tabular Islamic calendar (<see cref="HijriCalendar" /> with
    ///     <see cref="HijriCalendar.HijriAdjustment" /> 0) before and after it, from AH 1-01-01
    ///     (AD 622-07-18). One rule serves the decomposition, the rendering, the reading and the
    ///     arithmetic, so a date the system formats is a date this calendar adds months to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The table and the tabular calendar meet without a gap at AH 1318: the tabular
    ///         1317-12-29 is the day before the table's 1318-01-01. At AH 1500 they overlap by one
    ///         day: the tabular 1501-01-01 falls on the table's last day, 1500-12-30. The table wins
    ///         that day, so the first month after it, Muharram 1501, begins the day after the table
    ///         and is one day shorter than the tabular month (29 days, AD 2077-11-17 to 12-15); from
    ///         Safar 1501 on, every date is the tabular calendar's.
    ///     </para>
    ///     <para>Beyond the table every conversion is an approximation of the observed calendar.</para>
    /// </remarks>
    internal sealed class UmAlQuraExtendedCalendar : Calendar
    {
        /// <summary>The era of every date: the Hijri era.</summary>
        public const int HijriEra = 1;

        /// <summary>The largest month offset <see cref="AddMonths" /> accepts, as the framework's own calendars do.</summary>
        private const int MaxMonthOffset = 120000;

        /// <summary>The default of <see cref="TwoDigitYearMax" />, the framework Umm al-Qura calendar's.</summary>
        private const int DefaultTwoDigitYearMax = 1451;

        /// <summary>The exact table.</summary>
        private static readonly UmAlQuraCalendar Table = new();

        /// <summary>The tabular calendar. Its adjustment is fixed at 0: the framework's default reads a per-host setting on Windows.</summary>
        private static readonly HijriCalendar Tabular = new() { HijriAdjustment = 0 };

        /// <summary>The year whose two-digit form is the largest that maps into the current century.</summary>
        private int _twoDigitYearMax = DefaultTwoDigitYearMax;

        /// <summary>The first Gregorian day of the table.</summary>
        public static DateOnly TableStart { get; } = DateOnly.FromDateTime(Table.MinSupportedDateTime);

        /// <summary>The last Gregorian day of the table.</summary>
        public static DateOnly TableEnd { get; } = DateOnly.FromDateTime(Table.MaxSupportedDateTime);

        /// <summary>The first Gregorian day the tabular calendar converts: AH 1-01-01.</summary>
        public static DateOnly TabularStart { get; } = DateOnly.FromDateTime(Tabular.MinSupportedDateTime);

        /// <summary>The first year of the table.</summary>
        private static int TableFirstYear { get; } = Table.GetYear(Table.MinSupportedDateTime);

        /// <summary>The last year of the table.</summary>
        private static int TableLastYear { get; } = Table.GetYear(Table.MaxSupportedDateTime);

        /// <summary>The tabular date of the table's last day: the tabular month the table overlaps and how many of its days it takes.</summary>
        private static (int Year, int Month, int Day) Overlap { get; } = TabularDate(TableEnd);

        /// <summary>The last year the tabular calendar converts.</summary>
        private static int MaxYear { get; } = Tabular.GetYear(Tabular.MaxSupportedDateTime);

        /// <inheritdoc />
        public override DateTime MinSupportedDateTime => Tabular.MinSupportedDateTime;

        /// <inheritdoc />
        public override DateTime MaxSupportedDateTime => Tabular.MaxSupportedDateTime;

        /// <inheritdoc />
        public override CalendarAlgorithmType AlgorithmType => CalendarAlgorithmType.LunarCalendar;

        /// <inheritdoc />
        public override int[] Eras => [HijriEra];

        /// <inheritdoc />
        /// <remarks>1451 by default, as the framework's Umm al-Qura calendar.</remarks>
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

        /// <summary>Decomposes a Gregorian date, when it is on or after AH 1-01-01.</summary>
        /// <param name="date">The date.</param>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day.</param>
        /// <returns>False for a date before AH 1-01-01.</returns>
        public static bool TryDecompose(DateOnly date, out int year, out int month, out int day)
        {
            if (date < TabularStart)
            {
                (year, month, day) = (0, 0, 0);
                return false;
            }

            if (date >= TableStart && date <= TableEnd)
            {
                var time = date.ToDateTime(TimeOnly.MinValue);
                (year, month, day) = (Table.GetYear(time), Table.GetMonth(time), Table.GetDayOfMonth(time));
                return true;
            }

            // After the table, the tabular month it overlaps loses the days the table took.
            (year, month, day) = TabularDate(date);
            if (date > TableEnd && (year, month) == (Overlap.Year, Overlap.Month))
            {
                day -= Overlap.Day;
            }

            return true;
        }

        /// <summary>The number of days in a month; 0 for a month the calendar lacks.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <returns>The days.</returns>
        public static int DaysIn(int year, int month)
        {
            if (year < 1 || year > MaxYear || month is < 1 or > 12)
            {
                return 0;
            }

            if (year >= TableFirstYear && year <= TableLastYear)
            {
                return Table.GetDaysInMonth(year, month);
            }

            int days = Tabular.GetDaysInMonth(year, month);
            return (year, month) == (Overlap.Year, Overlap.Month) ? days - Overlap.Day : days;
        }

        /// <summary>The Gregorian date of a year, month and day.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day.</param>
        /// <returns>The date, or null when the three do not name a day the calendar converts.</returns>
        public static DateOnly? Compose(int year, int month, int day)
        {
            if (day < 1 || day > DaysIn(year, month))
            {
                return null;
            }

            try
            {
                return year >= TableFirstYear && year <= TableLastYear
                    ? DateOnly.FromDateTime(Table.ToDateTime(year, month, day, 0, 0, 0, 0))
                    : DateOnly.FromDateTime(Tabular.ToDateTime(
                        year, month, (year, month) == (Overlap.Year, Overlap.Month) ? day + Overlap.Day : day, 0, 0, 0, 0));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Within the tabular calendar's last year (AH 9666), past its last day.
                return null;
            }
        }

        /// <inheritdoc />
        public override DateTime AddMonths(DateTime time, int months)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(months, -MaxMonthOffset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(months, MaxMonthOffset);
            (int year, int month, int day) = Decompose(time);

            // Months counted from year 0, divided back with floor division.
            long index = (12L * year) + (month - 1) + months;
            long targetYear = Math.DivRem(index, 12, out long monthOffset);
            if (monthOffset < 0)
            {
                targetYear--;
                monthOffset += 12;
            }

            return Shift(targetYear, (int)monthOffset + 1, day, time, nameof(months));
        }

        /// <inheritdoc />
        public override DateTime AddYears(DateTime time, int years)
        {
            (int year, int month, int day) = Decompose(time);
            return Shift((long)year + years, month, day, time, nameof(years));
        }

        /// <inheritdoc />
        public override int GetDayOfMonth(DateTime time)
        {
            return Decompose(time).Day;
        }

        /// <inheritdoc />
        public override DayOfWeek GetDayOfWeek(DateTime time)
        {
            _ = Decompose(time);
            return time.DayOfWeek;
        }

        /// <inheritdoc />
        public override int GetDayOfYear(DateTime time)
        {
            (int year, int month, int day) = Decompose(time);
            return Enumerable.Range(1, month - 1).Sum(m => DaysIn(year, m)) + day;
        }

        /// <inheritdoc />
        public override int GetDaysInMonth(int year, int month, int era)
        {
            CheckEra(era);
            int days = DaysIn(year, month);
            return days > 0 ? days : throw new ArgumentOutOfRangeException(nameof(month), month, "The year and month do not name a month the calendar supports.");
        }

        /// <inheritdoc />
        public override int GetDaysInYear(int year, int era)
        {
            CheckYear(year, era);
            return Enumerable.Range(1, 12).Sum(m => DaysIn(year, m));
        }

        /// <inheritdoc />
        public override int GetEra(DateTime time)
        {
            _ = Decompose(time);
            return HijriEra;
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
            return 12;
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
            return month == 12 && day == 30 && IsLeapYear(year, era);
        }

        /// <inheritdoc />
        public override bool IsLeapMonth(int year, int month, int era)
        {
            CheckYear(year, era);
            ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
            return false;
        }

        /// <inheritdoc />
        /// <remarks>A leap year has 355 days.</remarks>
        public override bool IsLeapYear(int year, int era)
        {
            return GetDaysInYear(year, era) == 355;
        }

        /// <inheritdoc />
        public override DateTime ToDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int era)
        {
            CheckDate(year, month, day, era);
            TimeSpan timeOfDay = hour is >= 0 and < 24 && minute is >= 0 and < 60 && second is >= 0 and < 60 && millisecond is >= 0 and < 1000
                ? new TimeSpan(0, hour, minute, second, millisecond)
                : throw new ArgumentOutOfRangeException(null, "The time of day is not valid.");
            return Compose(year, month, day)!.Value.ToDateTime(TimeOnly.MinValue) + timeOfDay;
        }

        /// <summary>The tabular calendar's date of a Gregorian date on or after AH 1-01-01.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The year, month and day.</returns>
        private static (int Year, int Month, int Day) TabularDate(DateOnly date)
        {
            var time = date.ToDateTime(TimeOnly.MinValue);
            return (Tabular.GetYear(time), Tabular.GetMonth(time), Tabular.GetDayOfMonth(time));
        }

        /// <summary>Decomposes a time, refusing one before AH 1-01-01.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The year, month and day of its date.</returns>
        private static (int Year, int Month, int Day) Decompose(DateTime time)
        {
            return TryDecompose(DateOnly.FromDateTime(time), out int year, out int month, out int day)
                ? (year, month, day)
                : throw new ArgumentOutOfRangeException(nameof(time), time, "The date precedes the first day of the Islamic era (AD 622-07-18).");
        }

        /// <summary>The time of a year and month with a day clamped to the month, keeping another time's time of day.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day, clamped to the month's length.</param>
        /// <param name="time">The time whose time of day to keep.</param>
        /// <param name="parameter">The argument blamed when the result is out of range.</param>
        /// <returns>The time.</returns>
        private static DateTime Shift(long year, int month, int day, DateTime time, string parameter)
        {
            int days = year is >= 1 and <= int.MaxValue ? DaysIn((int)year, month) : 0;
            DateOnly? date = days > 0 ? Compose((int)year, month, Math.Min(day, days)) : null;
            return date is { } found
                ? found.ToDateTime(TimeOnly.MinValue) + time.TimeOfDay
                : throw new ArgumentOutOfRangeException(parameter, "The result is outside the range the calendar supports.");
        }

        /// <summary>Refuses an era the calendar does not have.</summary>
        /// <param name="era">The era: <see cref="Calendar.CurrentEra" /> or <see cref="HijriEra" />.</param>
        private static void CheckEra(int era)
        {
            if (era is not (CurrentEra or HijriEra))
            {
                throw new ArgumentOutOfRangeException(nameof(era), era, "The Islamic calendar has a single era.");
            }
        }

        /// <summary>Refuses a year or era the calendar does not have.</summary>
        /// <param name="year">The year.</param>
        /// <param name="era">The era.</param>
        private static void CheckYear(int year, int era)
        {
            CheckEra(era);
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
            if (Compose(year, month, day) is null)
            {
                throw new ArgumentOutOfRangeException(nameof(day), day, "The year, month and day do not name a date the calendar supports.");
            }
        }
    }
}
