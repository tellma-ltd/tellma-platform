// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The exact text grammars of the wire's string-encoded scalars, parsed by hand so the
    ///     accepted forms are the same on every machine, whatever its culture: no whitespace, no
    ///     culture-specific separators, and never a silently rounded or shifted value.
    /// </summary>
    internal static class ScalarText
    {
        /// <summary>The longest token text a scalar converter reads; anything longer is refused unread.</summary>
        public const int MaxTokenLength = 128;

        /// <summary>The most digits a decimal's 96-bit significand holds.</summary>
        private const int MaxDecimalDigits = 29;

        /// <summary>The largest scale a decimal holds.</summary>
        private const int MaxDecimalScale = 28;

        /// <summary>The largest 96-bit significand, the ceiling of a decimal's magnitude.</summary>
        private static readonly UInt128 MaxDecimalSignificand = (UInt128.One << 96) - 1;

        /// <summary>
        ///     Copies the current token's text, unescaped, into a buffer, refusing a token longer than
        ///     <see cref="MaxTokenLength" /> bytes.
        /// </summary>
        /// <param name="reader">The reader, positioned on a string, property name or number token.</param>
        /// <param name="buffer">A buffer of at least <see cref="MaxTokenLength" /> bytes.</param>
        /// <param name="length">The text's length in bytes when it fits.</param>
        /// <returns>True when the text fits.</returns>
        public static bool TryRead(ref Utf8JsonReader reader, scoped Span<byte> buffer, out int length)
        {
            length = 0;
            long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

            if ((reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) && reader.ValueIsEscaped)
            {
                // An escape sequence is at most six bytes per character, so a raw token beyond six
                // times the limit cannot unescape to something short enough. CopyString needs room
                // for the unescaped text, which is never longer than the raw token.
                if (rawLength > MaxTokenLength * 6L)
                {
                    return false;
                }

                Span<byte> unescaped = stackalloc byte[(int)rawLength];
                int written = reader.CopyString(unescaped);
                if (written > MaxTokenLength)
                {
                    return false;
                }

                unescaped[..written].CopyTo(buffer);
                length = written;
                return true;
            }

            // An unescaped string and a number token are their raw bytes.
            if (rawLength > MaxTokenLength)
            {
                return false;
            }

            if (reader.HasValueSequence)
            {
                reader.ValueSequence.CopyTo(buffer);
            }
            else
            {
                reader.ValueSpan.CopyTo(buffer);
            }

            length = (int)rawLength;
            return true;
        }

        /// <summary>Parses <c>-?[0-9]+</c> into a <see cref="long" />.</summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="value">The value when the text is in the grammar and in range.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseInt64(ReadOnlySpan<byte> text, out long value)
        {
            value = 0;
            int start = text.Length > 0 && text[0] == (byte)'-' ? 1 : 0;
            if (start == text.Length || !AllDigits(text[start..]))
            {
                return false;
            }

            // The grammar check above leaves only a sign and digits, which AllowLeadingSign accepts.
            return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        ///     Parses <c>-?[0-9]+(\.[0-9]+)?</c>, with an exponent when <paramref name="allowExponent" />,
        ///     into a <see cref="decimal" /> holding exactly the value written, its scale included.
        /// </summary>
        /// <remarks>
        ///     A value a decimal cannot hold exactly is refused rather than rounded: more significant
        ///     digits than the significand holds, a magnitude beyond its range, or non-zero digits
        ///     beyond scale 28. Trailing zeros are dropped only where the value needs it to fit, which
        ///     changes no digit.
        /// </remarks>
        /// <param name="text">The UTF-8 text, at most <see cref="MaxTokenLength" /> bytes.</param>
        /// <param name="allowExponent">Whether an <c>e</c> or <c>E</c> exponent is accepted, as a JSON number allows.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseDecimal(ReadOnlySpan<byte> text, bool allowExponent, out decimal value)
        {
            value = default;

            // The digit buffer below is sized for the longest token the converters read.
            if (text.Length > MaxTokenLength)
            {
                return false;
            }

            int i = 0;

            // --- Sign, integer part, fraction part ---
            bool negative = i < text.Length && text[i] == (byte)'-';
            if (negative)
            {
                i++;
            }

            int integerStart = i;
            while (i < text.Length && IsDigit(text[i]))
            {
                i++;
            }

            ReadOnlySpan<byte> integerDigits = text[integerStart..i];
            ReadOnlySpan<byte> fractionDigits = default;
            if (integerDigits.IsEmpty)
            {
                return false;
            }

            if (i < text.Length && text[i] == (byte)'.')
            {
                int fractionStart = ++i;
                while (i < text.Length && IsDigit(text[i]))
                {
                    i++;
                }

                fractionDigits = text[fractionStart..i];
                if (fractionDigits.IsEmpty)
                {
                    return false;
                }
            }

            // --- Exponent ---
            int exponent = 0;
            if (i < text.Length && (text[i] == (byte)'e' || text[i] == (byte)'E'))
            {
                if (!allowExponent)
                {
                    return false;
                }

                i++;
                bool negativeExponent = false;
                if (i < text.Length && (text[i] == (byte)'+' || text[i] == (byte)'-'))
                {
                    negativeExponent = text[i] == (byte)'-';
                    i++;
                }

                int exponentStart = i;
                while (i < text.Length && IsDigit(text[i]))
                {
                    // Saturated: any magnitude past this is unrepresentable for a non-zero value
                    // and irrelevant for zero, and the cap keeps the arithmetic below in range.
                    exponent = Math.Min((exponent * 10) + (text[i] - '0'), 100_000);
                    i++;
                }

                if (i == exponentStart)
                {
                    return false;
                }

                exponent = negativeExponent ? -exponent : exponent;
            }

            if (i != text.Length)
            {
                return false;
            }

            // --- The significand's digits and the scale ---
            // The value is digits × 10^-scale. Leading zeros carry no information.
            Span<byte> digits = stackalloc byte[MaxTokenLength + MaxDecimalDigits];
            int count = 0;
            foreach (byte digit in integerDigits)
            {
                if (count > 0 || digit != (byte)'0')
                {
                    digits[count++] = digit;
                }
            }

            foreach (byte digit in fractionDigits)
            {
                if (count > 0 || digit != (byte)'0')
                {
                    digits[count++] = digit;
                }
            }

            int scale = fractionDigits.Length - exponent;
            if (count == 0)
            {
                // Zero keeps the scale it was written with, as far as a decimal can hold it.
                value = new decimal(0, 0, 0, false, (byte)Math.Clamp(scale, 0, MaxDecimalScale));
                return true;
            }

            // A negative scale is an integer with trailing zeros, written out in full.
            if (scale < 0)
            {
                if (count - scale > MaxDecimalDigits)
                {
                    return false;
                }

                digits.Slice(count, -scale).Fill((byte)'0');
                count -= scale;
                scale = 0;
            }

            // Drop trailing zeros only while the value needs it to fit; this changes no digit.
            while (count > 1 && digits[count - 1] == (byte)'0' && scale > 0 && (scale > MaxDecimalScale || count > MaxDecimalDigits))
            {
                count--;
                scale--;
            }

            if (scale > MaxDecimalScale || count > MaxDecimalDigits)
            {
                return false;
            }

            UInt128 significand = Accumulate(digits[..count]);
            while (significand > MaxDecimalSignificand && scale > 0 && digits[count - 1] == (byte)'0')
            {
                count--;
                scale--;
                significand = Accumulate(digits[..count]);
            }

            if (significand > MaxDecimalSignificand)
            {
                return false;
            }

            value = new decimal(
                (int)(uint)(significand & uint.MaxValue),
                (int)(uint)((significand >> 32) & uint.MaxValue),
                (int)(uint)((significand >> 64) & uint.MaxValue),
                negative,
                (byte)scale);
            return true;
        }

        /// <summary>Parses <c>yyyy-MM-ddTHH:mm:ss</c> with an optional fraction of one to seven digits, and no offset.</summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="value">The value, of unspecified kind, when parsed.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseDateTime(ReadOnlySpan<byte> text, out DateTime value)
        {
            return TryParseDateTimeCore(text, out value, out int consumed) && consumed == text.Length;
        }

        /// <summary>
        ///     Parses <c>yyyy-MM-ddTHH:mm:ss</c> with an optional fraction of one to seven digits,
        ///     followed by <c>Z</c> or a <c>±HH:mm</c> offset, which is required.
        /// </summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseDateTimeOffset(ReadOnlySpan<byte> text, out DateTimeOffset value)
        {
            value = default;
            if (!TryParseDateTimeCore(text, out DateTime local, out int consumed))
            {
                return false;
            }

            ReadOnlySpan<byte> rest = text[consumed..];
            TimeSpan offset;
            if (rest.Length == 1 && (rest[0] == (byte)'Z' || rest[0] == (byte)'z'))
            {
                offset = TimeSpan.Zero;
            }
            else if (rest.Length == 6
                && (rest[0] == (byte)'+' || rest[0] == (byte)'-')
                && rest[3] == (byte)':'
                && TryTwoDigits(rest[1..], out int hours)
                && TryTwoDigits(rest[4..], out int minutes)
                && minutes < 60)
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (offset > TimeSpan.FromHours(14))
                {
                    return false;
                }

                offset = rest[0] == (byte)'-' ? -offset : offset;
            }
            else
            {
                return false;
            }

            // The offset can move the instant outside the representable range (0001-01-01 at +01:00).
            long utcTicks = local.Ticks - offset.Ticks;
            if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            value = new DateTimeOffset(local.Ticks, offset);
            return true;
        }

        /// <summary>Parses <c>HH:mm:ss</c> with an optional fraction of one to seven digits.</summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseTimeOnly(ReadOnlySpan<byte> text, out TimeOnly value)
        {
            value = default;
            if (!TryParseTime(text, out long ticks, out int consumed) || consumed != text.Length)
            {
                return false;
            }

            value = new TimeOnly(ticks);
            return true;
        }

        /// <summary>Parses the date and time without an offset, reporting how much of the text it read.</summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="value">The value, of unspecified kind, when parsed.</param>
        /// <param name="consumed">The bytes read.</param>
        /// <returns>True when a valid date and time starts the text.</returns>
        private static bool TryParseDateTimeCore(ReadOnlySpan<byte> text, out DateTime value, out int consumed)
        {
            value = default;
            consumed = 0;

            // yyyy-MM-dd and the T separator: eleven bytes before the time.
            if (text.Length < 11
                || text[4] != (byte)'-'
                || text[7] != (byte)'-'
                || (text[10] != (byte)'T' && text[10] != (byte)'t')
                || !TryDigits(text[..4], out int year)
                || !TryTwoDigits(text[5..], out int month)
                || !TryTwoDigits(text[8..], out int day)
                || year < 1
                || month is < 1 or > 12
                || day < 1
                || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            if (!TryParseTime(text[11..], out long timeTicks, out int timeConsumed))
            {
                return false;
            }

            value = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(timeTicks);
            consumed = 11 + timeConsumed;
            return true;
        }

        /// <summary>Parses <c>HH:mm:ss</c> and an optional fraction at the start of the text.</summary>
        /// <param name="text">The UTF-8 text.</param>
        /// <param name="ticks">The time of day in ticks when parsed.</param>
        /// <param name="consumed">The bytes read.</param>
        /// <returns>True when a valid time starts the text.</returns>
        private static bool TryParseTime(ReadOnlySpan<byte> text, out long ticks, out int consumed)
        {
            ticks = 0;
            consumed = 0;
            if (text.Length < 8
                || text[2] != (byte)':'
                || text[5] != (byte)':'
                || !TryTwoDigits(text, out int hour)
                || !TryTwoDigits(text[3..], out int minute)
                || !TryTwoDigits(text[6..], out int second)
                || hour > 23
                || minute > 59
                || second > 59)
            {
                return false;
            }

            ticks = (hour * TimeSpan.TicksPerHour) + (minute * TimeSpan.TicksPerMinute) + (second * TimeSpan.TicksPerSecond);
            consumed = 8;

            // An optional fraction of one to seven digits, each a tenth of the one before.
            if (consumed < text.Length && text[consumed] == (byte)'.')
            {
                int start = ++consumed;
                long fraction = 0;
                while (consumed < text.Length && IsDigit(text[consumed]) && consumed - start < 7)
                {
                    fraction = (fraction * 10) + (text[consumed] - '0');
                    consumed++;
                }

                int length = consumed - start;
                if (length == 0 || (consumed < text.Length && IsDigit(text[consumed])))
                {
                    return false;
                }

                for (int pad = length; pad < 7; pad++)
                {
                    fraction *= 10;
                }

                ticks += fraction;
            }

            return true;
        }

        /// <summary>Accumulates up to 29 ASCII digits into an unsigned integer.</summary>
        /// <param name="digits">The digits.</param>
        /// <returns>The integer.</returns>
        private static UInt128 Accumulate(ReadOnlySpan<byte> digits)
        {
            UInt128 result = UInt128.Zero;
            foreach (byte digit in digits)
            {
                result = (result * 10) + (uint)(digit - '0');
            }

            return result;
        }

        /// <summary>Parses exactly <paramref name="digits" />.Length ASCII digits.</summary>
        /// <param name="digits">The digits.</param>
        /// <param name="value">The value when every byte is a digit.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryDigits(ReadOnlySpan<byte> digits, out int value)
        {
            value = 0;
            foreach (byte digit in digits)
            {
                if (!IsDigit(digit))
                {
                    return false;
                }

                value = (value * 10) + (digit - '0');
            }

            return true;
        }

        /// <summary>Parses the two ASCII digits that start the text.</summary>
        /// <param name="text">The text.</param>
        /// <param name="value">The value when both are digits.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryTwoDigits(ReadOnlySpan<byte> text, out int value)
        {
            value = 0;
            return text.Length >= 2 && TryDigits(text[..2], out value);
        }

        /// <summary>Whether every byte is an ASCII digit.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when all are digits.</returns>
        private static bool AllDigits(ReadOnlySpan<byte> text)
        {
            foreach (byte b in text)
            {
                if (!IsDigit(b))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a byte is an ASCII digit.</summary>
        /// <param name="b">The byte.</param>
        /// <returns>True for <c>0</c> to <c>9</c>.</returns>
        private static bool IsDigit(byte b)
        {
            return b is >= (byte)'0' and <= (byte)'9';
        }
    }
}
