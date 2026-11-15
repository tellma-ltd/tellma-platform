// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Testing.Resources
{
    /// <summary>
    ///     Collects the string literals of TypeScript source: single- and double-quoted strings and
    ///     template literals, skipping comments and regular-expression literals. A template literal
    ///     yields each text run between its <c>${…}</c> holes, and the holes are scanned as code.
    /// </summary>
    /// <param name="text">The source text.</param>
    internal sealed class TypeScriptLiteralScanner(string text)
    {
        /// <summary>The keywords after which a <c>/</c> starts a regular expression rather than a division.</summary>
        private static readonly HashSet<string> RegexKeywords = new(StringComparer.Ordinal)
        {
            "return", "typeof", "case", "do", "else", "in", "of", "new", "delete", "void", "throw", "yield", "await", "instanceof",
        };

        private readonly List<string> _literals = [];
        private int _index;

        /// <summary>The last significant character of code read, which decides what a <c>/</c> starts; NUL at the start.</summary>
        private char _last;

        /// <summary>The last word of code read, when it was the last significant token.</summary>
        private string? _lastWord;

        /// <summary>The literals of a source text, in source order.</summary>
        /// <param name="text">The source text.</param>
        /// <returns>The literals' contents.</returns>
        public static IReadOnlyList<string> Scan(string text)
        {
            TypeScriptLiteralScanner scanner = new(text);
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

        /// <summary>Scans code: to the end of the text, or to the brace that closes a template hole.</summary>
        /// <param name="untilBrace">Whether to stop before an unmatched closing brace, which ends a hole.</param>
        private void Code(bool untilBrace)
        {
            int depth = 0;
            while (_index < text.Length)
            {
                char c = text[_index];

                // --- White space, which changes nothing ---
                if (char.IsWhiteSpace(c))
                {
                    _index++;
                }

                // --- Comments, then a slash that starts a regular expression or divides ---
                else if (c == '/' && Peek(1) == '/')
                {
                    SkipPast("\n");
                }
                else if (c == '/' && Peek(1) == '*')
                {
                    _index += 2;
                    SkipPast("*/");
                }
                else if (c == '/' && StartsRegex())
                {
                    SkipRegex();
                }

                // --- Strings and template literals ---
                else if (c is '\'' or '"')
                {
                    QuotedString(c);
                }
                else if (c == '`')
                {
                    Template();
                }

                // --- Words, which a keyword among them makes a following slash a regular expression ---
                else if (IsWordCharacter(c))
                {
                    int start = _index;
                    while (IsWordCharacter(Peek()))
                    {
                        _index++;
                    }

                    _lastWord = text[start.._index];
                    _last = 'a';
                }

                // --- Braces: an unmatched closing one ends a hole ---
                else if (c == '}' && untilBrace && depth == 0)
                {
                    return;
                }
                else
                {
                    depth += c switch
                    {
                        '{' => 1,
                        '}' => -1,
                        _ => 0,
                    };

                    _last = c;
                    _lastWord = null;
                    _index++;
                }
            }
        }

        /// <summary>Whether a character belongs to an identifier, a keyword or a number.</summary>
        /// <param name="c">The character.</param>
        /// <returns>True for a word character.</returns>
        private static bool IsWordCharacter(char c)
        {
            return char.IsLetterOrDigit(c) || c is '_' or '$';
        }

        /// <summary>Whether a slash here starts a regular expression: after an operator, an opening bracket, a separator or a keyword, or at the start.</summary>
        /// <returns>True for a regular expression.</returns>
        private bool StartsRegex()
        {
            return _lastWord is not null
                ? RegexKeywords.Contains(_lastWord)
                : _last is '\0' or '(' or ',' or '=' or ':' or '[' or '!' or '&' or '|' or '?' or '{' or '}' or ';' or '+' or '-' or '*' or '%' or '<' or '>' or '~' or '^';
        }

        /// <summary>Moves past the next occurrence of a marker, or to the end of the text.</summary>
        /// <param name="marker">The marker.</param>
        private void SkipPast(string marker)
        {
            int found = text.IndexOf(marker, _index, StringComparison.Ordinal);
            _index = found < 0 ? text.Length : found + marker.Length;
        }

        /// <summary>Skips a regular-expression literal: to its closing slash outside a character class, then its flags.</summary>
        private void SkipRegex()
        {
            _index++;
            bool inClass = false;
            while (_index < text.Length && text[_index] != '\n')
            {
                char c = text[_index];
                if (c == '\\')
                {
                    _index += 2;
                    continue;
                }

                _index++;
                if (c == '[')
                {
                    inClass = true;
                }
                else if (c == ']')
                {
                    inClass = false;
                }
                else if (c == '/' && !inClass)
                {
                    break;
                }
            }

            while (char.IsAsciiLetter(Peek()))
            {
                _index++;
            }

            _last = 'a';
            _lastWord = null;
        }

        /// <summary>Reads a single- or double-quoted string, from its opening quote.</summary>
        /// <param name="quote">The quote that opens and closes it.</param>
        private void QuotedString(char quote)
        {
            _index++;
            StringBuilder run = new();
            while (_index < text.Length)
            {
                char c = text[_index];
                if (c == quote)
                {
                    _index++;
                    break;
                }

                if (c == '\\')
                {
                    _ = run.Append(Peek(1));
                    _index += 2;
                    continue;
                }

                if (c == '\n')
                {
                    // An unterminated string: resynchronize at the next line rather than swallow the file.
                    break;
                }

                _ = run.Append(c);
                _index++;
            }

            _literals.Add(run.ToString());
            _last = '"';
            _lastWord = null;
        }

        /// <summary>Reads a template literal, from its opening backtick, its holes scanned as code.</summary>
        private void Template()
        {
            _index++;
            StringBuilder run = new();
            while (_index < text.Length)
            {
                char c = text[_index];
                if (c == '`')
                {
                    _index++;
                    break;
                }

                if (c == '\\')
                {
                    _ = run.Append(Peek(1));
                    _index += 2;
                    continue;
                }

                if (c == '$' && Peek(1) == '{')
                {
                    _literals.Add(run.ToString());
                    _ = run.Clear();
                    _index += 2;

                    // The hole is code of its own, with its own idea of what a slash starts.
                    (char last, string? lastWord) = (_last, _lastWord);
                    (_last, _lastWord) = ('\0', null);
                    Code(untilBrace: true);
                    (_last, _lastWord) = (last, lastWord);
                    _index++;
                    continue;
                }

                _ = run.Append(c);
                _index++;
            }

            _literals.Add(run.ToString());
            _last = '"';
            _lastWord = null;
        }
    }
}
