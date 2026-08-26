// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Functions;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     Whether two bound expressions mean the same thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Compared after binding rather than before, so two spellings of one thing count as one
    ///         thing: the same column reached by the same navigations, the same number however it
    ///         was written, the same overload of the same function. Where the text differed but the
    ///         meaning did not, this says so.
    ///     </para>
    ///     <para>
    ///         Two things are deliberately not equated. A node that failed to bind is equal to
    ///         nothing, itself included, so one mistake cannot be mistaken for a match; and a number
    ///         written with a different scale is a different number, because the scale it was
    ///         written with is part of how it reaches the backend.
    ///     </para>
    /// </remarks>
    internal static class TypedEquivalence
    {
        /// <summary>Whether two bound expressions mean the same thing.</summary>
        /// <param name="first">One expression.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        internal static bool AreEquivalent(TypedExpr? first, TypedExpr? second)
        {
            if (first is null || second is null)
            {
                return false;
            }

            // An error node is never equal even to itself: two uses of one failed subexpression
            // are still two separate failures.
            return ReferenceEquals(first, second)
                ? first.Kind != TypedExprKind.Error
                : first.Kind == second.Kind && first.Type == second.Type && SameShape(first, second);
        }

        /// <summary>Whether two nodes of the same kind and type say the same thing.</summary>
        /// <param name="first">One node.</param>
        /// <param name="second">The other, already known to be of the same kind.</param>
        /// <returns>True when they do.</returns>
        private static bool SameShape(TypedExpr first, TypedExpr second)
        {
            return first switch
            {
                TypedLiteral literal => SameLiteral(literal, (TypedLiteral)second),
                TypedNull => true,
                TypedPath path => SamePath(path, (TypedPath)second),
                TypedParameter parameter => string.Equals(
                    parameter.Symbol.Name,
                    ((TypedParameter)second).Symbol.Name,
                    StringComparison.OrdinalIgnoreCase),
                TypedNegate or TypedNot => AreEquivalent(
                    ((TypedUnaryBase)first).Operand,
                    ((TypedUnaryBase)second).Operand),
                TypedIsNull test => SameIsNull(test, (TypedIsNull)second),
                TypedArithmetic arithmetic => SameArithmetic(arithmetic, (TypedArithmetic)second),
                TypedComparison comparison => SameComparison(comparison, (TypedComparison)second),
                TypedIn membership => SameIn(membership, (TypedIn)second),
                TypedLogical logical => SameLogical(logical, (TypedLogical)second),
                TypedCall call => SameCall(call, (TypedCall)second),
                _ => false,
            };
        }

        /// <summary>Whether two literals denote the same value written the same way.</summary>
        /// <param name="first">One literal.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameLiteral(TypedLiteral first, TypedLiteral second)
        {
            return first.Scale == second.Scale
                && first.Precision == second.Precision
                && Equals(first.Value, second.Value);
        }

        /// <summary>Whether two paths reach the same column by the same route.</summary>
        /// <param name="first">One path.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SamePath(TypedPath first, TypedPath second)
        {
            if (!ReferenceEquals(first.Property, second.Property)
                || first.Navigations.Length != second.Navigations.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Navigations.Length; index++)
            {
                if (!ReferenceEquals(first.Navigations[index], second.Navigations[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether two absence tests ask the same question.</summary>
        /// <param name="first">One test.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameIsNull(TypedIsNull first, TypedIsNull second)
        {
            return first.Negated == second.Negated && AreEquivalent(first.Operand, second.Operand);
        }

        /// <summary>Whether two arithmetic nodes compute the same thing.</summary>
        /// <param name="first">One node.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameArithmetic(TypedArithmetic first, TypedArithmetic second)
        {
            return first.Operator == second.Operator
                && AreEquivalent(first.Left, second.Left)
                && AreEquivalent(first.Right, second.Right);
        }

        /// <summary>Whether two comparisons ask the same question.</summary>
        /// <param name="first">One comparison.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameComparison(TypedComparison first, TypedComparison second)
        {
            return first.Operator == second.Operator
                && AreEquivalent(first.Left, second.Left)
                && AreEquivalent(first.Right, second.Right);
        }

        /// <summary>Whether two membership tests ask the same question.</summary>
        /// <param name="first">One test.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        /// <remarks>
        ///     Order-sensitive over the elements. Two lists holding the same values in a different
        ///     order do mean the same thing, but the cost of proving it is not worth what the answer
        ///     is used for, and answering no merely declines an optimization.
        /// </remarks>
        private static bool SameIn(TypedIn first, TypedIn second)
        {
            if (first.Elements.Length != second.Elements.Length
                || !AreEquivalent(first.Value, second.Value))
            {
                return false;
            }

            for (int index = 0; index < first.Elements.Length; index++)
            {
                if (!AreEquivalent(first.Elements[index], second.Elements[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether two connectives join the same operands the same way.</summary>
        /// <param name="first">One node.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameLogical(TypedLogical first, TypedLogical second)
        {
            if (first.Operator != second.Operator || first.Operands.Length != second.Operands.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Operands.Length; index++)
            {
                if (!AreEquivalent(first.Operands[index], second.Operands[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether two calls resolve to the same overload over the same arguments.</summary>
        /// <param name="first">One call.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool SameCall(TypedCall first, TypedCall second)
        {
            if (!ReferenceEquals(first.Signature, second.Signature)
                || first.Arguments.Length != second.Arguments.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Selectors.Length; index++)
            {
                if (!string.Equals(
                    first.Selectors[index],
                    index < second.Selectors.Length ? second.Selectors[index] : null,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            for (int index = 0; index < first.Arguments.Length; index++)
            {
                if (!AreEquivalent(first.Arguments[index], second.Arguments[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a bound expression contains an aggregation anywhere.</summary>
        /// <param name="node">The expression.</param>
        /// <returns>True when it does.</returns>
        internal static bool IsAggregation(TypedExpr node)
        {
            return node is TypedCall { Definition.Category: FunctionCategory.Aggregate };
        }
    }
}
