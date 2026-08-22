// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Emit;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     One node of a lowered plan.
    /// </summary>
    /// <remarks>
    ///     Split by how the result is consumed rather than by what it is: whether something is read
    ///     as a truth value or as a scalar is a property of where it sits, and making that the
    ///     shape of the tree means the top-down walk that decides it happens once, during lowering,
    ///     and the writer that follows has no decisions left to make.
    /// </remarks>
    internal abstract class PlanNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        private protected PlanNode(QueryexSpan span)
        {
            Span = span;
        }

        /// <summary>Where the expression it came from sits in the input.</summary>
        internal QueryexSpan Span { get; }
    }

    /// <summary>A plan node consumed as a scalar.</summary>
    internal abstract class PlanValue : PlanNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="storeType">The type the backend will hold the result in.</param>
        private protected PlanValue(QueryexSpan span, BoundType type, QueryexStoreType storeType)
            : base(span)
        {
            Type = type;
            StoreType = storeType;
        }

        /// <summary>The value's type in the language.</summary>
        internal BoundType Type { get; }

        /// <summary>
        ///     The type the backend will hold the result in.
        /// </summary>
        /// <remarks>
        ///     Carried rather than worked out again when the statement is written, because several
        ///     of the writer's decisions turn on it and every one of them is silent when it goes
        ///     wrong: whether a division keeps its remainder, whether a running total overflows, and
        ///     whether joining two pieces of text clips them.
        /// </remarks>
        internal QueryexStoreType StoreType { get; }

        /// <summary>
        ///     Whether this node costs nothing to write twice.
        /// </summary>
        /// <remarks>
        ///     The guards that make comparison total need their operands more than once, so this is
        ///     what decides whether an operand has to be given a name first. Only a column, a bound
        ///     parameter, a hoisted variable, a binding, and an engine-authored constant qualify.
        /// </remarks>
        internal virtual bool IsAtomic => false;
    }

    /// <summary>A plan node consumed as a truth value.</summary>
    /// <remarks>
    ///     Every form here is two-valued. Nothing the plan can express evaluates to the backend's
    ///     third possibility, which is what makes wrapping any of them in a negation sound.
    /// </remarks>
    internal abstract class PlanPredicate : PlanNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        private protected PlanPredicate(QueryexSpan span)
            : base(span)
        {
        }
    }

    /// <summary>A column of one joined table.</summary>
    /// <remarks>The only place a host-authored column name enters the plan.</remarks>
    internal sealed class PlanColumn : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the path sits in the input.</param>
        /// <param name="table">The join whose alias qualifies the column.</param>
        /// <param name="property">The property whose column is read.</param>
        internal PlanColumn(QueryexSpan span, JoinNode table, PropertyDescriptor property)
            : base(span, BoundTypes.FromPublic(property.Type), StoreTypes.OfProperty(property))
        {
            Table = table;
            Property = property;
        }

        /// <summary>The join whose alias qualifies the column.</summary>
        internal JoinNode Table { get; }

        /// <summary>The property whose column is read.</summary>
        internal PropertyDescriptor Property { get; }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>A bound parameter.</summary>
    internal sealed class PlanParameterRef : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="slot">The slot the host binds.</param>
        internal PlanParameterRef(QueryexSpan span, ParameterSlot slot)
            : base(span, slot.Type, slot.StoreType)
        {
            Slot = slot;
        }

        /// <summary>The slot the host binds.</summary>
        internal ParameterSlot Slot { get; }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>A variable computed once before the statement runs.</summary>
    internal sealed class PlanVariableRef : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="variable">The variable.</param>
        internal PlanVariableRef(QueryexSpan span, HoistedVariable variable)
            : base(span, variable.Type, StoreTypes.Default(variable.Type, null, 0, 0))
        {
            Variable = variable;
        }

        /// <summary>The variable.</summary>
        internal HoistedVariable Variable { get; }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>A value computed once per row and given a name.</summary>
    internal sealed class PlanBindingRef : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="binding">The binding.</param>
        internal PlanBindingRef(QueryexSpan span, ValueBinding binding)
            : base(span, binding.Type, binding.Value.StoreType)
        {
            Binding = binding;
        }

        /// <summary>The binding.</summary>
        internal ValueBinding Binding { get; }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>The absent value, typed.</summary>
    internal sealed class PlanNullValue : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="type">The type it stands in for.</param>
        internal PlanNullValue(QueryexSpan span, BoundType type)
            : base(span, type, StoreTypes.Default(type, null, 0, 0))
        {
        }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>A truth value written as one of the backend's own, which is engine-authored text.</summary>
    internal sealed class PlanBoolConstant : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="value">The value.</param>
        internal PlanBoolConstant(QueryexSpan span, bool value)
            : base(span, BoundType.Bool, QueryexStoreType.QxInt)
        {
            Value = value;
        }

        /// <summary>The value.</summary>
        internal bool Value { get; }

        /// <inheritdoc />
        internal override bool IsAtomic => true;
    }

    /// <summary>A value produced by one of the function library's emission patterns.</summary>
    internal sealed class PlanTemplateValue : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="storeType">The type the backend will hold the result in.</param>
        /// <param name="template">The pattern.</param>
        /// <param name="operands">The operands, positional to the pattern's placeholders.</param>
        internal PlanTemplateValue(
            QueryexSpan span,
            BoundType type,
            QueryexStoreType storeType,
            EmitTemplate template,
            ImmutableArray<PlanNode> operands)
            : base(span, type, storeType)
        {
            Template = template;
            Operands = operands;
        }

        /// <summary>The pattern.</summary>
        internal EmitTemplate Template { get; }

        /// <summary>The operands, positional to the pattern's placeholders.</summary>
        internal ImmutableArray<PlanNode> Operands { get; }
    }

    /// <summary>A truth value produced by one of the function library's emission patterns.</summary>
    internal sealed class PlanTemplatePredicate : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="template">The pattern.</param>
        /// <param name="operands">The operands, positional to the pattern's placeholders.</param>
        internal PlanTemplatePredicate(
            QueryexSpan span,
            EmitTemplate template,
            ImmutableArray<PlanNode> operands)
            : base(span)
        {
            Template = template;
            Operands = operands;
        }

        /// <summary>The pattern.</summary>
        internal EmitTemplate Template { get; }

        /// <summary>The operands, positional to the pattern's placeholders.</summary>
        internal ImmutableArray<PlanNode> Operands { get; }
    }

    /// <summary>An aggregation over the rows of a group.</summary>
    internal sealed class PlanAggregate : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="kind">Which aggregate is computed.</param>
        /// <param name="argument">The value aggregated, when there is one.</param>
        /// <param name="condition">The per-row condition, when there is one.</param>
        /// <param name="argumentType">The argument's type in the language.</param>
        internal PlanAggregate(
            QueryexSpan span,
            AggregateKind kind,
            PlanValue? argument,
            PlanPredicate? condition,
            BoundType argumentType)
            : base(
                span,
                kind == AggregateKind.Count ? BoundType.Numeric : argumentType,
                ResultOf(kind, argument))
        {
            Kind = kind;
            Argument = argument;
            Condition = condition;
            ArgumentType = argumentType;
        }

        /// <summary>Which aggregate is computed.</summary>
        internal AggregateKind Kind { get; }

        /// <summary>The value aggregated, when there is one.</summary>
        internal PlanValue? Argument { get; }

        /// <summary>The per-row condition, when there is one.</summary>
        internal PlanPredicate? Condition { get; }

        /// <summary>The argument's type in the language.</summary>
        internal BoundType ArgumentType { get; }

        /// <summary>The type the backend holds an aggregation's result in.</summary>
        /// <param name="kind">Which aggregate is computed.</param>
        /// <param name="argument">The value aggregated, when there is one.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     A count is taken in the wide form, and a total or an average over whole numbers is
        ///     widened before it is taken — so both come back wider than they went in, and the
        ///     result type has to say so.
        /// </remarks>
        private static QueryexStoreType ResultOf(AggregateKind kind, PlanValue? argument)
        {
            if (kind == AggregateKind.Count)
            {
                return QueryexStoreType.QxBigInt;
            }

            QueryexStoreType inner = argument?.StoreType ?? SqlTypes.Decimal;
            return kind switch
            {
                AggregateKind.Sum => SqlTypes.SumOf(inner),
                AggregateKind.Average => SqlTypes.AverageOf(inner),
                AggregateKind.Count or AggregateKind.Minimum or AggregateKind.Maximum => inner,
                _ => inner,
            };
        }
    }

    /// <summary>
    ///     Arithmetic, or joining text.
    /// </summary>
    /// <remarks>
    ///     Kept as an operator rather than flattened into a pattern, because division has to be
    ///     written differently depending on how its operands are stored — two whole-number columns
    ///     divide to a whole number unless one side is widened first — and how something is stored
    ///     is known only when the statement is written.
    /// </remarks>
    internal sealed class PlanArithmetic : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal PlanArithmetic(
            QueryexSpan span,
            ArithmeticOperator op,
            PlanValue left,
            PlanValue right)
            : base(
                span,
                op == ArithmeticOperator.Concat ? BoundType.String : BoundType.Numeric,
                SqlTypes.ArithmeticOf(op, left.StoreType, right.StoreType))
        {
            Operator = op;
            Left = left;
            Right = right;
        }

        /// <summary>The operator.</summary>
        internal ArithmeticOperator Operator { get; }

        /// <summary>The left operand.</summary>
        internal PlanValue Left { get; }

        /// <summary>The right operand.</summary>
        internal PlanValue Right { get; }
    }

    /// <summary>Arithmetic negation.</summary>
    internal sealed class PlanNegate : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="operand">The operand.</param>
        internal PlanNegate(QueryexSpan span, PlanValue operand)
            : base(span, BoundType.Numeric, operand.StoreType)
        {
            Operand = operand;
        }

        /// <summary>The operand.</summary>
        internal PlanValue Operand { get; }
    }

    /// <summary>A truth value read as a scalar.</summary>
    internal sealed class PlanValueOfPredicate : PlanValue
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="inner">The predicate.</param>
        internal PlanValueOfPredicate(QueryexSpan span, PlanPredicate inner)
            : base(span, BoundType.Bool, QueryexStoreType.QxInt)
        {
            Inner = inner;
        }

        /// <summary>The predicate.</summary>
        internal PlanPredicate Inner { get; }
    }

    /// <summary>How reading a scalar as a truth value deals with the scalar being absent.</summary>
    internal enum PlanBoolGuard
    {
        /// <summary>None needed: the value is certainly present.</summary>
        None,

        /// <summary>
        ///     Test for presence, then compare. Costs a second mention of the value, so it is used
        ///     only where the value is atomic — and it is worth it there, because it leaves a plain
        ///     column comparison the backend can seek on.
        /// </summary>
        TestPresence,

        /// <summary>
        ///     Substitute falsehood for absence, then compare. Mentions the value once, at the price
        ///     of wrapping the column so the backend can no longer seek on it — which is moot,
        ///     because the values that need this form are computed rather than stored.
        /// </summary>
        SubstituteAbsent,
    }

    /// <summary>A scalar read as a truth value.</summary>
    /// <remarks>
    ///     An absent truth value has to read as false rather than as undecided. Leaving it undecided
    ///     would make a negation of it drop the row instead of keeping it, which is the one place
    ///     where the backend's three-valued logic would become visible in the language.
    /// </remarks>
    internal sealed class PlanPredicateOfValue : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="inner">The value.</param>
        /// <param name="guard">How absence is dealt with.</param>
        internal PlanPredicateOfValue(QueryexSpan span, PlanValue inner, PlanBoolGuard guard)
            : base(span)
        {
            Inner = inner;
            Guard = guard;
        }

        /// <summary>The value.</summary>
        internal PlanValue Inner { get; }

        /// <summary>How absence is dealt with.</summary>
        internal PlanBoolGuard Guard { get; }
    }

    /// <summary>A conjunction or a disjunction.</summary>
    internal sealed class PlanJunction : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="isConjunction">Whether every operand has to hold.</param>
        /// <param name="operands">The operands.</param>
        internal PlanJunction(QueryexSpan span, bool isConjunction, ImmutableArray<PlanPredicate> operands)
            : base(span)
        {
            IsConjunction = isConjunction;
            Operands = operands;
        }

        /// <summary>Whether every operand has to hold.</summary>
        internal bool IsConjunction { get; }

        /// <summary>The operands.</summary>
        internal ImmutableArray<PlanPredicate> Operands { get; }
    }

    /// <summary>A negation.</summary>
    internal sealed class PlanNegation : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="operand">The operand.</param>
        internal PlanNegation(QueryexSpan span, PlanPredicate operand)
            : base(span)
        {
            Operand = operand;
        }

        /// <summary>The operand.</summary>
        internal PlanPredicate Operand { get; }
    }

    /// <summary>A truth value already known at compile time.</summary>
    internal sealed class PlanConstantPredicate : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="value">The value.</param>
        internal PlanConstantPredicate(QueryexSpan span, bool value)
            : base(span)
        {
            Value = value;
        }

        /// <summary>The value.</summary>
        internal bool Value { get; }
    }

    /// <summary>Which way one raw comparison goes.</summary>
    internal enum PlanComparison
    {
        /// <summary>Equality.</summary>
        Equal,

        /// <summary>Inequality.</summary>
        NotEqual,

        /// <summary>Strictly less.</summary>
        Less,

        /// <summary>Less or equal.</summary>
        LessOrEqual,

        /// <summary>Strictly greater.</summary>
        Greater,

        /// <summary>Greater or equal.</summary>
        GreaterOrEqual,
    }

    /// <summary>
    ///     One raw comparison, with whatever guards it needs already built around it.
    /// </summary>
    /// <remarks>
    ///     The guards are not part of this node: they are ordinary conjunctions and absence tests
    ///     that lowering assembles, which is what keeps the writer from having to know the rules.
    /// </remarks>
    internal sealed class PlanCompare : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="comparison">Which way it goes.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal PlanCompare(
            QueryexSpan span,
            PlanComparison comparison,
            PlanValue left,
            PlanValue right)
            : base(span)
        {
            Comparison = comparison;
            Left = left;
            Right = right;
        }

        /// <summary>Which way it goes.</summary>
        internal PlanComparison Comparison { get; }

        /// <summary>The left operand.</summary>
        internal PlanValue Left { get; }

        /// <summary>The right operand.</summary>
        internal PlanValue Right { get; }
    }

    /// <summary>An absence test.</summary>
    internal sealed class PlanIsNull : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="value">The value tested.</param>
        /// <param name="negated">Whether the test is for presence instead.</param>
        internal PlanIsNull(QueryexSpan span, PlanValue value, bool negated)
            : base(span)
        {
            Value = value;
            Negated = negated;
        }

        /// <summary>The value tested.</summary>
        internal PlanValue Value { get; }

        /// <summary>Whether the test is for presence instead.</summary>
        internal bool Negated { get; }
    }

    /// <summary>
    ///     A set-membership test the backend can seek on.
    /// </summary>
    /// <remarks>
    ///     Kept as one node rather than expanded into a chain of equalities, because that is the
    ///     whole reason the language has the construct: on an indexed column the single form is what
    ///     the backend can turn into a seek.
    /// </remarks>
    internal sealed class PlanIn : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="value">The value tested.</param>
        /// <param name="elements">The elements, every one of them certainly present.</param>
        internal PlanIn(QueryexSpan span, PlanValue value, ImmutableArray<PlanValue> elements)
            : base(span)
        {
            Value = value;
            Elements = elements;
        }

        /// <summary>The value tested.</summary>
        internal PlanValue Value { get; }

        /// <summary>The elements.</summary>
        internal ImmutableArray<PlanValue> Elements { get; }
    }

    /// <summary>
    ///     A comparison written so that each operand appears exactly once.
    /// </summary>
    /// <remarks>
    ///     Used where a group-level value cannot be given a name first: an aggregate is computed
    ///     after grouping, and the per-row mechanism that names a value runs before it. The form is
    ///     total by construction — absence matches absence and nothing else — which is exactly the
    ///     equality the language promises.
    /// </remarks>
    internal sealed class PlanSetComparison : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="negated">Whether the test is for inequality instead.</param>
        internal PlanSetComparison(QueryexSpan span, PlanValue left, PlanValue right, bool negated)
            : base(span)
        {
            Left = left;
            Right = right;
            Negated = negated;
        }

        /// <summary>The left operand.</summary>
        internal PlanValue Left { get; }

        /// <summary>The right operand.</summary>
        internal PlanValue Right { get; }

        /// <summary>Whether the test is for inequality instead.</summary>
        internal bool Negated { get; }
    }

    /// <summary>
    ///     A membership test written so that each operand appears exactly once.
    /// </summary>
    internal sealed class PlanSetMembership : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="value">The value tested.</param>
        /// <param name="elements">The elements.</param>
        internal PlanSetMembership(
            QueryexSpan span,
            PlanValue value,
            ImmutableArray<PlanValue> elements)
            : base(span)
        {
            Value = value;
            Elements = elements;
        }

        /// <summary>The value tested.</summary>
        internal PlanValue Value { get; }

        /// <summary>The elements.</summary>
        internal ImmutableArray<PlanValue> Elements { get; }
    }

    /// <summary>
    ///     One row's position in a hierarchy tested against one looked-up position.
    /// </summary>
    /// <remarks>
    ///     The raw test only. Whatever presence checks it needs are ordinary absence tests that
    ///     lowering conjoins around it, exactly as for a comparison — the backend's own test yields
    ///     its third possibility when either side is missing, and a predicate that did that would
    ///     stop being the complement of its own negation.
    /// </remarks>
    internal sealed class PlanHierarchyTest : PlanPredicate
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="span">Where the expression it came from sits in the input.</param>
        /// <param name="direction">Which way the test looks.</param>
        /// <param name="node">The row's own position.</param>
        /// <param name="key">The looked-up position.</param>
        internal PlanHierarchyTest(
            QueryexSpan span,
            HierarchyDirection direction,
            PlanValue node,
            PlanValue key)
            : base(span)
        {
            Direction = direction;
            Node = node;
            Key = key;
        }

        /// <summary>Which way the test looks.</summary>
        internal HierarchyDirection Direction { get; }

        /// <summary>The row's own position.</summary>
        internal PlanValue Node { get; }

        /// <summary>The looked-up position.</summary>
        internal PlanValue Key { get; }
    }
}
