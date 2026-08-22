// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;

namespace Tellma.Core.Queryex.Lexing
{
    /// <summary>
    ///     The kind of one token.
    /// </summary>
    /// <remarks>
    ///     Fine-grained down to the individual keyword and punctuator, so the parser switches on one
    ///     value rather than on a kind plus its text. The coarser grouping a reader expects is
    ///     recovered through <see cref="TokenFacts.Category" />.
    /// </remarks>
    internal enum TokenKind
    {
        /// <summary>The end of the input.</summary>
        EndOfInput,

        /// <summary>A numeric literal.</summary>
        Number,

        /// <summary>A string literal.</summary>
        String,

        /// <summary>An identifier that is not a reserved word.</summary>
        Identifier,

        /// <summary>A named parameter.</summary>
        Parameter,

        /// <summary>The <c>and</c> keyword.</summary>
        And,

        /// <summary>The <c>or</c> keyword.</summary>
        Or,

        /// <summary>The <c>not</c> keyword.</summary>
        Not,

        /// <summary>The <c>in</c> keyword.</summary>
        In,

        /// <summary>The <c>is</c> keyword.</summary>
        Is,

        /// <summary>The <c>null</c> keyword.</summary>
        Null,

        /// <summary>The <c>true</c> keyword.</summary>
        True,

        /// <summary>The <c>false</c> keyword.</summary>
        False,

        /// <summary>The <c>asc</c> keyword.</summary>
        Asc,

        /// <summary>The <c>desc</c> keyword.</summary>
        Desc,

        /// <summary>The inequality operator.</summary>
        BangEquals,

        /// <summary>The less-or-equal operator.</summary>
        LessOrEqual,

        /// <summary>The greater-or-equal operator.</summary>
        GreaterOrEqual,

        /// <summary>The concatenation operator.</summary>
        BarBar,

        /// <summary>The equality operator.</summary>
        EqualsToken,

        /// <summary>The less-than operator.</summary>
        Less,

        /// <summary>The greater-than operator.</summary>
        Greater,

        /// <summary>The addition operator.</summary>
        Plus,

        /// <summary>The subtraction and negation operator.</summary>
        Minus,

        /// <summary>The multiplication operator.</summary>
        Asterisk,

        /// <summary>The division operator.</summary>
        Slash,

        /// <summary>The remainder operator.</summary>
        Percent,

        /// <summary>An opening parenthesis.</summary>
        OpenParen,

        /// <summary>A closing parenthesis.</summary>
        CloseParen,

        /// <summary>A comma.</summary>
        Comma,

        /// <summary>A path separator.</summary>
        Dot,
    }

    /// <summary>The coarse grouping of token kinds, as a reader of the language sees them.</summary>
    internal enum TokenCategory
    {
        /// <summary>The end of the input.</summary>
        EndOfInput,

        /// <summary>A numeric literal.</summary>
        Number,

        /// <summary>A string literal.</summary>
        String,

        /// <summary>An identifier.</summary>
        Identifier,

        /// <summary>A reserved word.</summary>
        Keyword,

        /// <summary>A named parameter.</summary>
        Parameter,

        /// <summary>An operator or separator.</summary>
        Punctuator,
    }

    /// <summary>
    ///     One lexical token.
    /// </summary>
    /// <remarks>
    ///     Carries the value it denotes rather than the text it was written as: an identifier's
    ///     brackets and a string's doubled quotes are gone by the time the parser sees it, so no
    ///     later stage has to know how a value was spelled.
    /// </remarks>
    internal readonly struct Token
    {
        /// <summary>Initializes a token.</summary>
        /// <param name="kind">The token kind.</param>
        /// <param name="span">The token's range in the input.</param>
        /// <param name="text">The denoted text, for identifiers, strings, and parameters.</param>
        /// <param name="value">The denoted number, for numeric literals.</param>
        /// <param name="precision">The literal's significant-digit count.</param>
        /// <param name="scale">The literal's written scale.</param>
        /// <param name="wasBracketed">Whether an identifier was written in brackets.</param>
        internal Token(
            TokenKind kind,
            QueryexSpan span,
            string? text = null,
            SqlDecimal value = default,
            byte precision = 0,
            byte scale = 0,
            bool wasBracketed = false)
        {
            Kind = kind;
            Span = span;
            Text = text;
            Value = value;
            Precision = precision;
            Scale = scale;
            WasBracketed = wasBracketed;
        }

        /// <summary>The token kind.</summary>
        internal TokenKind Kind { get; }

        /// <summary>The token's range in the input.</summary>
        internal QueryexSpan Span { get; }

        /// <summary>
        ///     The denoted text: an identifier's name without its brackets, a string literal's value
        ///     with its doubled quotes collapsed, or a parameter's name without its marker. Null for
        ///     every other kind.
        /// </summary>
        internal string? Text { get; }

        /// <summary>The denoted number, for a numeric literal.</summary>
        internal SqlDecimal Value { get; }

        /// <summary>
        ///     The literal's significant-digit count, following the backend's own reading: a lone
        ///     leading zero before the point does not count.
        /// </summary>
        internal byte Precision { get; }

        /// <summary>
        ///     The literal's written scale, which is retained rather than normalized because it
        ///     participates in result-type reasoning and in how the value is bound.
        /// </summary>
        internal byte Scale { get; }

        /// <summary>Whether an identifier was written in brackets. Affects nothing but display.</summary>
        internal bool WasBracketed { get; }
    }

    /// <summary>Facts about token kinds.</summary>
    internal static class TokenFacts
    {
        /// <summary>The coarse grouping a token kind belongs to.</summary>
        /// <param name="kind">The token kind.</param>
        /// <returns>Its category.</returns>
        internal static TokenCategory Category(TokenKind kind)
        {
            return kind switch
            {
                TokenKind.EndOfInput => TokenCategory.EndOfInput,
                TokenKind.Number => TokenCategory.Number,
                TokenKind.String => TokenCategory.String,
                TokenKind.Identifier => TokenCategory.Identifier,
                TokenKind.Parameter => TokenCategory.Parameter,
                TokenKind.And => TokenCategory.Keyword,
                TokenKind.Or => TokenCategory.Keyword,
                TokenKind.Not => TokenCategory.Keyword,
                TokenKind.In => TokenCategory.Keyword,
                TokenKind.Is => TokenCategory.Keyword,
                TokenKind.Null => TokenCategory.Keyword,
                TokenKind.True => TokenCategory.Keyword,
                TokenKind.False => TokenCategory.Keyword,
                TokenKind.Asc => TokenCategory.Keyword,
                TokenKind.Desc => TokenCategory.Keyword,
                TokenKind.BangEquals => TokenCategory.Punctuator,
                TokenKind.LessOrEqual => TokenCategory.Punctuator,
                TokenKind.GreaterOrEqual => TokenCategory.Punctuator,
                TokenKind.BarBar => TokenCategory.Punctuator,
                TokenKind.EqualsToken => TokenCategory.Punctuator,
                TokenKind.Less => TokenCategory.Punctuator,
                TokenKind.Greater => TokenCategory.Punctuator,
                TokenKind.Plus => TokenCategory.Punctuator,
                TokenKind.Minus => TokenCategory.Punctuator,
                TokenKind.Asterisk => TokenCategory.Punctuator,
                TokenKind.Slash => TokenCategory.Punctuator,
                TokenKind.Percent => TokenCategory.Punctuator,
                TokenKind.OpenParen => TokenCategory.Punctuator,
                TokenKind.CloseParen => TokenCategory.Punctuator,
                TokenKind.Comma => TokenCategory.Punctuator,
                TokenKind.Dot => TokenCategory.Punctuator,
                _ => TokenCategory.Punctuator,
            };
        }

        /// <summary>The canonical spelling of a token kind, for diagnostics and for printing.</summary>
        /// <param name="kind">The token kind.</param>
        /// <returns>The spelling, or a category name for the kinds that carry a value.</returns>
        internal static string Spelling(TokenKind kind)
        {
            return kind switch
            {
                TokenKind.EndOfInput => "end of input",
                TokenKind.Number => "number",
                TokenKind.String => "string",
                TokenKind.Identifier => "identifier",
                TokenKind.Parameter => "parameter",
                TokenKind.And => "and",
                TokenKind.Or => "or",
                TokenKind.Not => "not",
                TokenKind.In => "in",
                TokenKind.Is => "is",
                TokenKind.Null => "null",
                TokenKind.True => "true",
                TokenKind.False => "false",
                TokenKind.Asc => "asc",
                TokenKind.Desc => "desc",
                TokenKind.BangEquals => "!=",
                TokenKind.LessOrEqual => "<=",
                TokenKind.GreaterOrEqual => ">=",
                TokenKind.BarBar => "||",
                TokenKind.EqualsToken => "=",
                TokenKind.Less => "<",
                TokenKind.Greater => ">",
                TokenKind.Plus => "+",
                TokenKind.Minus => "-",
                TokenKind.Asterisk => "*",
                TokenKind.Slash => "/",
                TokenKind.Percent => "%",
                TokenKind.OpenParen => "(",
                TokenKind.CloseParen => ")",
                TokenKind.Comma => ",",
                TokenKind.Dot => ".",
                _ => "?",
            };
        }
    }
}
