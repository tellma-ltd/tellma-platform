// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Testing.Resources
{
    /// <summary>
    ///     Collects the string literals of C# source: regular, verbatim and raw strings, each of them
    ///     interpolated or not, skipping comments and character literals. An interpolated string
    ///     yields each text run between its holes, and the holes are scanned as code, so a literal
    ///     nested in a hole is found too.
    /// </summary>
    /// <param name="text">The source text.</param>
    internal sealed class CSharpLiteralScanner(string text)
    {
        private readonly List<string> _literals = [];
        private int _index;

        /// <summary>The literals of a source text, in source order.</summary>
        /// <param name="text">The source text.</param>
        /// <returns>The literals' contents.</returns>
        public static IReadOnlyList<string> Scan(string text)
        {
            CSharpLiteralScanner scanner = new(text);
            scanner.Code(untilBrace: false);
            return scanner._literals;
        }

        /// <summary>The character at an offset from the current one, or NUL past the end.</summary>
        /// <param name="offset">The offset.</param>
        /// <returns>The character.</returns>
        private char Peek(int offset = 0)
        {
            int index = _index + offset;
            return index < text.Length ? text[index] : '\0';
        }

        /// <summary>Scans code: to the end of the text, or to the brace that closes an interpolation hole.</summary>
        /// <param name="untilBrace">Whether to stop before an unmatched closing brace, which ends a hole.</param>
        private void Code(bool untilBrace)
        {
            int depth = 0;
            while (_index < text.Length)
            {
                char c = text[_index];

                // --- Comments ---
                if (c == '/' && Peek(1) == '/')
                {
                    SkipPast("\n");
                }
                else if (c == '/' && Peek(1) == '*')
                {
                    _index += 2;
                    SkipPast("*/");
                }

                // --- Character literals, whose quote must not start a string ---
                else if (c == '\'')
                {
                    CharLiteral();
                }

                // --- Strings, with their prefixes ---
                else if (c is '"' or '@' or '$' && TryString())
                {
                    continue;
                }

                // --- Braces: an unmatched closing one ends a hole ---
                else if (c == '{')
                {
                    depth++;
                    _index++;
                }
                else if (c == '}')
                {
                    if (untilBrace && depth == 0)
                    {
                        return;
                    }

                    depth--;
                    _index++;
                }
                else
                {
                    _index++;
                }
            }
        }

        /// <summary>Moves past the next occurrence of a marker, or to the end of the text.</summary>
        /// <param name="marker">The marker.</param>
        private void SkipPast(string marker)
        {
            int found = text.IndexOf(marker, _index, StringComparison.Ordinal);
            _index = found < 0 ? text.Length : found + marker.Length;
        }

        /// <summary>Skips a character literal, escapes included, stopping at the end of the line for a stray quote.</summary>
        private void CharLiteral()
        {
            _index++;
            while (_index < text.Length && text[_index] is not ('\'' or '\n'))
            {
                _index += text[_index] == '\\' ? 2 : 1;
            }

            _index++;
        }

        /// <summary>Reads a string at the current position, when its prefix and opening quote start one.</summary>
        /// <returns>True when a string was read; false when the character starts none (a verbatim identifier, say).</returns>
        private bool TryString()
        {
            // --- The prefix: any number of '$' and at most one '@', in either order ---
            int start = _index;
            int dollars = 0;
            bool verbatim = false;
            int at = _index;
            while (at < text.Length)
            {
                if (text[at] == '$')
                {
                    dollars++;
                }
                else if (text[at] == '@' && !verbatim)
                {
                    verbatim = true;
                }
                else
                {
                    break;
                }

                at++;
            }

            if (at >= text.Length || text[at] != '"')
            {
                _index = start;
                return false;
            }

            // --- The opening quotes: three or more open a raw string ---
            int quotes = 0;
            while (at + quotes < text.Length && text[at + quotes] == '"')
            {
                quotes++;
            }

            _index = at;
            if (!verbatim && quotes >= 3)
            {
                RawString(quotes, dollars);
            }
            else
            {
                _index++;
                QuotedString(verbatim, dollars > 0);
            }

            return true;
        }

        /// <summary>Reads a regular or verbatim string's content, after its opening quote.</summary>
        /// <param name="verbatim">Whether it is verbatim: <c>""</c> is a quote, and a backslash is itself.</param>
        /// <param name="interpolated">Whether braces open holes.</param>
        private void QuotedString(bool verbatim, bool interpolated)
        {
            StringBuilder run = new();
            while (_index < text.Length)
            {
                char c = text[_index];
                if (c == '"')
                {
                    if (verbatim && Peek(1) == '"')
                    {
                        _ = run.Append('"');
                        _index += 2;
                        continue;
                    }

                    _index++;
                    break;
                }

                if (!verbatim && c == '\\')
                {
                    _ = run.Append(Unescape(Peek(1)));
                    _index += 2;
                    continue;
                }

                if (!verbatim && c == '\n')
                {
                    // An unterminated string: resynchronize at the next line rather than swallow the file.
                    break;
                }

                if (interpolated && c == '{')
                {
                    if (Peek(1) == '{')
                    {
                        _ = run.Append('{');
                        _index += 2;
                        continue;
                    }

                    Flush(run);
                    Hole(1);
                    continue;
                }

                if (interpolated && c == '}' && Peek(1) == '}')
                {
                    _ = run.Append('}');
                    _index += 2;
                    continue;
                }

                _ = run.Append(c);
                _index++;
            }

            Flush(run);
        }

        /// <summary>Reads a raw string's content, at its opening quotes.</summary>
        /// <param name="quotes">The number of quotes that open and close it.</param>
        /// <param name="dollars">The number of <c>$</c> of its prefix: that many braces open a hole; zero for none.</param>
        private void RawString(int quotes, int dollars)
        {
            _index += quotes;
            string closing = new('"', quotes);
            StringBuilder run = new();
            while (_index < text.Length)
            {
                if (string.CompareOrdinal(text, _index, closing, 0, quotes) == 0)
                {
                    _index += quotes;
                    break;
                }

                // A run of at least as many braces as dollars opens a hole with its last ones; the
                // braces before them are text.
                if (dollars > 0 && text[_index] == '{')
                {
                    int braces = 0;
                    while (Peek(braces) == '{')
                    {
                        braces++;
                    }

                    if (braces >= dollars)
                    {
                        _ = run.Append('{', braces - dollars);
                        _index += braces - dollars;
                        Flush(run);
                        Hole(dollars);
                        continue;
                    }
                }

                _ = run.Append(text[_index]);
                _index++;
            }

            // A multi-line raw string's content starts after its opening line and ends before its
            // closing line's indentation.
            Flush(run, trim: true);
        }

        /// <summary>Scans an interpolation hole as code, from its opening braces to past its closing ones.</summary>
        /// <param name="braces">The number of braces that open and close it.</param>
        private void Hole(int braces)
        {
            _index += braces;
            Code(untilBrace: true);
            for (int closed = 0; closed < braces && Peek() == '}'; closed++)
            {
                _index++;
            }
        }

        /// <summary>Records a text run as a literal and clears it.</summary>
        /// <param name="run">The run.</param>
        /// <param name="trim">Whether to trim the white space around it.</param>
        private void Flush(StringBuilder run, bool trim = false)
        {
            string literal = run.ToString();
            _literals.Add(trim ? literal.Trim() : literal);
            _ = run.Clear();
        }

        /// <summary>The character a simple escape stands for; any other character as itself.</summary>
        /// <param name="escaped">The character after the backslash.</param>
        /// <returns>The character.</returns>
        private static char Unescape(char escaped)
        {
            return escaped switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '0' => '\0',
                _ => escaped,
            };
        }
    }
}
