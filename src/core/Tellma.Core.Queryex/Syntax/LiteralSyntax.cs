// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.SqlTypes;
using System.Globalization;
using Tellma.Core.Queryex.Binding;
using Tellma.Core.Queryex.Functions;

namespace Tellma.Core.Queryex.Syntax
{
    /// <summary>
    ///     Writes a host's value as the literal an author would have typed for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every value comes out in a form that reads back as exactly that value under the
    ///         type it was given for: a number as its digits, with a unary minus in front when it is
    ///         negative; text quoted; a truth value as its keyword; and the types the language has no
    ///         literal of their own for as a conversion of their canonical text, which is how an
    ///         author writes them too. The absent value is the conversion of <c>null</c> to the
    ///         type, so it keeps the type it was given for wherever it lands.
    ///     </para>
    ///     <para>
    ///         Every node built carries the one span it is given, so whatever is later said about
    ///         the literal points at what it replaced.
    ///     </para>
    /// </remarks>
    internal static class LiteralSyntax
    {
        /// <summary>The conversion's name, as an author writes it.</summary>
        private const string Conversion = "cast";

        /// <summary>The number of fractional-second digits the backend keeps.</summary>
        private const int FractionDigits = 7;

        /// <summary>Writes a value as a literal of a type.</summary>
        /// <param name="type">The type the value is given for.</param>
        /// <param name="value">The value, as the CLR value of that type, or null for the absent value.</param>
        /// <param name="span">The span every node built carries.</param>
        /// <param name="literal">The literal, when the value is one of the type's.</param>
        /// <returns>True when the value is one the type has, and a literal was written.</returns>
        internal static bool TryWrite(BoundType type, object? value, QueryexSpan span, out SyntaxNode literal)
        {
            SyntaxNode? written = (type, value) switch
            {
                (_, null) => Converted(new NullSyntax(span), type, span),
                (BoundType.Numeric, decimal number) => Number(number, span),
                (BoundType.Numeric, int number) => Number(number, span),
                (BoundType.Numeric, long number) => Number(number, span),
                (BoundType.String, string text) => new StringSyntax(span, text),
                (BoundType.Bool, bool flag) => new BooleanSyntax(span, flag),
                (BoundType.Date, DateOnly date) => Converted(
                    new StringSyntax(span, date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture)),
                    type,
                    span),
                (BoundType.DateTime, DateTime moment) => Converted(new StringSyntax(span, Moment(moment)), type, span),
                (BoundType.DateTimeOffset, DateTimeOffset instant) => Converted(
                    new StringSyntax(span, Moment(instant.DateTime) + Offset(instant.Offset)),
                    type,
                    span),
                (BoundType.Guid, Guid identifier) => Converted(
                    new StringSyntax(span, identifier.ToString("D", CultureInfo.InvariantCulture)),
                    type,
                    span),
                _ => null,
            };

            literal = written ?? new NullSyntax(span);
            return written is not null;
        }

        /// <summary>Writes a number, negative ones as a negation of their magnitude.</summary>
        /// <param name="value">The number.</param>
        /// <param name="span">The span every node built carries.</param>
        /// <returns>The literal.</returns>
        /// <remarks>
        ///     The grammar's numbers are unsigned, so a negative value is the negation of a
        ///     positive literal, which is also how an author writes one. The digits keep the scale
        ///     the value carries, since a literal's scale is part of what it denotes; a negative zero
        ///     is written as the zero it equals.
        /// </remarks>
        private static SyntaxNode Number(decimal value, QueryexSpan span)
        {
            decimal magnitude = Math.Abs(value);
            string text = magnitude.ToString(CultureInfo.InvariantCulture);
            byte scale = magnitude.Scale;

            // Significant digits the way the scanner counts them: the integer digits after any
            // leading zeros, then every fractional digit, and never fewer than one.
            int point = text.IndexOf('.', StringComparison.Ordinal);
            string integer = point < 0 ? text : text[..point];
            int precision = Math.Max(1, integer.TrimStart('0').Length + scale);

            int[] bits = decimal.GetBits(magnitude);
            SqlDecimal exact = new((byte)precision, scale, fPositive: true, bits[0], bits[1], bits[2], 0);
            NumberSyntax number = new(span, exact, (byte)precision, scale, text);

            return value < 0m
                ? new UnarySyntax(span, UnaryOperatorKind.Negate, span, number)
                : number;
        }

        /// <summary>Writes a value as a conversion to a type.</summary>
        /// <param name="operand">What is converted: the value's canonical text, or <c>null</c>.</param>
        /// <param name="type">The type converted to.</param>
        /// <param name="span">The span every node built carries.</param>
        /// <returns>The conversion.</returns>
        private static CallSyntax Converted(SyntaxNode operand, BoundType type, QueryexSpan span)
        {
            return new CallSyntax(
                span,
                Conversion,
                span,
                [operand, new StringSyntax(span, CastRules.TargetName(type))]);
        }

        /// <summary>A date and time's canonical text, to the backend's precision.</summary>
        /// <param name="moment">The date and time; its kind is ignored.</param>
        /// <returns>The text, with up to seven fractional digits and no trailing zeros.</returns>
        private static string Moment(DateTime moment)
        {
            string text = moment.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);
            long ticks = moment.Ticks % TimeSpan.TicksPerSecond;
            if (ticks == 0)
            {
                return text;
            }

            string fraction = ticks
                .ToString(CultureInfo.InvariantCulture)
                .PadLeft(FractionDigits, '0')
                .TrimEnd('0');

            return text + "." + fraction;
        }

        /// <summary>An offset from UTC, signed, in hours and minutes.</summary>
        /// <param name="offset">The offset, a whole number of minutes.</param>
        /// <returns>The text, such as <c>+03:00</c> or <c>-05:30</c>.</returns>
        private static string Offset(TimeSpan offset)
        {
            TimeSpan magnitude = offset.Duration();
            return (offset < TimeSpan.Zero ? "-" : "+")
                + magnitude.Hours.ToString("00", CultureInfo.InvariantCulture)
                + ":"
                + magnitude.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
