// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using Tellma.Core.Calendars;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     One culture value the platform pins: how it is read from a culture, cleaned, into its text
    ///     form, and how that text is applied to a culture.
    /// </summary>
    /// <param name="name">The field's name in the pinned data.</param>
    /// <param name="read">Reads the cleaned value of a culture, in its text form.</param>
    /// <param name="apply">Sets the value on a culture from its text form.</param>
    internal sealed class CultureField(string name, Func<CultureInfo, string> read, Action<CultureInfo, string> apply)
    {
        /// <summary>
        ///     Every pinned field, in the order the data lists them: the Gregorian date and time
        ///     formats, then the number formats.
        /// </summary>
        /// <remarks>
        ///     These are all the settable values of a culture's date-time and number formats that
        ///     the host's ICU release decides. Date and time text is cleaned as the calendar systems
        ///     clean it (no bidirectional controls, ordinary spaces); a number symbol loses its
        ///     bidirectional controls only. What a culture cannot be told stays the host's: its era
        ///     names, its sort order and its casing.
        /// </remarks>
        public static ImmutableArray<CultureField> All { get; } =
        [
            // --- Date and time patterns ---
            // Set through SetAllDateTimePatterns, so the host's alternative patterns go too.
            Pattern("shortDate", 'd', static format => format.ShortDatePattern),
            Pattern("longDate", 'D', static format => format.LongDatePattern),
            Pattern("shortTime", 't', static format => format.ShortTimePattern),
            Pattern("longTime", 'T', static format => format.LongTimePattern),
            Pattern("yearMonth", 'y', static format => format.YearMonthPattern),
            Date("monthDay", static format => format.MonthDayPattern, static (format, value) => format.MonthDayPattern = value),
            Date("dateSeparator", static format => format.DateSeparator, static (format, value) => format.DateSeparator = value),
            Date("timeSeparator", static format => format.TimeSeparator, static (format, value) => format.TimeSeparator = value),
            Date("am", static format => format.AMDesignator, static (format, value) => format.AMDesignator = value),
            Date("pm", static format => format.PMDesignator, static (format, value) => format.PMDesignator = value),

            // --- Month and weekday names ---
            // The framework holds thirteen month names; the Gregorian thirteenth is empty.
            Names("months", 12, static format => format.MonthNames, static (format, value) => format.MonthNames = [.. value, string.Empty]),
            Names("monthsAbbreviated", 12, static format => format.AbbreviatedMonthNames, static (format, value) => format.AbbreviatedMonthNames = [.. value, string.Empty]),
            Names("monthsGenitive", 12, static format => format.MonthGenitiveNames, static (format, value) => format.MonthGenitiveNames = [.. value, string.Empty]),
            Names("monthsAbbreviatedGenitive", 12, static format => format.AbbreviatedMonthGenitiveNames, static (format, value) => format.AbbreviatedMonthGenitiveNames = [.. value, string.Empty]),
            Names("days", 7, static format => format.DayNames, static (format, value) => format.DayNames = value),
            Names("daysAbbreviated", 7, static format => format.AbbreviatedDayNames, static (format, value) => format.AbbreviatedDayNames = value),
            Names("daysShortest", 7, static format => format.ShortestDayNames, static (format, value) => format.ShortestDayNames = value),

            // --- The week ---
            new CultureField(
                "firstDayOfWeek",
                static culture => culture.DateTimeFormat.FirstDayOfWeek.ToString(),
                static (culture, value) => culture.DateTimeFormat.FirstDayOfWeek = Enum.Parse<DayOfWeek>(value)),
            new CultureField(
                "weekRule",
                static culture => culture.DateTimeFormat.CalendarWeekRule.ToString(),
                static (culture, value) => culture.DateTimeFormat.CalendarWeekRule = Enum.Parse<CalendarWeekRule>(value)),

            // --- Numbers ---
            Symbol("decimalSeparator", static format => format.NumberDecimalSeparator, static (format, value) => format.NumberDecimalSeparator = value),
            Symbol("groupSeparator", static format => format.NumberGroupSeparator, static (format, value) => format.NumberGroupSeparator = value),
            Sizes("groupSizes", static format => format.NumberGroupSizes, static (format, value) => format.NumberGroupSizes = value),
            Number("decimalDigits", static format => format.NumberDecimalDigits, static (format, value) => format.NumberDecimalDigits = value),
            Number("negativePattern", static format => format.NumberNegativePattern, static (format, value) => format.NumberNegativePattern = value),
            Symbol("negativeSign", static format => format.NegativeSign, static (format, value) => format.NegativeSign = value),
            Symbol("positiveSign", static format => format.PositiveSign, static (format, value) => format.PositiveSign = value),
            Symbol("nan", static format => format.NaNSymbol, static (format, value) => format.NaNSymbol = value),
            Symbol("positiveInfinity", static format => format.PositiveInfinitySymbol, static (format, value) => format.PositiveInfinitySymbol = value),
            Symbol("negativeInfinity", static format => format.NegativeInfinitySymbol, static (format, value) => format.NegativeInfinitySymbol = value),

            // --- Percentages ---
            Symbol("percentSymbol", static format => format.PercentSymbol, static (format, value) => format.PercentSymbol = value),
            Symbol("perMilleSymbol", static format => format.PerMilleSymbol, static (format, value) => format.PerMilleSymbol = value),
            Symbol("percentDecimalSeparator", static format => format.PercentDecimalSeparator, static (format, value) => format.PercentDecimalSeparator = value),
            Symbol("percentGroupSeparator", static format => format.PercentGroupSeparator, static (format, value) => format.PercentGroupSeparator = value),
            Sizes("percentGroupSizes", static format => format.PercentGroupSizes, static (format, value) => format.PercentGroupSizes = value),
            Number("percentDecimalDigits", static format => format.PercentDecimalDigits, static (format, value) => format.PercentDecimalDigits = value),
            Number("percentPositivePattern", static format => format.PercentPositivePattern, static (format, value) => format.PercentPositivePattern = value),
            Number("percentNegativePattern", static format => format.PercentNegativePattern, static (format, value) => format.PercentNegativePattern = value),

            // --- Currency ---
            Symbol("currencySymbol", static format => format.CurrencySymbol, static (format, value) => format.CurrencySymbol = value),
            Symbol("currencyDecimalSeparator", static format => format.CurrencyDecimalSeparator, static (format, value) => format.CurrencyDecimalSeparator = value),
            Symbol("currencyGroupSeparator", static format => format.CurrencyGroupSeparator, static (format, value) => format.CurrencyGroupSeparator = value),
            Sizes("currencyGroupSizes", static format => format.CurrencyGroupSizes, static (format, value) => format.CurrencyGroupSizes = value),
            Number("currencyDecimalDigits", static format => format.CurrencyDecimalDigits, static (format, value) => format.CurrencyDecimalDigits = value),
            Number("currencyPositivePattern", static format => format.CurrencyPositivePattern, static (format, value) => format.CurrencyPositivePattern = value),
            Number("currencyNegativePattern", static format => format.CurrencyNegativePattern, static (format, value) => format.CurrencyNegativePattern = value),
        ];

        /// <summary>The field's name in the pinned data.</summary>
        public string Name { get; } = name;

        /// <summary>Reads the field from a culture, cleaned, in its text form.</summary>
        /// <param name="culture">The culture.</param>
        /// <returns>The value's text form.</returns>
        public string Read(CultureInfo culture)
        {
            return read(culture);
        }

        /// <summary>Sets the field on a culture.</summary>
        /// <param name="culture">The culture, which must not be read-only.</param>
        /// <param name="value">The value's text form.</param>
        /// <exception cref="FormatException">The text is not a value of this field.</exception>
        /// <exception cref="ArgumentException">The framework refuses the value.</exception>
        public void Apply(CultureInfo culture, string value)
        {
            apply(culture, value);
        }

        /// <summary>A date-time pattern that has a standard format character, set with every alternative of it.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="format">The standard format character.</param>
        /// <param name="get">Reads the pattern.</param>
        /// <returns>The field.</returns>
        private static CultureField Pattern(string name, char format, Func<DateTimeFormatInfo, string> get)
        {
            return new CultureField(
                name,
                culture => CultureText.Encode(CalendarText.Clean(get(culture.DateTimeFormat))),
                (culture, value) => culture.DateTimeFormat.SetAllDateTimePatterns([CultureText.Decode(value)], format));
        }

        /// <summary>A date-time string.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="get">Reads the string.</param>
        /// <param name="set">Sets the string.</param>
        /// <returns>The field.</returns>
        private static CultureField Date(string name, Func<DateTimeFormatInfo, string> get, Action<DateTimeFormatInfo, string> set)
        {
            return new CultureField(
                name,
                culture => CultureText.Encode(CalendarText.Clean(get(culture.DateTimeFormat))),
                (culture, value) => set(culture.DateTimeFormat, CultureText.Decode(value)));
        }

        /// <summary>A list of month or weekday names.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="count">How many names the field holds.</param>
        /// <param name="get">Reads the names.</param>
        /// <param name="set">Sets the names.</param>
        /// <returns>The field.</returns>
        private static CultureField Names(string name, int count, Func<DateTimeFormatInfo, string[]> get, Action<DateTimeFormatInfo, string[]> set)
        {
            return new CultureField(
                name,
                culture => CultureText.EncodeList(get(culture.DateTimeFormat).Take(count).Select(CalendarText.Clean)),
                (culture, value) =>
                {
                    string[] names = CultureText.DecodeList(value);
                    if (names.Length != count)
                    {
                        throw new FormatException($"The culture field '{name}' holds {names.Length} names, not {count}.");
                    }

                    set(culture.DateTimeFormat, names);
                });
        }

        /// <summary>A number symbol.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="get">Reads the symbol.</param>
        /// <param name="set">Sets the symbol.</param>
        /// <returns>The field.</returns>
        private static CultureField Symbol(string name, Func<NumberFormatInfo, string> get, Action<NumberFormatInfo, string> set)
        {
            return new CultureField(
                name,
                culture => CultureText.Encode(CultureText.StripBidi(get(culture.NumberFormat))),
                (culture, value) => set(culture.NumberFormat, CultureText.Decode(value)));
        }

        /// <summary>A number pattern index or a digit count.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="get">Reads the number.</param>
        /// <param name="set">Sets the number.</param>
        /// <returns>The field.</returns>
        private static CultureField Number(string name, Func<NumberFormatInfo, int> get, Action<NumberFormatInfo, int> set)
        {
            return new CultureField(
                name,
                culture => get(culture.NumberFormat).ToString(CultureInfo.InvariantCulture),
                (culture, value) => set(culture.NumberFormat, int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)));
        }

        /// <summary>The sizes of a number's digit groups.</summary>
        /// <param name="name">The field's name.</param>
        /// <param name="get">Reads the sizes.</param>
        /// <param name="set">Sets the sizes.</param>
        /// <returns>The field.</returns>
        private static CultureField Sizes(string name, Func<NumberFormatInfo, int[]> get, Action<NumberFormatInfo, int[]> set)
        {
            return new CultureField(
                name,
                culture => CultureText.EncodeNumbers(get(culture.NumberFormat)),
                (culture, value) => set(culture.NumberFormat, CultureText.DecodeNumbers(value)));
        }
    }
}
