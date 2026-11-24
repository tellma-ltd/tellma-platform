// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;

namespace Tellma.Queryex.Testing.Client
{
    /// <summary>A range of a text, in UTF-16 code units.</summary>
    /// <param name="Start">Where it begins.</param>
    /// <param name="Length">How many code units it covers.</param>
    public sealed record ClientSpan(int Start, int Length);

    /// <summary>One token the engine read, in the form the client's lexer reports it.</summary>
    /// <param name="Kind">
    ///     <c>number</c>, <c>string</c>, <c>identifier</c>, <c>keyword</c>, <c>parameter</c>,
    ///     <c>punctuator</c>, or <c>error</c> for a run that could not be read.
    /// </param>
    /// <param name="Start">Where it begins.</param>
    /// <param name="Length">How many code units it covers.</param>
    /// <param name="Text">
    ///     What it denotes: an identifier's name without brackets, a string's value with its doubled
    ///     quotes collapsed, a parameter's name without its marker, and the text as written for a
    ///     number, a keyword, a punctuator or an unreadable run.
    /// </param>
    /// <param name="Call">True for an identifier the next token opens a call after; null otherwise.</param>
    public sealed record ClientToken(string Kind, int Start, int Length, string Text, bool? Call);

    /// <summary>One parameter of a text, grouped as the engine matches parameter names.</summary>
    /// <param name="Name">The first spelling, without the marker.</param>
    /// <param name="Sites">Each occurrence's whole token, marker included, in order.</param>
    public sealed record ClientParameter(string Name, IReadOnlyList<ClientSpan> Sites);

    /// <summary>One problem the engine's lexer reported.</summary>
    /// <param name="Code">The diagnostic code, such as <c>QX1001</c>.</param>
    /// <param name="Start">Where the problem begins.</param>
    /// <param name="Length">How many code units it covers.</param>
    public sealed record ClientDiagnostic(string Code, int Start, int Length);

    /// <summary>What the engine's lexer makes of one text.</summary>
    /// <param name="Text">The text.</param>
    /// <param name="Tokens">The tokens it read and the runs it could not, in order.</param>
    /// <param name="Parameters">The parameters among the tokens, grouped by name.</param>
    /// <param name="Diagnostics">The lexical problems, in order.</param>
    public sealed record ClientLexerCase(
        string Text,
        IReadOnlyList<ClientToken> Tokens,
        IReadOnlyList<ClientParameter> Parameters,
        IReadOnlyList<ClientDiagnostic> Diagnostics);

    /// <summary>
    ///     The texts the client's Queryex lexer is held to, and what the engine's own lexer makes of
    ///     each.
    /// </summary>
    /// <remarks>
    ///     The client lexes for highlighting, parameters and safe insertion, and never parses: it
    ///     must therefore read every text exactly as the engine does, down to where a malformed run
    ///     ends. The engine writes the corpus, so a change to the engine's lexer shows up as a stale
    ///     corpus here and as a failing client suite once the corpus is regenerated.
    /// </remarks>
    public static class ClientLexerCorpus
    {
        /// <summary>Every text, in a stable order.</summary>
        public static IReadOnlyList<string> Texts { get; } =
        [
            // --- Identifiers and keywords (spec 0008 §4.2) ---
            "Name",
            "_x1",
            "Notes Ordering Internal",
            "a1.b2.c3",
            "Name2",
            "and or not in is null true false asc desc",
            "AND Or nOt IN iS NULL True FALSE ASC Desc",
            "\u0131n \u0131s \u017F",
            "i\u017F a\u017Fc de\u017Fc",
            "\u0130S \u0130N",
            "\u212Aey",
            "android island notes",
            "ناصر.الاسم",
            "名前 = 1",
            "\U0001D4B3y",
            "x\u0301",
            "x\u0662",

            // --- Bracketed identifiers ---
            "[not]",
            "[Order].[in]",
            "[a b] + [c-d]",
            "[]",
            "[unterminated",
            "[a]]",
            "[[a]",
            "[\u0131n]",

            // --- Numbers (spec 0008 §4.3) ---
            "1",
            "0",
            "3.14",
            "0.500",
            "007",
            ".5",
            "5.",
            "1e3",
            "0x0A",
            "1_000",
            "1.5.6",
            "12abc",
            "1\u0662",
            "\u0661\u0662",
            "12345678901234567890123456789012345678",
            "123456789012345678901234567890123456789",
            "0.00000000000000000000000000000000000001",
            "1.000000000000000000000000000000000000001",
            "a.5",
            "1.a",
            "1\U0001D4B3",
            "1\U0001F600",

            // --- Strings (spec 0008 §4.4) ---
            "'Hello'",
            "''",
            "'It''s fine'",
            "'back\\slash'",
            "'a@b'",
            "'line\nbreak'",
            "'unterminated",
            "'ends with a quote'''",
            "''''",
            "'a' 'b'",
            "'x''",

            // --- Parameters (spec 0008 §4.5) ---
            "@From",
            "@From = @from and @FROM > @to",
            "@[order]",
            "@[Order] = @order",
            "@",
            "@ x",
            "@1",
            "@[",
            "@[]",
            "@@x",
            "@_",
            "'@x' = @x",
            "@ن",

            // --- Punctuators (spec 0008 §4.6) ---
            "!= <= >= || = < > + - * / % ( ) , .",
            "a<=b>=c!=d||e",
            "!",
            "|",
            "a & b",
            "#",
            "a; b",
            "{x}",
            "\"quoted\"",
            "a..b",
            "a.",
            "-1",
            "--1",
            "<>",
            "=>",
            "=<",

            // --- Whitespace (spec 0008 §4.1) ---
            "a\tb\nc\rd",
            "a\u00A0b\u2003c\u2028d\u3000e",
            "a\u200Bb",
            "a\uFEFFb",
            "a\u0085b",
            "  ",
            "",

            // --- Surrogates ---
            "\uD800",
            "\uDC00",
            "a\uD800b",
            "\U0001F600",

            // --- Calls and paths (spec 0008 §5.3) ---
            "contains(Name, 'jed')",
            "today()",
            "f (x)",
            "[Count](1)",
            "a.f(b)",
            "count()",
            "x(",

            // --- Whole expressions ---
            "Invoice.PostingDate >= @From and Accnt.Code = '1101'",
            "CreditLimit >= 100000 and CreditLimit <= 500000",
            "contains(Name, 'O''Neil') or Code = '7'",
            "Name asc, Code desc",
            "not a = b",
            "a or b and c",
            "CenterType in ('Service', 'Sale')",
            "DueDate is not null",
            "-a * b",
            "x # y $ z",
            "'a' = 'b' and 12x = 3",
        ];

        /// <summary>Lexes one text with the engine's lexer.</summary>
        /// <param name="text">The text.</param>
        /// <returns>What the engine read, and the problems it reported.</returns>
        public static ClientLexerCase Lex(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            // --- The engine's own lexer, under its default ceilings ---
            DiagnosticSink sink = new();
            DiagnosticScope scope = sink.Scope(DiagnosticLocation.None);
            List<QueryexSpan> unreadable = [];
            Lexer.TryTokenize(text, new ParseBudget(QueryexLimits.Default), scope, out Token[] tokens, unreadable);

            // --- Each token in the client's terms; the end-of-input marker is not one ---
            List<ClientToken> rendered = [];
            for (int index = 0; index < tokens.Length; index++)
            {
                Token token = tokens[index];
                if (token.Kind == TokenKind.EndOfInput)
                {
                    continue;
                }

                // An identifier is a call's name exactly when the next token opens its arguments,
                // the one rule the parser consults (spec 0008 §5.3).
                bool call = token.Kind == TokenKind.Identifier
                    && index + 1 < tokens.Length
                    && tokens[index + 1].Kind == TokenKind.OpenParen;

                rendered.Add(new ClientToken(
                    KindOf(token.Kind),
                    token.Span.Start,
                    token.Span.Length,
                    token.Kind is TokenKind.Identifier or TokenKind.String or TokenKind.Parameter
                        ? token.Text!
                        : text.Substring(token.Span.Start, token.Span.Length),
                    call ? true : null));
            }

            // --- The unreadable runs, merged in among the tokens by position ---
            foreach (QueryexSpan run in unreadable)
            {
                rendered.Add(new ClientToken("error", run.Start, run.Length, text.Substring(run.Start, run.Length), null));
            }

            rendered.Sort(static (first, second) => first.Start.CompareTo(second.Start));

            // --- Parameters grouped by name as the engine matches them: ignoring case, first spelling ---
            List<(string Name, List<ClientSpan> Sites)> groups = [];
            foreach (Token token in tokens)
            {
                if (token.Kind != TokenKind.Parameter)
                {
                    continue;
                }

                ClientSpan site = new(token.Span.Start, token.Span.Length);
                int group = groups.FindIndex(candidate => string.Equals(candidate.Name, token.Text, StringComparison.OrdinalIgnoreCase));
                if (group < 0)
                {
                    groups.Add((token.Text!, [site]));
                }
                else
                {
                    groups[group].Sites.Add(site);
                }
            }

            // --- The problems, in the order they were reported, which is the order of the text ---
            List<ClientDiagnostic> diagnostics = [];
            foreach (QueryexDiagnostic diagnostic in sink.Drain())
            {
                diagnostics.Add(new ClientDiagnostic(diagnostic.Code, diagnostic.Span.Start, diagnostic.Span.Length));
            }

            return new ClientLexerCase(
                text,
                rendered,
                [.. groups.Select(static group => new ClientParameter(group.Name, group.Sites))],
                diagnostics);
        }

        /// <summary>The client's name for a token kind.</summary>
        /// <param name="kind">The engine's token kind.</param>
        /// <returns>The client's token kind.</returns>
        private static string KindOf(TokenKind kind)
        {
            return TokenFacts.Category(kind) switch
            {
                TokenCategory.Number => "number",
                TokenCategory.String => "string",
                TokenCategory.Identifier => "identifier",
                TokenCategory.Parameter => "parameter",
                TokenCategory.Keyword => "keyword",
                TokenCategory.Punctuator => "punctuator",
                TokenCategory.EndOfInput => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The end of the input is not a token the client reports."),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a token kind."),
            };
        }
    }
}
