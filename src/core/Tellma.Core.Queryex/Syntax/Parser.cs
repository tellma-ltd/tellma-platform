// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Lexing;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>
    ///     Turns tokens into a parse tree by precedence climbing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Knows the grammar and nothing else. Whether directions are permitted, and whether a
    ///         list may have more than one item, depend on the position an expression is being
    ///         compiled for — so they are checked afterwards, which is also what lets one parse be
    ///         cached against its text alone and reused wherever that text appears.
    ///     </para>
    ///     <para>
    ///         On the first problem within an item, the parser reports it and then skips to the next
    ///         separator, so a list reports one problem per item rather than one problem in total.
    ///         The skipped region becomes an error node, which no later stage can mistake for
    ///         well-formed input.
    ///     </para>
    /// </remarks>
    internal sealed class Parser
    {
        /// <summary>The ceilings for this call site.</summary>
        private readonly ParseBudget _budget;

        /// <summary>Where to report problems.</summary>
        private readonly DiagnosticScope _scope;

        /// <summary>Every parameter name the input mentions.</summary>
        private readonly SortedSet<string> _parameters = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The input text, for slicing error regions out of.</summary>
        private readonly string _text;

        /// <summary>The tokens, ending with an end-of-input token.</summary>
        private readonly Token[] _tokens;

        /// <summary>The next token to read.</summary>
        private int _position;

        /// <summary>Whether the item being parsed has already reported a problem.</summary>
        private bool _itemFailed;

        /// <summary>Whether any item failed.</summary>
        private bool _failed;

        /// <summary>Initializes a parser over one input.</summary>
        /// <param name="text">The input text.</param>
        /// <param name="tokens">The tokens scanned from it.</param>
        /// <param name="budget">The ceilings for this call site.</param>
        /// <param name="scope">Where to report problems.</param>
        private Parser(string text, Token[] tokens, ParseBudget budget, in DiagnosticScope scope)
        {
            _text = text;
            _tokens = tokens;
            _budget = budget;
            _scope = scope;
        }

        /// <summary>Parses a whole input.</summary>
        /// <param name="text">The input text.</param>
        /// <param name="tokens">The tokens scanned from it.</param>
        /// <param name="budget">The ceilings for this call site.</param>
        /// <param name="scope">Where to report problems.</param>
        /// <param name="list">The parse tree, which is produced even when problems were reported.</param>
        /// <returns>True when the whole input parsed without a problem.</returns>
        internal static bool TryParse(
            string text,
            Token[] tokens,
            ParseBudget budget,
            in DiagnosticScope scope,
            out ExpressionListSyntax list)
        {
            Parser parser = new(text, tokens, budget, scope);
            list = parser.ParseList();
            return !parser._failed && !budget.Exceeded;
        }

        /// <summary>Parses the comma-separated list of items.</summary>
        /// <returns>The list.</returns>
        private ExpressionListSyntax ParseList()
        {
            List<ExpressionItemSyntax> items = [];

            while (true)
            {
                ExpressionItemSyntax item = ParseItem();
                if (!_budget.TryAcceptListLength(items.Count + 1, item.Span, _scope))
                {
                    _failed = true;
                    break;
                }

                items.Add(item);

                if (Peek().Kind != TokenKind.Comma)
                {
                    break;
                }

                Advance();
            }

            if (!_budget.Exceeded && Peek().Kind != TokenKind.EndOfInput)
            {
                ReportUnexpected(TokenKind.EndOfInput);
                Recover();
            }

            return new ExpressionListSyntax(
                new QueryexSpan(0, _text.Length),
                items,
                [.. _parameters]);
        }

        /// <summary>Parses one item, with its optional direction suffix.</summary>
        /// <returns>The item.</returns>
        private ExpressionItemSyntax ParseItem()
        {
            _itemFailed = false;

            Token first = Peek();
            if (first.Kind is TokenKind.Comma or TokenKind.EndOfInput)
            {
                // A leading, trailing, or doubled separator is a typo that would otherwise be
                // silently accepted as "nothing here", which is exactly how it survives into stored
                // configuration.
                QueryexSpan empty = new(first.Span.Start, 0);
                Fail(DiagnosticCodes.EmptyListItem, empty);
                return new ExpressionItemSyntax(
                    empty,
                    new ErrorSyntax(empty, string.Empty),
                    QueryexDirection.None,
                    empty);
            }

            SyntaxNode expression = ParseExpression(0);
            QueryexDirection direction = QueryexDirection.None;
            QueryexSpan directionSpan = default;
            int end = expression.Span.End;

            if (Peek().Kind is TokenKind.Asc or TokenKind.Desc)
            {
                Token suffix = Advance();
                direction = suffix.Kind == TokenKind.Asc
                    ? QueryexDirection.Ascending
                    : QueryexDirection.Descending;
                directionSpan = suffix.Span;
                end = suffix.Span.End;

                if (Peek().Kind is not (TokenKind.Comma or TokenKind.EndOfInput))
                {
                    Fail(DiagnosticCodes.DirectionMustTerminateItem, Peek().Span);
                    expression = Recover();
                }
            }

            QueryexSpan span = new(first.Span.Start, end - first.Span.Start);
            return new ExpressionItemSyntax(span, expression, direction, directionSpan);
        }

        /// <summary>Parses an expression, consuming operators that bind at least as tightly.</summary>
        /// <param name="minimumBindingPower">The tightness floor for the operators to consume.</param>
        /// <returns>The expression.</returns>
        private SyntaxNode ParseExpression(int minimumBindingPower)
        {
            if (!_budget.TryEnterDepth(Peek().Span, _scope))
            {
                // Marked as failed rather than merely reported, so unwinding through the enclosing
                // groups does not add a complaint about every parenthesis it passes on the way out.
                _failed = true;
                _itemFailed = true;
                return new ErrorSyntax(Peek().Span, string.Empty);
            }

            try
            {
                SyntaxNode left = ParsePrefix();

                while (!_itemFailed)
                {
                    // Charged here rather than after the loop, so a chain that has already outgrown
                    // the ceiling stops growing instead of being built out and then rejected. The
                    // loop only ever leaves from inside its body, so this runs after the last
                    // re-wrap as well as before the first.
                    if (!_budget.TryAcceptDepth(left.Depth, left.Span, _scope))
                    {
                        _failed = true;
                        _itemFailed = true;
                        break;
                    }

                    Token token = Peek();

                    if (token.Kind is TokenKind.In or TokenKind.Is)
                    {
                        if (OperatorTable.Comparison < minimumBindingPower)
                        {
                            break;
                        }

                        left = token.Kind == TokenKind.In ? ParseIn(left) : ParseIsNull(left);
                        RejectChainedComparison();
                        continue;
                    }

                    if (!OperatorTable.TryInfix(token.Kind, out InfixOperator infix)
                        || infix.BindingPower < minimumBindingPower)
                    {
                        break;
                    }

                    Advance();

                    // A comparison parses its right operand above its own tightness, so a second
                    // comparison cannot be folded in; the explicit check below then rejects it with
                    // a diagnostic that names the real problem.
                    SyntaxNode right = ParseExpression(infix.BindingPower + 1);
                    left = new BinarySyntax(
                        Union(left.Span, right.Span),
                        infix.Kind,
                        token.Span,
                        left,
                        right);

                    if (infix.BindingPower == OperatorTable.Comparison)
                    {
                        RejectChainedComparison();
                    }
                }

                return left;
            }
            finally
            {
                _budget.ExitDepth();
            }
        }

        /// <summary>Rejects a second comparison at the same level.</summary>
        private void RejectChainedComparison()
        {
            if (!_itemFailed && OperatorTable.IsComparisonStart(Peek().Kind))
            {
                Fail(DiagnosticCodes.ChainedComparison, Peek().Span);
                Recover();
            }
        }

        /// <summary>Parses a prefix operator application, or falls through to a primary.</summary>
        /// <returns>The expression.</returns>
        private SyntaxNode ParsePrefix()
        {
            Token token = Peek();

            if (token.Kind == TokenKind.Minus)
            {
                Advance();
                SyntaxNode operand = ParseExpression(OperatorTable.Negate);
                return new UnarySyntax(
                    Union(token.Span, operand.Span),
                    UnaryOperatorKind.Negate,
                    token.Span,
                    operand);
            }

            if (token.Kind == TokenKind.Not)
            {
                Advance();
                SyntaxNode operand = ParseExpression(OperatorTable.LogicalNot);
                return new UnarySyntax(
                    Union(token.Span, operand.Span),
                    UnaryOperatorKind.Not,
                    token.Span,
                    operand);
            }

            return ParsePrimary();
        }

        /// <summary>Parses a set-membership test, the value having already been parsed.</summary>
        /// <param name="value">The value being tested.</param>
        /// <returns>The expression.</returns>
        private SyntaxNode ParseIn(SyntaxNode value)
        {
            Token keyword = Advance();

            if (!Expect(TokenKind.OpenParen, out Token open))
            {
                return Recover();
            }


            if (Peek().Kind == TokenKind.CloseParen)
            {
                Token close = Advance();
                Fail(DiagnosticCodes.EmptyParentheses, Union(open.Span, close.Span));
                return Recover();
            }

            List<SyntaxNode> elements = [];
            while (true)
            {
                if (Peek().Kind is TokenKind.Comma or TokenKind.CloseParen)
                {
                    Fail(DiagnosticCodes.EmptyArgument, new QueryexSpan(Peek().Span.Start, 0));
                    return Recover();
                }

                // Elements sit at the additive level, so a comparison or a connective inside one
                // has to be parenthesised rather than silently swallowing the rest of the list.
                SyntaxNode element = ParseExpression(OperatorTable.Additive);
                if (_itemFailed)
                {
                    return Recover();
                }

                // Charged against the same ceiling as any other comma list. Left uncharged, the
                // length of a membership test was bounded only by the token count, which is a much
                // higher ceiling than the one the reader was told about.
                if (!_budget.TryAcceptListLength(elements.Count + 1, element.Span, _scope))
                {
                    _failed = true;
                    return Recover();
                }

                elements.Add(element);

                if (Peek().Kind == TokenKind.Comma)
                {
                    Advance();
                    continue;
                }

                break;
            }

            return !Expect(TokenKind.CloseParen, out Token closing)
                ? Recover()
                : new InSyntax(Union(value.Span, closing.Span), keyword.Span, value, elements);
        }

        /// <summary>Parses an absence test, the operand having already been parsed.</summary>
        /// <param name="operand">The operand.</param>
        /// <returns>The expression.</returns>
        private SyntaxNode ParseIsNull(SyntaxNode operand)
        {
            Token keyword = Advance();
            bool negated = false;

            if (Peek().Kind == TokenKind.Not)
            {
                Advance();
                negated = true;
            }

            return !Expect(TokenKind.Null, out Token literal)
                ? Recover()
                : new IsNullSyntax(Union(operand.Span, literal.Span), keyword.Span, operand, negated);
        }

        /// <summary>Parses a primary expression.</summary>
        /// <returns>The expression.</returns>
        private SyntaxNode ParsePrimary()
        {
            Token token = Peek();

            if (token.Kind == TokenKind.Number)
            {
                Advance();
                return new NumberSyntax(
                    token.Span,
                    token.Value,
                    token.Precision,
                    token.Scale,
                    _text.Substring(token.Span.Start, token.Span.Length));
            }

            if (token.Kind == TokenKind.String)
            {
                Advance();
                return new StringSyntax(token.Span, token.Text!);
            }

            if (token.Kind is TokenKind.True or TokenKind.False)
            {
                Advance();
                return new BooleanSyntax(token.Span, token.Kind == TokenKind.True);
            }

            if (token.Kind == TokenKind.Null)
            {
                Advance();
                return new NullSyntax(token.Span);
            }

            if (token.Kind == TokenKind.Parameter)
            {
                Advance();
                _parameters.Add(token.Text!);
                return new ParameterSyntax(token.Span, token.Text!);
            }

            if (token.Kind == TokenKind.Identifier)
            {
                return ParseCallOrPath();
            }

            if (token.Kind == TokenKind.OpenParen)
            {
                return ParseParenthesized();
            }

            ReportUnexpected(
                TokenKind.Number,
                TokenKind.String,
                TokenKind.Identifier,
                TokenKind.Parameter,
                TokenKind.OpenParen);

            return Recover();
        }

        /// <summary>Parses a call or a path, which are told apart by a single character.</summary>
        /// <returns>The expression.</returns>
        /// <remarks>
        ///     An identifier immediately followed by an opening parenthesis is a call; otherwise it
        ///     begins a path. No other rule is consulted, which is why function names are not
        ///     reserved and an entity may have a property named after one.
        /// </remarks>
        private SyntaxNode ParseCallOrPath()
        {
            Token first = Advance();

            if (Peek().Kind == TokenKind.OpenParen)
            {
                return ParseCall(first);
            }

            List<PathSegment> segments =
            [
                new PathSegment(first.Text!, first.Span, first.WasBracketed),
            ];

            while (Peek().Kind == TokenKind.Dot)
            {
                Advance();
                if (!Expect(TokenKind.Identifier, out Token segment))
                {
                    return Recover();
                }

                segments.Add(new PathSegment(segment.Text!, segment.Span, segment.WasBracketed));
            }

            QueryexSpan span = Union(first.Span, segments[^1].Span);
            return new PathSyntax(span, segments);
        }

        /// <summary>Parses a call's argument list, the name having already been consumed.</summary>
        /// <param name="name">The function name token.</param>
        /// <returns>The expression.</returns>
        private SyntaxNode ParseCall(Token name)
        {
            Advance();

            List<SyntaxNode> arguments = [];

            if (Peek().Kind != TokenKind.CloseParen)
            {
                while (true)
                {
                    if (Peek().Kind is TokenKind.Comma or TokenKind.CloseParen)
                    {
                        // An omitted argument is a typo. Someone who means an absent value writes
                        // one, which also makes the intent visible to the next reader.
                        Fail(DiagnosticCodes.EmptyArgument, new QueryexSpan(Peek().Span.Start, 0));
                        return Recover();
                    }

                    SyntaxNode argument = ParseExpression(0);
                    if (_itemFailed)
                    {
                        return Recover();
                    }

                    if (!_budget.TryAcceptListLength(arguments.Count + 1, argument.Span, _scope))
                    {
                        _failed = true;
                        return Recover();
                    }

                    arguments.Add(argument);

                    if (Peek().Kind == TokenKind.Comma)
                    {
                        Advance();
                        continue;
                    }

                    break;
                }
            }

            return !Expect(TokenKind.CloseParen, out Token close)
                ? Recover()
                : new CallSyntax(Union(name.Span, close.Span), name.Text!, name.Span, arguments);
        }

        /// <summary>Parses a parenthesised expression.</summary>
        /// <returns>The expression.</returns>
        private SyntaxNode ParseParenthesized()
        {
            Token open = Advance();

            if (Peek().Kind == TokenKind.CloseParen)
            {
                Token empty = Advance();
                Fail(DiagnosticCodes.EmptyParentheses, Union(open.Span, empty.Span));
                return Recover();
            }

            SyntaxNode inner = ParseExpression(0);

            return _itemFailed || !Expect(TokenKind.CloseParen, out Token close)
                ? Recover()
                : new ParenthesizedSyntax(Union(open.Span, close.Span), inner);
        }

        /// <summary>Consumes the expected token, or reports that it was missing.</summary>
        /// <param name="kind">The expected token kind.</param>
        /// <param name="token">The consumed token, when it matched.</param>
        /// <returns>True when it matched.</returns>
        private bool Expect(TokenKind kind, out Token token)
        {
            token = Peek();
            if (token.Kind == kind)
            {
                Advance();
                return true;
            }

            // An unclosed parenthesis is worth its own code: the useful thing to say is where the
            // grouping went wrong, not which token happened to arrive first.
            if (kind == TokenKind.CloseParen && token.Kind == TokenKind.EndOfInput)
            {
                Fail(DiagnosticCodes.UnbalancedParenthesis, token.Span);
                return false;
            }

            ReportUnexpected(kind);
            return false;
        }

        /// <summary>Reports the token that arrived and what would have been accepted.</summary>
        /// <param name="expected">The acceptable token kinds.</param>
        private void ReportUnexpected(params TokenKind[] expected)
        {
            Token token = Peek();

            // A direction suffix in the wrong place is a specific, common mistake, and saying so is
            // more useful than listing every token the grammar would have accepted instead.
            if (token.Kind is TokenKind.Asc or TokenKind.Desc)
            {
                Fail(DiagnosticCodes.DirectionMustTerminateItem, token.Span);
                return;
            }

            Fail(
                DiagnosticCodes.UnexpectedToken,
                token.Span,
                new KeyValuePair<string, string>(
                    DiagnosticArgumentNames.Expected,
                    string.Join(", ", expected.Select(TokenFacts.Spelling))),
                new KeyValuePair<string, string>(
                    DiagnosticArgumentNames.Actual,
                    TokenFacts.Spelling(token.Kind)));
        }

        /// <summary>Records a problem, at most one per item.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="arguments">Named values for message composition.</param>
        private void Fail(string code, QueryexSpan span, params KeyValuePair<string, string>[] arguments)
        {
            if (_itemFailed)
            {
                return;
            }

            _itemFailed = true;
            _failed = true;
            _scope.Report(code, span, arguments);
        }

        /// <summary>
        ///     Skips to the next separator so the following items can still be parsed.
        /// </summary>
        /// <returns>An error node covering everything that was skipped.</returns>
        private ErrorSyntax Recover()
        {
            int start = Peek().Span.Start;
            int depth = 0;

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.EndOfInput)
                {
                    break;
                }

                if (token.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (token.Kind == TokenKind.CloseParen)
                {
                    // Stop at the parenthesis that closes the group the failure was inside, rather
                    // than swallowing the rest of the item along with it.
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }
                else if (token.Kind == TokenKind.Comma && depth == 0)
                {
                    break;
                }

                Advance();
            }

            int end = Peek().Span.Start;
            QueryexSpan span = new(start, Math.Max(0, end - start));
            return new ErrorSyntax(span, _text.Substring(span.Start, span.Length));
        }

        /// <summary>The next token, without consuming it.</summary>
        /// <returns>The token.</returns>
        private Token Peek()
        {
            return _tokens[_position];
        }

        /// <summary>Consumes and returns the next token.</summary>
        /// <returns>The consumed token.</returns>
        private Token Advance()
        {
            Token token = _tokens[_position];
            if (_position < _tokens.Length - 1)
            {
                _position++;
            }

            return token;
        }

        /// <summary>The smallest range covering both of the given ranges.</summary>
        /// <param name="first">The first range.</param>
        /// <param name="second">The second range.</param>
        /// <returns>The covering range.</returns>
        private static QueryexSpan Union(QueryexSpan first, QueryexSpan second)
        {
            int start = Math.Min(first.Start, second.Start);
            int end = Math.Max(first.End, second.End);
            return new QueryexSpan(start, end - start);
        }
    }
}
