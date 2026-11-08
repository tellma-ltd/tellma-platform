// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The rendering and reading every calendar system shares: a culture's pattern for a style,
    ///     filled with the system's year, month and day, its month names and the culture's weekday
    ///     names; and the inverse of the numeric style.
    /// </summary>
    internal static class CalendarFormatter
    {
        /// <summary>Renders a date.</summary>
        /// <param name="converter">The calendar.</param>
        /// <param name="date">The date.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="style">The style.</param>
        /// <returns>The rendered date.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style" /> is not a defined style.</exception>
        public static string Format(CalendarConverter converter, DateOnly date, CultureInfo culture, DateStyle style)
        {
            ArgumentNullException.ThrowIfNull(culture);
            var data = CalendarCultureData.For(culture);
            DatePattern pattern = data.PatternOf(style);

            // A date before the calendar's epoch has no rendering in it; the ISO Gregorian form keeps
            // the text unambiguous rather than failing the whole render.
            return converter.TryDecompose(date, out int year, out int month, out int day)
                ? Render(converter, pattern, date, year, month, day, culture, data)
                : date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
        }

        /// <summary>Renders an instant's date and, optionally, its time of day, on a zone's wall clock.</summary>
        /// <param name="converter">The calendar.</param>
        /// <param name="instant">The instant.</param>
        /// <param name="zone">The zone.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="style">The date's style.</param>
        /// <param name="includeTime">Whether to append the time in the culture's short time pattern.</param>
        /// <returns>The rendered date and time.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="zone" /> or <paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style" /> is not a defined style.</exception>
        public static string Format(
            CalendarConverter converter, DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime)
        {
            ArgumentNullException.ThrowIfNull(zone);
            ArgumentNullException.ThrowIfNull(culture);

            DateTime wallClock = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
            string date = Format(converter, DateOnly.FromDateTime(wallClock), culture, style);
            if (!includeTime)
            {
                return date;
            }

            // The time of day is calendar-independent, so the framework renders it, through the
            // culture's cleaned Gregorian formats (ASCII digits, no bidirectional marks).
            var data = CalendarCultureData.For(culture);
            return $"{date} {wallClock.ToString(data.ShortTimePattern, data.Format)}";
        }

        /// <summary>The name of a month, checked against the calendar's month count.</summary>
        /// <param name="converter">The calendar.</param>
        /// <param name="month">The month.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="abbreviated">Whether the abbreviated name is wanted.</param>
        /// <returns>The name.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="month" /> is outside 1 to the month count.</exception>
        public static string MonthName(CalendarConverter converter, int month, CultureInfo culture, bool abbreviated)
        {
            ArgumentNullException.ThrowIfNull(culture);
            ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(month, converter.MonthCount);
            return converter.MonthName(month, culture, CalendarCultureData.For(culture), abbreviated, governedByDay: false);
        }

        /// <summary>
        ///     Reads a date in the numeric style of a culture, or in the <c>yyyy-MM-dd</c> form: three
        ///     groups of digits (any script's) separated by anything but letters, the year in four
        ///     digits, in the culture's order, naming a day the calendar renders that way.
        /// </summary>
        /// <param name="converter">The calendar.</param>
        /// <param name="text">The text.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>The date, or null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        public static DateOnly? TryParse(CalendarConverter converter, string text, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            if (text is null)
            {
                return null;
            }

            // --- Split into digit groups and the separator runs between them ---
            List<string> groups = [];
            List<string> separators = [];
            StringBuilder digits = new();
            StringBuilder separator = new();
            foreach (char c in text)
            {
                if (CalendarText.IsBidiControl(c))
                {
                    continue;
                }

                int value = CalendarText.DigitValue(c);
                if (value >= 0)
                {
                    if (digits.Length == 0 && groups.Count > 0)
                    {
                        separators.Add(separator.ToString().Trim());
                    }

                    _ = separator.Clear();
                    _ = digits.Append((char)('0' + value));
                    continue;
                }

                if (char.IsLetter(c))
                {
                    // Month names are not part of the numeric style.
                    return null;
                }

                if (digits.Length > 0)
                {
                    groups.Add(digits.ToString());
                    _ = digits.Clear();
                }

                _ = separator.Append(c);
            }

            if (digits.Length > 0)
            {
                groups.Add(digits.ToString());
            }

            if (groups.Count != 3)
            {
                return null;
            }

            // --- Assign the groups: ISO order when it reads as yyyy-MM-dd, else the culture's ---
            IReadOnlyList<DatePatternTokenKind> order =
                groups[0].Length == 4 && separators is ["-", "-"]
                    ? [DatePatternTokenKind.Year, DatePatternTokenKind.Month, DatePatternTokenKind.Day]
                    : CalendarCultureData.For(culture).Short.FieldOrder;

            // The year must have four digits (the style always writes four, so a two-digit year is
            // never guessed at), the day and the month one or two.
            int year = 0, month = 0, day = 0;
            for (int i = 0; i < 3; i++)
            {
                string group = groups[i];
                bool fits = order[i] == DatePatternTokenKind.Year ? group.Length == 4 : group.Length <= 2;
                if (!fits)
                {
                    return null;
                }

                int value = int.Parse(group, CultureInfo.InvariantCulture);
                if (order[i] == DatePatternTokenKind.Year)
                {
                    year = value;
                }
                else if (order[i] == DatePatternTokenKind.Month)
                {
                    month = value;
                }
                else
                {
                    day = value;
                }
            }

            // --- The calendar must name that day, and render it with the same three numbers ---
            DateOnly? date = converter.Compose(year, month, day);
            return date is { } found
                && converter.TryDecompose(found, out int y, out int m, out int d)
                && (y, m, d) == (year, month, day)
                ? found
                : null;
        }

        /// <summary>Fills a pattern with a date's fields.</summary>
        /// <param name="converter">The calendar.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="date">The Gregorian date, for the weekday.</param>
        /// <param name="year">The calendar year.</param>
        /// <param name="month">The calendar month.</param>
        /// <param name="day">The calendar day.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="data">The culture's data.</param>
        /// <returns>The rendered text.</returns>
        private static string Render(
            CalendarConverter converter, DatePattern pattern, DateOnly date, int year, int month, int day, CultureInfo culture, CalendarCultureData data)
        {
            bool governedByDay = pattern.HasDayNumber;
            StringBuilder builder = new();
            foreach (DatePatternToken token in pattern.Tokens)
            {
                _ = token.Kind switch
                {
                    DatePatternTokenKind.Literal => builder.Append(token.Text),
                    DatePatternTokenKind.Day => builder.Append(Number(day, token.Width)),
                    DatePatternTokenKind.Month when token.Width <= 2 => builder.Append(Number(month, token.Width)),
                    DatePatternTokenKind.Month => builder.Append(converter.MonthName(month, culture, data, abbreviated: token.Width == 3, governedByDay)),
                    DatePatternTokenKind.Year => builder.Append(Number(year, 4)),
                    DatePatternTokenKind.Weekday => builder.Append(token.Width == 3
                        ? data.AbbreviatedDayNames[(int)date.DayOfWeek]
                        : data.DayNames[(int)date.DayOfWeek]),
                    DatePatternTokenKind.DateSeparator => builder.Append(data.DateSeparator),
                    _ => builder,
                };
            }

            return builder.ToString();
        }

        /// <summary>A number in ASCII digits, zero-padded to a width.</summary>
        /// <param name="value">The number.</param>
        /// <param name="width">The least number of digits.</param>
        /// <returns>The digits.</returns>
        private static string Number(int value, int width)
        {
            return value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }
    }
}
