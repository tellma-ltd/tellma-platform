// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>A value the statement's context slots supply.</summary>
    internal enum MeasureContextValue
    {
        /// <summary>The run's date.</summary>
        Today,

        /// <summary>The run's instant.</summary>
        Now,

        /// <summary>The signed-in user's key.</summary>
        UserId,
    }

    /// <summary>A function over one number.</summary>
    internal enum MeasureNumericFunction
    {
        /// <summary>The absolute value.</summary>
        Abs,

        /// <summary>The greatest whole number not above the value.</summary>
        Floor,

        /// <summary>The least whole number not below the value.</summary>
        Ceiling,
    }

    /// <summary>A component of a calendar date.</summary>
    internal enum MeasureDatePart
    {
        /// <summary>The year.</summary>
        Year,

        /// <summary>The quarter, 1 to 4.</summary>
        Quarter,

        /// <summary>The month, 1 to 12.</summary>
        Month,

        /// <summary>The day of the month.</summary>
        Day,
    }

    /// <summary>A unit elapsed time between two dates is measured in.</summary>
    internal enum MeasureDiffUnit
    {
        /// <summary>Days, as a fraction.</summary>
        Days,

        /// <summary>Hours, as a fraction.</summary>
        Hours,

        /// <summary>Minutes, as a fraction.</summary>
        Minutes,

        /// <summary>Whole seconds.</summary>
        Seconds,
    }

    /// <summary>
    ///     One node of a measure's evaluation tree: the part of the measure outside its aggregations,
    ///     with each aggregation replaced by the partial it reads.
    /// </summary>
    /// <remarks>
    ///     Immutable, and typed in the language's terms only: the backend type a numeric node is
    ///     held in depends on what one run's reader reported, so it is fixed per run, in an array
    ///     indexed by <see cref="Id" />.
    /// </remarks>
    internal abstract class MeasureNode
    {
        /// <summary>Initializes a node.</summary>
        /// <param name="id">The node's position among every node of the query's measures.</param>
        /// <param name="type">The node's type in the language.</param>
        private protected MeasureNode(int id, BoundType type)
        {
            Id = id;
            Type = type;
        }

        /// <summary>The node's position among every node of the query's measures.</summary>
        internal int Id { get; }

        /// <summary>The node's type in the language.</summary>
        internal BoundType Type { get; }

        /// <summary>Whether the node is a number, and so has a backend type per run.</summary>
        internal bool IsNumeric => Type == BoundType.Numeric;
    }

    /// <summary>The value one partial holds for the cell.</summary>
    internal sealed class MeasurePartialNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The partial's type.</param>
        /// <param name="partial">The partial's position among the query's partials.</param>
        internal MeasurePartialNode(int id, BoundType type, int partial)
            : base(id, type)
        {
            Partial = partial;
        }

        /// <summary>The partial's position among the query's partials.</summary>
        internal int Partial { get; }
    }

    /// <summary>A value fixed at compilation: a literal, or the absent value.</summary>
    internal sealed class MeasureConstantNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The value's type.</param>
        /// <param name="value">The value.</param>
        /// <param name="shape">For a number, the backend type it is written at.</param>
        /// <param name="isBareNull">
        ///     Whether it is the absent value with no type of its own, which takes no part in deciding
        ///     the type of a choice it is one branch of.
        /// </param>
        internal MeasureConstantNode(int id, BoundType type, MeasureValue value, NumericShape shape, bool isBareNull)
            : base(id, type)
        {
            Value = value;
            Shape = shape;
            IsBareNull = isBareNull;
        }

        /// <summary>The value.</summary>
        internal MeasureValue Value { get; }

        /// <summary>For a number, the backend type it is written at.</summary>
        internal NumericShape Shape { get; }

        /// <summary>Whether it is the absent value with no type of its own.</summary>
        internal bool IsBareNull { get; }
    }

    /// <summary>A declared parameter's value.</summary>
    internal sealed class MeasureParameterNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The parameter's declared type.</param>
        /// <param name="name">The parameter's declared name.</param>
        internal MeasureParameterNode(int id, BoundType type, string name)
            : base(id, type)
        {
            Name = name;
        }

        /// <summary>The parameter's declared name.</summary>
        internal string Name { get; }
    }

    /// <summary>A value one of the statement's context slots supplies.</summary>
    internal sealed class MeasureContextNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The value's type.</param>
        /// <param name="value">Which slot.</param>
        internal MeasureContextNode(int id, BoundType type, MeasureContextValue value)
            : base(id, type)
        {
            Value = value;
        }

        /// <summary>Which slot.</summary>
        internal MeasureContextValue Value { get; }
    }

    /// <summary>A number negated.</summary>
    internal sealed class MeasureNegateNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="operand">The number.</param>
        internal MeasureNegateNode(int id, MeasureNode operand)
            : base(id, BoundType.Numeric)
        {
            Operand = operand;
        }

        /// <summary>The number.</summary>
        internal MeasureNode Operand { get; }
    }

    /// <summary>One of the four arithmetic operations.</summary>
    internal sealed class MeasureArithmeticNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="op">The operation: add, subtract, multiply or divide.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal MeasureArithmeticNode(int id, ArithmeticOperator op, MeasureNode left, MeasureNode right)
            : base(id, BoundType.Numeric)
        {
            Operator = op;
            Left = left;
            Right = right;
        }

        /// <summary>The operation.</summary>
        internal ArithmeticOperator Operator { get; }

        /// <summary>The left operand.</summary>
        internal MeasureNode Left { get; }

        /// <summary>The right operand.</summary>
        internal MeasureNode Right { get; }
    }

    /// <summary>A comparison of two present-or-absent values, total over absence.</summary>
    internal sealed class MeasureComparisonNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="op">Which way it compares.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        internal MeasureComparisonNode(int id, ComparisonOperator op, MeasureNode left, MeasureNode right)
            : base(id, BoundType.Bool)
        {
            Operator = op;
            Left = left;
            Right = right;
        }

        /// <summary>Which way it compares.</summary>
        internal ComparisonOperator Operator { get; }

        /// <summary>The left operand.</summary>
        internal MeasureNode Left { get; }

        /// <summary>The right operand.</summary>
        internal MeasureNode Right { get; }
    }

    /// <summary>Whether a value equals one of a list, absence matching absence.</summary>
    internal sealed class MeasureInNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="value">The value, or null where it is known to be absent and is not read.</param>
        /// <param name="elements">The elements that are read; those known to be absent are not.</param>
        /// <param name="matchesAbsent">Whether some element is known to be absent.</param>
        internal MeasureInNode(int id, MeasureNode? value, ImmutableArray<MeasureNode> elements, bool matchesAbsent)
            : base(id, BoundType.Bool)
        {
            Value = value;
            Elements = elements;
            MatchesAbsent = matchesAbsent;
        }

        /// <summary>The value, or null where it is known to be absent.</summary>
        internal MeasureNode? Value { get; }

        /// <summary>The elements that are read.</summary>
        internal ImmutableArray<MeasureNode> Elements { get; }

        /// <summary>Whether some element is known to be absent, so an absent value matches.</summary>
        internal bool MatchesAbsent { get; }
    }

    /// <summary>Whether a value is absent, or present.</summary>
    internal sealed class MeasureIsNullNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="operand">The value.</param>
        /// <param name="negated">Whether the test asks for presence rather than absence.</param>
        internal MeasureIsNullNode(int id, MeasureNode operand, bool negated)
            : base(id, BoundType.Bool)
        {
            Operand = operand;
            Negated = negated;
        }

        /// <summary>The value.</summary>
        internal MeasureNode Operand { get; }

        /// <summary>Whether the test asks for presence.</summary>
        internal bool Negated { get; }
    }

    /// <summary>A conjunction or a disjunction, read left to right.</summary>
    internal sealed class MeasureLogicalNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="isConjunction">Whether every operand has to hold, rather than one.</param>
        /// <param name="operands">The operands, at least two.</param>
        internal MeasureLogicalNode(int id, bool isConjunction, ImmutableArray<MeasureNode> operands)
            : base(id, BoundType.Bool)
        {
            IsConjunction = isConjunction;
            Operands = operands;
        }

        /// <summary>Whether every operand has to hold.</summary>
        internal bool IsConjunction { get; }

        /// <summary>The operands.</summary>
        internal ImmutableArray<MeasureNode> Operands { get; }
    }

    /// <summary>A negation.</summary>
    internal sealed class MeasureNotNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="operand">The truth value negated.</param>
        internal MeasureNotNode(int id, MeasureNode operand)
            : base(id, BoundType.Bool)
        {
            Operand = operand;
        }

        /// <summary>The truth value negated.</summary>
        internal MeasureNode Operand { get; }
    }

    /// <summary>A choice between two values; only the chosen one is read.</summary>
    internal sealed class MeasureIfNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The choice's type.</param>
        /// <param name="condition">The condition; an absent one chooses the second value.</param>
        /// <param name="whenTrue">The value when the condition holds.</param>
        /// <param name="whenFalse">The value otherwise.</param>
        internal MeasureIfNode(int id, BoundType type, MeasureNode condition, MeasureNode whenTrue, MeasureNode whenFalse)
            : base(id, type)
        {
            Condition = condition;
            WhenTrue = whenTrue;
            WhenFalse = whenFalse;
        }

        /// <summary>The condition.</summary>
        internal MeasureNode Condition { get; }

        /// <summary>The value when the condition holds.</summary>
        internal MeasureNode WhenTrue { get; }

        /// <summary>The value otherwise.</summary>
        internal MeasureNode WhenFalse { get; }
    }

    /// <summary>The first present value of several; those after it are not read.</summary>
    internal sealed class MeasureCoalesceNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="type">The result's type.</param>
        /// <param name="operands">The values, in order.</param>
        internal MeasureCoalesceNode(int id, BoundType type, ImmutableArray<MeasureNode> operands)
            : base(id, type)
        {
            Operands = operands;
        }

        /// <summary>The values, in order.</summary>
        internal ImmutableArray<MeasureNode> Operands { get; }
    }

    /// <summary>An absolute value, a floor or a ceiling.</summary>
    internal sealed class MeasureNumericFunctionNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="function">Which function.</param>
        /// <param name="operand">The number.</param>
        internal MeasureNumericFunctionNode(int id, MeasureNumericFunction function, MeasureNode operand)
            : base(id, BoundType.Numeric)
        {
            Function = function;
            Operand = operand;
        }

        /// <summary>Which function.</summary>
        internal MeasureNumericFunction Function { get; }

        /// <summary>The number.</summary>
        internal MeasureNode Operand { get; }
    }

    /// <summary>A number rounded to a number of decimal places.</summary>
    internal sealed class MeasureRoundNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="operand">The number.</param>
        /// <param name="digits">The decimal places; negative ones round to the left of the point.</param>
        internal MeasureRoundNode(int id, MeasureNode operand, MeasureNode digits)
            : base(id, BoundType.Numeric)
        {
            Operand = operand;
            Digits = digits;
        }

        /// <summary>The number.</summary>
        internal MeasureNode Operand { get; }

        /// <summary>The decimal places.</summary>
        internal MeasureNode Digits { get; }
    }

    /// <summary>A component of a calendar date, in the Gregorian calendar.</summary>
    internal sealed class MeasureDatePartNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="part">Which component.</param>
        /// <param name="operand">The date.</param>
        internal MeasureDatePartNode(int id, MeasureDatePart part, MeasureNode operand)
            : base(id, BoundType.Numeric)
        {
            Part = part;
            Operand = operand;
        }

        /// <summary>Which component.</summary>
        internal MeasureDatePart Part { get; }

        /// <summary>The date.</summary>
        internal MeasureNode Operand { get; }
    }

    /// <summary>The time elapsed from one calendar date to another.</summary>
    internal sealed class MeasureDiffNode : MeasureNode
    {
        /// <summary>Initializes the node.</summary>
        /// <param name="id">The node's position.</param>
        /// <param name="unit">The unit it is measured in.</param>
        /// <param name="from">The earlier date, for a positive answer.</param>
        /// <param name="to">The later date, for a positive answer.</param>
        internal MeasureDiffNode(int id, MeasureDiffUnit unit, MeasureNode from, MeasureNode to)
            : base(id, BoundType.Numeric)
        {
            Unit = unit;
            From = from;
            To = to;
        }

        /// <summary>The unit it is measured in.</summary>
        internal MeasureDiffUnit Unit { get; }

        /// <summary>The first date.</summary>
        internal MeasureNode From { get; }

        /// <summary>The second date.</summary>
        internal MeasureNode To { get; }
    }
}
