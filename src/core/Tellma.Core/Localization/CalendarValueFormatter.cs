// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Jeffijoe.MessageFormat;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Renders the <c>date</c> and <c>time</c> arguments of ICU messages through the platform's
    ///     calendar systems, in the calendar and display zone of the flow's localization: the current
    ///     <see cref="LocalizationScope" />, else the request's, else the Gregorian calendar in UTC.
    ///     Numbers keep the message library's own rendering.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The four ICU styles <c>short</c>, <c>medium</c>, <c>long</c> and <c>full</c> are
    ///         supported for dates; an argument without a style renders <c>medium</c>, as in ICU. Any
    ///         other style (a skeleton, a pattern) is refused, which renders the message's raw value.
    ///     </para>
    ///     <para>
    ///         A <see cref="DateOnly" /> is a calendar date and renders as it is. A
    ///         <see cref="DateTimeOffset" /> is an instant, converted to the display zone. A
    ///         <see cref="DateTime" /> of unspecified kind is a wall-clock time in the tenant's zone and
    ///         renders without conversion; one of UTC or local kind is an instant, converted like a
    ///         <see cref="DateTimeOffset" />. A <see cref="TimeOnly" /> formats as a time only.
    ///     </para>
    ///     <para>
    ///         A time renders in the culture's short time pattern for <c>short</c>, and in its long
    ///         time pattern (with seconds) for every other style; no zone name is appended.
    ///     </para>
    /// </remarks>
    internal sealed class CalendarValueFormatter : CustomValueFormatter
    {
        /// <summary>The calendar of a flow with no localization applied.</summary>
        private static readonly GregorianCalendarSystem DefaultCalendar = new();

        /// <inheritdoc />
        public override bool TryFormatDate(CultureInfo culture, object? value, string? style, out string? formatted)
        {
            ArgumentNullException.ThrowIfNull(culture);
            formatted = null;
            if (!TryParseStyle(style, out DateStyle dateStyle))
            {
                return false;
            }

            (ICalendarSystem calendar, TimeZoneInfo zone) = Effective();
            switch (value)
            {
                // --- A calendar date, or a tenant-local wall-clock time: no zone conversion ---
                case DateOnly date:
                    formatted = calendar.Format(date, culture, dateStyle);
                    return true;
                case DateTime { Kind: DateTimeKind.Unspecified } wallClock:
                    formatted = calendar.Format(DateOnly.FromDateTime(wallClock), culture, dateStyle);
                    return true;

                // --- An instant: its date on the display zone's wall clock ---
                case DateTime instant:
                    formatted = calendar.Format(new DateTimeOffset(instant), zone, culture, dateStyle, includeTime: false);
                    return true;
                case DateTimeOffset instant:
                    formatted = calendar.Format(instant, zone, culture, dateStyle, includeTime: false);
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool TryFormatTime(CultureInfo culture, object? value, string? style, out string? formatted)
        {
            ArgumentNullException.ThrowIfNull(culture);
            formatted = null;
            if (!TryParseStyle(style, out DateStyle timeStyle))
            {
                return false;
            }

            // --- The time of day on the right wall clock ---
            (_, TimeZoneInfo zone) = Effective();
            TimeOnly? time = value switch
            {
                TimeOnly t => t,
                DateTime { Kind: DateTimeKind.Unspecified } wallClock => TimeOnly.FromDateTime(wallClock),
                DateTime instant => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(instant), zone).DateTime),
                DateTimeOffset instant => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime),
                _ => null,
            };

            if (time is not TimeOnly timeOfDay)
            {
                return false;
            }

            // --- The culture's cleaned Gregorian formats: ASCII digits, no bidirectional marks ---
            var data = CalendarCultureData.For(culture);
            string pattern = timeStyle == DateStyle.Short ? data.ShortTimePattern : data.LongTimePattern;
            formatted = CalendarText.Clean(timeOfDay.ToString(pattern, data.Format));
            return true;
        }

        /// <summary>The calendar and display zone of the flow: the scope's, else the request's, else Gregorian in UTC.</summary>
        /// <returns>The calendar and the zone.</returns>
        private static (ICalendarSystem Calendar, TimeZoneInfo Zone) Effective()
        {
            LocalizationContext? context = LocalizationScope.Current ?? LocalizationScope.Request;
            return context is null ? (DefaultCalendar, TimeZoneInfo.Utc) : (context.Calendar, context.TimeZone);
        }

        /// <summary>Reads an ICU date or time style.</summary>
        /// <param name="style">The style as written in the message, or null or empty when none was.</param>
        /// <param name="result">The style.</param>
        /// <returns>False for a style other than the four ICU names.</returns>
        private static bool TryParseStyle(string? style, out DateStyle result)
        {
            switch (style?.Trim())
            {
                case null or "" or "medium":
                    result = DateStyle.Medium;
                    return true;
                case "short":
                    result = DateStyle.Short;
                    return true;
                case "long":
                    result = DateStyle.Long;
                    return true;
                case "full":
                    result = DateStyle.Full;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }
}
