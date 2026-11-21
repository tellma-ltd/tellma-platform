// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Calendars;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The number format codes of exported cells, built from the request culture and calendar:
    ///     numbers by scale, and dates and times translated from the culture's patterns, behind a
    ///     locale tag that names the culture and the Windows calendar Excel renders the serial in.
    /// </summary>
    /// <remarks>
    ///     Separators in number codes are placeholders Excel renders per the viewer's locale, so
    ///     number codes are the same in every culture. A calendar without a Windows calendar id has
    ///     no date code at all: its dates are written as text.
    /// </remarks>
    internal static class NumberFormatBuilder
    {
        /// <summary>The general format: Excel picks the rendering.</summary>
        public const string General = "General";

        /// <summary>The text format, which also keeps a value typed into the cell as text.</summary>
        public const string Text = "@";

        /// <summary>The format of a time of day.</summary>
        public const string TimeOfDay = "hh:mm:ss";

        /// <summary>The first id of a custom number format; lower ids are Excel's built-in formats.</summary>
        public const int FirstCustomId = 164;

        /// <summary>The LCID .NET reports for a culture Windows has no identifier for.</summary>
        private const int CustomLcid = 0x1000;

        /// <summary>The built-in formats the codec writes, by code, which take their id rather than a custom entry.</summary>
        private static readonly Dictionary<string, int> BuiltIns = new(StringComparer.Ordinal)
        {
            [General] = 0,
            ["0"] = 1,
            ["0.00"] = 2,
            ["#,##0"] = 3,
            ["#,##0.00"] = 4,
            [Text] = 49,
        };

        /// <summary>The Windows calendar id of each calendar that has one, by calendar code; null for the default, Gregorian.</summary>
        private static readonly Dictionary<string, int?> WindowsCalendars = new(StringComparer.Ordinal)
        {
            [CalendarCodes.Gregorian] = null,
            [CalendarCodes.UmAlQura] = 0x17,
        };

        /// <summary>The literal characters a format code carries unquoted; every other literal is quoted.</summary>
        private static readonly string UnquotedLiterals = "$-+/():! ";

        /// <summary>The format of an integer.</summary>
        /// <param name="editable">Whether the cell belongs to an editable export, which drops the grouping.</param>
        /// <returns><c>0</c> when editable, <c>#,##0</c> otherwise.</returns>
        public static string Integer(bool editable)
        {
            return editable ? "0" : "#,##0";
        }

        /// <summary>The format of a decimal of a scale.</summary>
        /// <param name="scale">The scale; 0 takes the integer format.</param>
        /// <param name="editable">Whether the cell belongs to an editable export, which drops the grouping.</param>
        /// <returns><c>#,##0.</c> or <c>0.</c> followed by one <c>0</c> per decimal place.</returns>
        public static string Decimal(int scale, bool editable)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(scale);
            return scale == 0 ? Integer(editable) : string.Concat(editable ? "0." : "#,##0.", new string('0', scale));
        }

        /// <summary>Whether a calendar has a Windows calendar id, so its dates can be serials Excel renders in it.</summary>
        /// <param name="calendar">The calendar code.</param>
        /// <returns>True for the Gregorian and the Umm al-Qura calendars.</returns>
        public static bool HasWindowsCalendar(string calendar)
        {
            return WindowsCalendars.ContainsKey(calendar);
        }

        /// <summary>The format of a date: the culture's short date pattern, translated, behind the locale tag.</summary>
        /// <param name="culture">The request culture.</param>
        /// <param name="calendar">The calendar code; one with a Windows calendar id.</param>
        /// <returns>The format code (<c>[$-170401]dd/mm/yyyy</c>).</returns>
        /// <exception cref="ArgumentException"><paramref name="calendar" /> has no Windows calendar id.</exception>
        public static string DateFormat(CultureInfo culture, string calendar)
        {
            return string.Concat(LocaleTag(culture, calendar), DateCode(culture));
        }

        /// <summary>The format of a date and time: the short date pattern and the long time pattern, behind the locale tag.</summary>
        /// <param name="culture">The request culture.</param>
        /// <param name="calendar">The calendar code; one with a Windows calendar id.</param>
        /// <returns>The format code.</returns>
        /// <exception cref="ArgumentException"><paramref name="calendar" /> has no Windows calendar id.</exception>
        public static string DateTimeFormat(CultureInfo culture, string calendar)
        {
            return string.Concat(LocaleTag(culture, calendar), DateTimeCode(culture));
        }

        /// <summary>The culture's short date pattern in format code tokens, without the locale tag: the pattern a manifest records.</summary>
        /// <param name="culture">The request culture.</param>
        /// <returns>The pattern (<c>dd/mm/yyyy</c>).</returns>
        public static string DateCode(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            var data = CalendarCultureData.For(culture);
            return TranslateDate(data.Short, data.DateSeparator);
        }

        /// <summary>The culture's short date and long time patterns in format code tokens, without the locale tag.</summary>
        /// <param name="culture">The request culture.</param>
        /// <returns>The pattern (<c>dd/mm/yyyy hh:mm:ss</c>).</returns>
        public static string DateTimeCode(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            var data = CalendarCultureData.For(culture);
            return string.Concat(TranslateDate(data.Short, data.DateSeparator), " ", TranslateTime(data.LongTimePattern));
        }

        /// <summary>
        ///     The locale tag <c>[$-CCLLLL]</c>: <c>CC</c> the Windows calendar id in hexadecimal, none for
        ///     Gregorian; <c>LLLL</c> the culture's LCID in hexadecimal, <c>0000</c> (the viewer's locale)
        ///     for a culture without one.
        /// </summary>
        /// <param name="culture">The request culture.</param>
        /// <param name="calendar">The calendar code; one with a Windows calendar id.</param>
        /// <returns>The tag; empty for a Gregorian culture without an LCID.</returns>
        /// <exception cref="ArgumentException"><paramref name="calendar" /> has no Windows calendar id.</exception>
        public static string LocaleTag(CultureInfo culture, string calendar)
        {
            ArgumentNullException.ThrowIfNull(culture);
            ArgumentNullException.ThrowIfNull(calendar);
            if (!WindowsCalendars.TryGetValue(calendar, out int? calendarId))
            {
                throw new ArgumentException("The calendar has no Windows calendar id; its dates are written as text.", nameof(calendar));
            }

            // A custom culture reports 0x1000, the invariant culture 0x7F; neither names a locale.
            int lcid = culture.LCID;
            int? locale = lcid == CustomLcid || lcid == CultureInfo.InvariantCulture.LCID ? null : lcid;

            return (calendarId, locale) switch
            {
                (null, null) => string.Empty,
                (null, int l) => string.Create(CultureInfo.InvariantCulture, $"[$-{l:X4}]"),
                (int c, _) => string.Create(CultureInfo.InvariantCulture, $"[$-{c:X2}{locale ?? 0:X4}]"),
            };
        }

        /// <summary>The id of a format code: a built-in id when Excel has one for it, else null.</summary>
        /// <param name="code">The format code.</param>
        /// <returns>The built-in id, or null when the code needs a custom entry.</returns>
        public static int? BuiltInId(string code)
        {
            return BuiltIns.TryGetValue(code, out int id) ? id : null;
        }

        /// <summary>Translates a date pattern to format code tokens.</summary>
        /// <param name="pattern">The pattern, already cleaned.</param>
        /// <param name="separator">The culture's date separator, cleaned.</param>
        /// <returns>The format code tokens.</returns>
        internal static string TranslateDate(DatePattern pattern, string separator)
        {
            StringBuilder builder = new();
            foreach (DatePatternToken token in pattern.Tokens)
            {
                // Day and weekday share the letter d by width; months are m; the year is always in full.
                switch (token.Kind)
                {
                    case DatePatternTokenKind.Day:
                        _ = builder.Append('d', token.Width);
                        break;
                    case DatePatternTokenKind.Weekday:
                        _ = builder.Append('d', Math.Max(3, token.Width));
                        break;
                    case DatePatternTokenKind.Month:
                        _ = builder.Append('m', token.Width);
                        break;
                    case DatePatternTokenKind.Year:
                        _ = builder.Append("yyyy");
                        break;
                    case DatePatternTokenKind.DateSeparator:
                        AppendLiteral(builder, separator);
                        break;
                    case DatePatternTokenKind.Literal:
                    default:
                        AppendLiteral(builder, token.Text);
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Translates a .NET custom time pattern to format code tokens: <c>H</c> and <c>HH</c> become
        ///     <c>h</c> and <c>hh</c> (24-hour without a designator), <c>tt</c> becomes <c>AM/PM</c> and
        ///     <c>t</c> <c>A/P</c>; fractions of a second are dropped.
        /// </summary>
        /// <param name="pattern">The time pattern, cleaned.</param>
        /// <returns>The format code tokens.</returns>
        internal static string TranslateTime(string pattern)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            StringBuilder builder = new();
            StringBuilder literal = new();
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                int run = 1;
                while (i + run < pattern.Length && pattern[i + run] == c && c is 'H' or 'h' or 'm' or 's' or 't' or 'f' or 'F')
                {
                    run++;
                }

                switch (c)
                {
                    case 'H' or 'h':
                        Flush(builder, literal);
                        _ = builder.Append('h', Math.Min(run, 2));
                        break;
                    case 'm':
                        Flush(builder, literal);
                        _ = builder.Append('m', Math.Min(run, 2));
                        break;
                    case 's':
                        Flush(builder, literal);
                        _ = builder.Append('s', Math.Min(run, 2));
                        break;
                    case 't':
                        Flush(builder, literal);
                        _ = builder.Append(run == 1 ? "A/P" : "AM/PM");
                        break;
                    case 'f' or 'F':
                        // A fraction of a second has no place in the code; a separator before it goes too.
                        if (literal.Length > 0 && literal[^1] is '.' or ',')
                        {
                            _ = literal.Remove(literal.Length - 1, 1);
                        }

                        break;
                    case '\'' or '"':
                        // A quoted run is literal text.
                        int end = pattern.IndexOf(c, i + 1);
                        end = end < 0 ? pattern.Length : end;
                        _ = literal.Append(pattern, i + 1, end - i - 1);
                        run = end - i + 1;
                        break;
                    case '\\':
                        if (i + 1 < pattern.Length)
                        {
                            _ = literal.Append(pattern[i + 1]);
                        }

                        run = 2;
                        break;
                    case '%':
                        break;
                    default:
                        _ = literal.Append(c);
                        break;
                }

                i += run;
            }

            Flush(builder, literal);
            return builder.ToString().Trim();
        }

        /// <summary>Appends pending literal text, then clears it.</summary>
        /// <param name="builder">The code.</param>
        /// <param name="literal">The literal text.</param>
        private static void Flush(StringBuilder builder, StringBuilder literal)
        {
            if (literal.Length > 0)
            {
                AppendLiteral(builder, literal.ToString());
                _ = literal.Clear();
            }
        }

        /// <summary>Appends literal text: the characters a code carries unquoted as they are, every other run in quotes.</summary>
        /// <param name="builder">The code.</param>
        /// <param name="text">The literal text.</param>
        private static void AppendLiteral(StringBuilder builder, string text)
        {
            bool quoted = false;
            foreach (char c in text)
            {
                if (UnquotedLiterals.Contains(c, StringComparison.Ordinal))
                {
                    if (quoted)
                    {
                        _ = builder.Append('"');
                        quoted = false;
                    }

                    _ = builder.Append(c);
                }
                else if (c == '"')
                {
                    // A quote cannot sit inside a quoted run; it is escaped on its own.
                    if (quoted)
                    {
                        _ = builder.Append('"');
                        quoted = false;
                    }

                    _ = builder.Append("\\\"");
                }
                else
                {
                    if (!quoted)
                    {
                        _ = builder.Append('"');
                        quoted = true;
                    }

                    _ = builder.Append(c);
                }
            }

            if (quoted)
            {
                _ = builder.Append('"');
            }
        }
    }
}
