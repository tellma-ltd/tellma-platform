// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Data.SqlTypes;
using System.Text;
using Tellma.Core.Queryex.Diagnostics;

namespace Tellma.Core.Queryex.Lexing
{
    /// <summary>
    ///     Turns expression text into tokens.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Maximal munch: at each position the longest token that matches wins. There are no
    ///         comments, and whitespace is insignificant except inside a string literal.
    ///     </para>
    ///     <para>
    ///         Lexing reports as many problems as it can rather than stopping at the first: an
    ///         unexpected character is skipped and scanning continues, so a person fixing a stored
    ///         expression sees every bad character at once.
    ///     </para>
    /// </remarks>
    internal static class Lexer
    {
        /// <summary>The backend's decimal domain, and therefore the language's.</summary>
        private const int MaxSignificantDigits = 38;

        /// <summary>Scans the whole input.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="budget">The ceilings for this call site.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="tokens">The tokens, ending with an end-of-input token, when scanning succeeded.</param>
        /// <returns>True when the whole input scanned without a problem.</returns>
        internal static bool TryTokenize(
            string text,
            ParseBudget budget,
            in DiagnosticScope scope,
            out Token[] tokens)
        {
            tokens = [];
            if (!budget.TryAcceptInput(text, scope))
            {
                return false;
            }

            List<Token> scanned = [];
            bool healthy = true;
            int index = 0;

            while (index < text.Length)
            {
                char current = text[index];
                if (char.IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }

                int start = index;
                Token token;

                switch (current)
                {
                    case '\'':
                        if (!TryScanString(text, ref index, scope, out token))
                        {
                            healthy = false;
                            continue;
                        }

                        break;

                    case '[':
                        if (!TryScanBracketedIdentifier(text, ref index, scope, out token))
                        {
                            healthy = false;
                            continue;
                        }

                        break;

                    case '@':
                        if (!TryScanParameter(text, ref index, scope, out token))
                        {
                            healthy = false;
                            continue;
                        }

                        break;

                    default:
                        if (IsIdentifierStart(text, index, out int startWidth))
                        {
                            token = ScanIdentifier(text, ref index, startWidth);
                        }
                        else if (IsPlainDigit(current))
                        {
                            if (!TryScanNumber(text, ref index, scope, out token))
                            {
                                healthy = false;
                                continue;
                            }
                        }
                        else if (!TryScanPunctuator(text, ref index, scope, out token))
                        {
                            healthy = false;
                            continue;
                        }

                        break;
                }

                if (!budget.TryConsumeToken(token.Span, scope))
                {
                    return false;
                }

                scanned.Add(token);

                // Every scanner has to make progress, or a malformed input would spin forever.
                if (index <= start)
                {
                    throw new InvalidOperationException("The lexer failed to advance.");
                }
            }

            scanned.Add(new Token(TokenKind.EndOfInput, new QueryexSpan(text.Length, 0)));
            if (!healthy)
            {
                return false;
            }

            tokens = [.. scanned];
            return true;
        }

        /// <summary>Whether a character is one of the ten digits a number is written with.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True when it is.</returns>
        /// <remarks>
        ///     Not the platform's own digit test, which admits every decimal digit Unicode defines.
        ///     A number is read by taking each digit's distance from zero, so a digit from another
        ///     script would be read as a value in the thousands and overflow the conversion — and an
        ///     arithmetic failure escaping on something a user typed is exactly what this engine
        ///     promises never to do. Anything else is an unexpected character, reported as one.
        /// </remarks>
        private static bool IsPlainDigit(char character)
        {
            return character is >= '0' and <= '9';
        }

        /// <summary>Whether the character at an index begins an identifier.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The index to test.</param>
        /// <param name="width">The number of characters the starting rune occupies.</param>
        /// <returns>True when an identifier starts here.</returns>
        private static bool IsIdentifierStart(string text, int index, out int width)
        {
            if (text[index] == '_')
            {
                width = 1;
                return true;
            }

            return IsRune(text, index, static rune => Rune.IsLetter(rune), out width);
        }

        /// <summary>Whether the character at an index continues an identifier.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The index to test.</param>
        /// <param name="width">The number of characters the rune occupies.</param>
        /// <returns>True when an identifier continues here.</returns>
        private static bool IsIdentifierPart(string text, int index, out int width)
        {
            if (text[index] == '_')
            {
                width = 1;
                return true;
            }

            return IsRune(text, index, static rune => Rune.IsLetter(rune) || Rune.IsDigit(rune), out width);
        }

        /// <summary>
        ///     Decodes the rune at an index and tests it.
        /// </summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The index to decode at.</param>
        /// <param name="predicate">The test to apply.</param>
        /// <param name="width">The number of characters the rune occupies.</param>
        /// <returns>True when the rune decoded and passed the test.</returns>
        /// <remarks>
        ///     Decoded as a rune rather than tested as a single character so that letters outside the
        ///     basic plane are identifiers rather than unexpected characters. A lone surrogate is
        ///     never a letter, which is what the robustness tests rely on.
        /// </remarks>
        private static bool IsRune(string text, int index, Func<Rune, bool> predicate, out int width)
        {
            OperationStatus status = Rune.DecodeFromUtf16(text.AsSpan(index), out Rune rune, out width);
            if (status != OperationStatus.Done)
            {
                width = 1;
                return false;
            }

            return predicate(rune);
        }

        /// <summary>Scans an unbracketed identifier, reclassifying it if it is a reserved word.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the identifier.</param>
        /// <param name="startWidth">The width of the identifier's first rune.</param>
        /// <returns>The token.</returns>
        private static Token ScanIdentifier(string text, ref int index, int startWidth)
        {
            int start = index;
            index += startWidth;
            while (index < text.Length && IsIdentifierPart(text, index, out int width))
            {
                index += width;
            }

            string name = text[start..index];
            QueryexSpan span = new(start, index - start);

            return Keywords.TryResolve(name, out TokenKind keyword)
                ? new Token(keyword, span)
                : new Token(TokenKind.Identifier, span, name);
        }

        /// <summary>Scans a bracketed identifier, which escapes every reserved word.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the identifier.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">The token, when scanning succeeded.</param>
        /// <returns>True when scanning succeeded.</returns>
        private static bool TryScanBracketedIdentifier(
            string text,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int start = index;
            int close = text.IndexOf(']', start + 1);
            if (close < 0)
            {
                scope.Report(
                    DiagnosticCodes.UnterminatedBracketedIdentifier,
                    new QueryexSpan(start, text.Length - start));
                index = text.Length;
                token = default;
                return false;
            }

            if (close == start + 1)
            {
                scope.Report(DiagnosticCodes.UnexpectedCharacter, new QueryexSpan(start, 2));
                index = close + 1;
                token = default;
                return false;
            }

            index = close + 1;
            token = new Token(
                TokenKind.Identifier,
                new QueryexSpan(start, index - start),
                text[(start + 1)..close],
                wasBracketed: true);

            return true;
        }

        /// <summary>Scans a named parameter.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the parameter.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">The token, when scanning succeeded.</param>
        /// <returns>True when scanning succeeded.</returns>
        private static bool TryScanParameter(
            string text,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int start = index;
            int nameStart = start + 1;

            if (nameStart < text.Length && text[nameStart] == '[')
            {
                int inner = nameStart;
                if (!TryScanBracketedIdentifier(text, ref inner, scope, out Token bracketed))
                {
                    index = inner;
                    token = default;
                    return false;
                }

                index = inner;
                token = new Token(
                    TokenKind.Parameter,
                    new QueryexSpan(start, index - start),
                    bracketed.Text,
                    wasBracketed: true);

                return true;
            }

            if (nameStart >= text.Length || !IsIdentifierStart(text, nameStart, out int startWidth))
            {
                scope.Report(DiagnosticCodes.UnexpectedCharacter, new QueryexSpan(start, 1));
                index = start + 1;
                token = default;
                return false;
            }

            int scan = nameStart + startWidth;
            while (scan < text.Length && IsIdentifierPart(text, scan, out int width))
            {
                scan += width;
            }

            index = scan;
            token = new Token(
                TokenKind.Parameter,
                new QueryexSpan(start, index - start),
                text[nameStart..scan]);

            return true;
        }

        /// <summary>Scans a string literal, collapsing its doubled quotes.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the literal.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">The token, when scanning succeeded.</param>
        /// <returns>True when scanning succeeded.</returns>
        /// <remarks>
        ///     No lexical analysis happens between the delimiters, and there is no escape mechanism
        ///     beyond the doubled quote: a backslash is an ordinary character, which is what keeps
        ///     the search predicates free of anything a pattern language would have to escape.
        /// </remarks>
        private static bool TryScanString(
            string text,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int start = index;
            StringBuilder value = new();
            int scan = start + 1;

            while (scan < text.Length)
            {
                if (text[scan] != '\'')
                {
                    value.Append(text[scan]);
                    scan++;
                    continue;
                }

                if (scan + 1 < text.Length && text[scan + 1] == '\'')
                {
                    value.Append('\'');
                    scan += 2;
                    continue;
                }

                index = scan + 1;
                token = new Token(TokenKind.String, new QueryexSpan(start, index - start), value.ToString());
                return true;
            }

            scope.Report(DiagnosticCodes.UnterminatedString, new QueryexSpan(start, text.Length - start));
            index = text.Length;
            token = default;
            return false;
        }

        /// <summary>Scans a numeric literal.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the literal.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">The token, when scanning succeeded.</param>
        /// <returns>True when scanning succeeded.</returns>
        private static bool TryScanNumber(
            string text,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int start = index;
            int scan = start;
            while (scan < text.Length && IsPlainDigit(text[scan]))
            {
                scan++;
            }

            int integerEnd = scan;
            int fractionStart = scan;
            int fractionEnd = scan;

            if (scan < text.Length && text[scan] == '.')
            {
                if (scan + 1 >= text.Length || !IsPlainDigit(text[scan + 1]))
                {
                    // A digit is required on both sides of the point, so a trailing point is a typo
                    // rather than a number followed by a path separator.
                    return FailNumber(text, start, scan + 1, ref index, scope, out token);
                }

                fractionStart = scan + 1;
                scan = fractionStart;
                while (scan < text.Length && IsPlainDigit(text[scan]))
                {
                    scan++;
                }

                fractionEnd = scan;
            }

            // Nothing that could begin an identifier may follow a number. That is what rejects the
            // shapes people reach for out of habit — exponents, hex, digit separators — instead of
            // silently reading them as a number followed by a name.
            if (scan < text.Length && (IsIdentifierStart(text, scan, out _) || char.IsDigit(text[scan])))
            {
                return FailNumber(text, start, scan, ref index, scope, out token);
            }

            int scale = fractionEnd - fractionStart;
            int firstSignificant = start;
            while (firstSignificant < integerEnd && text[firstSignificant] == '0')
            {
                firstSignificant++;
            }

            int precision = integerEnd - firstSignificant + scale;
            if (precision == 0)
            {
                precision = 1;
            }

            if (precision > MaxSignificantDigits)
            {
                scope.Report(DiagnosticCodes.NumberPrecisionExceeded, new QueryexSpan(start, scan - start));
                index = scan;
                token = default;
                return false;
            }

            UInt128 mantissa = UInt128.Zero;
            for (int digit = firstSignificant; digit < integerEnd; digit++)
            {
                mantissa = (mantissa * 10) + (uint)(text[digit] - '0');
            }

            for (int digit = fractionStart; digit < fractionEnd; digit++)
            {
                mantissa = (mantissa * 10) + (uint)(text[digit] - '0');
            }

            index = scan;
            token = new Token(
                TokenKind.Number,
                new QueryexSpan(start, scan - start),
                text: null,
                value: MakeDecimal(mantissa, (byte)precision, (byte)scale),
                precision: (byte)precision,
                scale: (byte)scale);

            return true;
        }

        /// <summary>Reports a malformed number and skips the whole run it was written as.</summary>
        /// <param name="text">The input.</param>
        /// <param name="start">Where the number began.</param>
        /// <param name="scan">Where the malformed part was noticed.</param>
        /// <param name="index">The scanning position, advanced past the run.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">Always the default.</param>
        /// <returns>Always false.</returns>
        private static bool FailNumber(
            string text,
            int start,
            int scan,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int end = scan;
            while (end < text.Length
                && (IsIdentifierPart(text, end, out _) || char.IsDigit(text[end]) || text[end] == '.'))
            {
                end++;
            }

            scope.Report(DiagnosticCodes.MalformedNumber, new QueryexSpan(start, end - start));
            index = Math.Max(end, start + 1);
            token = default;
            return false;
        }

        /// <summary>Builds an exact decimal from a digit mantissa.</summary>
        /// <param name="mantissa">The significant digits as one integer.</param>
        /// <param name="precision">The significant-digit count.</param>
        /// <param name="scale">The written scale.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        ///     Assembled from the digits rather than parsed from the text, so no culture setting can
        ///     change what a literal means, and the exact 38-digit domain the language promises is
        ///     preserved rather than narrowed to what a smaller decimal type can hold.
        /// </remarks>
        private static SqlDecimal MakeDecimal(UInt128 mantissa, byte precision, byte scale)
        {
            int low = (int)(uint)mantissa;
            int mid = (int)(uint)(mantissa >> 32);
            int high = (int)(uint)(mantissa >> 64);
            int top = (int)(uint)(mantissa >> 96);

            return new SqlDecimal(precision, scale, fPositive: true, low, mid, high, top);
        }

        /// <summary>Scans an operator or separator.</summary>
        /// <param name="text">The input.</param>
        /// <param name="index">The scanning position, advanced past the token.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="token">The token, when scanning succeeded.</param>
        /// <returns>True when scanning succeeded.</returns>
        private static bool TryScanPunctuator(
            string text,
            ref int index,
            in DiagnosticScope scope,
            out Token token)
        {
            int start = index;
            char current = text[start];
            char next = start + 1 < text.Length ? text[start + 1] : '\0';

            // Two-character operators are tried first, so the longest match wins.
            TokenKind? twoCharacter = (current, next) switch
            {
                ('!', '=') => TokenKind.BangEquals,
                ('<', '=') => TokenKind.LessOrEqual,
                ('>', '=') => TokenKind.GreaterOrEqual,
                ('|', '|') => TokenKind.BarBar,
                _ => null,
            };

            if (twoCharacter is TokenKind pair)
            {
                index = start + 2;
                token = new Token(pair, new QueryexSpan(start, 2));
                return true;
            }

            TokenKind? oneCharacter = current switch
            {
                '=' => TokenKind.EqualsToken,
                '<' => TokenKind.Less,
                '>' => TokenKind.Greater,
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Asterisk,
                '/' => TokenKind.Slash,
                '%' => TokenKind.Percent,
                '(' => TokenKind.OpenParen,
                ')' => TokenKind.CloseParen,
                ',' => TokenKind.Comma,
                _ => null,
            };

            if (oneCharacter is TokenKind single)
            {
                index = start + 1;
                token = new Token(single, new QueryexSpan(start, 1));
                return true;
            }

            if (current == '.')
            {
                // A point followed by a digit is an attempt at a number missing its leading digit,
                // not a path separator, and saying so is more useful than a grammar complaint.
                if (next != '\0' && char.IsDigit(next))
                {
                    return FailNumber(text, start, start + 1, ref index, scope, out token);
                }

                index = start + 1;
                token = new Token(TokenKind.Dot, new QueryexSpan(start, 1));
                return true;
            }

            // Skipped rather than abandoned, so a second bad character is reported in the same pass.
            scope.Report(DiagnosticCodes.UnexpectedCharacter, new QueryexSpan(start, 1));
            index = start + 1;
            token = default;
            return false;
        }
    }
}
