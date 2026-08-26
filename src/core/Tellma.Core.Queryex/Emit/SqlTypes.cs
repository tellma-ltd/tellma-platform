// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>
    ///     What the backend does with a type, as opposed to what the language says about it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three of the emitter's decisions turn on how a value is stored rather than on its type
    ///         in the language, and all three are places where getting it wrong is silent. Dividing
    ///         two whole numbers throws the remainder away; averaging them does the same; adding them
    ///         up overflows where a wider running total would not; and joining two pieces of text
    ///         that are each as wide as the type allows quietly clips the result rather than growing
    ///         it.
    ///     </para>
    ///     <para>
    ///         Widening is applied only where it is provably lossless — the whole-number families
    ///         have no fractional part to round away — so a value is never mangled on the way to
    ///         being made safe. Where a type cannot be established the answer is the one that widens
    ///         nothing, on the grounds that the resulting overflow is an error the backend raises
    ///         rather than a number nobody checks.
    ///     </para>
    /// </remarks>
    internal static class SqlTypes
    {
        /// <summary>The widest a bounded Unicode value can be.</summary>
        internal const int UnicodeBound = 4000;

        /// <summary>The widest a bounded non-Unicode value can be.</summary>
        internal const int NarrowBound = 8000;

        /// <summary>The precision every widened whole number is given.</summary>
        internal const int WidePrecision = 38;

        /// <summary>Whether a family holds whole numbers only.</summary>
        /// <param name="family">The family.</param>
        /// <returns>True when it has no fractional part.</returns>
        internal static bool IsWhole(QueryexStoreFamily family)
        {
            return family is QueryexStoreFamily.QxBit
                or QueryexStoreFamily.QxTinyInt
                or QueryexStoreFamily.QxSmallInt
                or QueryexStoreFamily.QxInt
                or QueryexStoreFamily.QxBigInt;
        }

        /// <summary>Whether a family holds text.</summary>
        /// <param name="family">The family.</param>
        /// <returns>True when it does.</returns>
        internal static bool IsText(QueryexStoreFamily family)
        {
            return family is QueryexStoreFamily.QxChar
                or QueryexStoreFamily.QxVarChar
                or QueryexStoreFamily.QxNChar
                or QueryexStoreFamily.QxNVarChar;
        }

        /// <summary>Whether a family holds text in the backend's wide encoding.</summary>
        /// <param name="family">The family.</param>
        /// <returns>True when it does.</returns>
        internal static bool IsUnicode(QueryexStoreFamily family)
        {
            return family is QueryexStoreFamily.QxNChar or QueryexStoreFamily.QxNVarChar;
        }

        /// <summary>The fractional digits a type keeps, or zero where it keeps none.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The scale.</returns>
        internal static int ScaleOf(QueryexStoreType type)
        {
            return type.Family == QueryexStoreFamily.QxDecimal ? type.Scale ?? 0 : 0;
        }

        /// <summary>The decimal type with no stated precision, which the provider derives.</summary>
        internal static QueryexStoreType Decimal { get; } = new(QueryexStoreFamily.QxDecimal);

        /// <summary>The type a sum over a given argument produces.</summary>
        /// <param name="argument">The argument's type.</param>
        /// <returns>The result type.</returns>
        internal static QueryexStoreType SumOf(QueryexStoreType argument)
        {
            // A running total over whole numbers is widened before it is taken, so it carries the
            // widest whole-number range the backend has rather than the narrow one it started in.
            return IsWhole(argument.Family)
                ? QueryexStoreType.QxDecimal(WidePrecision, 0)
                : QueryexStoreType.QxDecimal(WidePrecision, ScaleOf(argument));
        }

        /// <summary>The type an average over a given argument produces.</summary>
        /// <param name="argument">The argument's type.</param>
        /// <returns>The result type.</returns>
        internal static QueryexStoreType AverageOf(QueryexStoreType argument)
        {
            int scale = Math.Max(6, ScaleOf(argument));
            return QueryexStoreType.QxDecimal(WidePrecision, scale);
        }

        /// <summary>The type joining two pieces of text produces.</summary>
        /// <param name="left">The left operand's type.</param>
        /// <param name="right">The right operand's type.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     Where the two widths could together exceed what a bounded type holds, the result is
        ///     the unbounded form. The backend would otherwise clip the join to the wider of the two
        ///     operands and say nothing about it.
        /// </remarks>
        internal static QueryexStoreType ConcatOf(QueryexStoreType left, QueryexStoreType right)
        {
            bool unicode = !IsText(left.Family)
                || !IsText(right.Family)
                || IsUnicode(left.Family)
                || IsUnicode(right.Family);

            int bound = unicode ? UnicodeBound : NarrowBound;
            int? size = left.Size is int leftSize && right.Size is int rightSize
                ? leftSize + rightSize
                : null;

            int? result = size is int total && total <= bound ? total : null;
            return unicode ? QueryexStoreType.QxNVarChar(result) : QueryexStoreType.QxVarChar(result);
        }

        /// <summary>The type arithmetic over two operands produces.</summary>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand's type.</param>
        /// <param name="right">The right operand's type.</param>
        /// <returns>The result type.</returns>
        internal static QueryexStoreType ArithmeticOf(
            ArithmeticOperator op,
            QueryexStoreType left,
            QueryexStoreType right)
        {
            if (op == ArithmeticOperator.Concat)
            {
                return ConcatOf(left, right);
            }

            if (op == ArithmeticOperator.Divide)
            {
                // Division always yields a fractional result: two whole numbers are widened before
                // they are divided, and anything else is fractional already.
                return Decimal;
            }

            return IsWhole(left.Family) && IsWhole(right.Family) ? Widest(left, right) : Decimal;
        }

        /// <summary>
        ///     The type that can hold whatever either of two types can.
        /// </summary>
        /// <param name="left">One type.</param>
        /// <param name="right">The other.</param>
        /// <returns>The wider one.</returns>
        /// <remarks>
        ///     Used where a pattern's result follows its operands. Only two distinctions have to be
        ///     right — whether the result keeps a fractional part, and how wide a piece of text can
        ///     get — because those are the only two the writer acts on.
        /// </remarks>
        internal static QueryexStoreType Widest(QueryexStoreType left, QueryexStoreType right)
        {
            if (IsText(left.Family) || IsText(right.Family))
            {
                bool unicode = !IsText(left.Family)
                    || !IsText(right.Family)
                    || IsUnicode(left.Family)
                    || IsUnicode(right.Family);

                int bound = unicode ? UnicodeBound : NarrowBound;
                int? size = left.Size is int leftSize && right.Size is int rightSize
                    ? Math.Max(leftSize, rightSize)
                    : null;

                int? result = size is int widest && widest <= bound ? widest : null;
                return unicode
                    ? QueryexStoreType.QxNVarChar(result)
                    : QueryexStoreType.QxVarChar(result);
            }

            if (IsWhole(left.Family) && IsWhole(right.Family))
            {
                // The families are declared narrowest first, so the later one holds more.
                return left.Family >= right.Family ? left : right;
            }

            if (left.Family == QueryexStoreFamily.QxDecimal
                && right.Family == QueryexStoreFamily.QxDecimal)
            {
                // A type with no stated precision counts as the widest there is, because whatever
                // the provider derives for it is bounded only by what the backend supports.
                return (left.Size ?? WidePrecision) >= (right.Size ?? WidePrecision) ? left : right;
            }

            return left.Family == QueryexStoreFamily.QxDecimal
                || right.Family != QueryexStoreFamily.QxDecimal
                    ? left
                    : right;
        }
    }
}
