// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Data.SqlTypes;
using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>What an evaluation depends on besides the row it is looking at.</summary>
    /// <param name="Today">The date the tenant is having.</param>
    /// <param name="Now">The instant.</param>
    /// <param name="UserId">The signed-in user's identifier, or null when there is none.</param>
    /// <param name="TimeZone">The tenant's zone.</param>
    /// <param name="Parameters">The values supplied for the declared parameters.</param>
    public sealed record InterpreterContext(
        DateOnly Today,
        DateTimeOffset Now,
        int? UserId,
        TimeZoneInfo TimeZone,
        IReadOnlyDictionary<string, QxValue> Parameters)
    {
        /// <summary>A context with fixed values, so a run produces the same answers as the last.</summary>
        public static InterpreterContext Fixed { get; } = new(
            new DateOnly(2024, 6, 15),
            new DateTimeOffset(2024, 6, 15, 9, 30, 0, TimeSpan.FromHours(3)),
            2,
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi"),
            new Dictionary<string, QxValue>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     A second reading of the language, written from what it is supposed to mean.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Its whole value is in being independent. It is written from the semantics rather than
    ///         from the compiler, so where the two agree the agreement is evidence, and where they
    ///         disagree one of them is wrong and the difference says which rows to look at.
    ///     </para>
    ///     <para>
    ///         Every node is evaluated for every row, with nothing skipped because something else
    ///         already decided the answer. That costs nothing here and is what lets a claim about a
    ///         node buried inside a conjunction be checked at all.
    ///     </para>
    /// </remarks>
    internal sealed partial class Interpreter
    {
        /// <summary>What the evaluation depends on besides the row.</summary>
        private readonly InterpreterContext _context;

        /// <summary>The row being read.</summary>
        private readonly LedgerRow _row;

        /// <summary>What every node evaluated to, so a sweep can look at each of them.</summary>
        private readonly Dictionary<TypedExpr, QxValue> _readings = new(ReferenceEqualityComparer.Instance);

        /// <summary>Initializes an evaluation.</summary>
        /// <param name="row">The row being read.</param>
        /// <param name="context">What the evaluation depends on besides the row.</param>
        internal Interpreter(LedgerRow row, InterpreterContext context)
        {
            _row = row;
            _context = context;
        }

        /// <summary>What every node evaluated to.</summary>
        internal IReadOnlyDictionary<TypedExpr, QxValue> Readings => _readings;

        /// <summary>Evaluates one node, and everything under it.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its value.</returns>
        internal QxValue Evaluate(TypedExpr node)
        {
            if (_readings.TryGetValue(node, out QxValue? cached))
            {
                return cached;
            }

            QxValue value = Compute(node);
            _readings[node] = value;
            return value;
        }

        /// <summary>Works out one node's value from its children's.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its value.</returns>
        private QxValue Compute(TypedExpr node)
        {
            return node switch
            {
                TypedLiteral literal => Literal(literal),
                TypedNull => QxValue.Absent(Named(node.Type)),
                TypedPath path => Read(path),
                TypedParameter parameter => Supplied(parameter),
                TypedNegate negate => Negate(Evaluate(negate.Operand)),
                TypedArithmetic arithmetic => Arithmetic(arithmetic),
                TypedComparison comparison => QxValue.Flag(Compare(comparison)),
                TypedIn membership => QxValue.Flag(Contains(membership)),
                TypedIsNull test => QxValue.Flag(Evaluate(test.Operand).IsAbsent != test.Negated),
                TypedNot not => QxValue.Flag(!Truth(not.Operand)),
                TypedLogical logical => QxValue.Flag(Junction(logical)),
                TypedCall call => Call(call),
                _ => QxValue.Absent(Named(node.Type)),
            };
        }

        /// <summary>The public name of a node's type.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name, or text for the two types the language never surfaces.</returns>
        /// <remarks>
        ///     The absent-value type and the failure type have no public counterpart, and a node
        ///     carrying either is absent anyway — so what is named there only ever appears in the
        ///     description of a mismatch.
        /// </remarks>
        internal static QueryexType Named(BoundType type)
        {
            return type is BoundType.Null or BoundType.Error
                ? QueryexType.QxString
                : BoundTypes.ToPublic(type);
        }

        /// <summary>Reads a node as a truth value, absence and all.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The truth value.</returns>
        /// <remarks>An absent truth value reads as false, which is what keeps negation sound.</remarks>
        internal bool Truth(TypedExpr node)
        {
            QxValue value = Evaluate(node);
            return !value.IsAbsent && value.AsFlag;
        }

        /// <summary>The value a literal denotes.</summary>
        /// <param name="literal">The literal.</param>
        /// <returns>Its value.</returns>
        private static QxValue Literal(TypedLiteral literal)
        {
            return QxValue.Of(Named(literal.Type), literal.Value);
        }

        /// <summary>Reads a path, following each navigation to the row it points at.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The value, or absence when a step along the way points at nothing.</returns>
        private QxValue Read(TypedPath path)
        {
            LedgerRow? current = _row;
            foreach (NavigationDescriptor navigation in path.Navigations)
            {
                QxValue key = current[navigation.ForeignKey.Name];
                current = LedgerData.Find(navigation.Target, key);
                if (current is null)
                {
                    // Nothing to read the next name from, which is exactly what makes a path through
                    // an optional step possibly absent.
                    return QxValue.Absent(Named(path.Type));
                }
            }

            return Stored(current[path.Property.Name], path.Property.StoreType);
        }

        /// <summary>Gives a value the width the column it came from is declared with.</summary>
        /// <param name="value">The value.</param>
        /// <param name="store">The column's store type.</param>
        /// <returns>The value at that width.</returns>
        /// <remarks>
        ///     Numbers only, and it matters. Decimal arithmetic derives the result's scale from the
        ///     operands' declared widths, not from the digits they happen to be carrying, so a value
        ///     read at whatever width its literal needed divides to a different number of digits
        ///     than the same value read out of its column. This is the whole reason the second
        ///     reading carries the backend's own decimal type rather than the platform's: given the
        ///     right widths it reproduces the backend's algebra instead of approximating it.
        /// </remarks>
        private static QxValue Stored(QxValue value, QueryexStoreType? store)
        {
            bool numeric = !value.IsAbsent && value.Type == QueryexType.QxNumeric && store is not null;

            return numeric && Widths(store!.Value) is (int precision, int scale)
                ? QxValue.Number(SqlDecimal.ConvertToPrecScale(value.AsNumber, precision, scale))
                : value;
        }

        /// <summary>The precision and scale a numeric store type stands for.</summary>
        /// <param name="store">The store type.</param>
        /// <returns>The widths, or null when the type holds no number.</returns>
        private static (int Precision, int Scale)? Widths(QueryexStoreType store)
        {
            return store.Family switch
            {
                QueryexStoreFamily.QxTinyInt => (3, 0),
                QueryexStoreFamily.QxSmallInt => (5, 0),
                QueryexStoreFamily.QxInt => (10, 0),
                QueryexStoreFamily.QxBigInt => (19, 0),
                QueryexStoreFamily.QxDecimal => (store.Size ?? 18, store.Scale ?? 0),
                QueryexStoreFamily.QxBit or QueryexStoreFamily.QxChar or QueryexStoreFamily.QxVarChar
                    or QueryexStoreFamily.QxNChar or QueryexStoreFamily.QxNVarChar
                    or QueryexStoreFamily.QxUniqueIdentifier or QueryexStoreFamily.QxDate
                    or QueryexStoreFamily.QxDateTime or QueryexStoreFamily.QxDateTime2
                    or QueryexStoreFamily.QxDateTimeOffset or QueryexStoreFamily.QxHierarchyId
                    or QueryexStoreFamily.QxGeography => null,
                _ => null,
            };
        }

        /// <summary>The value supplied for a declared parameter.</summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>Its value.</returns>
        private QxValue Supplied(TypedParameter parameter)
        {
            return _context.Parameters.TryGetValue(parameter.Symbol.Name, out QxValue? value)
                ? value
                : QxValue.Absent(Named(parameter.Type));
        }

        /// <summary>Negates a number.</summary>
        /// <param name="operand">The operand.</param>
        /// <returns>The result.</returns>
        private static QxValue Negate(QxValue operand)
        {
            return operand.IsAbsent ? operand : QxValue.Number(-operand.AsNumber);
        }

        /// <summary>Combines two values arithmetically, or joins two pieces of text.</summary>
        /// <param name="arithmetic">The node.</param>
        /// <returns>The result.</returns>
        private QxValue Arithmetic(TypedArithmetic arithmetic)
        {
            QxValue left = Evaluate(arithmetic.Left);
            QxValue right = Evaluate(arithmetic.Right);
            QueryexType type = Named(arithmetic.Type);

            // Absence travels through arithmetic and through joining text alike: joining an absent
            // value to something yields an absent value rather than treating it as empty.
            if (left.IsAbsent || right.IsAbsent)
            {
                return QxValue.Absent(type);
            }

            if (arithmetic.Operator == ArithmeticOperator.Concat)
            {
                return QxValue.Text(left.AsText + right.AsText);
            }

            SqlDecimal one = left.AsNumber;
            SqlDecimal other = right.AsNumber;
            return arithmetic.Operator switch
            {
                ArithmeticOperator.Add => QxValue.Number(one + other),
                ArithmeticOperator.Subtract => QxValue.Number(one - other),
                ArithmeticOperator.Multiply => QxValue.Number(one * other),
                ArithmeticOperator.Divide => QxValue.Number(Divide(one, other)),
                ArithmeticOperator.Remainder => QxValue.Number(Remainder(one, other)),
                ArithmeticOperator.Concat => QxValue.Absent(type),
                _ => QxValue.Absent(type),
            };
        }

        /// <summary>What is left over after dividing one whole number of times.</summary>
        /// <param name="left">The dividend.</param>
        /// <param name="right">The divisor.</param>
        /// <returns>The remainder, which takes the dividend's sign.</returns>
        private static SqlDecimal Remainder(SqlDecimal left, SqlDecimal right)
        {
            var whole = SqlDecimal.Truncate(left / right, 0);
            return left - (whole * right);
        }

        /// <summary>Divides, keeping at least the fractional digits the language promises.</summary>
        /// <param name="left">The dividend.</param>
        /// <param name="right">The divisor.</param>
        /// <returns>The quotient.</returns>
        /// <remarks>
        ///     The same rule the emitted form follows, and for the same reason. Two whole numbers
        ///     divide to a whole number and lose the remainder, so the dividend is widened first to
        ///     make the division a decimal one; nothing else is widened, because a widening carries
        ///     its own precision, and a precision at the ceiling forces the quotient's scale down to
        ///     the minimum — throwing away digits the backend would have kept.
        /// </remarks>
        private static SqlDecimal Divide(SqlDecimal left, SqlDecimal right)
        {
            if (left.Scale > 0 || right.Scale > 0)
            {
                return left / right;
            }

            var widened = SqlDecimal.ConvertToPrecScale(left, 19, 0);
            return widened / right;
        }

        /// <summary>Compares two values, totally.</summary>
        /// <param name="comparison">The node.</param>
        /// <returns>Whether the comparison holds.</returns>
        /// <remarks>
        ///     Two absent values are equal to each other, an absent value is ordered against nothing,
        ///     and inequality is exactly the complement of equality. Those three rules are what make
        ///     the answer always one of two things.
        /// </remarks>
        private bool Compare(TypedComparison comparison)
        {
            QxValue left = Evaluate(comparison.Left);
            QxValue right = Evaluate(comparison.Right);

            if (comparison.Operator is ComparisonOperator.Equal or ComparisonOperator.NotEqual)
            {
                bool equal = left.IsAbsent || right.IsAbsent
                    ? left.IsAbsent && right.IsAbsent
                    : Order(left, right) == 0;

                return comparison.Operator == ComparisonOperator.Equal ? equal : !equal;
            }

            if (left.IsAbsent || right.IsAbsent)
            {
                return false;
            }

            int order = Order(left, right);
            return comparison.Operator switch
            {
                ComparisonOperator.Less => order < 0,
                ComparisonOperator.LessOrEqual => order <= 0,
                ComparisonOperator.Greater => order > 0,
                ComparisonOperator.GreaterOrEqual => order >= 0,
                ComparisonOperator.Equal or ComparisonOperator.NotEqual => order == 0,
                _ => false,
            };
        }

        /// <summary>Orders two present values of one type.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        /// <returns>Negative, zero, or positive.</returns>
        internal static int Order(QxValue left, QxValue right)
        {
            return left.Type switch
            {
                QueryexType.QxNumeric => left.AsNumber.CompareTo(right.AsNumber),
                QueryexType.QxString => Collation.Compare(left.AsText, right.AsText),
                QueryexType.QxBool => left.AsFlag.CompareTo(right.AsFlag),
                QueryexType.QxGuid => left.AsIdentifier.Equals(right.AsIdentifier) ? 0 : 1,
                QueryexType.QxDate => left.AsDate.CompareTo(right.AsDate),
                QueryexType.QxDateTime => left.AsMoment.CompareTo(right.AsMoment),
                QueryexType.QxDateTimeOffset => left.AsInstant.CompareTo(right.AsInstant),
                QueryexType.QxHierarchyId => string.CompareOrdinal(left.AsNode, right.AsNode),

                // Spatial values have no order and no equality in the language, so nothing should
                // ever ask; answering "different" is the harmless reply if something does.
                QueryexType.QxGeography => 1,
                _ => 1,
            };
        }

        /// <summary>Whether a value is one of a list.</summary>
        /// <param name="membership">The node.</param>
        /// <returns>Whether it is.</returns>
        /// <remarks>
        ///     Exactly the disjunction of the equalities, which is what makes an absent value belong
        ///     to a list that has an absent value in it.
        /// </remarks>
        private bool Contains(TypedIn membership)
        {
            QxValue value = Evaluate(membership.Value);
            foreach (TypedExpr element in membership.Elements)
            {
                QxValue candidate = Evaluate(element);
                bool equal = value.IsAbsent || candidate.IsAbsent
                    ? value.IsAbsent && candidate.IsAbsent
                    : Order(value, candidate) == 0;

                if (equal)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Combines truth values.</summary>
        /// <param name="logical">The node.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        ///     Every operand is evaluated whatever the answer turns out to be, so that a claim about
        ///     a node inside the part a short circuit would have skipped is still checkable.
        /// </remarks>
        private bool Junction(TypedLogical logical)
        {
            bool conjunction = logical.Operator == LogicalOperator.And;
            bool result = conjunction;
            foreach (TypedExpr operand in logical.Operands)
            {
                bool truth = Truth(operand);
                result = conjunction ? result && truth : result || truth;
            }

            return result;
        }

        /// <summary>Evaluates every node of a bound list against this row.</summary>
        /// <param name="items">The bound items.</param>
        internal void Sweep(ImmutableArray<BoundItem> items)
        {
            foreach (BoundItem item in items)
            {
                Visit(item.Expression);
            }
        }

        /// <summary>Evaluates one node and everything under it, leaving nothing out.</summary>
        /// <param name="node">The node.</param>
        private void Visit(TypedExpr node)
        {
            foreach (TypedExpr child in node.Children)
            {
                Visit(child);
            }

            Evaluate(node);
        }
    }
}
