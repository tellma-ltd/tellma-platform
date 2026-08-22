// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     Comparison, made total.
    /// </summary>
    /// <remarks>
    ///     The backend's comparison has three answers and the language's has two, and everything
    ///     here exists to close that gap: two missing values are equal, a missing value is ordered
    ///     against nothing, and inequality is exactly the complement of equality — so a negation is
    ///     always safe to wrap around whatever comes out.
    /// </remarks>
    internal sealed partial class Lowerer
    {
        /// <summary>Lowers a comparison.</summary>
        /// <param name="node">The bound comparison.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate LowerComparison(TypedComparison node)
        {
            QueryexNullity left = _nullity[node.Left];
            QueryexNullity right = _nullity[node.Right];
            PlanComparison comparison = Map(node.Operator);

            if (left == QueryexNullity.Null || right == QueryexNullity.Null)
            {
                return CompareAgainstAbsent(node, comparison, left, right);
            }

            // Each side is told which column the other names, so a written value binds in that
            // column's family and the backend is left free to seek on it.
            PlanValue leftValue = LowerValueAgainst(node.Left, HintOf(node.Right));
            PlanValue rightValue = LowerValueAgainst(node.Right, HintOf(node.Left));
            return Guarded(node.Span, comparison, leftValue, left, rightValue, right);
        }

        /// <summary>Lowers a comparison one of whose sides can only ever be absent.</summary>
        /// <param name="node">The bound comparison.</param>
        /// <param name="comparison">Which way it goes.</param>
        /// <param name="left">The left operand's nullity.</param>
        /// <param name="right">The right operand's nullity.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate CompareAgainstAbsent(
            TypedComparison node,
            PlanComparison comparison,
            QueryexNullity left,
            QueryexNullity right)
        {
            if (comparison is not (PlanComparison.Equal or PlanComparison.NotEqual))
            {
                // Nothing is less or greater than a value that is not there.
                return new PlanConstantPredicate(node.Span, false);
            }

            bool wantsEqual = comparison == PlanComparison.Equal;
            if (left == QueryexNullity.Null && right == QueryexNullity.Null)
            {
                return new PlanConstantPredicate(node.Span, wantsEqual);
            }

            bool leftIsAbsent = left == QueryexNullity.Null;
            TypedExpr other = leftIsAbsent ? node.Right : node.Left;
            QueryexNullity otherNullity = leftIsAbsent ? right : left;

            if (otherNullity == QueryexNullity.NotNull)
            {
                return new PlanConstantPredicate(node.Span, !wantsEqual);
            }

            // Comparing against something that is never there is asking whether the other side is
            // there either, which is what makes the whole subtree on the absent side disappear.
            return new PlanIsNull(node.Span, LowerValue(other), negated: !wantsEqual);
        }

        /// <summary>Wraps a raw comparison in whatever the operands' nullities call for.</summary>
        /// <param name="span">The range of the comparison.</param>
        /// <param name="comparison">Which way it goes.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="leftNullity">Its nullity.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="rightNullity">Its nullity.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate Guarded(
            QueryexSpan span,
            PlanComparison comparison,
            PlanValue left,
            QueryexNullity leftNullity,
            PlanValue right,
            QueryexNullity rightNullity)
        {
            bool leftTwice = leftNullity == QueryexNullity.Nullable;
            bool rightTwice = rightNullity == QueryexNullity.Nullable;
            if (!leftTwice && !rightTwice)
            {
                return new PlanCompare(span, comparison, left, right);
            }

            bool shareLeft = leftTwice && !left.IsAtomic;
            bool shareRight = rightTwice && !right.IsAtomic;
            if (shareLeft || shareRight)
            {
                if (!_bindingsAllowed)
                {
                    return SingleEmission(span, comparison, left, right);
                }

                if (shareLeft)
                {
                    TryShare(ref left);
                }

                if (shareRight)
                {
                    TryShare(ref right);
                }
            }

            return comparison switch
            {
                PlanComparison.Equal => EqualityGuard(span, left, leftTwice, right, rightTwice),
                PlanComparison.NotEqual => InequalityGuard(span, left, leftTwice, right, rightTwice),
                PlanComparison.Less or PlanComparison.LessOrEqual or PlanComparison.Greater
                    or PlanComparison.GreaterOrEqual =>
                    OrderingGuard(span, comparison, left, leftTwice, right, rightTwice),
                _ => OrderingGuard(span, comparison, left, leftTwice, right, rightTwice),
            };
        }

        /// <summary>The inequality form for operands that may be absent.</summary>
        /// <param name="span">The range of the comparison.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="leftTwice">Whether the left operand may be absent.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="rightTwice">Whether the right operand may be absent.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     Where both sides may be absent this is the negation of the equality form rather than
        ///     a fourth hand-written one, so the two stay exact complements of each other by
        ///     construction — which is the property the language promises and the one a separate
        ///     formulation would eventually break.
        /// </remarks>
        private static PlanPredicate InequalityGuard(
            QueryexSpan span,
            PlanValue left,
            bool leftTwice,
            PlanValue right,
            bool rightTwice)
        {
            return leftTwice && rightTwice
                ? Negate(EqualityGuard(span, left, leftTwice, right, rightTwice), span)
                : new PlanJunction(
                    span,
                    isConjunction: false,
                    [
                        new PlanIsNull(span, leftTwice ? left : right, negated: false),
                        new PlanCompare(span, PlanComparison.NotEqual, left, right),
                    ]);
        }

        /// <summary>The ordering form for operands that may be absent.</summary>
        /// <param name="span">The range of the comparison.</param>
        /// <param name="comparison">Which way it goes.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="leftTwice">Whether the left operand may be absent.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="rightTwice">Whether the right operand may be absent.</param>
        /// <returns>The lowered predicate.</returns>
        private static PlanJunction OrderingGuard(
            QueryexSpan span,
            PlanComparison comparison,
            PlanValue left,
            bool leftTwice,
            PlanValue right,
            bool rightTwice)
        {
            ImmutableArray<PlanPredicate>.Builder parts = ImmutableArray.CreateBuilder<PlanPredicate>(3);
            if (leftTwice)
            {
                parts.Add(new PlanIsNull(span, left, negated: true));
            }

            if (rightTwice)
            {
                parts.Add(new PlanIsNull(span, right, negated: true));
            }

            parts.Add(new PlanCompare(span, comparison, left, right));
            return new PlanJunction(span, isConjunction: true, parts.ToImmutable());
        }

        /// <summary>The equality form for operands that may be absent.</summary>
        /// <param name="span">The range of the comparison.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="leftTwice">Whether the left operand may be absent.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="rightTwice">Whether the right operand may be absent.</param>
        /// <returns>The lowered predicate.</returns>
        private static PlanJunction EqualityGuard(
            QueryexSpan span,
            PlanValue left,
            bool leftTwice,
            PlanValue right,
            bool rightTwice)
        {
            PlanCompare compare = new(span, PlanComparison.Equal, left, right);

            // Two missing values are equal to each other, which is the one rule that makes equality
            // total rather than merely defined most of the time.
            return leftTwice && rightTwice
                ? new PlanJunction(
                    span,
                    isConjunction: false,
                    [
                        new PlanJunction(
                            span,
                            isConjunction: true,
                            [
                                new PlanIsNull(span, left, negated: false),
                                new PlanIsNull(span, right, negated: false),
                            ]),
                        new PlanJunction(
                            span,
                            isConjunction: true,
                            [
                                new PlanIsNull(span, left, negated: true),
                                new PlanIsNull(span, right, negated: true),
                                compare,
                            ]),
                    ])
                : new PlanJunction(
                    span,
                    isConjunction: true,
                    [new PlanIsNull(span, leftTwice ? left : right, negated: true), compare]);
        }

        /// <summary>
        ///     The form to use where an operand needs writing twice and cannot be given a name.
        /// </summary>
        /// <param name="span">The range of the comparison.</param>
        /// <param name="comparison">Which way it goes.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     Both forms write each operand exactly once. Set intersection already treats two
        ///     missing values as one, which is precisely the equality this language wants; and for
        ///     ordering, reading the raw comparison as a number turns the backend's undecided answer
        ///     into falsehood, which is exactly what ordering against a missing value should mean.
        /// </remarks>
        private static PlanPredicate SingleEmission(
            QueryexSpan span,
            PlanComparison comparison,
            PlanValue left,
            PlanValue right)
        {
            return comparison is PlanComparison.Equal or PlanComparison.NotEqual
                ? new PlanSetComparison(
                    span,
                    left,
                    right,
                    negated: comparison == PlanComparison.NotEqual)
                : new PlanPredicateOfValue(
                    span,
                    new PlanValueOfPredicate(span, new PlanCompare(span, comparison, left, right)),
                    PlanBoolGuard.None);
        }

        /// <summary>Lowers a set-membership test.</summary>
        /// <param name="node">The bound test.</param>
        /// <returns>The lowered predicate.</returns>
        /// <remarks>
        ///     Elements are partitioned by what is known about them rather than lumped together, so
        ///     that a list of ordinary values keeps the single seekable form even when one entry
        ///     beside them might be missing.
        /// </remarks>
        private PlanPredicate LowerIn(TypedIn node)
        {
            QueryexNullity valueNullity = _nullity[node.Value];
            int certain = 0;
            int uncertain = 0;
            bool anyAbsent = false;

            foreach (TypedExpr element in node.Elements)
            {
                QueryexNullity elementNullity = _nullity[element];
                if (elementNullity == QueryexNullity.NotNull)
                {
                    certain++;
                }
                else if (elementNullity == QueryexNullity.Null)
                {
                    anyAbsent = true;
                }
                else
                {
                    uncertain++;
                }
            }

            if (valueNullity == QueryexNullity.Null)
            {
                return MembershipOfAbsent(node, anyAbsent, uncertain);
            }

            // The value tells the elements which column they will be matched against, and if the
            // value is itself a written one, the first element that names a column tells it.
            QueryexStoreType? hint = HintOf(node.Value);
            QueryexStoreType? valueHint = null;
            foreach (TypedExpr element in node.Elements)
            {
                valueHint = HintOf(element);
                if (valueHint is not null)
                {
                    break;
                }
            }

            PlanValue value = LowerValueAgainst(node.Value, valueHint);

            // How often the value is written: once for the seekable form, once more to check it is
            // there at all, and once or twice for every element that might be missing.
            bool nullable = valueNullity == QueryexNullity.Nullable;
            int uses = (certain > 0 ? (nullable ? 2 : 1) : 0)
                + (anyAbsent && nullable ? 1 : 0)
                + (uncertain * (nullable ? 2 : 1));

            bool shareValue = uses > 1 && !value.IsAtomic;
            if (!_bindingsAllowed && (shareValue || uncertain > 0))
            {
                // Neither the value nor an uncertain element can be written twice here, and set
                // intersection is the one form that needs to write neither of them twice.
                return MembershipAsSet(node, value, hint);
            }

            if (shareValue)
            {
                TryShare(ref value);
            }

            return MembershipDisjunction(node, value, nullable, certain, anyAbsent, hint);
        }

        /// <summary>Lowers a membership test whose value can only ever be absent.</summary>
        /// <param name="node">The bound test.</param>
        /// <param name="anyAbsent">Whether some element can only ever be absent.</param>
        /// <param name="uncertain">How many elements might be absent.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate MembershipOfAbsent(TypedIn node, bool anyAbsent, int uncertain)
        {
            if (anyAbsent)
            {
                return new PlanConstantPredicate(node.Span, true);
            }

            if (uncertain == 0)
            {
                return new PlanConstantPredicate(node.Span, false);
            }

            ImmutableArray<PlanPredicate>.Builder disjuncts =
                ImmutableArray.CreateBuilder<PlanPredicate>(uncertain);

            foreach (TypedExpr element in node.Elements)
            {
                if (_nullity[element] == QueryexNullity.Nullable)
                {
                    // A missing value belongs to the list exactly when something in the list is
                    // missing too.
                    disjuncts.Add(new PlanIsNull(element.Span, LowerValue(element), negated: false));
                }
            }

            return disjuncts.Count == 1
                ? disjuncts[0]
                : new PlanJunction(node.Span, isConjunction: false, disjuncts.ToImmutable());
        }

        /// <summary>Lowers a membership test as one set intersection.</summary>
        /// <param name="node">The bound test.</param>
        /// <param name="value">The already-lowered value.</param>
        /// <param name="hint">The column the value names, when it names one.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanSetMembership MembershipAsSet(TypedIn node, PlanValue value, QueryexStoreType? hint)
        {
            ImmutableArray<PlanValue>.Builder elements =
                ImmutableArray.CreateBuilder<PlanValue>(node.Elements.Length);

            foreach (TypedExpr element in node.Elements)
            {
                elements.Add(LowerValueAgainst(element, hint));
            }

            return new PlanSetMembership(node.Span, value, elements.ToImmutable());
        }

        /// <summary>Lowers a membership test as a seekable list beside whatever it cannot cover.</summary>
        /// <param name="node">The bound test.</param>
        /// <param name="value">The already-lowered value.</param>
        /// <param name="nullable">Whether the value might be absent.</param>
        /// <param name="certain">How many elements are certainly present.</param>
        /// <param name="anyAbsent">Whether some element can only ever be absent.</param>
        /// <param name="hint">The column the value names, when it names one.</param>
        /// <returns>The lowered predicate.</returns>
        private PlanPredicate MembershipDisjunction(
            TypedIn node,
            PlanValue value,
            bool nullable,
            int certain,
            bool anyAbsent,
            QueryexStoreType? hint)
        {
            ImmutableArray<PlanValue>.Builder present = ImmutableArray.CreateBuilder<PlanValue>(certain);
            List<PlanPredicate> extra = [];

            // Lowered in the order they were written, so which parameter gets which number does not
            // depend on how the elements happened to be partitioned.
            foreach (TypedExpr element in node.Elements)
            {
                QueryexNullity elementNullity = _nullity[element];
                if (elementNullity == QueryexNullity.Null)
                {
                    continue;
                }

                PlanValue lowered = LowerValueAgainst(element, hint);
                if (elementNullity == QueryexNullity.NotNull)
                {
                    present.Add(lowered);
                    continue;
                }

                if (!lowered.IsAtomic)
                {
                    TryShare(ref lowered);
                }

                extra.Add(Guarded(
                    element.Span,
                    PlanComparison.Equal,
                    value,
                    nullable ? QueryexNullity.Nullable : QueryexNullity.NotNull,
                    lowered,
                    QueryexNullity.Nullable));
            }

            List<PlanPredicate> disjuncts = [];
            if (certain > 0)
            {
                PlanPredicate list = new PlanIn(node.Span, value, present.ToImmutable());
                disjuncts.Add(nullable
                    ? new PlanJunction(
                        node.Span,
                        isConjunction: true,
                        [new PlanIsNull(node.Span, value, negated: true), list])
                    : list);
            }

            if (anyAbsent && nullable)
            {
                disjuncts.Add(new PlanIsNull(node.Span, value, negated: false));
            }

            disjuncts.AddRange(extra);

            return disjuncts.Count switch
            {
                0 => new PlanConstantPredicate(node.Span, false),
                1 => disjuncts[0],
                _ => new PlanJunction(node.Span, isConjunction: false, [.. disjuncts]),
            };
        }

        /// <summary>Translates a bound comparison operator to a plan one.</summary>
        /// <param name="op">The bound operator.</param>
        /// <returns>The plan operator.</returns>
        private static PlanComparison Map(ComparisonOperator op)
        {
            return op switch
            {
                ComparisonOperator.Equal => PlanComparison.Equal,
                ComparisonOperator.NotEqual => PlanComparison.NotEqual,
                ComparisonOperator.Less => PlanComparison.Less,
                ComparisonOperator.LessOrEqual => PlanComparison.LessOrEqual,
                ComparisonOperator.Greater => PlanComparison.Greater,
                ComparisonOperator.GreaterOrEqual => PlanComparison.GreaterOrEqual,
                _ => PlanComparison.Equal,
            };
        }
    }
}
