// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The Gregorian calendar (<c>gc</c>), over the framework's <see cref="GregorianCalendar" />:
    ///     every date a <see cref="DateOnly" /> holds, with the culture's own Gregorian month names.
    /// </summary>
    /// <remarks>
    ///     A culture whose default calendar is another (<c>ar-SA</c>'s is Umm al-Qura) still renders
    ///     here with its Gregorian month names and patterns.
    /// </remarks>
    public sealed class GregorianCalendarSystem : ICalendarSystem
    {
        /// <summary>The conversion and naming this system renders with.</summary>
        private static readonly Converter Rules = new();

        /// <inheritdoc />
        public string Code => CalendarCodes.Gregorian;

        /// <inheritdoc />
        public Calendar Calendar { get; } = Calendar.ReadOnly(new GregorianCalendar());

        /// <inheritdoc />
        public int MonthCount => 12;

        /// <inheritdoc />
        public DateOnly MinSupported => DateOnly.MinValue;

        /// <inheritdoc />
        public DateOnly MaxSupported => DateOnly.MaxValue;

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

        /// <summary>The Gregorian conversion (the identity) and the culture's Gregorian month names.</summary>
        private sealed class Converter : CalendarConverter
        {
            /// <inheritdoc />
            public override string Code => CalendarCodes.Gregorian;

            /// <inheritdoc />
            public override int MonthCount => 12;

            /// <inheritdoc />
            public override bool TryDecompose(DateOnly date, out int year, out int month, out int day)
            {
                (year, month, day) = (date.Year, date.Month, date.Day);
                return true;
            }

            /// <inheritdoc />
            public override DateOnly? Compose(int year, int month, int day)
            {
                return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
                    ? new DateOnly(year, month, day)
                    : null;
            }

            /// <inheritdoc />
            public override string MonthName(int month, CultureInfo culture, CalendarCultureData data, bool abbreviated, bool governedByDay)
            {
                // As the framework does, a name a day number governs takes its genitive form, which
                // differs from the nominative only in the languages that have one.
                return (abbreviated, governedByDay) switch
                {
                    (false, false) => data.MonthNames[month - 1],
                    (false, true) => data.MonthGenitiveNames[month - 1],
                    (true, false) => data.AbbreviatedMonthNames[month - 1],
                    (true, true) => data.AbbreviatedMonthGenitiveNames[month - 1],
                };
            }
        }
    }
}
