// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Resources;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     What distinguishes one calendar system from another for <see cref="CalendarFormatter" />:
    ///     the conversion between Gregorian dates and this calendar's year, month and day, and the
    ///     names of its months.
    /// </summary>
    internal abstract class CalendarConverter
    {
        /// <summary>The system's code, which also keys its month names in Core's resources.</summary>
        public abstract string Code { get; }

        /// <summary>The number of months in every year.</summary>
        public abstract int MonthCount { get; }

        /// <summary>Decomposes a Gregorian date, when the calendar can represent it.</summary>
        /// <param name="date">The date.</param>
        /// <param name="year">The calendar year.</param>
        /// <param name="month">The month, 1 to <see cref="MonthCount" />.</param>
        /// <param name="day">The day of the month.</param>
        /// <returns>False when the date precedes the calendar's epoch.</returns>
        public abstract bool TryDecompose(DateOnly date, out int year, out int month, out int day);

        /// <summary>The Gregorian date of a year, month and day of this calendar.</summary>
        /// <param name="year">The year.</param>
        /// <param name="month">The month.</param>
        /// <param name="day">The day.</param>
        /// <returns>The date, or null when the three do not name a day this calendar converts.</returns>
        public abstract DateOnly? Compose(int year, int month, int day);

        /// <summary>The name of a month.</summary>
        /// <param name="month">The month, already checked to be in range.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="data">The culture's data.</param>
        /// <param name="abbreviated">Whether the abbreviated name is wanted.</param>
        /// <param name="governedByDay">Whether a day number stands beside the name, which selects the genitive form where a language has one.</param>
        /// <returns>The name, cleaned.</returns>
        public virtual string MonthName(int month, CultureInfo culture, CalendarCultureData data, bool abbreviated, bool governedByDay)
        {
            // Calendars the framework knows no names for take them from Core's resources, which
            // hold one name per month (CLDR's abbreviated names of these calendars are, in the
            // shipped languages, the full ones or English stand-ins).
            string key = $"Calendar_{Code}_Month{month.ToString(CultureInfo.InvariantCulture)}";
            string? name = Strings.ResourceManager.GetString(key, culture);
            return name is null ? key : CalendarText.Clean(name).Trim();
        }

        /// <summary>
        ///     Decomposes a date, throwing for one the calendar cannot represent: the
        ///     <see cref="ICalendarSystem.Decompose" /> contract.
        /// </summary>
        /// <param name="date">The date.</param>
        /// <returns>The year, month and day.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The date precedes the calendar's epoch.</exception>
        public (int Year, int Month, int Day) Decompose(DateOnly date)
        {
            return TryDecompose(date, out int year, out int month, out int day)
                ? (year, month, day)
                : throw new ArgumentOutOfRangeException(
                    nameof(date), date, $"The date precedes the first day the '{Code}' calendar can represent.");
        }
    }
}
