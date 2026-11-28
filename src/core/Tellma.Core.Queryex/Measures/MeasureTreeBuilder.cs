// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Data.SqlTypes;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Nullity;

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>
    ///     Builds each measure's evaluation tree from its bound expression, collecting the partials
    ///     the trees read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each aggregation is a leaf: <c>count</c>, <c>sum</c>, <c>min</c> and <c>max</c> read one
    ///         partial each, and <c>avg</c> reads a sum over a count of the same argument and
    ///         condition. Structurally equal aggregations read one partial, numbered in the order the
    ///         measures first use them.
    ///     </para>
    ///     <para>
    ///         Everything settled before the statement runs is settled here as lowering settles it
    ///         in a statement without grouping keys — a condition that is a written truth value, a
    ///         comparison or an absence test whose answer the nullity analysis already knows — so the
    ///         evaluator never reads an operand the backend would not have read, and fails exactly
    ///         where the backend would.
    ///     </para>
    ///     <para>
    ///         The nullity is a grand total's, never the grouped statement's: a cell may combine no
    ///         row at all, and over no row a sum or an extreme is absent even where every group of
    ///         the statement has one.
    ///     </para>
    /// </remarks>
    internal sealed class MeasureTreeBuilder
    {
        /// <summary>The nullity answers for every node of the measures, as a grand total has them.</summary>
        private readonly NullityMap _nullity;

        /// <summary>The statement's own nullity answers, which lower the partial columns.</summary>
        private readonly NullityMap _statement;

        /// <summary>The distinct aggregations, in first-use order.</summary>
        private readonly List<TypedCall> _partials = [];

        /// <summary>For each partial, the measure that first used it.</summary>
        private readonly List<int> _firstUses = [];

        /// <summary>The aggregations an average was split into, which nobody wrote.</summary>
        private readonly HashSet<TypedCall> _synthesized = new(ReferenceEqualityComparer.Instance);

        /// <summary>The measure being built.</summary>
        private int _item;

        /// <summary>Initializes a builder.</summary>
        /// <param name="nullity">
        ///     The nullity answers for every node of the measures as a grand total has them, which the
        ///     trees fold by; the aggregations an average splits into are added to it.
        /// </param>
        /// <param name="statement">
        ///     The statement's own nullity answers, which lower the partial columns; the aggregations
        ///     an average splits into are added to it too.
        /// </param>
        internal MeasureTreeBuilder(NullityMap nullity, NullityMap statement)
        {
            _nullity = nullity;
            _statement = statement;
        }

        /// <summary>The distinct aggregations the trees read, in first-use order.</summary>
        internal IReadOnlyList<TypedCall> Partials => _partials;

        /// <summary>For each partial, the position of the measure that first used it.</summary>
        internal IReadOnlyList<int> FirstUses => _firstUses;

        /// <summary>How many nodes the trees built so far hold between them.</summary>
        internal int NodeCount { get; private set; }

        /// <summary>Whether an aggregation is part of an average rather than written as it is.</summary>
        /// <param name="call">The aggregation.</param>
        /// <returns>True when it is.</returns>
        internal bool IsSynthesized(TypedCall call)
        {
            return _synthesized.Contains(call);
        }

        /// <summary>Builds one measure's tree.</summary>
        /// <param name="expression">The measure's bound expression.</param>
        /// <param name="item">The measure's position.</param>
        /// <returns>The tree's root.</returns>
        internal MeasureNode Build(TypedExpr expression, int item)
        {
            _item = item;
            return Value(expression);
        }

        /// <summary>The written value of a truth constant, when a node is one.</summary>
        /// <param name="node">The node.</param>
        /// <param name="value">The truth value.</param>
        /// <returns>True when the node is a present truth constant.</returns>
        private static bool IsConstant(MeasureNode node, out bool value)
        {
            if (node is MeasureConstantNode { Type: BoundType.Bool, Value.IsNull: false } constant)
            {
                value = constant.Value.Boolean;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>Builds a node read as a value.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Value(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral literal => Literal(literal),
                TypedNull absent => Absent(absent.Type),
                TypedParameter parameter => new MeasureParameterNode(NodeCount++, parameter.Type, parameter.Symbol.Name),
                TypedNegate negate => new MeasureNegateNode(NodeCount++, Value(negate.Operand)),
                TypedArithmetic arithmetic => new MeasureArithmeticNode(
                    NodeCount++,
                    arithmetic.Operator,
                    Value(arithmetic.Left),
                    Value(arithmetic.Right)),
                TypedCall call => Call(call),

                // Everything left is a truth value, which as a value is always present.
                _ => Truth(node),
            };
        }

        /// <summary>Builds a node read as a truth value, folding what is already settled.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Truth(TypedExpr node)
        {
            switch (node)
            {
                case TypedLiteral { Value: bool flag }:
                    return Constant(flag);

                case TypedNull:
                    // An absent truth value reads as false.
                    return Constant(false);

                case TypedLogical logical:
                    return Logical(logical);

                case TypedNot not:
                    {
                        MeasureNode operand = Truth(not.Operand);
                        return IsConstant(operand, out bool settled)
                            ? Constant(!settled)
                            : operand is MeasureNotNode inner
                                ? inner.Operand
                                : new MeasureNotNode(NodeCount++, operand);
                    }

                case TypedComparison comparison:
                    return Comparison(comparison);

                case TypedIn membership:
                    return Membership(membership);

                case TypedIsNull test:
                    {
                        QueryexNullity nullity = _nullity[test.Operand];
                        if (nullity != QueryexNullity.Nullable)
                        {
                            // Asking whether something always present is absent has an answer already.
                            return Constant(nullity == QueryexNullity.Null != test.Negated);
                        }

                        return new MeasureIsNullNode(NodeCount++, Value(test.Operand), test.Negated);
                    }

                default:
                    // A value read as a truth value: one that is always absent reads as false.
                    return _nullity[node] == QueryexNullity.Null ? Constant(false) : Value(node);
            }
        }

        /// <summary>Builds a conjunction or a disjunction, folding operands already settled.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Logical(TypedLogical node)
        {
            bool conjunction = node.Operator == LogicalOperator.And;
            ImmutableArray<MeasureNode>.Builder operands = ImmutableArray.CreateBuilder<MeasureNode>();
            foreach (TypedExpr operand in node.Operands)
            {
                MeasureNode built = Truth(operand);
                if (IsConstant(built, out bool settled))
                {
                    // One false in a conjunction, or one true in a disjunction, settles it; the other
                    // value says nothing.
                    if (settled != conjunction)
                    {
                        return Constant(settled);
                    }

                    continue;
                }

                operands.Add(built);
            }

            return operands.Count switch
            {
                0 => Constant(conjunction),
                1 => operands[0],
                _ => new MeasureLogicalNode(NodeCount++, conjunction, operands.ToImmutable()),
            };
        }

        /// <summary>Builds a comparison, settling one against a value that is always absent.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Comparison(TypedComparison node)
        {
            QueryexNullity left = _nullity[node.Left];
            QueryexNullity right = _nullity[node.Right];
            if (left != QueryexNullity.Null && right != QueryexNullity.Null)
            {
                return new MeasureComparisonNode(NodeCount++, node.Operator, Value(node.Left), Value(node.Right));
            }

            // Nothing is less or greater than a value that is not there.
            if (node.Operator is not (ComparisonOperator.Equal or ComparisonOperator.NotEqual))
            {
                return Constant(false);
            }

            bool wantsEqual = node.Operator == ComparisonOperator.Equal;
            if (left == QueryexNullity.Null && right == QueryexNullity.Null)
            {
                return Constant(wantsEqual);
            }

            // Comparing with something never there asks whether the other side is there either.
            bool leftIsAbsent = left == QueryexNullity.Null;
            TypedExpr other = leftIsAbsent ? node.Right : node.Left;
            QueryexNullity otherNullity = leftIsAbsent ? right : left;
            return otherNullity == QueryexNullity.NotNull
                ? Constant(!wantsEqual)
                : new MeasureIsNullNode(NodeCount++, Value(other), negated: !wantsEqual);
        }

        /// <summary>Builds a membership test, leaving out the elements known to be absent.</summary>
        /// <param name="node">The bound node.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Membership(TypedIn node)
        {
            QueryexNullity valueNullity = _nullity[node.Value];
            bool anyAbsent = false;
            int uncertain = 0;
            foreach (TypedExpr element in node.Elements)
            {
                QueryexNullity nullity = _nullity[element];
                anyAbsent |= nullity == QueryexNullity.Null;
                uncertain += nullity == QueryexNullity.Nullable ? 1 : 0;
            }

            ImmutableArray<MeasureNode>.Builder elements = ImmutableArray.CreateBuilder<MeasureNode>();
            if (valueNullity == QueryexNullity.Null)
            {
                // A value never there matches an element never there, and otherwise whichever of the
                // elements that might be absent is.
                if (anyAbsent)
                {
                    return Constant(true);
                }

                if (uncertain == 0)
                {
                    return Constant(false);
                }

                foreach (TypedExpr element in node.Elements)
                {
                    if (_nullity[element] == QueryexNullity.Nullable)
                    {
                        elements.Add(Value(element));
                    }
                }

                return new MeasureInNode(NodeCount++, null, elements.ToImmutable(), matchesAbsent: false);
            }

            MeasureNode value = Value(node.Value);
            foreach (TypedExpr element in node.Elements)
            {
                if (_nullity[element] != QueryexNullity.Null)
                {
                    elements.Add(Value(element));
                }
            }

            bool matchesAbsent = anyAbsent && valueNullity == QueryexNullity.Nullable;
            return elements.Count == 0 && !matchesAbsent
                ? Constant(false)
                : new MeasureInNode(NodeCount++, value, elements.ToImmutable(), matchesAbsent);
        }

        /// <summary>Builds a call.</summary>
        /// <param name="call">The bound call.</param>
        /// <returns>The tree node.</returns>
        /// <exception cref="InvalidOperationException">The call is outside what a measure may use.</exception>
        private MeasureNode Call(TypedCall call)
        {
            if (call.IsAggregate)
            {
                return Aggregate(call);
            }

            if (call.Signature.Emit.ResultShape == EmitShape.Predicate)
            {
                return Truth(call);
            }

            switch (call.Definition.Name.ToUpperInvariant())
            {
                case "IF":
                    // A condition that is a written truth value takes the whole choice with it, as it
                    // does in the statement, so the unchosen branch takes no part in the type either.
                    if (call.Arguments[0] is TypedLiteral { Value: bool taken })
                    {
                        return Value(call.Arguments[taken ? 1 : 2]);
                    }

                    return new MeasureIfNode(
                        NodeCount++,
                        call.Type,
                        Truth(call.Arguments[0]),
                        Value(call.Arguments[1]),
                        Value(call.Arguments[2]));

                case "COALESCE":
                    return new MeasureCoalesceNode(NodeCount++, call.Type, [.. call.Arguments.Select(Value)]);

                case "CAST":
                    // Only the identity reaches here: converting a value to its own type leaves it
                    // alone, and converting the absent value gives it the type converted to.
                    return call.Arguments[0] is TypedNull ? Absent(call.Type) : Value(call.Arguments[0]);

                case "ABS":
                    return new MeasureNumericFunctionNode(NodeCount++, MeasureNumericFunction.Abs, Value(call.Arguments[0]));

                case "FLOOR":
                    return new MeasureNumericFunctionNode(NodeCount++, MeasureNumericFunction.Floor, Value(call.Arguments[0]));

                case "CEILING":
                    return new MeasureNumericFunctionNode(NodeCount++, MeasureNumericFunction.Ceiling, Value(call.Arguments[0]));

                case "ROUND":
                    return new MeasureRoundNode(NodeCount++, Value(call.Arguments[0]), Value(call.Arguments[1]));

                case "YEAR":
                    return new MeasureDatePartNode(NodeCount++, MeasureDatePart.Year, Value(call.Arguments[0]));

                case "QUARTER":
                    return new MeasureDatePartNode(NodeCount++, MeasureDatePart.Quarter, Value(call.Arguments[0]));

                case "MONTH":
                    return new MeasureDatePartNode(NodeCount++, MeasureDatePart.Month, Value(call.Arguments[0]));

                case "DAY":
                    return new MeasureDatePartNode(NodeCount++, MeasureDatePart.Day, Value(call.Arguments[0]));

                case "DIFFDAYS":
                    return Diff(MeasureDiffUnit.Days, call);

                case "DIFFHOURS":
                    return Diff(MeasureDiffUnit.Hours, call);

                case "DIFFMINUTES":
                    return Diff(MeasureDiffUnit.Minutes, call);

                case "DIFFSECONDS":
                    return Diff(MeasureDiffUnit.Seconds, call);

                case "TODAY":
                    return new MeasureContextNode(NodeCount++, call.Type, MeasureContextValue.Today);

                case "NOW":
                    return new MeasureContextNode(NodeCount++, call.Type, MeasureContextValue.Now);

                case "ME":
                    return new MeasureContextNode(NodeCount++, call.Type, MeasureContextValue.UserId);

                default:
                    throw new InvalidOperationException(
                        "The function '" + call.Definition.Name + "' is outside what a measure may use, and binding refuses it.");
            }
        }

        /// <summary>Builds a measurement of elapsed time.</summary>
        /// <param name="unit">The unit.</param>
        /// <param name="call">The bound call.</param>
        /// <returns>The tree node.</returns>
        private MeasureDiffNode Diff(MeasureDiffUnit unit, TypedCall call)
        {
            return new MeasureDiffNode(NodeCount++, unit, Value(call.Arguments[0]), Value(call.Arguments[1]));
        }

        /// <summary>Builds an aggregation: the partial it reads, or for an average a sum over a count.</summary>
        /// <param name="call">The bound call.</param>
        /// <returns>The tree node.</returns>
        private MeasureNode Aggregate(TypedCall call)
        {
            if (!call.Definition.Name.Equals("avg", StringComparison.OrdinalIgnoreCase))
            {
                return new MeasurePartialNode(NodeCount++, call.Type, PartialOf(call));
            }

            // The sum and the count of the same argument under the same condition. Their quotient
            // lands on the backend's own average type, and is absent exactly when the count is zero,
            // since the sum is then absent too.
            FunctionDefinition sum = FunctionRegistry.Find("sum")!;
            FunctionDefinition count = FunctionRegistry.Find("count")!;
            int signature = call.Definition.Signatures.IndexOf(call.Signature);
            TypedCall total = Synthesized(call, sum, sum.Signatures[signature]);

            // The count's overloads start with the one that counts rows, so each of the average's
            // overloads lines up with the one after it.
            TypedCall tally = Synthesized(call, count, count.Signatures[signature + 1]);
            _synthesized.Add(total);
            _synthesized.Add(tally);
            _nullity.Set(total, _nullity[call]);
            _nullity.Set(tally, QueryexNullity.NotNull);
            _statement.Set(total, _statement[call]);
            _statement.Set(tally, QueryexNullity.NotNull);

            MeasureNode dividend = new MeasurePartialNode(NodeCount++, BoundType.Numeric, PartialOf(total));
            MeasureNode divisor = new MeasurePartialNode(NodeCount++, BoundType.Numeric, PartialOf(tally));
            return new MeasureArithmeticNode(NodeCount++, ArithmeticOperator.Divide, dividend, divisor);
        }

        /// <summary>An aggregation of the same arguments as another, by another function.</summary>
        /// <param name="call">The aggregation whose arguments are taken.</param>
        /// <param name="definition">The function.</param>
        /// <param name="signature">The overload.</param>
        /// <returns>The bound call.</returns>
        private static TypedCall Synthesized(TypedCall call, FunctionDefinition definition, FunctionSignature signature)
        {
            return new TypedCall(
                call.Span,
                BoundType.Numeric,
                definition,
                signature,
                call.Arguments,
                -1,
                [.. new string?[signature.Parameters.Length]],
                []);
        }

        /// <summary>The partial an aggregation reads, adding it when no equal one was seen.</summary>
        /// <param name="call">The aggregation.</param>
        /// <returns>The partial's position.</returns>
        private int PartialOf(TypedCall call)
        {
            for (int index = 0; index < _partials.Count; index++)
            {
                if (TypedEquivalence.AreEquivalent(call, _partials[index]))
                {
                    return index;
                }
            }

            _partials.Add(call);
            _firstUses.Add(_item);
            return _partials.Count - 1;
        }

        /// <summary>Builds a literal.</summary>
        /// <param name="literal">The bound literal.</param>
        /// <returns>The tree node.</returns>
        private MeasureConstantNode Literal(TypedLiteral literal)
        {
            switch (literal.Value)
            {
                case bool flag:
                    return Constant(flag);

                case SqlDecimal or decimal:
                    {
                        // Written at the precision and scale it was written with, as the statement
                        // binds it; a value with no written form takes its own.
                        SqlDecimal number = literal.Value is SqlDecimal written ? written : new SqlDecimal((decimal)literal.Value);
                        NumericShape shape = literal.Precision > 0
                            ? NumericShape.Decimal(literal.Precision, literal.Scale)
                            : NumericShape.Decimal(number.Precision, number.Scale);
                        return new MeasureConstantNode(
                            NodeCount++,
                            BoundType.Numeric,
                            new MeasureValue(QueryexType.QxNumeric, false, MeasureArithmetic.At(number, shape)),
                            shape,
                            isBareNull: false);
                    }

                case string text:
                    return Typed(literal.Type, new MeasureValue(QueryexType.QxString, false, text: text));

                case DateOnly date:
                    return Typed(literal.Type, new MeasureValue(QueryexType.QxDate, false, date: date));

                case DateTime moment:
                    return Typed(literal.Type, new MeasureValue(QueryexType.QxDateTime, false, dateTime: moment));

                case DateTimeOffset instant:
                    return Typed(literal.Type, new MeasureValue(QueryexType.QxDateTimeOffset, false, dateTimeOffset: instant));

                default:
                    throw new InvalidOperationException(
                        "A literal of type " + BoundTypes.Name(literal.Type) + " is outside what a measure may use.");
            }
        }

        /// <summary>A constant that is not a number.</summary>
        /// <param name="type">Its type.</param>
        /// <param name="value">Its value.</param>
        /// <returns>The tree node.</returns>
        private MeasureConstantNode Typed(BoundType type, MeasureValue value)
        {
            return new MeasureConstantNode(NodeCount++, type, value, default, isBareNull: false);
        }

        /// <summary>A truth constant.</summary>
        /// <param name="value">The truth value.</param>
        /// <returns>The tree node.</returns>
        private MeasureConstantNode Constant(bool value)
        {
            return Typed(BoundType.Bool, new MeasureValue(QueryexType.QxBool, false, boolean: value));
        }

        /// <summary>The absent value of a type.</summary>
        /// <param name="type">The type; <see cref="BoundType.Null" /> for the absent value with no type of its own.</param>
        /// <returns>The tree node.</returns>
        /// <remarks>
        ///     A typed absent number is written as a <c>decimal(38, 6)</c>, and takes part in the type
        ///     of a choice it is one branch of; the absent value with no type of its own is an
        ///     <c>int</c> to the backend, and takes no part.
        /// </remarks>
        private MeasureConstantNode Absent(BoundType type)
        {
            bool bare = type == BoundType.Null;
            QueryexType visible = bare ? QueryexType.QxNumeric : BoundTypes.ToPublic(type);
            NumericShape shape = bare
                ? NumericShape.Whole(WholeWidth.Int)
                : type == BoundType.Numeric ? NumericShape.Decimal(38, 6) : default;
            return new MeasureConstantNode(NodeCount++, type, new MeasureValue(visible, true), shape, bare);
        }
    }
}
