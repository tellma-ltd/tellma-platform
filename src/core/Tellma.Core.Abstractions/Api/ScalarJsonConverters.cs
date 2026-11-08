// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes a <see cref="long" /> as a JSON string of every digit, so a client that reads JSON
    ///     numbers as doubles never loses one; reads a string or a number.
    /// </summary>
    internal sealed class Int64JsonConverter : JsonConverter<long>
    {
        /// <inheritdoc />
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // A number token must be an integer in range; a string token must be in the digit grammar.
            return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long number)
                ? number
                : reader.TokenType == JsonTokenType.String && TryReadText(ref reader, out long parsed)
                    ? parsed
                    : throw new JsonException("A 64-bit integer is a JSON number or a string of digits, within its range.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[24];
            value.TryFormat(text[1..], out int written, default, CultureInfo.InvariantCulture);
            ScalarJson.WriteQuoted(writer, text, written);
        }

        /// <inheritdoc />
        public override long ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadText(ref reader, out long parsed)
                ? parsed
                : throw new JsonException("A 64-bit integer key is a string of digits, within its range.");
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[20];
            value.TryFormat(text, out int written, default, CultureInfo.InvariantCulture);
            writer.WritePropertyName(text[..written]);
        }

        /// <summary>Parses the current string or property-name token as a 64-bit integer.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryReadText(ref Utf8JsonReader reader, out long value)
        {
            value = 0;
            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return ScalarText.TryRead(ref reader, buffer, out int length) && ScalarText.TryParseInt64(buffer[..length], out value);
        }
    }

    /// <summary>
    ///     Writes a <see cref="decimal" /> as a JSON string at its full scale, never in exponent form
    ///     (<c>"1234.5000"</c>); reads a plain decimal string or a JSON number, refusing a value it
    ///     cannot hold exactly rather than rounding it.
    /// </summary>
    internal sealed class DecimalJsonConverter : JsonConverter<decimal>
    {
        /// <summary>The fixed-point formats by scale, so a value is written with exactly the digits it holds.</summary>
        private static readonly string[] FixedFormats = [.. Enumerable.Range(0, 29).Select(static scale => "F" + scale.ToString(CultureInfo.InvariantCulture))];

        /// <inheritdoc />
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // A JSON number may carry an exponent, which JSON allows; a string may not, so a string
            // is always the plain form this converter writes.
            return (reader.TokenType is JsonTokenType.Number or JsonTokenType.String)
                && TryReadText(ref reader, allowExponent: reader.TokenType == JsonTokenType.Number, out decimal value)
                ? value
                : throw new JsonException("A decimal is a JSON number or a plain decimal string, exactly representable.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[48];
            value.TryFormat(text[1..], out int written, FixedFormats[value.Scale], CultureInfo.InvariantCulture);
            ScalarJson.WriteQuoted(writer, text, written);
        }

        /// <inheritdoc />
        public override decimal ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadText(ref reader, allowExponent: false, out decimal value)
                ? value
                : throw new JsonException("A decimal key is a plain decimal string, exactly representable.");
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[48];
            value.TryFormat(text, out int written, FixedFormats[value.Scale], CultureInfo.InvariantCulture);
            writer.WritePropertyName(text[..written]);
        }

        /// <summary>Parses the current token as a decimal.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="allowExponent">Whether an exponent is accepted.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryReadText(ref Utf8JsonReader reader, bool allowExponent, out decimal value)
        {
            value = default;
            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return ScalarText.TryRead(ref reader, buffer, out int length)
                && ScalarText.TryParseDecimal(buffer[..length], allowExponent, out value);
        }
    }

    /// <summary>
    ///     Writes a <see cref="DateTime" /> as <c>"yyyy-MM-ddTHH:mm:ss.fffffff"</c>: seven fractional
    ///     digits always and no offset, whatever its kind. Reads the same form, with one to seven
    ///     fractional digits or none, and refuses an offset, which a wall-clock value cannot carry.
    /// </summary>
    internal sealed class DateTimeJsonConverter : JsonConverter<DateTime>
    {
        /// <summary>The written form.</summary>
        private const string Format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff";

        /// <inheritdoc />
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.String && TryReadText(ref reader, out DateTime value)
                ? value
                : throw new JsonException("A date-time is a string 'yyyy-MM-ddTHH:mm:ss' with up to seven fractional digits and no offset.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[32];
            value.TryFormat(text[1..], out int written, Format, CultureInfo.InvariantCulture);
            ScalarJson.WriteQuoted(writer, text, written);
        }

        /// <inheritdoc />
        public override DateTime ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadText(ref reader, out DateTime value)
                ? value
                : throw new JsonException("A date-time key is 'yyyy-MM-ddTHH:mm:ss' with up to seven fractional digits and no offset.");
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[32];
            value.TryFormat(text, out int written, Format, CultureInfo.InvariantCulture);
            writer.WritePropertyName(text[..written]);
        }

        /// <summary>Parses the current token as a date-time.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryReadText(ref Utf8JsonReader reader, out DateTime value)
        {
            value = default;
            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return ScalarText.TryRead(ref reader, buffer, out int length) && ScalarText.TryParseDateTime(buffer[..length], out value);
        }
    }

    /// <summary>
    ///     Writes a <see cref="DateTimeOffset" /> as <c>"yyyy-MM-ddTHH:mm:ss.fffffff+00:00"</c>: seven
    ///     fractional digits always and the offset, so a stored stamp round-trips bit for bit. Reads
    ///     the same form with one to seven fractional digits or none, and <c>Z</c> for a zero offset;
    ///     the offset is required.
    /// </summary>
    /// <remarks>
    ///     A value is written raw, so its <c>+</c> stays a plus. A dictionary key goes through the
    ///     writer's encoder, which escapes <c>+</c> as <c>\u002B</c>: the key is still the same text to
    ///     every JSON reader, and reads back here.
    /// </remarks>
    internal sealed class DateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        /// <summary>The written form.</summary>
        private const string Format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffffzzz";

        /// <inheritdoc />
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.String && TryReadText(ref reader, out DateTimeOffset value)
                ? value
                : throw new JsonException("An instant is a string 'yyyy-MM-ddTHH:mm:ss' with up to seven fractional digits and an offset.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[40];
            value.TryFormat(text[1..], out int written, Format, CultureInfo.InvariantCulture);
            ScalarJson.WriteQuoted(writer, text, written);
        }

        /// <inheritdoc />
        public override DateTimeOffset ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadText(ref reader, out DateTimeOffset value)
                ? value
                : throw new JsonException("An instant key is 'yyyy-MM-ddTHH:mm:ss' with up to seven fractional digits and an offset.");
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[40];
            value.TryFormat(text, out int written, Format, CultureInfo.InvariantCulture);
            writer.WritePropertyName(text[..written]);
        }

        /// <summary>Parses the current token as an instant.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryReadText(ref Utf8JsonReader reader, out DateTimeOffset value)
        {
            value = default;
            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return ScalarText.TryRead(ref reader, buffer, out int length) && ScalarText.TryParseDateTimeOffset(buffer[..length], out value);
        }
    }

    /// <summary>
    ///     Writes a <see cref="TimeOnly" /> as <c>"HH:mm:ss.fffffff"</c>, seven fractional digits
    ///     always; reads the same form with one to seven fractional digits or none.
    /// </summary>
    internal sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
    {
        /// <summary>The written form.</summary>
        private const string Format = "HH':'mm':'ss'.'fffffff";

        /// <inheritdoc />
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.String && TryReadText(ref reader, out TimeOnly value)
                ? value
                : throw new JsonException("A time of day is a string 'HH:mm:ss' with up to seven fractional digits.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[24];
            value.TryFormat(text[1..], out int written, Format, CultureInfo.InvariantCulture);
            ScalarJson.WriteQuoted(writer, text, written);
        }

        /// <inheritdoc />
        public override TimeOnly ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadText(ref reader, out TimeOnly value)
                ? value
                : throw new JsonException("A time-of-day key is 'HH:mm:ss' with up to seven fractional digits.");
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        {
            Span<byte> text = stackalloc byte[24];
            value.TryFormat(text, out int written, Format, CultureInfo.InvariantCulture);
            writer.WritePropertyName(text[..written]);
        }

        /// <summary>Parses the current token as a time of day.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="value">The value when parsed.</param>
        /// <returns>True when parsed.</returns>
        private static bool TryReadText(ref Utf8JsonReader reader, out TimeOnly value)
        {
            value = default;
            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return ScalarText.TryRead(ref reader, buffer, out int length) && ScalarText.TryParseTimeOnly(buffer[..length], out value);
        }
    }

    /// <summary>Helpers the scalar converters share.</summary>
    internal static class ScalarJson
    {
        /// <summary>
        ///     Writes formatted ASCII text as a JSON string. The text is in <paramref name="buffer" />
        ///     from index 1; the quotes go at index 0 and after the text. The value is written raw,
        ///     because the text is plain ASCII the encoder would otherwise escape in part (<c>+</c>).
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="buffer">The buffer, with room for the closing quote.</param>
        /// <param name="length">The text's length.</param>
        public static void WriteQuoted(Utf8JsonWriter writer, Span<byte> buffer, int length)
        {
            buffer[0] = (byte)'"';
            buffer[length + 1] = (byte)'"';
            writer.WriteRawValue(buffer[..(length + 2)], skipInputValidation: true);
        }
    }
}
