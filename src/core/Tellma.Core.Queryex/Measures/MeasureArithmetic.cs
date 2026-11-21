// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Numerics;

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>
    ///     The backend's arithmetic over numbers held at the backend's own types, digit for digit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every operand arrives at the type <see cref="NumericShape" /> says the backend holds it
    ///         in, and every result leaves at the type the backend gives the operation. A whole
    ///         number is held as a decimal of its width's digit count and scale zero, and is checked
    ///         against its width's range wherever a result lands in one.
    ///     </para>
    ///     <para>
    ///         Sums, products and quotients are computed exactly on the mantissas and brought to the
    ///         result type once, rounded or truncated as the backend does it. The operators of
    ///         <see cref="SqlDecimal" /> differ from the backend wherever 38 digits force the scale
    ///         down: they truncate a sum the backend rounds, and drop the sign of a negative product
    ///         that rounds to one unit of its last place. Roundings are done on the mantissa too,
    ///         because a rounding that needs one more digit fails in the backend.
    ///     </para>
    /// </remarks>
    internal static class MeasureArithmetic
    {
        /// <summary>The powers of ten a 38-digit mantissa needs, from 10^0 to 10^38.</summary>
        private static readonly Int128[] PowersOfTen = BuildPowers();

        /// <summary>The least value of each whole type, indexed by width.</summary>
        private static readonly SqlDecimal[] WholeMinimums =
        [
            new SqlDecimal(NumericShape.MinimumOf(WholeWidth.TinyInt)),
            new SqlDecimal(NumericShape.MinimumOf(WholeWidth.SmallInt)),
            new SqlDecimal(NumericShape.MinimumOf(WholeWidth.Int)),
            new SqlDecimal(NumericShape.MinimumOf(WholeWidth.BigInt)),
        ];

        /// <summary>The greatest value of each whole type, indexed by width.</summary>
        private static readonly SqlDecimal[] WholeMaximums =
        [
            new SqlDecimal(NumericShape.MaximumOf(WholeWidth.TinyInt)),
            new SqlDecimal(NumericShape.MaximumOf(WholeWidth.SmallInt)),
            new SqlDecimal(NumericShape.MaximumOf(WholeWidth.Int)),
            new SqlDecimal(NumericShape.MaximumOf(WholeWidth.BigInt)),
        ];

        /// <summary>Ten to a power.</summary>
        /// <param name="exponent">The power, 0 to 38.</param>
        /// <returns>The value.</returns>
        internal static Int128 PowerOfTen(int exponent)
        {
            return PowersOfTen[exponent];
        }

        /// <summary>A number from its mantissa, at a precision and scale.</summary>
        /// <param name="mantissa">The digits as a whole number.</param>
        /// <param name="precision">The precision.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The number.</returns>
        /// <exception cref="QueryexEvaluationException">The mantissa has more digits than the precision.</exception>
        internal static SqlDecimal FromMantissa(Int128 mantissa, byte precision, byte scale)
        {
            bool positive = mantissa >= 0;
            UInt128 magnitude = positive ? (UInt128)mantissa : (UInt128)(-mantissa);
            return magnitude >= (UInt128)PowersOfTen[precision]
                ? throw QueryexEvaluationException.Overflowed()
                : new SqlDecimal(
                    precision,
                    scale,
                    positive,
                    unchecked((int)(uint)magnitude),
                    unchecked((int)(uint)(magnitude >> 32)),
                    unchecked((int)(uint)(magnitude >> 64)),
                    unchecked((int)(uint)(magnitude >> 96)));
        }

        /// <summary>A number's mantissa at its own scale.</summary>
        /// <param name="value">The number, present.</param>
        /// <returns>The digits as a whole number, signed.</returns>
        internal static Int128 MantissaOf(SqlDecimal value)
        {
            int[] data = value.Data;
            UInt128 magnitude = (UInt128)(uint)data[0]
                | ((UInt128)(uint)data[1] << 32)
                | ((UInt128)(uint)data[2] << 64)
                | ((UInt128)(uint)data[3] << 96);

            // A 38-digit magnitude is below 2^127, so it always fits the signed form.
            var signed = (Int128)magnitude;
            return value.IsPositive ? signed : -signed;
        }

        /// <summary>
        ///     The mantissa a reader's decimal has at a given scale, exactly.
        /// </summary>
        /// <param name="value">The value as the reader delivered it, at its own scale.</param>
        /// <param name="precision">The precision it has to fit.</param>
        /// <param name="scale">The scale it is taken to.</param>
        /// <param name="mantissa">The digits at that scale.</param>
        /// <returns>
        ///     False when the value has nonzero digits past the scale, or more digits than the
        ///     precision allows.
        /// </returns>
        internal static bool TryRescale(decimal value, byte precision, byte scale, out Int128 mantissa)
        {
            // The 96-bit mantissa and the scale, read straight from the bits so nothing rounds.
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            var digits = (Int128)(((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0]);
            int own = (bits[3] >> 16) & 0xFF;
            bool negative = bits[3] < 0;

            if (own > scale)
            {
                // Only trailing zeros may be dropped: anything else is a digit the type cannot hold.
                Int128 divisor = PowersOfTen[own - scale];
                if (digits % divisor != 0)
                {
                    mantissa = default;
                    return false;
                }

                digits /= divisor;
            }
            else if (own < scale)
            {
                // A 96-bit mantissa times at most 10^38 can exceed 128 bits, so the precision is
                // checked before the scaling rather than after it.
                int shift = scale - own;
                if (digits != 0 && digits >= PowersOfTen[Math.Max(0, precision - shift)])
                {
                    mantissa = default;
                    return false;
                }

                digits *= PowersOfTen[shift];
            }

            if (digits >= PowersOfTen[precision])
            {
                mantissa = default;
                return false;
            }

            mantissa = negative ? -digits : digits;
            return true;
        }

        /// <summary>A number brought to a backend type, as the backend converts it.</summary>
        /// <param name="value">The number, present.</param>
        /// <param name="shape">The type.</param>
        /// <returns>The number at that type, rounded half away from zero where the scale shrinks.</returns>
        /// <exception cref="QueryexEvaluationException">The number does not fit the type.</exception>
        internal static SqlDecimal At(SqlDecimal value, NumericShape shape)
        {
            if (shape.IsWhole
                && (value.CompareTo(WholeMinimums[(int)shape.Width]) < 0
                    || value.CompareTo(WholeMaximums[(int)shape.Width]) > 0))
            {
                // A whole result past its type, which the backend reports rather than widens.
                throw QueryexEvaluationException.Overflowed();
            }

            if (value.Precision == shape.Precision && value.Scale == shape.Scale)
            {
                return value;
            }

            try
            {
                return SqlDecimal.ConvertToPrecScale(value, shape.Precision, shape.Scale);
            }
            catch (SqlTruncateException)
            {
                throw QueryexEvaluationException.Overflowed();
            }
            catch (OverflowException)
            {
                throw QueryexEvaluationException.Overflowed();
            }
        }

        /// <summary>A sum or difference.</summary>
        /// <param name="left">The left operand, at its type.</param>
        /// <param name="right">The right operand, at its type.</param>
        /// <param name="subtract">Whether the right operand is subtracted.</param>
        /// <param name="result">The result type.</param>
        /// <returns>The result, at its type, rounded half away from zero where the scale shrinks.</returns>
        /// <exception cref="QueryexEvaluationException">The result does not fit its type.</exception>
        /// <remarks>
        ///     Exact, then rounded once. <see cref="SqlDecimal" /> truncates a sum whose scale has to
        ///     shrink to fit 38 digits, where the backend rounds it: 2.5 + 1008 at
        ///     <c>decimal(38, 0)</c> is 1011 there, never 1010.
        /// </remarks>
        internal static SqlDecimal Add(SqlDecimal left, SqlDecimal right, bool subtract, NumericShape result)
        {
            int scale = Math.Max(left.Scale, right.Scale);
            BigInteger augend = ExactAt(left, scale);
            BigInteger addend = ExactAt(right, scale);
            return Fitted(subtract ? augend - addend : augend + addend, scale, result, truncate: false);
        }

        /// <summary>A product.</summary>
        /// <param name="left">The left operand, at its type.</param>
        /// <param name="right">The right operand, at its type.</param>
        /// <param name="result">The result type.</param>
        /// <returns>The result, at its type, rounded half away from zero where the scale shrinks.</returns>
        /// <exception cref="QueryexEvaluationException">The result does not fit its type.</exception>
        /// <remarks>
        ///     Exact, then rounded once, which keeps the sign of a negative product that rounds to one
        ///     unit of its last place: <see cref="SqlDecimal" /> drops it, and the backend keeps it.
        /// </remarks>
        internal static SqlDecimal Multiply(SqlDecimal left, SqlDecimal right, NumericShape result)
        {
            BigInteger product = (BigInteger)MantissaOf(left) * MantissaOf(right);
            return Fitted(product, left.Scale + right.Scale, result, truncate: false);
        }

        /// <summary>A quotient.</summary>
        /// <param name="left">The dividend, at its type.</param>
        /// <param name="right">The divisor, at its type.</param>
        /// <param name="result">The result type.</param>
        /// <returns>The result, at its type, truncated toward zero as the backend truncates a quotient.</returns>
        /// <exception cref="QueryexEvaluationException">The divisor is zero, or the result does not fit its type.</exception>
        /// <remarks>
        ///     The quotient is taken directly at the result's scale. Widening a whole dividend first,
        ///     as the emitted statement does, changes only the result's type, which the caller has
        ///     already fixed, and never the number.
        /// </remarks>
        internal static SqlDecimal Divide(SqlDecimal left, SqlDecimal right, NumericShape result)
        {
            if (IsZero(right))
            {
                throw QueryexEvaluationException.DividedByZero();
            }

            // The quotient at scale s is (l / 10^ls) / (r / 10^rs) * 10^s, so the dividend scales by
            // 10^(s - ls + rs); a negative power scales the divisor instead, which truncates alike.
            BigInteger dividend = MantissaOf(left);
            BigInteger divisor = MantissaOf(right);
            int shift = result.Scale - left.Scale + right.Scale;
            if (shift >= 0)
            {
                dividend *= BigInteger.Pow(10, shift);
            }
            else
            {
                divisor *= BigInteger.Pow(10, -shift);
            }

            return Fitted(BigInteger.Divide(dividend, divisor), result.Scale, result, truncate: true);
        }

        /// <summary>A negation.</summary>
        /// <param name="operand">The number, at its type.</param>
        /// <param name="result">The result type.</param>
        /// <returns>The result, at its type.</returns>
        internal static SqlDecimal Negate(SqlDecimal operand, NumericShape result)
        {
            return At(IsZero(operand) ? operand : -operand, result);
        }

        /// <summary>An absolute value.</summary>
        /// <param name="operand">The number, at its type.</param>
        /// <param name="result">The result type.</param>
        /// <returns>The result, at its type.</returns>
        internal static SqlDecimal Abs(SqlDecimal operand, NumericShape result)
        {
            return At(Magnitude(operand), result);
        }

        /// <summary>A floor or a ceiling.</summary>
        /// <param name="operand">The number, at its type.</param>
        /// <param name="ceiling">Whether the ceiling is taken rather than the floor.</param>
        /// <param name="result">The result type: the operand's precision at scale zero, or a whole type.</param>
        /// <returns>The result, at its type.</returns>
        internal static SqlDecimal Integral(SqlDecimal operand, bool ceiling, NumericShape result)
        {
            Int128 mantissa = MantissaOf(operand);
            Int128 unit = PowersOfTen[operand.Scale];
            (Int128 whole, Int128 fraction) = Int128.DivRem(mantissa, unit);
            if (ceiling && fraction > 0)
            {
                whole++;
            }
            else if (!ceiling && fraction < 0)
            {
                whole--;
            }

            return At(FromMantissa(whole, NumericShape.MaxPrecision, 0), result);
        }

        /// <summary>A rounding to a number of decimal places, half away from zero.</summary>
        /// <param name="operand">The number, at its type.</param>
        /// <param name="digits">The decimal places; negative ones round to the left of the point.</param>
        /// <param name="result">The result type: the operand's, or an int for a narrow whole type.</param>
        /// <returns>The result, at its type.</returns>
        /// <remarks>
        ///     Done on the mantissa, because the backend keeps the operand's precision and fails a
        ///     rounding that needs one more digit — 9.99 rounded to one place in
        ///     <c>decimal(3, 2)</c> — where <see cref="SqlDecimal.Round" /> quietly returns it.
        /// </remarks>
        internal static SqlDecimal Round(SqlDecimal operand, int digits, NumericShape result)
        {
            int scale = operand.Scale;
            if (digits >= scale)
            {
                return At(operand, result);
            }

            Int128 mantissa = MantissaOf(operand);
            int dropped = scale - digits;
            Int128 rounded;
            if (dropped > NumericShape.MaxPrecision)
            {
                // Every digit a 38-digit number has lies below the half-unit of the rounding.
                rounded = 0;
            }
            else
            {
                Int128 unit = PowersOfTen[dropped];
                (Int128 quotient, Int128 remainder) = Int128.DivRem(mantissa, unit);
                // Compared without doubling the remainder, which past half of 10^38 would not fit
                // the 128 bits.
                var magnitude = Int128.Abs(remainder);
                if (magnitude >= unit - magnitude)
                {
                    quotient += mantissa < 0 ? -1 : 1;
                }

                // Below 10^38 + 10^38, so the product fits the 128 bits.
                rounded = quotient * unit;
            }

            // At the operand's own precision: the digit a rounding up carries into has to fit it.
            byte precision = result.IsWhole ? (byte)NumericShape.MaxPrecision : operand.Precision;
            return At(FromMantissa(rounded, precision, (byte)scale), result);
        }

        /// <summary>Whether a number is zero.</summary>
        /// <param name="value">The number, present.</param>
        /// <returns>True when it is.</returns>
        internal static bool IsZero(SqlDecimal value)
        {
            int[] data = value.Data;
            return data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 0;
        }

        /// <summary>A number's magnitude.</summary>
        /// <param name="value">The number, present.</param>
        /// <returns>The number without its sign, at its type.</returns>
        private static SqlDecimal Magnitude(SqlDecimal value)
        {
            return value.IsPositive ? value : -value;
        }

        /// <summary>A number's mantissa at a scale at least its own, exactly.</summary>
        /// <param name="value">The number, present.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The digits at that scale.</returns>
        private static BigInteger ExactAt(SqlDecimal value, int scale)
        {
            return MantissaOf(value) * BigInteger.Pow(10, scale - value.Scale);
        }

        /// <summary>An exact result brought to its backend type.</summary>
        /// <param name="mantissa">The result's digits.</param>
        /// <param name="scale">The scale the digits are at.</param>
        /// <param name="result">The result type.</param>
        /// <param name="truncate">Whether dropped digits are cut off rather than rounded half away from zero.</param>
        /// <returns>The result, at its type.</returns>
        /// <exception cref="QueryexEvaluationException">The result does not fit its type.</exception>
        private static SqlDecimal Fitted(BigInteger mantissa, int scale, NumericShape result, bool truncate)
        {
            if (scale > result.Scale)
            {
                var unit = BigInteger.Pow(10, scale - result.Scale);
                var quotient = BigInteger.DivRem(mantissa, unit, out BigInteger remainder);
                if (!truncate && BigInteger.Abs(remainder) * 2 >= unit)
                {
                    quotient += mantissa.Sign;
                }

                mantissa = quotient;
            }
            else if (scale < result.Scale)
            {
                mantissa *= BigInteger.Pow(10, result.Scale - scale);
            }

            // A whole result is held to its width's range, a decimal to its precision's digits.
            bool fits = result.IsWhole
                ? mantissa >= NumericShape.MinimumOf(result.Width) && mantissa <= NumericShape.MaximumOf(result.Width)
                : BigInteger.Abs(mantissa) < (BigInteger)PowersOfTen[result.Precision];
            return fits
                ? FromMantissa((Int128)mantissa, result.Precision, result.Scale)
                : throw QueryexEvaluationException.Overflowed();
        }

        /// <summary>Builds the powers of ten.</summary>
        /// <returns>10^0 to 10^38.</returns>
        private static Int128[] BuildPowers()
        {
            var powers = new Int128[NumericShape.MaxPrecision + 1];
            powers[0] = 1;
            for (int exponent = 1; exponent < powers.Length; exponent++)
            {
                powers[exponent] = powers[exponent - 1] * 10;
            }

            return powers;
        }
    }
}
