// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Data.SqlTypes;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>
    ///     A query's measures fixed for one run: every numeric node's backend type, and the value of
    ///     every parameter and context slot the measures read.
    /// </summary>
    /// <remarks>
    ///     Immutable once built; evaluating only reads it, so one run can be evaluated from several
    ///     threads over separate tables.
    /// </remarks>
    internal sealed class MeasureRun
    {
        /// <summary>The backend type an absent numeric parameter is bound at.</summary>
        private static readonly NumericShape AbsentNumber = NumericShape.Decimal(29, 0);

        /// <summary>The partials' precisions and scales, as the reader reported them.</summary>
        private readonly ImmutableArray<QueryexNumericType?> _decimalTypes;

        /// <summary>Every numeric node's backend type, by node.</summary>
        private readonly NumericShape[] _shapes;

        /// <summary>Every parameter's and context slot's value, by node.</summary>
        private readonly MeasureValue[] _values;

        /// <summary>Initializes a run.</summary>
        /// <param name="measures">The measures.</param>
        /// <param name="decimalTypes">The partials' precisions and scales.</param>
        /// <param name="shapes">Every numeric node's backend type.</param>
        /// <param name="values">Every parameter's and context slot's value.</param>
        private MeasureRun(
            CompiledMeasures measures,
            ImmutableArray<QueryexNumericType?> decimalTypes,
            NumericShape[] shapes,
            MeasureValue[] values)
        {
            Measures = measures;
            _decimalTypes = decimalTypes;
            _shapes = shapes;
            _values = values;
        }

        /// <summary>The query's measures.</summary>
        internal CompiledMeasures Measures { get; }

        /// <summary>Checks what one run read and fixes the measures' types for it.</summary>
        /// <param name="measures">The measures.</param>
        /// <param name="context">What the run read.</param>
        /// <returns>The prepared evaluator.</returns>
        /// <exception cref="ArgumentNullException">A member of <paramref name="context" /> is null.</exception>
        /// <exception cref="ArgumentException">What the run read does not fit the measures.</exception>
        internal static MeasureEvaluator Prepare(CompiledMeasures measures, MeasureContext context)
        {
            ArgumentNullException.ThrowIfNull(context.DecimalTypes, nameof(context));
            ArgumentNullException.ThrowIfNull(context.Arguments, nameof(context));

            ImmutableArray<QueryexNumericType?> decimalTypes = CheckDecimalTypes(measures, context.DecimalTypes);
            Dictionary<string, object?> arguments = CopyArguments(context.Arguments);

            var shapes = new NumericShape[measures.NodeCount];
            var values = new MeasureValue[measures.NodeCount];
            Preparer preparer = new(measures, decimalTypes, arguments, context, shapes, values);
            foreach (MeasureNode root in measures.Roots)
            {
                preparer.Visit(root);
            }

            MeasureRun run = new(measures, decimalTypes, shapes, values);

            // A numeric measure reports the type its value comes back at; any other reports none.
            ImmutableArray<QueryexNumericType?> resultTypes =
            [
                .. measures.Roots.Select(root => root.IsNumeric ? shapes[root.Id].ToPublic() : (QueryexNumericType?)null),
            ];

            return new MeasureEvaluator(run, resultTypes);
        }

        /// <summary>Builds the columns of a new table for this run.</summary>
        /// <param name="capacity">The cells to hold at first.</param>
        /// <returns>One column per partial.</returns>
        internal PartialColumn[] CreateColumns(int capacity)
        {
            var columns = new PartialColumn[Measures.Plans.Length];
            for (int index = 0; index < columns.Length; index++)
            {
                columns[index] = CreateColumn(Measures.Plans[index], _decimalTypes[index], capacity);
            }

            return columns;
        }

        /// <summary>Evaluates one measure over one cell.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="measure">The measure's position.</param>
        /// <returns>The value.</returns>
        /// <exception cref="QueryexEvaluationException">The measure divides by zero or overflows for the cell.</exception>
        internal MeasureValue Evaluate(PartialColumn[] columns, int cell, int measure)
        {
            return Evaluate(columns, cell, Measures.Roots[measure]);
        }

        /// <summary>Checks the reader's decimal types against the partials.</summary>
        /// <param name="measures">The measures.</param>
        /// <param name="types">The types the run supplied.</param>
        /// <returns>The types, copied.</returns>
        private static ImmutableArray<QueryexNumericType?> CheckDecimalTypes(
            CompiledMeasures measures,
            IReadOnlyList<QueryexNumericType?> types)
        {
            if (types.Count != measures.Plans.Length)
            {
                throw new ArgumentException(
                    "The decimal types must be one per partial: " + measures.Plans.Length
                        + " were expected and " + types.Count + " were given.");
            }

            for (int index = 0; index < types.Count; index++)
            {
                PartialPlan plan = measures.Plans[index];
                QueryexNumericType? type = types[index];
                if (!plan.NeedsDecimalType)
                {
                    if (type is not null)
                    {
                        throw new ArgumentException(
                            "Partial " + index + " is not a decimal, so its decimal type must be null.");
                    }

                    continue;
                }

                if (type is not QueryexNumericType given)
                {
                    throw new ArgumentException(
                        "Partial " + index + " is a decimal, so the reader's precision and scale for it are required.");
                }

                if (given.Precision is < 1 or > NumericShape.MaxPrecision || given.Scale > given.Precision)
                {
                    throw new ArgumentException(
                        "Partial " + index + "'s decimal type must have a precision from 1 to 38 and a scale no greater than it.");
                }
            }

            return [.. types];
        }

        /// <summary>Copies the arguments into a dictionary that ignores case.</summary>
        /// <param name="arguments">The arguments the run supplied.</param>
        /// <returns>The copy.</returns>
        private static Dictionary<string, object?> CopyArguments(IReadOnlyDictionary<string, object?> arguments)
        {
            Dictionary<string, object?> copy = new(arguments.Count, StringComparer.OrdinalIgnoreCase);
            foreach ((string name, object? value) in arguments)
            {
                // A dictionary that tells names apart by case can hold two values for one parameter,
                // and nothing says which of them the caller meant.
                if (!copy.TryAdd(name, value))
                {
                    throw new ArgumentException(
                        "A parameter cannot be given more than one value; '" + name
                            + "' is given one under each of two spellings.");
                }
            }

            return copy;
        }

        /// <summary>Builds the column one partial is held in.</summary>
        /// <param name="plan">How the partial is held.</param>
        /// <param name="type">Its precision and scale, for a decimal.</param>
        /// <param name="capacity">The cells to hold at first.</param>
        /// <returns>The column.</returns>
        private static PartialColumn CreateColumn(PartialPlan plan, QueryexNumericType? type, int capacity)
        {
            bool maximum = plan.IsMaximum;
            return plan.Storage switch
            {
                PartialStorage.Sum => new SumColumn(type!.Value, capacity),
                PartialStorage.Count => new CountColumn(capacity),
                PartialStorage.Whole => WholeColumn(maximum, plan.Width, capacity),
                PartialStorage.Decimal => DecimalColumn(maximum, type!.Value, capacity),
                PartialStorage.Bool => new ExtremeColumn<bool>(
                    maximum,
                    Comparer<bool>.Default,
                    null,
                    QueryexType.QxBool,
                    static (buffer, row) => buffer is bool[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(bool)),
                    static value => new MeasureValue(QueryexType.QxBool, false, boolean: value),
                    capacity),
                PartialStorage.Date => new ExtremeColumn<DateOnly>(
                    maximum,
                    Comparer<DateOnly>.Default,
                    null,
                    QueryexType.QxDate,
                    static (buffer, row) => buffer is DateOnly[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(DateOnly)),
                    static value => new MeasureValue(QueryexType.QxDate, false, date: value),
                    capacity),
                PartialStorage.DateTime => new ExtremeColumn<DateTime>(
                    maximum,
                    Comparer<DateTime>.Default,
                    null,
                    QueryexType.QxDateTime,
                    static (buffer, row) => buffer is DateTime[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(DateTime)),
                    static value => new MeasureValue(QueryexType.QxDateTime, false, dateTime: value),
                    capacity),
                PartialStorage.DateTimeOffset => new ExtremeColumn<DateTimeOffset>(
                    maximum,
                    InstantOrder.Instance,
                    null,
                    QueryexType.QxDateTimeOffset,
                    static (buffer, row) => buffer is DateTimeOffset[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(DateTimeOffset)),
                    static value => new MeasureValue(QueryexType.QxDateTimeOffset, false, dateTimeOffset: value),
                    capacity),
                PartialStorage.Text => new ExtremeColumn<string?>(
                    maximum,
                    TextOrder.Primary,
                    TextOrder.Tie,
                    QueryexType.QxString,
                    static (buffer, row) => ReadText(buffer, row),
                    static value => new MeasureValue(QueryexType.QxString, false, text: value),
                    capacity),
                PartialStorage.Path => new ExtremeColumn<string?>(
                    maximum,
                    HierarchyPathOrder.Instance,
                    TextOrder.Tie,
                    QueryexType.QxHierarchyId,
                    static (buffer, row) => ReadText(buffer, row),
                    static value => new MeasureValue(QueryexType.QxHierarchyId, false, text: value),
                    capacity),
                _ => new CountColumn(capacity),
            };
        }

        /// <summary>Builds the column a least or greatest whole number is held in.</summary>
        /// <param name="maximum">Whether the greatest value is kept.</param>
        /// <param name="width">The whole type the backend holds it in.</param>
        /// <param name="capacity">The cells to hold at first.</param>
        /// <returns>The column.</returns>
        private static ExtremeColumn<long> WholeColumn(bool maximum, WholeWidth width, int capacity)
        {
            var shape = NumericShape.Whole(width);

            // The reader's array for each width is the one element type the backend's type reads
            // as; any other is a column read as something it is not.
            Func<Array, int, long> reader = width switch
            {
                WholeWidth.TinyInt => static (buffer, row) => buffer is byte[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(byte)),
                WholeWidth.SmallInt => static (buffer, row) => buffer is short[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(short)),
                WholeWidth.Int => static (buffer, row) => buffer is int[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(int)),
                WholeWidth.BigInt or _ => static (buffer, row) => buffer is long[] values ? values[row] : throw PartialColumn.WrongBuffer(buffer, typeof(long)),
            };

            return new ExtremeColumn<long>(
                maximum,
                Comparer<long>.Default,
                null,
                QueryexType.QxNumeric,
                reader,
                value => new MeasureValue(QueryexType.QxNumeric, false, MeasureArithmetic.At(new SqlDecimal(value), shape)),
                capacity);
        }

        /// <summary>Builds the column a least or greatest decimal is held in.</summary>
        /// <param name="maximum">Whether the greatest value is kept.</param>
        /// <param name="type">The column's precision and scale.</param>
        /// <param name="capacity">The cells to hold at first.</param>
        /// <returns>The column.</returns>
        private static ExtremeColumn<Int128> DecimalColumn(bool maximum, QueryexNumericType type, int capacity)
        {
            byte precision = type.Precision;
            byte scale = type.Scale;
            return new ExtremeColumn<Int128>(
                maximum,
                Comparer<Int128>.Default,
                null,
                QueryexType.QxNumeric,
                (buffer, row) =>
                {
                    if (buffer is not decimal[] values)
                    {
                        throw PartialColumn.WrongBuffer(buffer, typeof(decimal));
                    }

                    // Taken to the column's own scale, exactly, so every value in the column compares
                    // as a whole number against every other.
                    return MeasureArithmetic.TryRescale(values[row], precision, scale, out Int128 mantissa)
                        ? mantissa
                        : throw PartialColumn.Unfit();
                },
                value => new MeasureValue(QueryexType.QxNumeric, false, MeasureArithmetic.FromMantissa(value, precision, scale)),
                capacity);
        }

        /// <summary>Reads one row's text from a reader array.</summary>
        /// <param name="buffer">The array.</param>
        /// <param name="row">The row.</param>
        /// <returns>The text.</returns>
        private static string ReadText(Array buffer, int row)
        {
            string?[] values = buffer as string?[] ?? throw PartialColumn.WrongBuffer(buffer, typeof(string));
            return values[row]
                ?? throw new ArgumentException("Only a row's present value is added; this row's is absent.", nameof(row));
        }

        /// <summary>A number at a node's type, or the absence as it is.</summary>
        /// <param name="value">The value.</param>
        /// <param name="shape">The node's type.</param>
        /// <returns>The value at that type.</returns>
        private static MeasureValue Numeric(MeasureValue value, NumericShape shape)
        {
            return value.IsNull
                ? new MeasureValue(QueryexType.QxNumeric, true)
                : Number(MeasureArithmetic.At(value.Number, shape));
        }

        /// <summary>A present number.</summary>
        /// <param name="number">The number.</param>
        /// <returns>The value.</returns>
        private static MeasureValue Number(SqlDecimal number)
        {
            return new MeasureValue(QueryexType.QxNumeric, false, number);
        }

        /// <summary>A present truth value.</summary>
        /// <param name="value">The truth value.</param>
        /// <returns>The value.</returns>
        private static MeasureValue Truth(bool value)
        {
            return new MeasureValue(QueryexType.QxBool, false, boolean: value);
        }

        /// <summary>The absent value of a type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The value.</returns>
        private static MeasureValue Absent(BoundType type)
        {
            return new MeasureValue(PublicType(type), true);
        }

        /// <summary>The public form of a node's type.</summary>
        /// <param name="type">The node's type.</param>
        /// <returns>The public type; a number for the absent value alone, which reads as one.</returns>
        private static QueryexType PublicType(BoundType type)
        {
            return type is BoundType.Null or BoundType.Error ? QueryexType.QxNumeric : BoundTypes.ToPublic(type);
        }

        /// <summary>Compares two present values of one type, as the backend compares them.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>Negative, zero or positive.</returns>
        /// <remarks>Numbers by value at any scale, instants by the moment they denote, truth values false first.</remarks>
        private static int Compare(MeasureValue left, MeasureValue right)
        {
            return left.Type switch
            {
                QueryexType.QxNumeric => left.Number.CompareTo(right.Number),
                QueryexType.QxBool => left.Boolean.CompareTo(right.Boolean),
                QueryexType.QxDate => left.Date.CompareTo(right.Date),
                QueryexType.QxDateTime => left.DateTime.CompareTo(right.DateTime),
                QueryexType.QxDateTimeOffset => left.DateTimeOffset.UtcTicks.CompareTo(right.DateTimeOffset.UtcTicks),
                QueryexType.QxString or QueryexType.QxGuid or QueryexType.QxHierarchyId or QueryexType.QxGeography => 0,
                _ => 0,
            };
        }

        /// <summary>Whether two values are equal, absence equal to absence.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when they are.</returns>
        private static bool TotalEquals(MeasureValue left, MeasureValue right)
        {
            return left.IsNull || right.IsNull ? left.IsNull && right.IsNull : Compare(left, right) == 0;
        }

        /// <summary>Evaluates a node over one cell.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="node">The node.</param>
        /// <returns>The value, a number at the node's type.</returns>
        private MeasureValue Evaluate(PartialColumn[] columns, int cell, MeasureNode node)
        {
            switch (node)
            {
                case MeasurePartialNode partial:
                    // A bare partial is already at its type: the column holds it there.
                    return columns[partial.Partial].Read(cell);

                case MeasureConstantNode constant:
                    return constant.Value;

                case MeasureParameterNode or MeasureContextNode:
                    return _values[node.Id];

                case MeasureNegateNode negate:
                    {
                        MeasureValue operand = Evaluate(columns, cell, negate.Operand);
                        return operand.IsNull
                            ? Absent(BoundType.Numeric)
                            : Number(MeasureArithmetic.Negate(operand.Number, _shapes[node.Id]));
                    }

                case MeasureArithmeticNode arithmetic:
                    return Arithmetic(columns, cell, arithmetic);

                case MeasureComparisonNode comparison:
                    return Truth(Comparison(columns, cell, comparison));

                case MeasureInNode membership:
                    return Truth(Membership(columns, cell, membership));

                case MeasureIsNullNode test:
                    return Truth(Evaluate(columns, cell, test.Operand).IsNull != test.Negated);

                case MeasureLogicalNode logical:
                    {
                        // Left to right, stopping at the first operand that decides.
                        foreach (MeasureNode operand in logical.Operands)
                        {
                            if (IsTrue(columns, cell, operand) != logical.IsConjunction)
                            {
                                return Truth(!logical.IsConjunction);
                            }
                        }

                        return Truth(logical.IsConjunction);
                    }

                case MeasureNotNode not:
                    return Truth(!IsTrue(columns, cell, not.Operand));

                case MeasureIfNode choice:
                    {
                        // Only the chosen branch is read, as the backend reads a choice after aggregation.
                        MeasureNode taken = IsTrue(columns, cell, choice.Condition) ? choice.WhenTrue : choice.WhenFalse;
                        return Chosen(Evaluate(columns, cell, taken), node);
                    }

                case MeasureCoalesceNode coalesce:
                    {
                        foreach (MeasureNode operand in coalesce.Operands)
                        {
                            MeasureValue value = Evaluate(columns, cell, operand);
                            if (!value.IsNull)
                            {
                                return Chosen(value, node);
                            }
                        }

                        return Absent(node.Type);
                    }

                case MeasureNumericFunctionNode function:
                    {
                        MeasureValue operand = Evaluate(columns, cell, function.Operand);
                        if (operand.IsNull)
                        {
                            return Absent(BoundType.Numeric);
                        }

                        NumericShape shape = _shapes[node.Id];
                        return Number(function.Function switch
                        {
                            MeasureNumericFunction.Abs => MeasureArithmetic.Abs(operand.Number, shape),
                            MeasureNumericFunction.Floor => MeasureArithmetic.Integral(operand.Number, ceiling: false, shape),
                            MeasureNumericFunction.Ceiling => MeasureArithmetic.Integral(operand.Number, ceiling: true, shape),
                            _ => MeasureArithmetic.Abs(operand.Number, shape),
                        });
                    }

                case MeasureRoundNode round:
                    {
                        MeasureValue operand = Evaluate(columns, cell, round.Operand);
                        MeasureValue digits = Evaluate(columns, cell, round.Digits);
                        return operand.IsNull || digits.IsNull
                            ? Absent(BoundType.Numeric)
                            : Number(MeasureArithmetic.Round(operand.Number, DigitsOf(digits.Number), _shapes[node.Id]));
                    }

                case MeasureDatePartNode part:
                    {
                        MeasureValue date = Evaluate(columns, cell, part.Operand);
                        if (date.IsNull)
                        {
                            return Absent(BoundType.Numeric);
                        }

                        DateOnly day = date.Date;
                        int component = part.Part switch
                        {
                            MeasureDatePart.Year => day.Year,
                            MeasureDatePart.Quarter => ((day.Month - 1) / 3) + 1,
                            MeasureDatePart.Month => day.Month,
                            MeasureDatePart.Day => day.Day,
                            _ => day.Day,
                        };

                        return Number(MeasureArithmetic.At(new SqlDecimal(component), _shapes[node.Id]));
                    }

                case MeasureDiffNode diff:
                    {
                        MeasureValue from = Evaluate(columns, cell, diff.From);
                        MeasureValue to = Evaluate(columns, cell, diff.To);
                        if (from.IsNull || to.IsNull)
                        {
                            return Absent(BoundType.Numeric);
                        }

                        // Whole days between two midnights, so every finer unit is an exact multiple.
                        long days = to.Date.DayNumber - from.Date.DayNumber;
                        long elapsed = diff.Unit switch
                        {
                            MeasureDiffUnit.Days => days,
                            MeasureDiffUnit.Hours => days * 24,
                            MeasureDiffUnit.Minutes => days * 1440,
                            MeasureDiffUnit.Seconds => days * 86400,
                            _ => days,
                        };

                        return Number(MeasureArithmetic.At(new SqlDecimal(elapsed), _shapes[node.Id]));
                    }

                default:
                    return Absent(node.Type);
            }
        }

        /// <summary>Evaluates an arithmetic node.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="node">The node.</param>
        /// <returns>The value.</returns>
        /// <remarks>
        ///     Both operands are evaluated before absence is looked at, and absence before a zero
        ///     divisor: an absent operand beside a failing one still fails, and an absent dividend
        ///     over zero is absent, as in the backend.
        /// </remarks>
        private MeasureValue Arithmetic(PartialColumn[] columns, int cell, MeasureArithmeticNode node)
        {
            MeasureValue left = Evaluate(columns, cell, node.Left);
            MeasureValue right = Evaluate(columns, cell, node.Right);
            if (left.IsNull || right.IsNull)
            {
                return Absent(BoundType.Numeric);
            }

            NumericShape shape = _shapes[node.Id];
            return Number(node.Operator switch
            {
                ArithmeticOperator.Add => MeasureArithmetic.Add(left.Number, right.Number, subtract: false, shape),
                ArithmeticOperator.Subtract => MeasureArithmetic.Add(left.Number, right.Number, subtract: true, shape),
                ArithmeticOperator.Multiply => MeasureArithmetic.Multiply(left.Number, right.Number, shape),
                ArithmeticOperator.Divide => MeasureArithmetic.Divide(left.Number, right.Number, shape),
                ArithmeticOperator.Remainder or ArithmeticOperator.Concat => throw new InvalidOperationException(
                    "The operator is outside what a measure may use, and compilation refuses it."),
                _ => throw new InvalidOperationException("The operator is not known."),
            });
        }

        /// <summary>Evaluates a comparison, total over absence.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="node">The node.</param>
        /// <returns>Its truth value.</returns>
        private bool Comparison(PartialColumn[] columns, int cell, MeasureComparisonNode node)
        {
            MeasureValue left = Evaluate(columns, cell, node.Left);
            MeasureValue right = Evaluate(columns, cell, node.Right);
            if (left.IsNull || right.IsNull)
            {
                // Absence equals only absence, and nothing is less or greater than it.
                bool both = left.IsNull && right.IsNull;
                return node.Operator switch
                {
                    ComparisonOperator.Equal => both,
                    ComparisonOperator.NotEqual => !both,
                    ComparisonOperator.Less or ComparisonOperator.LessOrEqual
                        or ComparisonOperator.Greater or ComparisonOperator.GreaterOrEqual => false,
                    _ => false,
                };
            }

            int order = Compare(left, right);
            return node.Operator switch
            {
                ComparisonOperator.Equal => order == 0,
                ComparisonOperator.NotEqual => order != 0,
                ComparisonOperator.Less => order < 0,
                ComparisonOperator.LessOrEqual => order <= 0,
                ComparisonOperator.Greater => order > 0,
                ComparisonOperator.GreaterOrEqual => order >= 0,
                _ => false,
            };
        }

        /// <summary>Evaluates a membership test, absence matching absence.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="node">The node.</param>
        /// <returns>Its truth value.</returns>
        private bool Membership(PartialColumn[] columns, int cell, MeasureInNode node)
        {
            MeasureValue value = node.Value is null
                ? Absent(BoundType.Numeric)
                : Evaluate(columns, cell, node.Value);

            bool matched = value.IsNull && node.MatchesAbsent;

            // Every element is read, as the backend reads the whole list it is handed.
            foreach (MeasureNode element in node.Elements)
            {
                matched |= TotalEquals(value, Evaluate(columns, cell, element));
            }

            return matched;
        }

        /// <summary>Reads a node as a truth value, an absent one reading as false.</summary>
        /// <param name="columns">The table's columns.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="node">The node.</param>
        /// <returns>The truth value.</returns>
        private bool IsTrue(PartialColumn[] columns, int cell, MeasureNode node)
        {
            MeasureValue value = Evaluate(columns, cell, node);
            return !value.IsNull && value.Type == QueryexType.QxBool && value.Boolean;
        }

        /// <summary>A chosen value, brought to the choice's type.</summary>
        /// <param name="value">The value of the branch taken.</param>
        /// <param name="node">The choice.</param>
        /// <returns>The value.</returns>
        private MeasureValue Chosen(MeasureValue value, MeasureNode node)
        {
            return node.IsNumeric
                ? Numeric(value, _shapes[node.Id])
                : value.IsNull ? Absent(node.Type) : value;
        }

        /// <summary>The decimal places a rounding asks for, as the backend reads them.</summary>
        /// <param name="digits">The places, as a number.</param>
        /// <returns>The places, truncated to a whole number and held within what a decimal has.</returns>
        private static int DigitsOf(SqlDecimal digits)
        {
            var whole = SqlDecimal.Truncate(digits, 0);
            return whole.CompareTo(new SqlDecimal(NumericShape.MaxPrecision)) > 0
                ? NumericShape.MaxPrecision
                : whole.CompareTo(new SqlDecimal(-(NumericShape.MaxPrecision + 1))) < 0
                ? -(NumericShape.MaxPrecision + 1)
                : (int)whole.ToDouble();
        }

        /// <summary>Fixes every node's type and every slot's value for one run.</summary>
        /// <param name="measures">The measures.</param>
        /// <param name="decimalTypes">The partials' precisions and scales.</param>
        /// <param name="arguments">The declared parameters' values, ignoring case.</param>
        /// <param name="context">What the run read.</param>
        /// <param name="shapes">Where every numeric node's type is written.</param>
        /// <param name="values">Where every slot's value is written.</param>
        private sealed class Preparer(
            CompiledMeasures measures,
            ImmutableArray<QueryexNumericType?> decimalTypes,
            Dictionary<string, object?> arguments,
            MeasureContext context,
            NumericShape[] shapes,
            MeasureValue[] values)
        {
            /// <summary>Fixes one node's type, after its operands'.</summary>
            /// <param name="node">The node.</param>
            /// <returns>The node's type; an <c>int</c> where the node is not a number.</returns>
            internal NumericShape Visit(MeasureNode node)
            {
                NumericShape shape = Shape(node);
                if (node.IsNumeric)
                {
                    shapes[node.Id] = shape;
                }

                return node.IsNumeric ? shape : NumericShape.Whole(WholeWidth.Int);
            }

            /// <summary>Works out one node's type, visiting its operands.</summary>
            /// <param name="node">The node.</param>
            /// <returns>The type.</returns>
            private NumericShape Shape(MeasureNode node)
            {
                switch (node)
                {
                    case MeasurePartialNode partial:
                        return PartialShape(partial.Partial);

                    case MeasureConstantNode constant:
                        return constant.Shape;

                    case MeasureParameterNode parameter:
                        return Parameter(parameter);

                    case MeasureContextNode slot:
                        return Context(slot);

                    case MeasureNegateNode negate:
                        return NumericShape.Negation(Visit(negate.Operand));

                    case MeasureArithmeticNode arithmetic:
                        {
                            NumericShape left = Visit(arithmetic.Left);
                            NumericShape right = Visit(arithmetic.Right);
                            return arithmetic.Operator switch
                            {
                                ArithmeticOperator.Multiply => NumericShape.Multiplicative(left, right),
                                ArithmeticOperator.Divide => NumericShape.Quotient(left, right),
                                ArithmeticOperator.Add or ArithmeticOperator.Subtract => NumericShape.Additive(left, right),

                                // Refused by binding; typed as a sum only so the switch is total.
                                ArithmeticOperator.Remainder or ArithmeticOperator.Concat => NumericShape.Additive(left, right),
                                _ => NumericShape.Additive(left, right),
                            };
                        }

                    case MeasureComparisonNode comparison:
                        Visit(comparison.Left);
                        Visit(comparison.Right);
                        return default;

                    case MeasureInNode membership:
                        if (membership.Value is not null)
                        {
                            Visit(membership.Value);
                        }

                        VisitAll(membership.Elements);
                        return default;

                    case MeasureIsNullNode test:
                        Visit(test.Operand);
                        return default;

                    case MeasureLogicalNode logical:
                        VisitAll(logical.Operands);
                        return default;

                    case MeasureNotNode not:
                        Visit(not.Operand);
                        return default;

                    case MeasureIfNode choice:
                        Visit(choice.Condition);
                        return Union(node, [choice.WhenTrue, choice.WhenFalse]);

                    case MeasureCoalesceNode coalesce:
                        return Union(node, coalesce.Operands);

                    case MeasureNumericFunctionNode function:
                        {
                            NumericShape operand = Visit(function.Operand);
                            return function.Function == MeasureNumericFunction.Abs
                                ? NumericShape.NumericFunction(operand)
                                : NumericShape.IntegralFunction(operand);
                        }

                    case MeasureRoundNode round:
                        Visit(round.Digits);
                        return NumericShape.NumericFunction(Visit(round.Operand));

                    case MeasureDatePartNode part:
                        Visit(part.Operand);
                        return NumericShape.Whole(WholeWidth.Int);

                    case MeasureDiffNode diff:
                        Visit(diff.From);
                        Visit(diff.To);
                        return diff.Unit == MeasureDiffUnit.Seconds
                            ? NumericShape.Whole(WholeWidth.BigInt)
                            : NumericShape.Decimal(26, 6);

                    default:
                        return NumericShape.Whole(WholeWidth.Int);
                }
            }

            /// <summary>Visits several nodes.</summary>
            /// <param name="nodes">The nodes.</param>
            private void VisitAll(ImmutableArray<MeasureNode> nodes)
            {
                foreach (MeasureNode child in nodes)
                {
                    Visit(child);
                }
            }

            /// <summary>The type of a choice between values.</summary>
            /// <param name="node">The choice.</param>
            /// <param name="branches">Its branches.</param>
            /// <returns>The type.</returns>
            private NumericShape Union(MeasureNode node, IReadOnlyList<MeasureNode> branches)
            {
                List<NumericShape> typed = new(branches.Count);
                foreach (MeasureNode branch in branches)
                {
                    NumericShape shape = Visit(branch);

                    // The absent value with no type of its own takes no part in the choice's type.
                    if (branch is not MeasureConstantNode { IsBareNull: true })
                    {
                        typed.Add(shape);
                    }
                }

                return !node.IsNumeric || typed.Count == 0
                    ? NumericShape.Whole(WholeWidth.Int)
                    : NumericShape.Union(typed);
            }

            /// <summary>The type of a partial.</summary>
            /// <param name="index">The partial's position.</param>
            /// <returns>The type.</returns>
            private NumericShape PartialShape(int index)
            {
                PartialPlan plan = measures.Plans[index];
                return plan.Storage switch
                {
                    PartialStorage.Count => NumericShape.Whole(WholeWidth.BigInt),
                    PartialStorage.Whole => NumericShape.Whole(plan.Width),
                    PartialStorage.Sum or PartialStorage.Decimal => NumericShape.Decimal(
                        decimalTypes[index]!.Value.Precision,
                        decimalTypes[index]!.Value.Scale),

                    // Not a number, so never read as one.
                    PartialStorage.Bool or PartialStorage.Date or PartialStorage.DateTime or PartialStorage.DateTimeOffset
                        or PartialStorage.Text or PartialStorage.Path => NumericShape.Whole(WholeWidth.Int),
                    _ => NumericShape.Whole(WholeWidth.Int),
                };
            }

            /// <summary>Fixes a declared parameter's value and type.</summary>
            /// <param name="node">The node.</param>
            /// <returns>The type.</returns>
            private NumericShape Parameter(MeasureParameterNode node)
            {
                return !arguments.TryGetValue(node.Name, out object? value)
                    ? throw new ArgumentException(
                        "The measures read the parameter '" + node.Name + "', which is given no value.")
                    : Slot(node, value, "the parameter '" + node.Name + "'");
            }

            /// <summary>Fixes a context slot's value and type.</summary>
            /// <param name="node">The node.</param>
            /// <returns>The type.</returns>
            private NumericShape Context(MeasureContextNode node)
            {
                return node.Value switch
                {
                    MeasureContextValue.Today => Slot(node, context.Today, "today"),
                    MeasureContextValue.Now => Slot(node, context.Now, "now"),
                    MeasureContextValue.UserId or _ => Slot(node, context.UserId, "the user"),
                };
            }

            /// <summary>Fixes one slot's value, and for a number its type.</summary>
            /// <param name="node">The node.</param>
            /// <param name="value">The value the slot was bound with.</param>
            /// <param name="what">What the slot is, for the error.</param>
            /// <returns>The type.</returns>
            /// <remarks>
            ///     A number takes the type the data provider infers for it, which is the type its value
            ///     has on its own; an absent one is bound at <c>decimal(29, 0)</c>.
            /// </remarks>
            private NumericShape Slot(MeasureNode node, object? value, string what)
            {
                if (value is null)
                {
                    values[node.Id] = Absent(node.Type);
                    return AbsentNumber;
                }

                MeasureValue? typed = node.Type switch
                {
                    BoundType.Numeric => value switch
                    {
                        decimal number => Number(new SqlDecimal(number)),
                        int number => Number(new SqlDecimal((decimal)number)),
                        long number => Number(new SqlDecimal((decimal)number)),
                        _ => null,
                    },
                    BoundType.Bool => value is bool flag ? Truth(flag) : null,
                    BoundType.String => value is string text ? new MeasureValue(QueryexType.QxString, false, text: text) : null,
                    BoundType.Date => value is DateOnly date ? new MeasureValue(QueryexType.QxDate, false, date: date) : null,
                    BoundType.DateTime => value is DateTime moment ? new MeasureValue(QueryexType.QxDateTime, false, dateTime: moment) : null,
                    BoundType.DateTimeOffset => value is DateTimeOffset instant
                        ? new MeasureValue(QueryexType.QxDateTimeOffset, false, dateTimeOffset: instant)
                        : null,

                    // An identifier reaches a measure only to be tested for absence, so a present one
                    // carries nothing but its presence. No parameter of the other types can be
                    // declared.
                    BoundType.Guid => value is Guid ? new MeasureValue(QueryexType.QxGuid, false) : null,
                    BoundType.HierarchyId or BoundType.Geography or BoundType.Null or BoundType.Error => null,
                    _ => null,
                };

                if (typed is not MeasureValue present)
                {
                    throw new ArgumentException(
                        "The value given for " + what + " is not a value of its type, " + BoundTypes.Name(node.Type) + ".");
                }

                values[node.Id] = present;
                return node.IsNumeric
                    ? NumericShape.Decimal(present.Number.Precision, present.Number.Scale)
                    : default;
            }
        }
    }
}
