// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Queryex.Testing.Client
{
    /// <summary>One expression the client's printer is held to.</summary>
    /// <param name="Name">What the row shows.</param>
    /// <param name="Source">
    ///     The expression as written here, with whatever grouping makes the intended tree plain; the
    ///     golden holds the engine's own printing of it.
    /// </param>
    /// <param name="Root">
    ///     The fixture entity the expression is a filter on, so discovery can check that it reads
    ///     cleanly against a schema; null for a row about syntax alone.
    /// </param>
    public sealed record ClientPrintRow(string Name, string Source, string? Root = null);

    /// <summary>What the engine makes of one print row.</summary>
    /// <param name="Name">What the row shows.</param>
    /// <param name="Text">The engine's canonical printing of the expression.</param>
    /// <param name="Expression">The expression's tree, as the client's expression type spells it.</param>
    public sealed record ClientPrintCase(string Name, string Text, ClientJson Expression);

    /// <summary>
    ///     The expressions the client's Queryex printer is held to: each tree, and the text the
    ///     engine prints it as.
    /// </summary>
    /// <remarks>
    ///     The client builds the filters and order terms it writes as trees and prints them, so no
    ///     client code concatenates query text. Its printer must print every tree exactly as the
    ///     engine's canonical printer does — the same grouping, the same bracketing, the same
    ///     literal spelling — or a filter the client writes would read differently from the one the
    ///     engine shows back.
    /// </remarks>
    public static class ClientPrintGolden
    {
        /// <summary>The fixture entity the filter rows are written against.</summary>
        public const string Invoice = "Invoice";

        /// <summary>Every row, in a stable order.</summary>
        public static IReadOnlyList<ClientPrintRow> Rows { get; } =
        [
            // --- Grouping: looser children, equal-power right operands, comparisons in comparisons ---
            new("an equal-power right operand is grouped", "a - (b - c)"),
            new("an equal-power left operand is not", "(a - b) - c"),
            new("a looser operand of a product is grouped", "a * (b + c)"),
            new("a tighter operand of a sum is not", "(a * b) + c"),
            new("a sum under a negation is grouped", "-(a + b)"),
            new("a negation under a negation is not", "-(-a)"),
            new("a negation in a product is not grouped on the left", "(-a) * b"),
            new("a negation in a product is not grouped on the right", "a * (-b)"),
            new("a comparison under not is not grouped", "not (a = b)"),
            new("not under a comparison is grouped", "(not a) = b"),
            new("a comparison on the left of a comparison is grouped", "(a = b) = c"),
            new("a comparison on the right of a comparison is grouped", "a = (b = c)"),
            new("an or under an and is grouped", "(a or b) and c"),
            new("an and under an or is not", "a or (b and c)"),
            new("an and under not is grouped", "not (a and b)"),
            new("not under not is not", "not (not a)"),
            new("an and chain grouped to the left needs no parentheses", "(a and b) and c"),
            new("an and chain grouped to the right keeps them", "a and (b and c)"),
            new("a concatenation chain grouped to the left needs no parentheses", "(a || b) || c"),
            new("a sum on the right of a concatenation is grouped", "a || (b + c)"),
            new("a remainder, a quotient and a product read left to right", "((a % b) / c) * d"),
            new("a product on the right of a quotient is grouped", "a / (b * c)"),
            new("an in list takes sums bare and comparisons grouped", "(a + b) in (1, 2 + 3, (4 = 5))"),
            new("a comparison tested for membership is grouped", "(a = b) in (1)"),
            new("a sum tested for absence is not grouped", "(a + b) is null"),
            new("an and tested for presence is grouped", "(a and b) is not null"),
            new("a negated path", "-(a.b)"),
            new("call arguments are never grouped", "f((a or b), (-c))"),

            // --- Identifiers ---
            new("a reserved word in a path is bracketed", "[not].[in] = [desc]"),
            new("a name that is not reserved is not bracketed", "[Order] = 1"),
            new("a name with a space or a leading digit is bracketed", "[a b] = [1a]"),
            new("an underscore name is not bracketed", "[_x1] = 1"),
            new("an Arabic name is not bracketed", "[ناصر].[الاسم] = 1"),
            new("a name with a combining mark is bracketed", "[x\u0301] = 1"),
            new("a name with a letter beyond the basic plane is bracketed", "[\U0001D4B3] = 1"),
            new("a parameter", "@From = 1"),
            new("a parameter named by a reserved word", "@[order] = @[in]"),
            new("a parameter with a space", "@[a b] = 1"),

            // --- Literals ---
            new("a string with a quote", "Name = 'O''Neil'"),
            new("an empty string", "Name = ''"),
            new("a string that looks like a parameter", "Name = 'a@b'"),
            new("a string with a line break", "Name = 'line\nbreak'"),
            new("a number keeps its trailing zeros", "x = 0.500"),
            new("a number keeps its leading zeros", "x = 007"),
            new("a negative number is a negation", "x = -1.5"),
            new("true, false and null", "a = true or b = false or c = null"),

            // --- Calls ---
            new("a call", "contains(Name, 'jed')"),
            new("a call with no arguments", "today()"),
            new("a call with three arguments", "coalesce(a, b, 0)"),

            // --- The worked examples ---
            new("a numeric between", "CreditLimit >= 100000 and CreditLimit <= 500000"),
            new("an escaped string beside a code", "contains(Name, 'O''Neil') or Code = '7'"),

            // --- Filter leaves, against the fixture schema ---
            new("text contains, over two slots", "contains(Memo, 'jed') or contains(Notes, 'jed')", Invoice),
            new("text starts with, over two slots", "startsWith(Memo, 'jed') or startsWith(Notes, 'jed')", Invoice),
            new("text ends with, over two slots", "endsWith(Memo, 'jed') or endsWith(Notes, 'jed')", Invoice),
            new("text equals, over two slots", "Memo = 'x' or Notes = 'x'", Invoice),
            new("text differs, over two slots", "Memo != 'x' and Notes != 'x'", Invoice),
            new("number between", "Amount >= 100000 and Amount <= 500000", Invoice),
            new("number less than a negative", "Amount < -5", Invoice),
            new("number at most a fraction", "Amount <= 0.25", Invoice),
            new("number equals", "Amount = 12", Invoice),
            new("number at least", "Amount >= 1", Invoice),
            new("number greater than", "Amount > 2.5", Invoice),
            new("date between", "PostingDate >= '2026-01-01' and PostingDate <= '2026-01-31'", Invoice),
            new("date before", "PostingDate < '2026-03-01'", Invoice),
            new("date after", "PostingDate > '2026-03-01'", Invoice),
            new("date-time between", "DueOn >= '2026-01-01T00:00:00' and DueOn < '2026-02-01T00:00:00'", Invoice),
            new("date-time before", "DueOn < '2026-03-01T00:00:00'", Invoice),
            new("date-time after", "DueOn >= '2026-03-02T00:00:00'", Invoice),
            new("instant between", "PostedAt >= '2025-12-31T21:00:00+00:00' and PostedAt < '2026-01-31T21:00:00+00:00'", Invoice),
            new("instant before", "PostedAt < '2026-02-28T21:00:00+00:00'", Invoice),
            new("instant after", "PostedAt >= '2026-03-01T21:00:00+00:00'", Invoice),
            new("enum in", "Gender in ('M', 'F')", Invoice),
            new("bool is true", "IsPosted = true", Invoice),
            new("bool is false", "IsApproved = false", Invoice),
            new("navigation in", "CustomerId in (1012, 1015)", Invoice),
            new("navigation in, through a navigation", "Customer.ManagerId in (7)", Invoice),
            new("absent", "DueDate is null", Invoice),
            new("present, through a navigation", "Customer.ManagerId is not null", Invoice),
            new("leaves joined", "(contains(Memo, 'jed') or contains(Notes, 'jed')) and Amount > 0", Invoice),
        ];

        /// <summary>Parses one row and renders its tree and canonical text.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The case the golden holds for it.</returns>
        /// <exception cref="InvalidOperationException">The row does not parse as one expression.</exception>
        public static ClientPrintCase Render(ClientPrintRow row)
        {
            ArgumentNullException.ThrowIfNull(row);

            // --- The engine's own lexer and parser, under its default ceilings ---
            DiagnosticSink sink = new();
            DiagnosticScope scope = sink.Scope(DiagnosticLocation.None);
            ParseBudget budget = new(QueryexLimits.Default);
            if (!Lexer.TryTokenize(row.Source, budget, scope, out Token[] tokens)
                || !Parser.TryParse(row.Source, tokens, budget, scope, out ExpressionListSyntax list)
                || list.Items.Count != 1
                || list.Items[0].Direction != QueryexDirection.None)
            {
                throw new InvalidOperationException($"The print row '{row.Name}' does not parse as one expression.");
            }

            SyntaxNode expression = list.Items[0].Expression;
            return new ClientPrintCase(row.Name, SyntaxPrinter.Print(expression), TreeOf(expression));
        }

        /// <summary>Spells a syntax tree as the client's expression type does.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The node, with its members in the order the client's type declares them.</returns>
        private static ClientJson TreeOf(SyntaxNode node)
        {
            return node switch
            {
                // Grouping is the printer's to decide, so it never appears in a tree.
                ParenthesizedSyntax parenthesized => TreeOf(parenthesized.Inner),
                PathSyntax path => ClientJson.Object(
                    ("kind", "path"),
                    ("segments", ClientJson.Array(path.Segments.Select(static segment => ClientJson.String(segment.Name))))),
                StringSyntax text => ClientJson.Object(("kind", "string"), ("value", text.Value)),
                NumberSyntax number => ClientJson.Object(("kind", "number"), ("value", number.Text)),
                BooleanSyntax boolean => ClientJson.Object(("kind", "bool"), ("value", ClientJson.Boolean(boolean.Value))),
                NullSyntax => ClientJson.Object(("kind", "null")),
                ParameterSyntax parameter => ClientJson.Object(("kind", "param"), ("name", parameter.Name)),
                CallSyntax call => ClientJson.Object(
                    ("kind", "call"),
                    ("name", call.Name),
                    ("args", ClientJson.Array(call.Arguments.Select(TreeOf)))),
                UnarySyntax unary => ClientJson.Object(
                    ("kind", unary.OperatorKind == UnaryOperatorKind.Negate ? "negate" : "not"),
                    ("operand", TreeOf(unary.Operand))),
                BinarySyntax binary => ClientJson.Object(
                    ("kind", "binary"),
                    ("op", OperatorTable.Spelling(binary.OperatorKind)),
                    ("left", TreeOf(binary.Left)),
                    ("right", TreeOf(binary.Right))),
                InSyntax membership => ClientJson.Object(
                    ("kind", "in"),
                    ("value", TreeOf(membership.Value)),
                    ("items", ClientJson.Array(membership.Elements.Select(TreeOf)))),
                IsNullSyntax absence => ClientJson.Object(
                    ("kind", "is-null"),
                    ("value", TreeOf(absence.Operand)),
                    ("negated", ClientJson.Boolean(absence.Negated))),
                _ => throw new InvalidOperationException($"A {node.Kind} node has no client spelling."),
            };
        }
    }
}
