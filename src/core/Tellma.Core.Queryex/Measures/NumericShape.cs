// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>The backend's whole-number types, narrowest first.</summary>
    internal enum WholeWidth : byte
    {
        /// <summary><c>tinyint</c>: 0 to 255.</summary>
        TinyInt,

        /// <summary><c>smallint</c>: 16 bits.</summary>
        SmallInt,

        /// <summary><c>int</c>: 32 bits.</summary>
        Int,

        /// <summary><c>bigint</c>: 64 bits.</summary>
        BigInt,
    }

    /// <summary>
    ///     The backend type a number is held in: one of the whole-number types, or a decimal at a
    ///     precision and scale.
    /// </summary>
    /// <remarks>
    ///     Precision alone cannot stand in for the whole-number types, because the backend treats
    ///     them differently from the decimals of the same digit count: an <c>int</c> product
    ///     overflows at 32 bits, where a <c>decimal(10, 0)</c> product grows to 21 digits.
    /// </remarks>
    /// <param name="IsWhole">Whether the type is one of the whole-number types.</param>
    /// <param name="Width">Which whole-number type, when it is one.</param>
    /// <param name="Precision">The decimal's precision, or the whole type's equivalent.</param>
    /// <param name="Scale">The decimal's scale; zero for a whole type.</param>
    internal readonly record struct NumericShape(bool IsWhole, WholeWidth Width, byte Precision, byte Scale)
    {
        /// <summary>The widest precision the backend holds.</summary>
        internal const int MaxPrecision = 38;

        /// <summary>
        ///     The scale a product or quotient keeps at least, when it has that many digits to keep,
        ///     however many integer digits it needs.
        /// </summary>
        private const int MinimumDivisionScale = 6;

        /// <summary>A whole-number type.</summary>
        /// <param name="width">Which one.</param>
        /// <returns>The shape.</returns>
        internal static NumericShape Whole(WholeWidth width)
        {
            return new NumericShape(true, width, PrecisionOf(width), 0);
        }

        /// <summary>A decimal type.</summary>
        /// <param name="precision">The precision.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The shape.</returns>
        internal static NumericShape Decimal(int precision, int scale)
        {
            return new NumericShape(false, default, (byte)precision, (byte)scale);
        }

        /// <summary>The decimal a whole-number type becomes where it meets a decimal.</summary>
        /// <param name="width">The whole type.</param>
        /// <returns>Its digit count: 3, 5, 10 or 19.</returns>
        internal static byte PrecisionOf(WholeWidth width)
        {
            return width switch
            {
                WholeWidth.TinyInt => 3,
                WholeWidth.SmallInt => 5,
                WholeWidth.Int => 10,
                WholeWidth.BigInt => 19,
                _ => 19,
            };
        }

        /// <summary>The least value a whole-number type holds.</summary>
        /// <param name="width">The whole type.</param>
        /// <returns>The least value.</returns>
        internal static long MinimumOf(WholeWidth width)
        {
            return width switch
            {
                WholeWidth.TinyInt => byte.MinValue,
                WholeWidth.SmallInt => short.MinValue,
                WholeWidth.Int => int.MinValue,
                WholeWidth.BigInt => long.MinValue,
                _ => long.MinValue,
            };
        }

        /// <summary>The greatest value a whole-number type holds.</summary>
        /// <param name="width">The whole type.</param>
        /// <returns>The greatest value.</returns>
        internal static long MaximumOf(WholeWidth width)
        {
            return width switch
            {
                WholeWidth.TinyInt => byte.MaxValue,
                WholeWidth.SmallInt => short.MaxValue,
                WholeWidth.Int => int.MaxValue,
                WholeWidth.BigInt => long.MaxValue,
                _ => long.MaxValue,
            };
        }

        /// <summary>The type of a sum or difference.</summary>
        /// <param name="left">The left operand's type.</param>
        /// <param name="right">The right operand's type.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     Two whole numbers stay in the wider whole type. Otherwise the result keeps the larger
        ///     scale and the larger integer part plus a carry digit; past 38 digits the integer part
        ///     wins and the scale shrinks to what is left.
        /// </remarks>
        internal static NumericShape Additive(NumericShape left, NumericShape right)
        {
            if (left.IsWhole && right.IsWhole)
            {
                return Whole(Wider(left.Width, right.Width));
            }

            int scale = Math.Max(left.Scale, right.Scale);
            int integer = Math.Max(left.Precision - left.Scale, right.Precision - right.Scale);
            int precision = integer + scale + 1;
            if (precision > MaxPrecision)
            {
                scale = Math.Max(0, Math.Min(scale, MaxPrecision - integer));
                precision = MaxPrecision;
            }

            return Decimal(precision, scale);
        }

        /// <summary>The type of a product.</summary>
        /// <param name="left">The left operand's type.</param>
        /// <param name="right">The right operand's type.</param>
        /// <returns>The result type.</returns>
        internal static NumericShape Multiplicative(NumericShape left, NumericShape right)
        {
            return left.IsWhole && right.IsWhole
                ? Whole(Wider(left.Width, right.Width))
                : Reduced(left.Precision + right.Precision + 1, left.Scale + right.Scale);
        }

        /// <summary>The type of a quotient.</summary>
        /// <param name="left">The dividend's type.</param>
        /// <param name="right">The divisor's type.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     Two whole numbers never divide as whole numbers: the dividend is widened to
        ///     <c>decimal(19, 0)</c> first, exactly as the emitted statement widens it.
        /// </remarks>
        internal static NumericShape Quotient(NumericShape left, NumericShape right)
        {
            NumericShape dividend = left.IsWhole && right.IsWhole ? Decimal(19, 0) : left;
            int scale = Math.Max(MinimumDivisionScale, dividend.Scale + right.Precision + 1);
            int precision = dividend.Precision - dividend.Scale + right.Scale + scale;
            return Reduced(precision, scale);
        }

        /// <summary>The type of a negation.</summary>
        /// <param name="operand">The operand's type.</param>
        /// <returns>The result type: a <c>tinyint</c>, which holds no negative value, negates into a <c>smallint</c>.</returns>
        internal static NumericShape Negation(NumericShape operand)
        {
            return operand.IsWhole && operand.Width == WholeWidth.TinyInt ? Whole(WholeWidth.SmallInt) : operand;
        }

        /// <summary>The type of an absolute value or a rounding.</summary>
        /// <param name="operand">The operand's type.</param>
        /// <returns>The result type: a narrow whole number comes back as an <c>int</c>.</returns>
        internal static NumericShape NumericFunction(NumericShape operand)
        {
            return operand.IsWhole && operand.Width < WholeWidth.Int ? Whole(WholeWidth.Int) : operand;
        }

        /// <summary>The type of a floor or a ceiling.</summary>
        /// <param name="operand">The operand's type.</param>
        /// <returns>The result type: as <see cref="NumericFunction" />, and a decimal loses its scale.</returns>
        internal static NumericShape IntegralFunction(NumericShape operand)
        {
            return operand.IsWhole ? NumericFunction(operand) : Decimal(operand.Precision, 0);
        }

        /// <summary>The type of a choice between values: a conditional or a first-present.</summary>
        /// <param name="branches">The branches' types; at least one.</param>
        /// <returns>The result type.</returns>
        /// <remarks>
        ///     Whole types alone choose the widest of them. Otherwise the result keeps the largest
        ///     scale and the largest integer part, with no carry digit; past 38 digits the integer
        ///     part wins and the scale shrinks to what is left, with no minimum.
        /// </remarks>
        internal static NumericShape Union(IReadOnlyList<NumericShape> branches)
        {
            bool whole = true;
            WholeWidth widest = WholeWidth.TinyInt;
            int scale = 0;
            int integer = 0;
            foreach (NumericShape branch in branches)
            {
                whole &= branch.IsWhole;
                if (branch.IsWhole)
                {
                    widest = Wider(widest, branch.Width);
                }

                scale = Math.Max(scale, branch.Scale);
                integer = Math.Max(integer, branch.Precision - branch.Scale);
            }

            if (whole)
            {
                return Whole(widest);
            }

            if (integer + scale > MaxPrecision)
            {
                scale = MaxPrecision - integer;
            }

            return Decimal(Math.Max(1, integer + scale), scale);
        }

        /// <summary>The public form of this type: a whole type as the decimal it meets decimals as.</summary>
        /// <returns>The precision and scale.</returns>
        internal QueryexNumericType ToPublic()
        {
            return new QueryexNumericType(Precision, Scale);
        }

        /// <summary>The wider of two whole types.</summary>
        /// <param name="one">One type.</param>
        /// <param name="other">The other.</param>
        /// <returns>The wider.</returns>
        private static WholeWidth Wider(WholeWidth one, WholeWidth other)
        {
            return one >= other ? one : other;
        }

        /// <summary>A product's or quotient's type, brought within 38 digits.</summary>
        /// <param name="precision">The precision before reduction.</param>
        /// <param name="scale">The scale before reduction.</param>
        /// <returns>The type.</returns>
        /// <remarks>
        ///     The integer part is kept whole, up to 38 digits, and the scale gives way to it — but
        ///     never below six digits, or below the scale itself where that is smaller, which is what
        ///     makes a quotient of two wide decimals still carry six decimals.
        /// </remarks>
        private static NumericShape Reduced(int precision, int scale)
        {
            if (precision <= MaxPrecision)
            {
                return Decimal(precision, scale);
            }

            int integer = Math.Min(precision - scale, MaxPrecision);
            int floor = Math.Min(scale, MinimumDivisionScale);
            int kept = Math.Max(Math.Min(scale, MaxPrecision - integer), floor);
            return Decimal(MaxPrecision, kept);
        }
    }
}
