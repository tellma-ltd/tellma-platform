// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The Ethiopian calendar (<c>et</c>): twelve months of 30 days and Pagume of 5, or 6 in a year
    ///     <c>y</c> with <c>y % 4 == 3</c>, in a single era. Month names come from Core's resources.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The framework has no Ethiopian calendar; this is a port of the Julian-day-number
    ///         arithmetic ICU's <c>ethiopic</c> calendar uses, so the server, the query language and
    ///         the browser agree. New Year (Meskerem 1) falls on Gregorian 11 September, or 12 September
    ///         in the year before a Gregorian leap year, from March 1900 to February 2100; each
    ///         Gregorian century year not divisible by 400 moves it a day later.
    ///     </para>
    ///     <para>
    ///         <see cref="MinSupported" /> and <see cref="MaxSupported" /> (AD 1752-09-14 to
    ///         2500-01-01) are the range the platform guarantees and validators warn beyond; the
    ///         arithmetic itself is exact from the epoch, AD 0008-08-27, onwards. A date before the
    ///         epoch has no rendering in this calendar and renders in the ISO Gregorian form marked
    ///         <c>gc</c>.
    ///     </para>
    /// </remarks>
    public sealed class EthiopianCalendarSystem : ICalendarSystem
    {
        /// <summary>The conversion and naming this system renders with.</summary>
        private static readonly Converter Rules = new();

        /// <inheritdoc />
        public string Code => CalendarCodes.Ethiopian;

        /// <inheritdoc />
        public Calendar Calendar { get; } = Calendar.ReadOnly(new EthiopianCalendar());

        /// <inheritdoc />
        public int MonthCount => 13;

        /// <inheritdoc />
        /// <remarks>AD 1752-09-14, Meskerem 6, 1745.</remarks>
        public DateOnly MinSupported { get; } = new(1752, 9, 14);

        /// <inheritdoc />
        /// <remarks>AD 2500-01-01, Tahsas 19, 2492.</remarks>
        public DateOnly MaxSupported { get; } = new(2500, 1, 1);

        /// <inheritdoc />
        public (int Year, int Month, int Day) Decompose(DateOnly date)
        {
            return Rules.Decompose(date);
        }

        /// <inheritdoc />
        public string Format(DateOnly date, CultureInfo culture, DateStyle style)
        {
            return CalendarFormatter.Format(Rules, date, culture, style);
        }

        /// <inheritdoc />
        public string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true)
        {
            return CalendarFormatter.Format(Rules, instant, zone, culture, style, includeTime);
        }

        /// <inheritdoc />
        public string MonthName(int month, CultureInfo culture, bool abbreviated)
        {
            return CalendarFormatter.MonthName(Rules, month, culture, abbreviated);
        }

        /// <inheritdoc />
        public DateOnly? TryParse(string text, CultureInfo culture)
        {
            return CalendarFormatter.TryParse(Rules, text, culture);
        }

        /// <summary>The Ethiopian conversion over day numbers.</summary>
        private sealed class Converter : CalendarConverter
        {
            /// <inheritdoc />
            public override string Code => CalendarCodes.Ethiopian;

            /// <inheritdoc />
            public override int MonthCount => 13;

            /// <inheritdoc />
            public override bool TryDecompose(DateOnly date, out int year, out int month, out int day)
            {
                if (date.DayNumber < EthiopianCalendar.EpochDayNumber)
                {
                    (year, month, day) = (0, 0, 0);
                    return false;
                }

                (year, month, day) = EthiopianCalendar.FromDayNumber(date.DayNumber);
                return true;
            }

            /// <inheritdoc />
            public override DateOnly? Compose(int year, int month, int day)
            {
                return EthiopianCalendar.IsValid(year, month, day)
                    ? DateOnly.FromDayNumber((int)EthiopianCalendar.ToDayNumber(year, month, day))
                    : null;
            }
        }
    }
}
