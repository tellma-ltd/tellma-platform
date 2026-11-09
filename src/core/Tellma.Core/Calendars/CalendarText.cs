// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The text clean-up every culture-derived string goes through before the calendar systems
    ///     render with it, and the digit folding their parser applies.
    /// </summary>
    internal static class CalendarText
    {
        /// <summary>
        ///     Removes bidirectional control characters and turns the space variants of the culture
        ///     data into ordinary spaces.
        /// </summary>
        /// <remarks>
        ///     CLDR's Arabic patterns put a right-to-left mark between the parts of a numeric date and
        ///     an Arabic letter mark before a minus sign, and newer CLDR releases put a narrow no-break
        ///     space before the day period. Server text must survive a copy into a spreadsheet or an
        ///     email as a date, and render the same on every host's ICU release, so none of them
        ///     reaches the output.
        /// </remarks>
        /// <param name="text">The text.</param>
        /// <returns>The cleaned text; the same instance when nothing needed changing.</returns>
        public static string Clean(string text)
        {
            // --- Fast path: most strings carry none of the characters ---
            bool needsWork = false;
            foreach (char c in text)
            {
                if (IsBidiControl(c) || IsSpaceVariant(c))
                {
                    needsWork = true;
                    break;
                }
            }

            if (!needsWork)
            {
                return text;
            }

            // --- Rewrite ---
            StringBuilder builder = new(text.Length);
            foreach (char c in text)
            {
                if (IsBidiControl(c))
                {
                    continue;
                }

                _ = builder.Append(IsSpaceVariant(c) ? ' ' : c);
            }

            return builder.ToString();
        }

        /// <summary>Whether a character is a bidirectional control: a mark, an embedding, an override or an isolate.</summary>
        /// <param name="c">The character.</param>
        /// <returns>True for U+061C, U+200E, U+200F, U+202A to U+202E and U+2066 to U+2069.</returns>
        public static bool IsBidiControl(char c)
        {
            return c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
        }

        /// <summary>Whether a character is a space variant culture data uses where an ordinary space reads the same.</summary>
        /// <param name="c">The character.</param>
        /// <returns>True for the no-break, figure, thin and narrow no-break spaces.</returns>
        public static bool IsSpaceVariant(char c)
        {
            return c is ' ' or ' ' or ' ' or ' ';
        }

        /// <summary>
        ///     The value of a decimal digit in any script (ASCII, Arabic-Indic, Extended Arabic-Indic,
        ///     Devanagari, …), or -1 for any other character.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns>0 to 9, or -1.</returns>
        public static int DigitValue(char c)
        {
            return char.IsDigit(c) ? CharUnicodeInfo.GetDecimalDigitValue(c) : -1;
        }
    }
}
