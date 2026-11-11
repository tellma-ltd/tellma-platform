// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The composite format of a raw statement, split into literal text and holes. A hole is a
    ///     bare argument index: an alignment or a format string has no meaning in SQL and is refused.
    /// </summary>
    internal static class SqlTemplate
    {
        /// <summary>Renders a format by replacing each hole with the text <paramref name="renderHole" /> returns for its index.</summary>
        /// <param name="format">The composite format, with <c>{{</c> and <c>}}</c> for literal braces.</param>
        /// <param name="argumentCount">How many arguments the format has.</param>
        /// <param name="renderHole">Renders one hole, by argument index, once per occurrence.</param>
        /// <returns>The rendered text.</returns>
        /// <exception cref="FormatException">The format is malformed or a hole carries an alignment or a format string.</exception>
        public static string Render(string format, int argumentCount, Func<int, string> renderHole)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(renderHole);

            StringBuilder text = new(format.Length + 32);
            int index = 0;
            while (index < format.Length)
            {
                char current = format[index];

                // --- Literal braces, doubled ---
                if (current is '{' or '}')
                {
                    if (index + 1 < format.Length && format[index + 1] == current)
                    {
                        _ = text.Append(current);
                        index += 2;
                        continue;
                    }

                    if (current == '}')
                    {
                        throw new FormatException("The statement text has an unmatched '}'. Double a literal brace.");
                    }

                    // --- A hole: digits only, up to the closing brace ---
                    int close = format.IndexOf('}', index + 1);
                    if (close < 0)
                    {
                        throw new FormatException("The statement text has an unmatched '{'. Double a literal brace.");
                    }

                    ReadOnlySpan<char> hole = format.AsSpan(index + 1, close - index - 1);
                    if (hole.IsEmpty || !int.TryParse(hole, NumberStyles.None, CultureInfo.InvariantCulture, out int argument))
                    {
                        throw new FormatException(
                            $"The statement hole '{{{hole}}}' carries an alignment or a format string; a raw statement's holes are bare values, identifiers or table-valued parameters.");
                    }

                    if (argument >= argumentCount)
                    {
                        throw new FormatException($"The statement hole {{{argument}}} names no argument.");
                    }

                    _ = text.Append(renderHole(argument));
                    index = close + 1;
                    continue;
                }

                // --- A run of literal text up to the next brace ---
                int next = format.IndexOfAny(['{', '}'], index);
                int end = next < 0 ? format.Length : next;
                _ = text.Append(format, index, end - index);
                index = end;
            }

            return text.ToString();
        }
    }
}
