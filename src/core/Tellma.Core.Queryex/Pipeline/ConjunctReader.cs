// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     Reads the restrictions a bound filter leaf imposes at its top level: each atom under
    ///     <c>and</c> alone that compares a path or an expression with a literal.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every row the leaf admits satisfies each restriction found, which is what makes them
    ///         safe to show as a description of the filter. Anything that does not fit is passed
    ///         over rather than approximated: a disjunction, a negation, a comparison with a
    ///         parameter, two literals, a literal truth value.
    ///     </para>
    ///     <para>
    ///         A literal is one of the forms an author, or the engine printing an argument, writes
    ///         one in: a number, negated or not; a string; <c>true</c> or <c>false</c>; the absent
    ///         value, bare or converted to a type; and a conversion of a string to a type. Its value
    ///         is reported as the CLR value of its type.
    ///     </para>
    /// </remarks>
    internal static class ConjunctReader
    {
        /// <summary>The hierarchy predicate a restriction can be read from.</summary>
        private const string DescendantOf = "descendantOf";

        /// <summary>The conversion's name.</summary>
        private const string Conversion = "cast";

        /// <summary>The largest scale a <see cref="decimal" /> holds.</summary>
        private const int MaxDecimalScale = 28;

        /// <summary>
        ///     The 32-bit word of an exact number's 128-bit magnitude that a <see cref="decimal" />,
        ///     with its 96-bit magnitude, has no room for.
        /// </summary>
        private const int BeyondDecimalWord = 3;

        /// <summary>Reads the top-level restrictions of one bound filter leaf.</summary>
        /// <param name="predicate">The leaf's bound predicate.</param>
        /// <param name="syntax">The leaf's parse, which compared expressions are printed from.</param>
        /// <param name="location">The leaf's location text.</param>
        /// <param name="conjuncts">Where the restrictions found are added, in the order they are written.</param>
        internal static void Read(
            TypedExpr predicate,
            ExpressionListSyntax syntax,
            string? location,
            List<FilterConjunct> conjuncts)
        {
            // The binder flattens nested conjunctions, so the atoms under "and" alone are the
            // operands of the outermost one, in the order they are written.
            IEnumerable<TypedExpr> atoms = predicate is TypedLogical { Operator: LogicalOperator.And } conjunction
                ? conjunction.Operands
                : [predicate];

            foreach (TypedExpr atom in atoms.OrderBy(static atom => atom.Span.Start))
            {
                if (TryRead(atom, syntax, location, out FilterConjunct? conjunct))
                {
                    conjuncts.Add(conjunct);
                }
            }
        }

        /// <summary>Reads one atom as a restriction, when it is one.</summary>
        /// <param name="atom">The atom.</param>
        /// <param name="syntax">The leaf's parse.</param>
        /// <param name="location">The leaf's location text.</param>
        /// <param name="conjunct">The restriction, when the atom is one.</param>
        /// <returns>True when it is.</returns>
        private static bool TryRead(
            TypedExpr atom,
            ExpressionListSyntax syntax,
            string? location,
            [NotNullWhen(true)] out FilterConjunct? conjunct)
        {
            conjunct = null;
            QueryexTextSite site = new(location, atom.Span);

            switch (atom)
            {
                case TypedComparison comparison:

                    // Either operand order; a literal on the left mirrors the operator, so the
                    // restriction always reads as the compared side against the value.
                    if (TryValue(comparison.Right, out object? right) && IsCompared(comparison.Left))
                    {
                        conjunct = Conjunct(site, comparison.Left, syntax, Operator(comparison.Operator), right);
                    }
                    else if (TryValue(comparison.Left, out object? left) && IsCompared(comparison.Right))
                    {
                        conjunct = Conjunct(site, comparison.Right, syntax, Mirrored(comparison.Operator), left);
                    }

                    break;

                case TypedIsNull absence when IsCompared(absence.Operand):

                    // A test for absence reads as equality with the absent value, and a test for
                    // presence as its inequality.
                    conjunct = Conjunct(
                        site,
                        absence.Operand,
                        syntax,
                        absence.Negated ? FilterConjunctOperator.NotEqual : FilterConjunctOperator.Equal,
                        value: null);

                    break;

                case TypedCall call
                    when call.Definition.Name.Equals(DescendantOf, StringComparison.OrdinalIgnoreCase)
                        && call.Arguments.Length == 2
                        && call.Arguments[0] is TypedPath
                        && TryValue(call.Arguments[1], out object? key):

                    // One key only: a list of keys is a disjunction of subtrees, which no single
                    // operator and value describe.
                    conjunct = Conjunct(site, call.Arguments[0], syntax, FilterConjunctOperator.DescendantOf, key);
                    break;

                default:
                    break;
            }

            return conjunct is not null;
        }

        /// <summary>Builds a restriction on a path or an expression.</summary>
        /// <param name="site">Where the atom is written.</param>
        /// <param name="compared">The path or expression restricted.</param>
        /// <param name="syntax">The leaf's parse, which an expression is printed from.</param>
        /// <param name="op">How it is compared.</param>
        /// <param name="value">The literal's value.</param>
        /// <returns>
        ///     The restriction; or null when the expression cannot be found in the parse, or when an
        ///     operator other than equality or inequality is compared with the absent value.
        /// </returns>
        private static FilterConjunct? Conjunct(
            QueryexTextSite site,
            TypedExpr compared,
            ExpressionListSyntax syntax,
            FilterConjunctOperator op,
            object? value)
        {
            // Equality with the absent value tests for absence and inequality for presence, but an
            // ordering or a subtree measured against it admits no row at all: that is not a
            // restriction to report as a value.
            if (value is null && op is not (FilterConjunctOperator.Equal or FilterConjunctOperator.NotEqual))
            {
                return null;
            }

            QueryexType type = BoundTypes.ToPublic(compared.Type);
            if (compared is TypedPath path)
            {
                return new FilterConjunct(site, [.. path.Segments], null, op, value, type);
            }

            // Printed from the parse rather than from the bound tree, so the text is the author's
            // own expression in canonical form, without the grouping that set it apart.
            SyntaxNode? written = Find(syntax, compared.Span);
            return written is null
                ? null
                : new FilterConjunct(site, null, SyntaxPrinter.Print(written), op, value, type);
        }

        /// <summary>Whether an operand is something a restriction can be on.</summary>
        /// <param name="operand">The operand.</param>
        /// <returns>
        ///     True for a path, or an expression that is neither a literal nor mentions a parameter,
        ///     and that bound cleanly.
        /// </returns>
        private static bool IsCompared(TypedExpr operand)
        {
            return operand.Type is not (BoundType.Error or BoundType.Null)
                && !operand.ContainsError
                && (operand.Flags & TypedExprFlags.ContainsParameter) == 0
                && !TryValue(operand, out _);
        }

        /// <summary>Reads an operand as a literal, when it is written as one.</summary>
        /// <param name="operand">The operand.</param>
        /// <param name="value">The literal's value as the CLR value of its type; null for the absent value.</param>
        /// <returns>True when the operand is a literal.</returns>
        private static bool TryValue(TypedExpr operand, out object? value)
        {
            value = null;
            switch (operand)
            {
                case TypedNull:
                    return true;

                case TypedLiteral { Type: BoundType.Numeric } number:
                    return TryNumber((SqlDecimal)number.Value, negate: false, out value);

                case TypedNegate { Operand: TypedLiteral { Type: BoundType.Numeric } number }:
                    return TryNumber((SqlDecimal)number.Value, negate: true, out value);

                case TypedLiteral literal:
                    value = literal.Value;
                    return true;

                case TypedCall call
                    when call.Definition.Name.Equals(Conversion, StringComparison.OrdinalIgnoreCase)
                        && call.Arguments.Length > 0:
                    return TryConverted(call.Arguments[0], call.Type, out value);

                default:
                    return false;
            }
        }

        /// <summary>Reads a conversion's operand as a literal of the type converted to.</summary>
        /// <param name="operand">What is converted.</param>
        /// <param name="target">The type converted to.</param>
        /// <param name="value">The value, when the operand is the absent value or a string of that type.</param>
        /// <returns>True when it is.</returns>
        /// <remarks>
        ///     The binder may already have read the string at the target type, in which case the
        ///     value is the one it read; otherwise it is read here by the same rules.
        /// </remarks>
        private static bool TryConverted(TypedExpr operand, BoundType target, out object? value)
        {
            value = null;
            switch (operand)
            {
                case TypedNull:
                    return true;

                case TypedLiteral literal when literal.Type == target && target != BoundType.Numeric:
                    value = literal.Value;
                    return true;

                case TypedLiteral { Type: BoundType.String, Value: string text }:
                    value = target switch
                    {
                        BoundType.Date when LiteralCoercions.TryParseDate(text, out DateOnly date) => date,
                        BoundType.DateTime when LiteralCoercions.TryParseDateTime(text, out DateTime moment) => moment,
                        BoundType.DateTimeOffset when LiteralCoercions.TryParseDateTimeOffset(text, out DateTimeOffset instant) => instant,
                        BoundType.Guid when LiteralCoercions.TryParseGuid(text, out Guid identifier) => identifier,
                        BoundType.Bool or BoundType.Numeric or BoundType.String or BoundType.Date
                            or BoundType.DateTime or BoundType.DateTimeOffset or BoundType.Guid
                            or BoundType.HierarchyId or BoundType.Geography or BoundType.Null
                            or BoundType.Error => null,
                        _ => null,
                    };

                    return value is not null;

                default:
                    return false;
            }
        }

        /// <summary>Reads a number literal as a <see cref="decimal" />.</summary>
        /// <param name="number">The literal's exact value.</param>
        /// <param name="negate">Whether it is written negated.</param>
        /// <param name="value">The value, when a <see cref="decimal" /> holds it.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     The language's numbers reach 38 digits and a <see cref="decimal" /> fewer, so a literal
        ///     beyond it is passed over rather than rounded into a value the filter does not use.
        ///     Beyond it means a scale above 28 or a magnitude wider than 96 bits as written: a value
        ///     within range can still carry more digits than a <see cref="decimal" /> holds.
        /// </remarks>
        private static bool TryNumber(SqlDecimal number, bool negate, out object? value)
        {
            value = null;
            if (number.Scale > MaxDecimalScale || number.Data[BeyondDecimalWord] != 0)
            {
                return false;
            }

            decimal exact = number.Value;
            value = negate ? -exact : exact;
            return true;
        }

        /// <summary>The restriction's operator for a comparison written with the literal on the right.</summary>
        /// <param name="op">The comparison's operator.</param>
        /// <returns>The restriction's operator.</returns>
        private static FilterConjunctOperator Operator(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.Equal => FilterConjunctOperator.Equal,
                ComparisonOperator.NotEqual => FilterConjunctOperator.NotEqual,
                ComparisonOperator.Less => FilterConjunctOperator.Less,
                ComparisonOperator.LessOrEqual => FilterConjunctOperator.LessOrEqual,
                ComparisonOperator.Greater => FilterConjunctOperator.Greater,
                ComparisonOperator.GreaterOrEqual => FilterConjunctOperator.GreaterOrEqual,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a comparison operator."),
            };
        }

        /// <summary>The restriction's operator for a comparison written with the literal on the left.</summary>
        /// <param name="op">The comparison's operator.</param>
        /// <returns>The restriction's operator, read from the other side.</returns>
        private static FilterConjunctOperator Mirrored(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.Less => FilterConjunctOperator.Greater,
                ComparisonOperator.LessOrEqual => FilterConjunctOperator.GreaterOrEqual,
                ComparisonOperator.Greater => FilterConjunctOperator.Less,
                ComparisonOperator.GreaterOrEqual => FilterConjunctOperator.LessOrEqual,
                ComparisonOperator.Equal or ComparisonOperator.NotEqual => Operator(op),
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a comparison operator."),
            };
        }

        /// <summary>Finds the parsed expression a bound node was bound from.</summary>
        /// <param name="node">The parse to search.</param>
        /// <param name="span">The bound node's span.</param>
        /// <returns>The expression, or null when none has that span.</returns>
        /// <remarks>
        ///     Grouping is transparent to binding, so a bound node's span is that of the expression
        ///     inside any parentheses, which is the first node with that span that is neither a
        ///     grouping nor an item of the list.
        /// </remarks>
        private static SyntaxNode? Find(SyntaxNode node, QueryexSpan span)
        {
            if (node is not (ParenthesizedSyntax or ExpressionItemSyntax or ExpressionListSyntax)
                && node.Span == span)
            {
                return node;
            }

            foreach (SyntaxNode child in node.Children)
            {
                if (child.Span.Start <= span.Start && Find(child, span) is SyntaxNode found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
