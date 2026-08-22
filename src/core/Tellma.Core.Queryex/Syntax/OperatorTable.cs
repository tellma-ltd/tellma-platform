// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Lexing;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>One infix operator's parsing behaviour.</summary>
    /// <param name="Kind">The operator this token denotes.</param>
    /// <param name="BindingPower">How tightly it binds.</param>
    internal readonly record struct InfixOperator(BinaryOperatorKind Kind, int BindingPower);

    /// <summary>
    ///     How tightly each operator binds, and in which direction.
    /// </summary>
    /// <remarks>
    ///     The parser is driven entirely by this table, so introducing an operator is a new row here
    ///     rather than a change to the parsing code.
    /// </remarks>
    internal static class OperatorTable
    {
        /// <summary>Postfix call and path access, which bind tightest.</summary>
        internal const int PathOrCall = 90;

        /// <summary>Arithmetic negation.</summary>
        internal const int Negate = 80;

        /// <summary>Multiplication, division, and remainder.</summary>
        internal const int Multiplicative = 70;

        /// <summary>Addition, subtraction, and concatenation.</summary>
        internal const int Additive = 60;

        /// <summary>
        ///     The comparisons, set membership, and the absence test. Non-associative: chaining them
        ///     is rejected rather than read as a nested comparison against a truth value.
        /// </summary>
        internal const int Comparison = 50;

        /// <summary>
        ///     Logical negation. Looser than comparison, matching how the same word behaves in SQL
        ///     and in Python, so a negated comparison reads the way it looks. Because there is no
        ///     symbolic negation operator in the language, the tighter-binding reading from the
        ///     C family is never available to be mistaken for this one.
        /// </summary>
        internal const int LogicalNot = 40;

        /// <summary>Conjunction.</summary>
        internal const int LogicalAnd = 30;

        /// <summary>Disjunction, which binds loosest.</summary>
        internal const int LogicalOr = 20;

        /// <summary>
        ///     The infix operators, by the token that introduces them. Literally the table the
        ///     parser is driven by.
        /// </summary>
        private static readonly FrozenDictionary<TokenKind, InfixOperator> Infixes =
            new Dictionary<TokenKind, InfixOperator>
            {
                [TokenKind.Asterisk] = new InfixOperator(BinaryOperatorKind.Multiply, Multiplicative),
                [TokenKind.Slash] = new InfixOperator(BinaryOperatorKind.Divide, Multiplicative),
                [TokenKind.Percent] = new InfixOperator(BinaryOperatorKind.Remainder, Multiplicative),
                [TokenKind.Plus] = new InfixOperator(BinaryOperatorKind.Add, Additive),
                [TokenKind.Minus] = new InfixOperator(BinaryOperatorKind.Subtract, Additive),
                [TokenKind.BarBar] = new InfixOperator(BinaryOperatorKind.Concat, Additive),
                [TokenKind.EqualsToken] = new InfixOperator(BinaryOperatorKind.Equal, Comparison),
                [TokenKind.BangEquals] = new InfixOperator(BinaryOperatorKind.NotEqual, Comparison),
                [TokenKind.Less] = new InfixOperator(BinaryOperatorKind.Less, Comparison),
                [TokenKind.LessOrEqual] = new InfixOperator(BinaryOperatorKind.LessOrEqual, Comparison),
                [TokenKind.Greater] = new InfixOperator(BinaryOperatorKind.Greater, Comparison),
                [TokenKind.GreaterOrEqual] = new InfixOperator(BinaryOperatorKind.GreaterOrEqual, Comparison),
                [TokenKind.And] = new InfixOperator(BinaryOperatorKind.And, LogicalAnd),
                [TokenKind.Or] = new InfixOperator(BinaryOperatorKind.Or, LogicalOr),
            }.ToFrozenDictionary();

        /// <summary>Looks up a token's infix behaviour.</summary>
        /// <param name="kind">The token kind.</param>
        /// <param name="infix">The operator's behaviour, when it has one.</param>
        /// <returns>True when the token is an infix operator.</returns>
        internal static bool TryInfix(TokenKind kind, out InfixOperator infix)
        {
            return Infixes.TryGetValue(kind, out infix);
        }

        /// <summary>
        ///     Whether a token starts one of the non-associative comparison forms.
        /// </summary>
        /// <param name="kind">The token kind.</param>
        /// <returns>True for the comparison operators, set membership, and the absence test.</returns>
        internal static bool IsComparisonStart(TokenKind kind)
        {
            return kind is TokenKind.EqualsToken
                or TokenKind.BangEquals
                or TokenKind.Less
                or TokenKind.LessOrEqual
                or TokenKind.Greater
                or TokenKind.GreaterOrEqual
                or TokenKind.In
                or TokenKind.Is;
        }

        /// <summary>The canonical spelling of an infix operator, for printing.</summary>
        /// <param name="kind">The operator.</param>
        /// <returns>The spelling.</returns>
        internal static string Spelling(BinaryOperatorKind kind)
        {
            return kind switch
            {
                BinaryOperatorKind.Add => "+",
                BinaryOperatorKind.Subtract => "-",
                BinaryOperatorKind.Multiply => "*",
                BinaryOperatorKind.Divide => "/",
                BinaryOperatorKind.Remainder => "%",
                BinaryOperatorKind.Concat => "||",
                BinaryOperatorKind.Equal => "=",
                BinaryOperatorKind.NotEqual => "!=",
                BinaryOperatorKind.Less => "<",
                BinaryOperatorKind.LessOrEqual => "<=",
                BinaryOperatorKind.Greater => ">",
                BinaryOperatorKind.GreaterOrEqual => ">=",
                BinaryOperatorKind.And => "and",
                BinaryOperatorKind.Or => "or",
                _ => "?",
            };
        }
    }
}
