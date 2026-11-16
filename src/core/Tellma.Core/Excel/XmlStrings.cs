// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     SpreadsheetML's escaping of text XML cannot carry: a character XML forbids (a control
    ///     character, a lone surrogate) is written <c>_xHHHH_</c>, and an underscore that would read as
    ///     the start of such an escape is itself written <c>_x005F_</c>. Excel reads both back.
    /// </summary>
    internal static class XmlStrings
    {
        /// <summary>Escapes text for a cell or a shared string.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The escaped text; the same instance when nothing needed escaping.</returns>
        public static string Escape(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (!NeedsEscaping(text))
            {
                return text;
            }

            StringBuilder builder = new(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // A well-formed pair passes as it is.
                    _ = builder.Append(c).Append(text[i + 1]);
                    i++;
                    continue;
                }

                if (!IsXmlChar(c) || char.IsSurrogate(c))
                {
                    _ = builder.Append(Escaped(c));
                    continue;
                }

                // An underscore that would read as the start of an escape is escaped itself.
                _ = c == '_' && LooksEscaped(text, i) ? builder.Append("_x005F_") : builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>Reads text a workbook stores, undoing its escapes.</summary>
        /// <param name="text">The stored text.</param>
        /// <returns>The text; the same instance when it held no escape.</returns>
        public static string Unescape(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (!text.Contains("_x", StringComparison.Ordinal))
            {
                return text;
            }

            StringBuilder builder = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '_' && LooksEscaped(text, i))
                {
                    _ = builder.Append((char)int.Parse(text.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 6;
                }
                else
                {
                    _ = builder.Append(text[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>Whether any character of the text needs escaping.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when one does.</returns>
        private static bool NeedsEscaping(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                }
                else if (!IsXmlChar(c) || char.IsSurrogate(c) || (c == '_' && LooksEscaped(text, i)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a character, taken alone, is one XML allows.</summary>
        /// <param name="c">The character.</param>
        /// <returns>True for tab, line feed, carriage return and the characters from space up, less U+FFFE and U+FFFF.</returns>
        private static bool IsXmlChar(char c)
        {
            return c is '\t' or '\n' or '\r' or (>= ' ' and not '\uFFFE' and not '\uFFFF');
        }

        /// <summary>Whether the text at an underscore reads <c>_xHHHH_</c>.</summary>
        /// <param name="text">The text.</param>
        /// <param name="index">The underscore's index.</param>
        /// <returns>True when it does.</returns>
        private static bool LooksEscaped(string text, int index)
        {
            return index + 6 < text.Length
                && text[index + 1] == 'x'
                && char.IsAsciiHexDigit(text[index + 2])
                && char.IsAsciiHexDigit(text[index + 3])
                && char.IsAsciiHexDigit(text[index + 4])
                && char.IsAsciiHexDigit(text[index + 5])
                && text[index + 6] == '_';
        }

        /// <summary>The escape of one character.</summary>
        /// <param name="c">The character.</param>
        /// <returns><c>_xHHHH_</c>.</returns>
        private static string Escaped(char c)
        {
            return string.Create(CultureInfo.InvariantCulture, $"_x{(int)c:X4}_");
        }
    }
}
