// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     Reading a string literal at a type its context demands.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written by hand rather than handed to the platform's parsers, which accept far more
    ///         than the language does — a great many separators, orderings, and abbreviations, some
    ///         of them culture-dependent. Using one of those would widen the language silently and
    ///         make what a stored expression means depend on where it was compiled.
    ///     </para>
    ///     <para>
    ///         The conversion applies to a written literal only, never to text that arrived in a
    ///         column, a parameter, or a computed value. A date that came from data is converted
    ///         explicitly or not at all.
    ///     </para>
    /// </remarks>
    internal static class LiteralCoercions
    {
        /// <summary>Reads a date.</summary>
        /// <param name="text">The literal's text.</param>
        /// <param name="value">The value, when the text is one.</param>
        /// <returns>True when the text is a date.</returns>
        internal static bool TryParseDate(string text, out DateOnly value)
        {
            value = default;
            return TryReadDate(text, 0, out value, out int consumed) && consumed == text.Length;
        }

        /// <summary>Reads a date and time.</summary>
        /// <param name="text">The literal's text.</param>
        /// <param name="value">The value, when the text is one.</param>
        /// <returns>True when the text is a date and time.</returns>
        internal static bool TryParseDateTime(string text, out DateTime value)
        {
            value = default;
            if (!TryReadDateTime(text, out DateTime parsed, out int consumed) || consumed != text.Length)
            {
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>Reads an instant with an offset.</summary>
        /// <param name="text">The literal's text.</param>
        /// <param name="value">The value, when the text is one.</param>
        /// <returns>True when the text is an instant with an offset.</returns>
        internal static bool TryParseDateTimeOffset(string text, out DateTimeOffset value)
        {
            value = default;
            if (!TryReadDateTime(text, out DateTime local, out int consumed))
            {
                return false;
            }

            // An offset is what tells this apart from a plain date and time. Without one there
            // would be nothing to convert, so requiring it keeps the two literal forms distinct.
            if (consumed >= text.Length)
            {
                return false;
            }

            if (text[consumed] is 'Z' or 'z')
            {
                if (consumed + 1 != text.Length)
                {
                    return false;
                }

                value = new DateTimeOffset(local, TimeSpan.Zero);
                return true;
            }

            if (text[consumed] is not ('+' or '-') || consumed + 6 != text.Length)
            {
                return false;
            }

            bool negative = text[consumed] == '-';
            if (!TryReadDigits(text, consumed + 1, 2, out int hours)
                || text[consumed + 3] != ':'
                || !TryReadDigits(text, consumed + 4, 2, out int minutes))
            {
                return false;
            }

            if (hours > 14 || minutes > 59)
            {
                return false;
            }

            TimeSpan offset = new(hours, minutes, 0);
            if (negative)
            {
                offset = offset.Negate();
            }

            // The moment a written offset denotes can fall outside the domain even when the local
            // part written beside it is inside it — one minute east of the first midnight is a
            // moment in the year before the first year. Checked here, because the constructor
            // answers that by throwing, and nothing a reader types may leave through an exception.
            long ticks = local.Ticks - offset.Ticks;
            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return false;
            }

            value = new DateTimeOffset(local, offset);
            return true;
        }

        /// <summary>Reads a globally unique identifier.</summary>
        /// <param name="text">The literal's text.</param>
        /// <param name="value">The value, when the text is one.</param>
        /// <returns>True when the text is one.</returns>
        /// <remarks>
        ///     The hyphenated form only, without braces or any of the other renderings the platform
        ///     would otherwise accept.
        /// </remarks>
        internal static bool TryParseGuid(string text, out Guid value)
        {
            value = default;
            if (text.Length != 36)
            {
                return false;
            }

            for (int index = 0; index < text.Length; index++)
            {
                bool shouldBeHyphen = index is 8 or 13 or 18 or 23;
                if (shouldBeHyphen)
                {
                    if (text[index] != '-')
                    {
                        return false;
                    }
                }
                else if (!IsHex(text[index]))
                {
                    return false;
                }
            }

            return Guid.TryParseExact(text, "D", out value);
        }

        /// <summary>Reads a date at an offset within the text.</summary>
        /// <param name="text">The text.</param>
        /// <param name="start">Where to read from.</param>
        /// <param name="value">The value, when one was read.</param>
        /// <param name="consumed">How far reading got.</param>
        /// <returns>True when a date was read.</returns>
        private static bool TryReadDate(string text, int start, out DateOnly value, out int consumed)
        {
            value = default;
            consumed = start;

            if (text.Length < start + 10
                || !TryReadDigits(text, start, 4, out int year)
                || text[start + 4] != '-'
                || !TryReadDigits(text, start + 5, 2, out int month)
                || text[start + 7] != '-'
                || !TryReadDigits(text, start + 8, 2, out int day))
            {
                return false;
            }

            if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            value = new DateOnly(year, month, day);
            consumed = start + 10;
            return true;
        }

        /// <summary>Reads a date and an optional time.</summary>
        /// <param name="text">The text.</param>
        /// <param name="value">The value, when one was read.</param>
        /// <param name="consumed">How far reading got.</param>
        /// <returns>True when a date and time were read.</returns>
        private static bool TryReadDateTime(string text, out DateTime value, out int consumed)
        {
            value = default;
            if (!TryReadDate(text, 0, out DateOnly date, out consumed))
            {
                return false;
            }

            value = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            if (consumed >= text.Length || text[consumed] is not ('T' or 't' or ' '))
            {
                return true;
            }

            int index = consumed + 1;
            if (!TryReadDigits(text, index, 2, out int hours)
                || index + 2 >= text.Length
                || text[index + 2] != ':'
                || !TryReadDigits(text, index + 3, 2, out int minutes))
            {
                return false;
            }

            if (hours > 23 || minutes > 59)
            {
                return false;
            }

            index += 5;
            int seconds = 0;
            long ticks = 0;

            if (index < text.Length && text[index] == ':')
            {
                if (!TryReadDigits(text, index + 1, 2, out seconds) || seconds > 59)
                {
                    return false;
                }

                index += 3;

                if (index < text.Length && text[index] == '.')
                {
                    int digits = 0;
                    int scan = index + 1;
                    while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                    {
                        // Beyond the backend's own precision the remaining digits cannot be
                        // represented, so accepting them would silently discard them.
                        if (digits < 7)
                        {
                            ticks = (ticks * 10) + (text[scan] - '0');
                            digits++;
                        }
                        else
                        {
                            return false;
                        }

                        scan++;
                    }

                    if (digits == 0)
                    {
                        return false;
                    }

                    for (int pad = digits; pad < 7; pad++)
                    {
                        ticks *= 10;
                    }

                    index = scan;
                }
            }

            value = date.ToDateTime(new TimeOnly(hours, minutes, seconds), DateTimeKind.Unspecified)
                .AddTicks(ticks);

            consumed = index;
            return true;
        }

        /// <summary>Reads a fixed run of digits.</summary>
        /// <param name="text">The text.</param>
        /// <param name="start">Where to read from.</param>
        /// <param name="count">How many digits to read.</param>
        /// <param name="value">The value, when they were all there.</param>
        /// <returns>True when the digits were all there.</returns>
        private static bool TryReadDigits(string text, int start, int count, out int value)
        {
            value = 0;
            if (start + count > text.Length)
            {
                return false;
            }

            for (int index = start; index < start + count; index++)
            {
                if (!char.IsAsciiDigit(text[index]))
                {
                    return false;
                }

                value = (value * 10) + (text[index] - '0');
            }

            return true;
        }

        /// <summary>Whether a character is a hexadecimal digit.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHex(char character)
        {
            return char.IsAsciiHexDigit(character);
        }
    }
}
