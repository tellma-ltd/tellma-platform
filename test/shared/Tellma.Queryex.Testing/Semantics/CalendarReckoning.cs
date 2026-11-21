// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>A date in one calendar.</summary>
    /// <param name="Year">The year.</param>
    /// <param name="Month">The month, from 1.</param>
    /// <param name="Day">The day of the month, from 1.</param>
    public readonly record struct CalendarDate(int Year, int Month, int Day);

    /// <summary>
    ///     The reference reading's conversions between the Gregorian calendar and the others the
    ///     language counts in.
    /// </summary>
    /// <remarks>
    ///     Exposed so a suite can hold the reading itself to an outside source — the Ethiopian
    ///     conversion to ICU's vectors — before trusting it to judge the engine's SQL.
    /// </remarks>
    public static class ReferenceCalendars
    {
        /// <summary>A Gregorian date in another calendar.</summary>
        /// <param name="calendar">The calendar code: <c>et</c> or <c>uq</c>.</param>
        /// <param name="date">The Gregorian date.</param>
        /// <returns>The date in that calendar, or null where the calendar does not reach it.</returns>
        /// <exception cref="ArgumentException">The code names no calendar the reading counts in.</exception>
        public static CalendarDate? ToCalendar(string calendar, DateOnly date)
        {
            CalendarReckoning reckoning = CalendarReckoning.For(calendar)
                ?? throw new ArgumentException("The calendar '" + calendar + "' is not one the reading converts.", nameof(calendar));
            return reckoning.TryParts(date, out CalendarDate parts) ? parts : null;
        }

        /// <summary>A date in another calendar, as a Gregorian date.</summary>
        /// <param name="calendar">The calendar code: <c>et</c> or <c>uq</c>.</param>
        /// <param name="date">The date in that calendar.</param>
        /// <returns>The Gregorian date, or null where the calendar does not reach it.</returns>
        /// <exception cref="ArgumentException">The code names no calendar the reading counts in.</exception>
        public static DateOnly? ToGregorian(string calendar, CalendarDate date)
        {
            CalendarReckoning reckoning = CalendarReckoning.For(calendar)
                ?? throw new ArgumentException("The calendar '" + calendar + "' is not one the reading converts.", nameof(calendar));
            return reckoning.TryDate(date, out DateOnly gregorian) ? gregorian : null;
        }
    }

    /// <summary>
    ///     A calendar other than the Gregorian, as the reference reading counts in it.
    /// </summary>
    /// <remarks>
    ///     Only the conversions and the shape of the year. Everything a calendar function answers —
    ///     its components, its boundaries, its additions and its differences — is worked out from
    ///     these once, in the interpreter, so the calendars cannot disagree about what a function
    ///     means.
    /// </remarks>
    internal abstract class CalendarReckoning
    {
        /// <summary>The Ethiopian calendar.</summary>
        internal static CalendarReckoning Ethiopian { get; } = new EthiopianReckoning();

        /// <summary>The Umm al-Qura calendar.</summary>
        internal static CalendarReckoning UmAlQura { get; } = new UmAlQuraReckoning();

        /// <summary>How many months a year has.</summary>
        internal abstract int MonthsInYear { get; }

        /// <summary>The calendar a code names.</summary>
        /// <param name="code">The code, in any case.</param>
        /// <returns>The calendar, or null for the Gregorian calendar or a code the reading does not know.</returns>
        internal static CalendarReckoning? For(string? code)
        {
            return code?.ToUpperInvariant() switch
            {
                "ET" => Ethiopian,
                "UQ" => UmAlQura,
                _ => null,
            };
        }

        /// <summary>A Gregorian date in this calendar.</summary>
        /// <param name="date">The Gregorian date.</param>
        /// <param name="parts">The date in this calendar.</param>
        /// <returns>False where the calendar does not reach the date.</returns>
        internal abstract bool TryParts(DateOnly date, out CalendarDate parts);

        /// <summary>A date in this calendar, as a Gregorian date.</summary>
        /// <param name="parts">The date, whose day is valid for its month.</param>
        /// <param name="date">The Gregorian date.</param>
        /// <returns>False where the calendar or the Gregorian range does not reach it.</returns>
        internal abstract bool TryDate(CalendarDate parts, out DateOnly date);

        /// <summary>How many days a month has.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <returns>The count, or null where the calendar does not reach the month.</returns>
        internal abstract int? DaysInMonth(int year, int month);

        /// <summary>The quarter a month belongs to.</summary>
        /// <param name="month">The month.</param>
        /// <returns>The quarter, from 1.</returns>
        internal virtual int QuarterOf(int month)
        {
            return (month + 2) / 3;
        }

        /// <summary>The Ethiopian calendar, by its arithmetic on fixed day numbers.</summary>
        /// <remarks>
        ///     Days are counted from 1 January of year 1 of the proleptic Gregorian calendar, which
        ///     is day 1; the Ethiopian calendar's first day, 1 Meskerem 1, is day 2,796. Written from
        ///     the calendar's definition rather than from the engine's SQL, so the two are
        ///     independent readings of it.
        /// </remarks>
        private sealed class EthiopianReckoning : CalendarReckoning
        {
            /// <summary>The fixed day number of 1 Meskerem 1.</summary>
            private const int Epoch = 2796;

            /// <inheritdoc />
            internal override int MonthsInYear => 13;

            /// <inheritdoc />
            internal override bool TryParts(DateOnly date, out CalendarDate parts)
            {
                long day = date.DayNumber + 1L;
                long year = FloorDivide((4 * (day - Epoch)) + 1463, 1461);
                long month = FloorDivide(day - Fixed(year, 1, 1), 30) + 1;
                long dayOfMonth = day + 1 - Fixed(year, month, 1);
                parts = new CalendarDate((int)year, (int)month, (int)dayOfMonth);
                return true;
            }

            /// <inheritdoc />
            internal override bool TryDate(CalendarDate parts, out DateOnly date)
            {
                long day = Fixed(parts.Year, parts.Month, parts.Day) - 1;
                if (day < DateOnly.MinValue.DayNumber || day > DateOnly.MaxValue.DayNumber)
                {
                    date = default;
                    return false;
                }

                date = DateOnly.FromDayNumber((int)day);
                return true;
            }

            /// <inheritdoc />
            internal override int? DaysInMonth(int year, int month)
            {
                // Pagume has a sixth day in a year that leaves 3 when divided by 4.
                return month < 13 ? 30 : FloorModulo(year, 4) == 3 ? 6 : 5;
            }

            /// <inheritdoc />
            /// <remarks>Pagume is in the fourth quarter.</remarks>
            internal override int QuarterOf(int month)
            {
                return Math.Min(4, base.QuarterOf(month));
            }

            /// <summary>The fixed day number of an Ethiopian date.</summary>
            /// <param name="year">The year.</param>
            /// <param name="month">The month.</param>
            /// <param name="day">The day.</param>
            /// <returns>The day number.</returns>
            private static long Fixed(long year, long month, long day)
            {
                return Epoch - 1 + (365 * (year - 1)) + FloorDivide(year, 4) + (30 * (month - 1)) + day;
            }

            /// <summary>Division that rounds toward negative infinity.</summary>
            /// <param name="dividend">The dividend.</param>
            /// <param name="divisor">The divisor, positive.</param>
            /// <returns>The quotient.</returns>
            private static long FloorDivide(long dividend, long divisor)
            {
                long quotient = dividend / divisor;
                return dividend % divisor < 0 ? quotient - 1 : quotient;
            }

            /// <summary>A remainder that is never negative.</summary>
            /// <param name="dividend">The dividend.</param>
            /// <param name="divisor">The divisor, positive.</param>
            /// <returns>The remainder.</returns>
            private static long FloorModulo(long dividend, long divisor)
            {
                return dividend - (FloorDivide(dividend, divisor) * divisor);
            }
        }

        /// <summary>The Umm al-Qura calendar, as the runtime defines it.</summary>
        /// <remarks>
        ///     The same calendar the host seeds its month table from, so it reaches exactly the
        ///     months the table holds: AH 1318 to 1500.
        /// </remarks>
        private sealed class UmAlQuraReckoning : CalendarReckoning
        {
            /// <summary>The runtime's calendar.</summary>
            private static readonly UmAlQuraCalendar Calendar = new();

            /// <inheritdoc />
            internal override int MonthsInYear => 12;

            /// <inheritdoc />
            internal override bool TryParts(DateOnly date, out CalendarDate parts)
            {
                var moment = date.ToDateTime(TimeOnly.MinValue);
                if (moment < Calendar.MinSupportedDateTime || moment > Calendar.MaxSupportedDateTime)
                {
                    parts = default;
                    return false;
                }

                parts = new CalendarDate(Calendar.GetYear(moment), Calendar.GetMonth(moment), Calendar.GetDayOfMonth(moment));
                return true;
            }

            /// <inheritdoc />
            internal override bool TryDate(CalendarDate parts, out DateOnly date)
            {
                if (DaysInMonth(parts.Year, parts.Month) is not int days || parts.Day < 1 || parts.Day > days)
                {
                    date = default;
                    return false;
                }

                date = DateOnly.FromDateTime(Calendar.ToDateTime(parts.Year, parts.Month, parts.Day, 0, 0, 0, 0));
                return true;
            }

            /// <inheritdoc />
            internal override int? DaysInMonth(int year, int month)
            {
                return year is >= CalendarMonths.FirstYear and <= CalendarMonths.LastYear && month is >= 1 and <= 12
                    ? Calendar.GetDaysInMonth(year, month)
                    : null;
            }
        }
    }
}
