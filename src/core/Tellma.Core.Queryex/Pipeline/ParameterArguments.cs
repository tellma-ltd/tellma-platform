// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Pipeline
{
    /// <summary>
    ///     A caller's arguments and omitted parameters, checked against the declarations and ready
    ///     to be written into a parse tree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Everything here is syntactic. Omitted parameters fold away by the same rule a
    ///         compilation folds them by, and each parameter given a value is replaced by the literal
    ///         an author would have written for that value. A parameter in neither stays as written,
    ///         so what comes out is a parse tree like any other, and prints, parses and compiles
    ///         like one.
    ///     </para>
    ///     <para>
    ///         Immutable once resolved, and so safe to share.
    ///     </para>
    /// </remarks>
    internal sealed class ParameterArguments
    {
        /// <summary>The values given, by the parameter's declared name ignoring case, with its declaration.</summary>
        private readonly FrozenDictionary<string, (ParameterSymbol Symbol, object? Value)> _values;

        /// <summary>Initializes a resolved set.</summary>
        /// <param name="values">The values given, by parameter name ignoring case.</param>
        /// <param name="omitted">The parameters left out.</param>
        private ParameterArguments(
            FrozenDictionary<string, (ParameterSymbol Symbol, object? Value)> values,
            OmittedParameters omitted)
        {
            _values = values;
            Omitted = omitted;
        }

        /// <summary>The parameters left out.</summary>
        internal OmittedParameters Omitted { get; }

        /// <summary>Checks a caller's arguments and omitted parameters against the declarations.</summary>
        /// <param name="options">The binding options.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The resolved set.</returns>
        /// <exception cref="ArgumentException">
        ///     The declarations are not usable, a valued or omitted name is not declared, a name is
        ///     given more than one value or is both given a value and omitted, or a value is not one
        ///     of its parameter's declared type.
        /// </exception>
        internal static ParameterArguments Resolve(ParameterBindingOptions options, string parameterName)
        {
            FrozenDictionary<string, ParameterSymbol> declarations = Symbols.From(options.Parameters);
            var omitted = OmittedParameters.Resolve(options.Omitted, declarations, parameterName);

            Dictionary<string, (ParameterSymbol Symbol, object? Value)> values = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string? name, object? value) in options.Values)
            {
                if (name is null || !declarations.TryGetValue(name, out ParameterSymbol? symbol))
                {
                    throw new ArgumentException(
                        "A parameter given a value must be one of the declared parameters; '" + name
                            + "' is not declared.",
                        parameterName);
                }

                if (omitted.Contains(symbol.Name))
                {
                    throw new ArgumentException(
                        "A parameter cannot be both given a value and omitted; '" + symbol.Name + "' is both.",
                        parameterName);
                }

                // A dictionary that tells names apart by case can hold two values for one
                // parameter, and nothing says which of them the caller meant.
                if (!values.TryAdd(symbol.Name, (symbol, value)))
                {
                    throw new ArgumentException(
                        "A parameter cannot be given more than one value; '" + symbol.Name
                            + "' is given one under each of two spellings.",
                        parameterName);
                }

                // The compilation leaves out the guards for absence on a parameter promised never to
                // be absent, so a printed absent value would mean something the compiled query does
                // not.
                if (value is null && symbol.Nullity == QueryexNullity.NotNull)
                {
                    throw new ArgumentException(
                        "The parameter '" + symbol.Name + "' is declared never absent, so it cannot be given null.",
                        parameterName);
                }

                if (!LiteralSyntax.TryWrite(symbol.Type, value, default, out _))
                {
                    throw new ArgumentException(
                        "The value given for '" + symbol.Name + "' is not a value of its declared type, "
                            + BoundTypes.Name(symbol.Type) + ".",
                        parameterName);
                }
            }

            return new ParameterArguments(values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), omitted);
        }

        /// <summary>Writes the arguments into a whole text, read in a given position.</summary>
        /// <param name="syntax">The parsed text.</param>
        /// <param name="mode">The position the text is written for.</param>
        /// <returns>
        ///     The truth value the whole text folded to, or the text that is left with every valued
        ///     parameter replaced by its literal.
        /// </returns>
        /// <remarks>
        ///     Folding comes first. It decides what is left by the omitted parameters alone, and an
        ///     atom it removes takes whatever valued parameters it mentioned with it.
        /// </remarks>
        internal FoldedPredicateList Apply(ExpressionListSyntax syntax, QueryexMode mode)
        {
            if (mode.Shape == QueryexShape.Predicate)
            {
                FoldedPredicateList folded = Omitted.Folder.FoldPredicate(syntax, negated: false);
                return folded.Constant is not null
                    ? folded
                    : new FoldedPredicateList(null, Substituted(folded.List!));
            }

            return new FoldedPredicateList(null, Substituted(Omitted.Folder.FoldValues(syntax)));
        }

        /// <summary>Writes the arguments into the condition of an aggregation.</summary>
        /// <param name="condition">The condition as parsed.</param>
        /// <returns>
        ///     The truth value the condition folded to, or the condition that is left with every
        ///     valued parameter replaced by its literal.
        /// </returns>
        /// <remarks>
        ///     An aggregation's condition is a predicate wherever the aggregation sits, so it folds as
        ///     one from an un-negated start whatever position the text is written for.
        /// </remarks>
        internal FoldedPredicate ApplyToCondition(SyntaxNode condition)
        {
            FoldedPredicate folded = Omitted.Folder.FoldPredicate(condition, negated: false);
            return folded.IsConstant ? folded : FoldedPredicate.Left(Substituted(folded.Node!));
        }

        /// <summary>Replaces every valued parameter in a list with its literal.</summary>
        /// <param name="list">The list.</param>
        /// <returns>The list with the literals in, or the same instance when it mentions none.</returns>
        private ExpressionListSyntax Substituted(ExpressionListSyntax list)
        {
            if (_values.Count == 0)
            {
                return list;
            }

            ExpressionItemSyntax[]? items = null;
            for (int index = 0; index < list.Items.Count; index++)
            {
                ExpressionItemSyntax item = list.Items[index];
                SyntaxNode replaced = Substituted(item.Expression);
                if (ReferenceEquals(replaced, item.Expression))
                {
                    continue;
                }

                items ??= [.. list.Items];
                items[index] = new ExpressionItemSyntax(item.Span, replaced, item.Direction, item.DirectionSpan);
            }

            if (items is null)
            {
                return list;
            }

            // The names the list still mentions are the ones it mentioned less those now literals.
            List<string> referenced = [.. list.ReferencedParameters.Where(name => !_values.ContainsKey(name))];
            return new ExpressionListSyntax(list.Span, items, referenced);
        }

        /// <summary>Replaces every valued parameter in an expression with its literal.</summary>
        /// <param name="node">The expression.</param>
        /// <returns>
        ///     The expression with the literals in, or the same instance when it mentions none.
        /// </returns>
        /// <remarks>
        ///     Recursive, which the parser's depth ceiling keeps bounded. Grouping is kept as
        ///     written; the printer recomputes what a literal in its new place needs.
        /// </remarks>
        private SyntaxNode Substituted(SyntaxNode node)
        {
            switch (node)
            {
                case ParameterSyntax parameter when _values.TryGetValue(parameter.Name, out (ParameterSymbol Symbol, object? Value) argument):

                    // Checked when the arguments were resolved, so a literal is always written.
                    LiteralSyntax.TryWrite(argument.Symbol.Type, argument.Value, parameter.Span, out SyntaxNode literal);
                    return literal;

                case ParenthesizedSyntax parenthesized:
                    SyntaxNode inner = Substituted(parenthesized.Inner);
                    return ReferenceEquals(inner, parenthesized.Inner)
                        ? node
                        : new ParenthesizedSyntax(parenthesized.Span, inner);

                case UnarySyntax unary:
                    SyntaxNode operand = Substituted(unary.Operand);
                    return ReferenceEquals(operand, unary.Operand)
                        ? node
                        : new UnarySyntax(unary.Span, unary.OperatorKind, unary.OperatorSpan, operand);

                case BinarySyntax binary:
                    SyntaxNode left = Substituted(binary.Left);
                    SyntaxNode right = Substituted(binary.Right);
                    return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                        ? node
                        : new BinarySyntax(binary.Span, binary.OperatorKind, binary.OperatorSpan, left, right);

                case IsNullSyntax absence:
                    SyntaxNode tested = Substituted(absence.Operand);
                    return ReferenceEquals(tested, absence.Operand)
                        ? node
                        : new IsNullSyntax(absence.Span, absence.KeywordSpan, tested, absence.Negated);

                case InSyntax membership:
                    SyntaxNode value = Substituted(membership.Value);
                    SyntaxNode[]? elements = Substituted(membership.Elements);
                    return ReferenceEquals(value, membership.Value) && elements is null
                        ? node
                        : new InSyntax(membership.Span, membership.KeywordSpan, value, elements ?? membership.Elements);

                case CallSyntax call:
                    SyntaxNode[]? arguments = Substituted(call.Arguments);
                    return arguments is null
                        ? node
                        : new CallSyntax(call.Span, call.Name, call.NameSpan, arguments, call.StandsForParameter);

                default:
                    return node;
            }
        }

        /// <summary>Replaces every valued parameter in a run of operands with its literal.</summary>
        /// <param name="operands">The operands.</param>
        /// <returns>The operands with the literals in, or null when none changed.</returns>
        private SyntaxNode[]? Substituted(IReadOnlyList<SyntaxNode> operands)
        {
            SyntaxNode[]? replaced = null;
            for (int index = 0; index < operands.Count; index++)
            {
                SyntaxNode operand = operands[index];
                SyntaxNode result = Substituted(operand);
                if (ReferenceEquals(result, operand))
                {
                    continue;
                }

                replaced ??= [.. operands];
                replaced[index] = result;
            }

            return replaced;
        }
    }
}
