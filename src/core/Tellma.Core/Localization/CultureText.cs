// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Calendars;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The text form of a culture value in the pinned culture data, and the clean-up a value
    ///     goes through before it is pinned.
    /// </summary>
    /// <remarks>
    ///     A value is written on one line. Every character a reader could not see or tell apart
    ///     (a control, a format character, a space other than an ordinary one, a leading or trailing
    ///     space) and the three characters the form itself uses (the backslash, the semicolon that
    ///     separates list items, and the number sign) are written as <c>\uXXXX</c>, so the data
    ///     diffs and reviews as plain text.
    /// </remarks>
    internal static class CultureText
    {
        /// <summary>Removes the bidirectional controls from a number symbol, and nothing else.</summary>
        /// <remarks>
        ///     CLDR's Arabic data puts a left-to-right mark before the minus and plus signs and
        ///     around the percent sign. An amount pasted from an email must stay a number, so the
        ///     marks go; a group separator's no-break space stays, since it is the separator.
        /// </remarks>
        /// <param name="text">The symbol.</param>
        /// <returns>The symbol without bidirectional controls.</returns>
        public static string StripBidi(string text)
        {
            if (!text.AsSpan().ContainsAnyInRange('؜', '⁩'))
            {
                return text;
            }

            StringBuilder builder = new(text.Length);
            foreach (char c in text)
            {
                if (!CalendarText.IsBidiControl(c))
                {
                    _ = builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>Writes one value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>Its one-line form.</returns>
        public static string Encode(string value)
        {
            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool edge = i == 0 || i == value.Length - 1;
                _ = IsPlain(c, edge)
                    ? builder.Append(c)
                    : builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>Writes a list of values, separated by semicolons.</summary>
        /// <param name="values">The values.</param>
        /// <returns>The list's one-line form.</returns>
        public static string EncodeList(IEnumerable<string> values)
        {
            return string.Join(';', values.Select(Encode));
        }

        /// <summary>Reads one value.</summary>
        /// <param name="text">Its one-line form.</param>
        /// <returns>The value.</returns>
        /// <exception cref="FormatException">An escape is not <c>\u</c> and four hexadecimal digits.</exception>
        public static string Decode(string text)
        {
            if (!text.Contains('\\', StringComparison.Ordinal))
            {
                return text;
            }

            StringBuilder builder = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\')
                {
                    _ = builder.Append(text[i]);
                    continue;
                }

                if (i + 5 >= text.Length
                    || text[i + 1] != 'u'
                    || !ushort.TryParse(text.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                {
                    throw new FormatException($"The culture value '{text}' holds an escape that is not \\u and four hexadecimal digits.");
                }

                _ = builder.Append((char)code);
                i += 5;
            }

            return builder.ToString();
        }

        /// <summary>Reads a list of values.</summary>
        /// <param name="text">The list's one-line form.</param>
        /// <returns>The values.</returns>
        /// <exception cref="FormatException">An escape is not <c>\u</c> and four hexadecimal digits.</exception>
        public static string[] DecodeList(string text)
        {
            return [.. text.Split(';').Select(Decode)];
        }

        /// <summary>Writes a list of small integers, separated by commas.</summary>
        /// <param name="values">The values.</param>
        /// <returns>The list's one-line form.</returns>
        public static string EncodeNumbers(int[] values)
        {
            return string.Join(',', values.Select(static value => value.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>Reads a list of small integers.</summary>
        /// <param name="text">The list's one-line form.</param>
        /// <returns>The values.</returns>
        /// <exception cref="FormatException">An item is not an integer.</exception>
        public static int[] DecodeNumbers(string text)
        {
            return text.Length == 0
                ? []
                : [.. text.Split(',').Select(static item => int.Parse(item, NumberStyles.None, CultureInfo.InvariantCulture))];
        }

        /// <summary>Whether a character is written as itself.</summary>
        /// <param name="c">The character.</param>
        /// <param name="edge">Whether it is the first or the last character of its value.</param>
        /// <returns>True when it needs no escape.</returns>
        private static bool IsPlain(char c, bool edge)
        {
            if (c == ' ')
            {
                return !edge;
            }

            if (c is '\\' or ';' or '#')
            {
                return false;
            }

            // Surrogates pass: a pair is one visible character, and the data holds no lone ones.
            return CharUnicodeInfo.GetUnicodeCategory(c) is not (
                UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned);
        }
    }
}
