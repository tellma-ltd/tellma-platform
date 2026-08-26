// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Queryex.Lexing;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>How much parenthesisation the printer emits.</summary>
    internal enum ParenthesisStyle
    {
        /// <summary>Only where precedence requires it. The canonical form.</summary>
        Minimal,

        /// <summary>
        ///     Around every operator application. Used by the round-trip test, so that reparsing the
        ///     printed form does not lean on the very precedence table it is meant to be checking.
        /// </summary>
        Explicit,
    }

    /// <summary>
    ///     Prints a parse tree back to canonical Queryex text.
    /// </summary>
    /// <remarks>
    ///     Canonical rather than faithful: redundant parentheses disappear, keywords come out
    ///     lowercase, and spacing is uniform. What never changes is the value a literal denotes —
    ///     a number keeps the scale it was written with, because that scale is load-bearing.
    /// </remarks>
    internal static class SyntaxPrinter
    {
        /// <summary>The binding power of something that can never need parentheses.</summary>
        private const int Atomic = 100;

        /// <summary>Prints a node.</summary>
        /// <param name="node">The node to print.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        /// <returns>The printed text.</returns>
        internal static string Print(SyntaxNode node, ParenthesisStyle style = ParenthesisStyle.Minimal)
        {
            StringBuilder builder = new();
            Write(node, builder, style);
            return builder.ToString();
        }

        /// <summary>Writes a node into a builder.</summary>
        /// <param name="node">The node to print.</param>
        /// <param name="builder">Where to write.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        private static void Write(SyntaxNode node, StringBuilder builder, ParenthesisStyle style)
        {
            switch (node)
            {
                case ExpressionListSyntax list:
                    WriteSeparated(list.Items, builder, style);
                    return;

                case ExpressionItemSyntax item:
                    Write(item.Expression, builder, style);
                    if (item.Direction != QueryexDirection.None)
                    {
                        builder.Append(item.Direction == QueryexDirection.Ascending ? " asc" : " desc");
                    }

                    return;

                case NumberSyntax number:
                    builder.Append(number.Text);
                    return;

                case StringSyntax text:
                    WriteString(text.Value, builder);
                    return;

                case BooleanSyntax boolean:
                    builder.Append(boolean.Value ? "true" : "false");
                    return;

                case NullSyntax:
                    builder.Append("null");
                    return;

                case ParameterSyntax parameter:
                    builder.Append('@');
                    WriteIdentifier(parameter.Name, builder);
                    return;

                case PathSyntax path:
                    for (int index = 0; index < path.Segments.Count; index++)
                    {
                        if (index > 0)
                        {
                            builder.Append('.');
                        }

                        WriteIdentifier(path.Segments[index].Name, builder);
                    }

                    return;

                case CallSyntax call:
                    WriteIdentifier(call.Name, builder);
                    builder.Append('(');
                    WriteSeparated(call.Arguments, builder, style);
                    builder.Append(')');
                    return;

                case ParenthesizedSyntax parenthesized:

                    // Transparent: the parentheses a reader needs are recomputed from precedence, so
                    // printing is idempotent and redundant grouping does not accumulate.
                    Write(parenthesized.Inner, builder, style);
                    return;

                case UnarySyntax unary:
                    WriteUnary(unary, builder, style);
                    return;

                case BinarySyntax binary:
                    WriteBinary(binary, builder, style);
                    return;

                case InSyntax membership:
                    WriteOperand(membership.Value, builder, style, OperatorTable.Comparison + 1);
                    builder.Append(" in (");
                    WriteSeparated(membership.Elements, builder, style, OperatorTable.Additive);
                    builder.Append(')');
                    return;

                case IsNullSyntax absence:
                    WriteOperand(absence.Operand, builder, style, OperatorTable.Comparison + 1);
                    builder.Append(absence.Negated ? " is not null" : " is null");
                    return;

                default:
                    builder.Append(((ErrorSyntax)node).SourceSlice);
                    return;
            }
        }

        /// <summary>Writes a prefix operator application.</summary>
        /// <param name="unary">The node.</param>
        /// <param name="builder">Where to write.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        private static void WriteUnary(UnarySyntax unary, StringBuilder builder, ParenthesisStyle style)
        {
            if (unary.OperatorKind == UnaryOperatorKind.Negate)
            {
                builder.Append('-');
                WriteOperand(unary.Operand, builder, style, OperatorTable.Negate);
                return;
            }

            builder.Append("not ");
            WriteOperand(unary.Operand, builder, style, OperatorTable.LogicalNot);
        }

        /// <summary>Writes an infix operator application.</summary>
        /// <param name="binary">The node.</param>
        /// <param name="builder">Where to write.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        private static void WriteBinary(BinarySyntax binary, StringBuilder builder, ParenthesisStyle style)
        {
            int power = BindingPowerOf(binary);

            // Left-associative, so an equal-power right operand needs grouping and an equal-power
            // left operand does not. A comparison needs it on both sides, since it does not chain.
            int leftFloor = power == OperatorTable.Comparison ? power + 1 : power;

            WriteOperand(binary.Left, builder, style, leftFloor);
            builder.Append(' ');
            builder.Append(OperatorTable.Spelling(binary.OperatorKind));
            builder.Append(' ');
            WriteOperand(binary.Right, builder, style, power + 1);
        }

        /// <summary>Writes one operand, grouping it when precedence or the style requires.</summary>
        /// <param name="operand">The operand.</param>
        /// <param name="builder">Where to write.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        /// <param name="floor">The binding power the operand must reach to stand unparenthesised.</param>
        private static void WriteOperand(
            SyntaxNode operand,
            StringBuilder builder,
            ParenthesisStyle style,
            int floor)
        {
            SyntaxNode inner = Unwrap(operand);
            int power = BindingPowerOf(inner);
            bool needed = power < floor || (style == ParenthesisStyle.Explicit && power < Atomic);

            if (needed)
            {
                builder.Append('(');
            }

            Write(inner, builder, style);

            if (needed)
            {
                builder.Append(')');
            }
        }

        /// <summary>Writes a comma-separated run.</summary>
        /// <param name="nodes">The nodes.</param>
        /// <param name="builder">Where to write.</param>
        /// <param name="style">How much parenthesisation to emit.</param>
        /// <param name="floor">The binding power each element must reach, or zero for none.</param>
        private static void WriteSeparated<T>(
            IReadOnlyList<T> nodes,
            StringBuilder builder,
            ParenthesisStyle style,
            int floor = 0)
            where T : SyntaxNode
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                if (floor > 0)
                {
                    WriteOperand(nodes[index], builder, style, floor);
                }
                else
                {
                    Write(nodes[index], builder, style);
                }
            }
        }

        /// <summary>Writes a string literal, re-doubling the quotes inside it.</summary>
        /// <param name="value">The denoted text.</param>
        /// <param name="builder">Where to write.</param>
        private static void WriteString(string value, StringBuilder builder)
        {
            builder.Append('\'');
            foreach (char character in value)
            {
                if (character == '\'')
                {
                    builder.Append('\'');
                }

                builder.Append(character);
            }

            builder.Append('\'');
        }

        /// <summary>Writes an identifier, bracketing it only when it would not read back as itself.</summary>
        /// <param name="name">The identifier name.</param>
        /// <param name="builder">Where to write.</param>
        private static void WriteIdentifier(string name, StringBuilder builder)
        {
            if (NeedsBrackets(name))
            {
                builder.Append('[').Append(name).Append(']');
                return;
            }

            builder.Append(name);
        }

        /// <summary>Whether a name has to be bracketed to read back as itself.</summary>
        /// <param name="name">The identifier name.</param>
        /// <returns>True when brackets are required.</returns>
        private static bool NeedsBrackets(string name)
        {
            if (name.Length == 0 || Keywords.IsReserved(name))
            {
                return true;
            }

            if (!char.IsLetter(name[0]) && name[0] != '_')
            {
                return true;
            }

            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Sees through any number of layers of parentheses.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The first node that is not a parenthesised wrapper.</returns>
        internal static SyntaxNode Unwrap(SyntaxNode node)
        {
            SyntaxNode current = node;
            while (current is ParenthesizedSyntax parenthesized)
            {
                current = parenthesized.Inner;
            }

            return current;
        }

        /// <summary>How tightly a node's own outermost operator binds.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its binding power, or the atomic power when it has no operator.</returns>
        private static int BindingPowerOf(SyntaxNode node)
        {
            return node switch
            {
                BinarySyntax binary => OperatorTable.TryInfix(TokenOf(binary.OperatorKind), out InfixOperator infix)
                    ? infix.BindingPower
                    : Atomic,
                UnarySyntax unary => unary.OperatorKind == UnaryOperatorKind.Negate
                    ? OperatorTable.Negate
                    : OperatorTable.LogicalNot,
                InSyntax or IsNullSyntax => OperatorTable.Comparison,
                _ => Atomic,
            };
        }

        /// <summary>The token that introduces an infix operator.</summary>
        /// <param name="kind">The operator.</param>
        /// <returns>Its token kind.</returns>
        private static TokenKind TokenOf(BinaryOperatorKind kind)
        {
            return kind switch
            {
                BinaryOperatorKind.Add => TokenKind.Plus,
                BinaryOperatorKind.Subtract => TokenKind.Minus,
                BinaryOperatorKind.Multiply => TokenKind.Asterisk,
                BinaryOperatorKind.Divide => TokenKind.Slash,
                BinaryOperatorKind.Remainder => TokenKind.Percent,
                BinaryOperatorKind.Concat => TokenKind.BarBar,
                BinaryOperatorKind.Equal => TokenKind.EqualsToken,
                BinaryOperatorKind.NotEqual => TokenKind.BangEquals,
                BinaryOperatorKind.Less => TokenKind.Less,
                BinaryOperatorKind.LessOrEqual => TokenKind.LessOrEqual,
                BinaryOperatorKind.Greater => TokenKind.Greater,
                BinaryOperatorKind.GreaterOrEqual => TokenKind.GreaterOrEqual,
                BinaryOperatorKind.And => TokenKind.And,
                BinaryOperatorKind.Or => TokenKind.Or,
                _ => TokenKind.EndOfInput,
            };
        }
    }
}
