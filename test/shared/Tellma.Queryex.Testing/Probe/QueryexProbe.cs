// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>One scanned token, rendered.</summary>
    /// <param name="Kind">The fine-grained kind, down to the individual operator.</param>
    /// <param name="Category">The coarse grouping a reader of the language sees.</param>
    /// <param name="Start">Where the token begins.</param>
    /// <param name="Length">How many characters it covers.</param>
    /// <param name="Text">The value it denotes, where it denotes one.</param>
    public sealed record ProbeToken(string Kind, string Category, int Start, int Length, string? Text);

    /// <summary>One parse-tree node, flattened.</summary>
    /// <param name="Index">This node's position in the flat list.</param>
    /// <param name="Parent">The parent's position, or -1 at the root.</param>
    /// <param name="Kind">The node kind.</param>
    /// <param name="Detail">The operator, name, or literal this node carries.</param>
    /// <param name="Start">Where the node begins.</param>
    /// <param name="Length">How many characters it covers.</param>
    /// <param name="Children">The children's positions, in source order.</param>
    public sealed record ProbeSyntaxNode(
        int Index,
        int Parent,
        string Kind,
        string Detail,
        int Start,
        int Length,
        IReadOnlyList<int> Children);

    /// <summary>The result of scanning and parsing one input.</summary>
    /// <param name="Succeeded">Whether the whole input scanned and parsed without a problem.</param>
    /// <param name="Tokens">The tokens, in source order.</param>
    /// <param name="Nodes">The parse tree, flattened, with the root first.</param>
    /// <param name="CanonicalText">The tree printed back with only the parentheses it needs.</param>
    /// <param name="ExplicitText">The tree printed back fully parenthesised.</param>
    /// <param name="ReferencedParameters">Every parameter the input mentions, deduplicated.</param>
    /// <param name="Diagnostics">Whatever problems were reported.</param>
    public sealed record ProbeSyntax(
        bool Succeeded,
        IReadOnlyList<ProbeToken> Tokens,
        IReadOnlyList<ProbeSyntaxNode> Nodes,
        string CanonicalText,
        string ExplicitText,
        IReadOnlyList<string> ReferencedParameters,
        IReadOnlyList<QueryexDiagnostic> Diagnostics);

    /// <summary>
    ///     A window onto the compiler's internal stages.
    /// </summary>
    /// <remarks>
    ///     Every intermediate representation in the engine is internal, and stays that way: this
    ///     project is the only one granted access, and it mirrors what it sees into the public
    ///     records above. The suites and the inspection playground both consume the mirror, so what
    ///     a person eyeballs and what the tests pin are the same objects.
    /// </remarks>
    public static partial class QueryexProbe
    {
        /// <summary>Every diagnostic code the engine declares.</summary>
        public static IReadOnlySet<string> DiagnosticCodes => Core.Queryex.Diagnostics.DiagnosticCodes.All;

        /// <summary>Every backend zone name the embedded table can resolve to.</summary>
        public static IReadOnlyList<string> BackendZoneNames { get; } =
            [.. Core.Queryex.Time.CldrTimeZones.BackendNames];

        /// <summary>How many zone identifiers the embedded table knows.</summary>
        public static int ZoneIdentifierCount => Core.Queryex.Time.CldrTimeZones.Count;

        /// <summary>Scans and parses one input.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="limits">The ceilings to apply, or null for the defaults.</param>
        /// <returns>The rendered result.</returns>
        public static ProbeSyntax Parse(string text, QueryexLimits? limits = null)
        {
            ArgumentNullException.ThrowIfNull(text);

            QueryexLimits effective = limits ?? QueryexLimits.Default;
            DiagnosticSink sink = new();
            DiagnosticScope scope = sink.Scope(DiagnosticLocation.None);
            ParseBudget budget = new(effective);

            if (!Lexer.TryTokenize(text, budget, scope, out Token[] tokens))
            {
                return new ProbeSyntax(
                    Succeeded: false,
                    RenderTokens(tokens),
                    [],
                    string.Empty,
                    string.Empty,
                    [],
                    sink.Drain());
            }

            bool parsed = Parser.TryParse(text, tokens, budget, scope, out ExpressionListSyntax list);

            return new ProbeSyntax(
                parsed,
                RenderTokens(tokens),
                Flatten(list),
                SyntaxPrinter.Print(list),
                SyntaxPrinter.Print(list, ParenthesisStyle.Explicit),
                list.ReferencedParameters,
                sink.Drain());
        }

        /// <summary>Whether two inputs parse to the same tree, spans and grouping aside.</summary>
        /// <param name="first">The first input.</param>
        /// <param name="second">The second input.</param>
        /// <returns>True when both parse cleanly and denote the same expression.</returns>
        public static bool AreEquivalent(string first, string second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);

            return TryParseTree(first, out ExpressionListSyntax? left)
                && TryParseTree(second, out ExpressionListSyntax? right)
                && SyntaxEquivalence.AreEquivalent(left, right);
        }

        /// <summary>Parses an input, discarding everything but the tree.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="list">The tree, when parsing succeeded.</param>
        /// <returns>True when the input parsed cleanly.</returns>
        private static bool TryParseTree(string text, out ExpressionListSyntax? list)
        {
            list = null;
            DiagnosticSink sink = new();
            DiagnosticScope scope = sink.Scope(DiagnosticLocation.None);
            ParseBudget budget = new(QueryexLimits.Default);

            if (!Lexer.TryTokenize(text, budget, scope, out Token[] tokens))
            {
                return false;
            }

            if (!Parser.TryParse(text, tokens, budget, scope, out ExpressionListSyntax parsed))
            {
                return false;
            }

            list = parsed;
            return true;
        }

        /// <summary>Renders the tokens.</summary>
        /// <param name="tokens">The scanned tokens.</param>
        /// <returns>The rendered tokens.</returns>
        private static List<ProbeToken> RenderTokens(Token[] tokens)
        {
            List<ProbeToken> rendered = new(tokens.Length);
            foreach (Token token in tokens)
            {
                rendered.Add(new ProbeToken(
                    token.Kind.ToString(),
                    TokenFacts.Category(token.Kind).ToString(),
                    token.Span.Start,
                    token.Span.Length,
                    token.Kind == TokenKind.Number
                        ? token.Value.ToString()
                        : token.Text));
            }

            return rendered;
        }

        /// <summary>Flattens a tree into a parent-indexed list, root first.</summary>
        /// <param name="root">The tree.</param>
        /// <returns>The flattened nodes.</returns>
        private static List<ProbeSyntaxNode> Flatten(SyntaxNode root)
        {
            List<ProbeSyntaxNode> nodes = [];
            Visit(root, parent: -1, nodes);
            return nodes;
        }

        /// <summary>Adds one node and its descendants to the flat list.</summary>
        /// <param name="node">The node.</param>
        /// <param name="parent">The parent's position.</param>
        /// <param name="nodes">The list being built.</param>
        /// <returns>This node's position.</returns>
        private static int Visit(SyntaxNode node, int parent, List<ProbeSyntaxNode> nodes)
        {
            int index = nodes.Count;
            List<int> children = [];
            nodes.Add(new ProbeSyntaxNode(
                index,
                parent,
                node.Kind.ToString(),
                DetailOf(node),
                node.Span.Start,
                node.Span.Length,
                children));

            foreach (SyntaxNode child in node.Children)
            {
                children.Add(Visit(child, index, nodes));
            }

            return index;
        }

        /// <summary>The operator, name, or literal a node carries.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The detail, or an empty string when the node carries none.</returns>
        private static string DetailOf(SyntaxNode node)
        {
            return node switch
            {
                NumberSyntax number => number.Text,
                StringSyntax text => text.Value,
                BooleanSyntax boolean => boolean.Value ? "true" : "false",
                ParameterSyntax parameter => parameter.Name,
                PathSyntax path => string.Join(".", path.Segments.Select(static segment => segment.Name)),
                CallSyntax call => call.Name,
                UnarySyntax unary => unary.OperatorKind == UnaryOperatorKind.Negate ? "-" : "not",
                BinarySyntax binary => OperatorTable.Spelling(binary.OperatorKind),
                IsNullSyntax absence => absence.Negated ? "is not null" : "is null",
                ExpressionItemSyntax item => item.Direction.ToString(),
                ErrorSyntax error => error.SourceSlice,
                _ => string.Empty,
            };
        }

        /// <summary>Formats a value with the invariant culture.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The formatted value.</returns>
        internal static string Invariant(FormattableString value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
