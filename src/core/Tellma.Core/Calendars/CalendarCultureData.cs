// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     What the calendar systems take from one culture, read once and cleaned: the culture's
    ///     Gregorian formats (its own calendar may be another), the four date patterns, the short
    ///     time pattern and the Gregorian month and weekday names.
    /// </summary>
    /// <remarks>
    ///     Read once per culture instance and kept as long as the instance lives, so a culture must
    ///     not be changed after it was first used to format.
    /// </remarks>
    internal sealed class CalendarCultureData
    {
        /// <summary>The data of every culture instance used so far, released with the instance.</summary>
        private static readonly ConditionalWeakTable<CultureInfo, CalendarCultureData> Cache = [];

        /// <summary>The patterns used when a culture's own lacks the day, the month or the year.</summary>
        private static readonly DatePattern FallbackShort = DatePattern.Parse("yyyy'-'MM'-'dd");

        /// <summary>The long pattern used when a culture's own lacks the day, the month or the year.</summary>
        private static readonly DatePattern FallbackFull = DatePattern.Parse("dddd', 'd' 'MMMM' 'yyyy");

        /// <summary>Reads a culture.</summary>
        /// <param name="culture">The culture.</param>
        private CalendarCultureData(CultureInfo culture)
        {
            // --- The culture's formats under the Gregorian calendar ---
            // A culture whose calendar is another (ar-SA's is Umm al-Qura under ICU before 78, and any
            // clone may be set to one) carries its Gregorian names and patterns behind its optional
            // Gregorian calendar; a clone switched to it reads them. The decision rests on the
            // culture's own calendar, never on the host's default for the culture's name, and a
            // culture already on a Gregorian calendar (a pinned negotiated clone) is used as it is.
            // Only this private clone is ever switched.
            var format = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
            if (format.Calendar is not GregorianCalendar)
            {
                Calendar? gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>()
                    .OrderBy(static g => g.CalendarType == GregorianCalendarTypes.Localized ? 0 : 1)
                    .FirstOrDefault();
                format = gregorian is not null
                    ? Switch(format, gregorian)
                    : (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
            }

            // --- Clean the parts the framework itself renders (the time of day, the separator) ---
            format.AMDesignator = CalendarText.Clean(format.AMDesignator);
            format.PMDesignator = CalendarText.Clean(format.PMDesignator);
            format.TimeSeparator = CalendarText.Clean(format.TimeSeparator);
            format.DateSeparator = CalendarText.Clean(format.DateSeparator).Trim();
            DateSeparator = format.DateSeparator;
            ShortTimePattern = CalendarText.Clean(format.ShortTimePattern).Trim();

            // --- Names ---
            MonthNames = Names(format.MonthNames, 12);
            AbbreviatedMonthNames = Names(format.AbbreviatedMonthNames, 12);
            MonthGenitiveNames = Names(format.MonthGenitiveNames, 12);
            AbbreviatedMonthGenitiveNames = Names(format.AbbreviatedMonthGenitiveNames, 12);
            DayNames = Names(format.DayNames, 7);
            AbbreviatedDayNames = Names(format.AbbreviatedDayNames, 7);
            format.MonthNames = [.. MonthNames, string.Empty];
            format.AbbreviatedMonthNames = [.. AbbreviatedMonthNames, string.Empty];
            format.MonthGenitiveNames = [.. MonthGenitiveNames, string.Empty];
            format.AbbreviatedMonthGenitiveNames = [.. AbbreviatedMonthGenitiveNames, string.Empty];
            format.DayNames = [.. DayNames];
            format.AbbreviatedDayNames = [.. AbbreviatedDayNames];
            Format = DateTimeFormatInfo.ReadOnly(format);

            // --- The four date patterns ---
            DatePattern shortPattern = DatePattern.Parse(format.ShortDatePattern).ToNumeric();
            Short = shortPattern.IsComplete ? shortPattern : FallbackShort;

            DatePattern full = DatePattern.Parse(format.LongDatePattern).WithWeekday();
            Full = full.IsComplete ? full : FallbackFull;
            Long = Full.WithoutWeekday();
            Medium = Long.WithAbbreviatedMonth();
        }

        /// <summary>
        ///     The culture's Gregorian formats with cleaned designators, separators and names, read
        ///     only. The time of day renders through it.
        /// </summary>
        public DateTimeFormatInfo Format { get; }

        /// <summary>The culture's date separator, cleaned.</summary>
        public string DateSeparator { get; }

        /// <summary>The culture's short time pattern, cleaned.</summary>
        public string ShortTimePattern { get; }

        /// <summary>The numeric pattern of <see cref="DateStyle.Short" />.</summary>
        public DatePattern Short { get; }

        /// <summary>The pattern of <see cref="DateStyle.Medium" />.</summary>
        public DatePattern Medium { get; }

        /// <summary>The pattern of <see cref="DateStyle.Long" />.</summary>
        public DatePattern Long { get; }

        /// <summary>The pattern of <see cref="DateStyle.Full" />.</summary>
        public DatePattern Full { get; }

        /// <summary>The twelve Gregorian month names.</summary>
        public ImmutableArray<string> MonthNames { get; }

        /// <summary>The twelve abbreviated Gregorian month names.</summary>
        public ImmutableArray<string> AbbreviatedMonthNames { get; }

        /// <summary>The twelve Gregorian month names in the form a day number governs.</summary>
        public ImmutableArray<string> MonthGenitiveNames { get; }

        /// <summary>The twelve abbreviated Gregorian month names in the form a day number governs.</summary>
        public ImmutableArray<string> AbbreviatedMonthGenitiveNames { get; }

        /// <summary>The seven weekday names, Sunday first.</summary>
        public ImmutableArray<string> DayNames { get; }

        /// <summary>The seven abbreviated weekday names, Sunday first.</summary>
        public ImmutableArray<string> AbbreviatedDayNames { get; }

        /// <summary>The data of a culture.</summary>
        /// <param name="culture">The culture.</param>
        /// <returns>The data, read on first use of the instance.</returns>
        public static CalendarCultureData For(CultureInfo culture)
        {
            return Cache.GetValue(culture, static c => new CalendarCultureData(c));
        }

        /// <summary>The pattern of a style.</summary>
        /// <param name="style">The style.</param>
        /// <returns>The pattern.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="style" /> is not a defined style.</exception>
        public DatePattern PatternOf(DateStyle style)
        {
            return style switch
            {
                DateStyle.Short => Short,
                DateStyle.Medium => Medium,
                DateStyle.Long => Long,
                DateStyle.Full => Full,
                _ => throw new ArgumentOutOfRangeException(nameof(style), style, "The value is not a defined date style."),
            };
        }

        /// <summary>Switches a format clone to a Gregorian calendar the culture offers.</summary>
        /// <param name="format">The clone.</param>
        /// <param name="gregorian">The calendar, one of the culture's optional calendars.</param>
        /// <returns>The clone.</returns>
        private static DateTimeFormatInfo Switch(DateTimeFormatInfo format, Calendar gregorian)
        {
            format.Calendar = gregorian;
            return format;
        }

        /// <summary>Cleans a name list and cuts it to its meaningful length (the framework appends an empty thirteenth month).</summary>
        /// <param name="names">The names.</param>
        /// <param name="count">How many to keep.</param>
        /// <returns>The cleaned names.</returns>
        private static ImmutableArray<string> Names(string[] names, int count)
        {
            return [.. names.Take(count).Select(static n => CalendarText.Clean(n).Trim())];
        }
    }
}
