// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>A calendar system whose code a test chooses; it has no parameterless constructor.</summary>
    /// <param name="code">The code.</param>
    internal sealed class TestCalendar(string code) : ICalendarSystem
    {
        /// <inheritdoc />
        public string Code { get; } = code;

        /// <inheritdoc />
        public Calendar Calendar { get; } = new GregorianCalendar();

        /// <inheritdoc />
        public int MonthCount => 12;

        /// <inheritdoc />
        public DateOnly MinSupported => DateOnly.MinValue;

        /// <inheritdoc />
        public DateOnly MaxSupported => DateOnly.MaxValue;

        /// <inheritdoc />
        public (int Year, int Month, int Day) Decompose(DateOnly date)
        {
            return (date.Year, date.Month, date.Day);
        }

        /// <inheritdoc />
        public string Format(DateOnly date, CultureInfo culture, DateStyle style)
        {
            return date.ToString(culture);
        }

        /// <inheritdoc />
        public string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true)
        {
            return instant.ToString(culture);
        }

        /// <inheritdoc />
        public string MonthName(int month, CultureInfo culture, bool abbreviated)
        {
            return month.ToString(culture);
        }

        /// <inheritdoc />
        public DateOnly? TryParse(string text, CultureInfo culture)
        {
            return null;
        }
    }
}
