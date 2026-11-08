// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Calendars
{
    /// <summary>
    ///     One calendar system: how a Gregorian date decomposes into this calendar's year, month and
    ///     day, and how the platform renders and reads dates in it. Server text (messages, emails,
    ///     spreadsheets, agent results) renders every date through the calendar system of the request
    ///     or of the reader, never through a culture's own calendar.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rendering is the platform's own code. A calendar is never assigned to a culture's
    ///         <see cref="DateTimeFormatInfo" /> (a custom calendar cannot be, and a culture accepts
    ///         only its optional calendars) and never selected through a <c>-u-ca-</c> culture name.
    ///         From the culture a system takes only the order of day, month and year, the separators
    ///         and punctuation of its date patterns, its short time pattern, its weekday names and, for
    ///         the Gregorian calendar, its month names; every digit is ASCII.
    ///     </para>
    ///     <para>
    ///         Implementations are immutable and safe to share across threads. The platform composes
    ///         every system into an <see cref="ICalendarRegistry" />; a distribution adds one through
    ///         its feature contribution.
    ///     </para>
    /// </remarks>
    [CollectionService]
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "A date is what these members take; calendar systems are written in C#, where date is not a keyword.")]
    public interface ICalendarSystem
    {
        /// <summary>The system's code: lower-case ASCII, at most 16 characters (<see cref="CalendarCodes" />).</summary>
        string Code { get; }

        /// <summary>
        ///     A framework calendar for date arithmetic in this system. It is never assigned to a
        ///     <see cref="DateTimeFormatInfo" />.
        /// </summary>
        Calendar Calendar { get; }

        /// <summary>The number of months in every year: 12, or 13 for the Ethiopian calendar.</summary>
        int MonthCount { get; }

        /// <summary>
        ///     The first date the system converts exactly. A validator warns about a date before it;
        ///     the system may still convert one, by an approximation its remarks describe.
        /// </summary>
        DateOnly MinSupported { get; }

        /// <summary>
        ///     The last date the system converts exactly. A validator warns about a date after it; the
        ///     system may still convert one, by an approximation its remarks describe.
        /// </summary>
        DateOnly MaxSupported { get; }

        /// <summary>Decomposes a Gregorian date into this calendar's year, month and day.</summary>
        /// <param name="date">The date.</param>
        /// <returns>The year, the month (1 to <see cref="MonthCount" />) and the day of the month.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The calendar cannot represent the date at all (it precedes the calendar's epoch).</exception>
        (int Year, int Month, int Day) Decompose(DateOnly date);

        /// <summary>Renders a date in this calendar.</summary>
        /// <param name="date">The date.</param>
        /// <param name="culture">The culture whose order, separators and names to use.</param>
        /// <param name="style">The rendering.</param>
        /// <returns>The rendered date, with ASCII digits and no bidirectional control characters.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style" /> is not a defined style.</exception>
        string Format(DateOnly date, CultureInfo culture, DateStyle style);

        /// <summary>
        ///     Converts an instant to a zone and renders its date in this calendar and, when
        ///     <paramref name="includeTime" />, its time in the culture's short time pattern after it.
        /// </summary>
        /// <param name="instant">The instant.</param>
        /// <param name="zone">The zone whose wall clock to show.</param>
        /// <param name="culture">The culture whose order, separators, names and time pattern to use.</param>
        /// <param name="style">The rendering of the date.</param>
        /// <param name="includeTime">Whether to append the time of day.</param>
        /// <returns>The rendered date and time, with ASCII digits and no bidirectional control characters.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="zone" /> or <paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style" /> is not a defined style.</exception>
        string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true);

        /// <summary>The name of a month of this calendar in a culture.</summary>
        /// <param name="month">The month, 1 to <see cref="MonthCount" />.</param>
        /// <param name="culture">The culture.</param>
        /// <param name="abbreviated">Whether to return the abbreviated name.</param>
        /// <returns>The month's name, with no bidirectional control characters.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="month" /> is outside 1 to <see cref="MonthCount" />.</exception>
        string MonthName(int month, CultureInfo culture, bool abbreviated);

        /// <summary>
        ///     Reads a date in this calendar: the inverse of <see cref="Format(DateOnly, CultureInfo, DateStyle)" />
        ///     with <see cref="DateStyle.Short" />, and the numeric <c>yyyy-MM-dd</c> form.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="culture">The culture whose order of day, month and year to expect.</param>
        /// <returns>The date, or null when the text is not a date this calendar renders.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="culture" /> is null.</exception>
        DateOnly? TryParse(string text, CultureInfo culture);
    }
}
