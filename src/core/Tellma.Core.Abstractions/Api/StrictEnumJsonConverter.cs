// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes every enum as its member name (or the name its member declares for the wire), and
    ///     reads back only what the enum defines: one declared member, or, for a <see cref="FlagsAttribute" />
    ///     enum, a comma list of declared members whose bits are all declared.
    /// </summary>
    /// <remarks>
    ///     The framework's string converter does the naming; this one refuses what it would let
    ///     through. An integer, a numeric string or list element, a comma list on an enum without
    ///     <see cref="FlagsAttribute" />, an undefined value and an undeclared bit are each a
    ///     <see cref="JsonException" />, the ordinary binding error, so an undefined value never
    ///     reaches a row, where every later read of it would fail to serialize. A name an enum member
    ///     declares for the wire therefore never starts with a digit or a sign.
    /// </remarks>
    internal sealed class StrictEnumJsonConverter : JsonConverterFactory
    {
        /// <summary>The framework's converter, which maps names to members and refuses JSON numbers.</summary>
        private readonly JsonStringEnumConverter _names = new(namingPolicy: null, allowIntegerValues: false);

        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsEnum;
        }

        /// <inheritdoc />
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            JsonConverter names = _names.CreateConverter(typeToConvert, options)
                ?? throw new InvalidOperationException($"The framework has no string converter for '{typeToConvert}'.");

            return (JsonConverter)Activator.CreateInstance(typeof(DefinedEnumJsonConverter<>).MakeGenericType(typeToConvert), names)!;
        }
    }

    /// <summary>
    ///     One enum's strict converter: the framework's name mapping, then a refusal of anything the
    ///     enum does not define.
    /// </summary>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="names">The framework's string converter for the enum.</param>
    internal sealed class DefinedEnumJsonConverter<TEnum>(JsonConverter<TEnum> names) : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        /// <summary>Whether the enum is a set of flags, which a comma list may combine.</summary>
        private static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);

        /// <summary>Every bit some declared member sets; a flags value may set no other.</summary>
        private static readonly ulong DeclaredBits = Enum.GetValues<TEnum>().Aggregate(0UL, static (bits, member) => bits | Bits(member));

        /// <inheritdoc />
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            RefuseForm(ref reader);
            return Defined(names.Read(ref reader, typeToConvert, options));
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            names.Write(writer, value, options);
        }

        /// <inheritdoc />
        public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            RefuseForm(ref reader);
            return Defined(names.ReadAsPropertyName(ref reader, typeToConvert, options));
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            names.WriteAsPropertyName(writer, value, options);
        }

        /// <summary>
        ///     Refuses, before any name is mapped, a text the enum's grammar does not admit: a comma
        ///     list on an enum without flags, and a list element that starts like a number.
        /// </summary>
        /// <param name="reader">The reader, on a string or property-name token.</param>
        private static void RefuseForm(ref Utf8JsonReader reader)
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
            {
                // A number, a boolean or a null: the name converter refuses it.
                return;
            }

            // The common token is read in place. An escaped or segmented one is unescaped first, so
            // an escaped comma counts as the comma it stands for.
            if (reader.ValueIsEscaped || reader.HasValueSequence)
            {
                RefuseForm((reader.GetString() ?? string.Empty).AsSpan());
            }
            else
            {
                RefuseForm(reader.ValueSpan);
            }
        }

        /// <summary>Refuses a comma list on an enum without flags, and a list element that starts like a number.</summary>
        /// <typeparam name="TUnit">The text's code unit: a UTF-8 byte or a UTF-16 character.</typeparam>
        /// <param name="text">The text, unescaped.</param>
        private static void RefuseForm<TUnit>(ReadOnlySpan<TUnit> text)
            where TUnit : unmanaged, IBinaryInteger<TUnit>
        {
            // Every character this grammar looks at is ASCII, so a UTF-8 byte and a UTF-16 unit compare alike.
            bool hasComma = false;
            bool atElementStart = true;
            foreach (TUnit code in text)
            {
                int unit = int.CreateTruncating(code);
                if (unit == ',')
                {
                    hasComma = true;
                    atElementStart = true;
                }
                else if (atElementStart && unit is not (' ' or '\t' or '\r' or '\n'))
                {
                    // A member name never starts with a digit or a sign, so an element that does is a
                    // number in disguise.
                    if (unit is (>= '0' and <= '9') or '-' or '+')
                    {
                        throw new JsonException($"'{typeof(TEnum).Name}' is written as member names, never as a number.");
                    }

                    atElementStart = false;
                }
            }

            if (hasComma && !IsFlags)
            {
                throw new JsonException($"'{typeof(TEnum).Name}' takes one member name, not a list.");
            }
        }

        /// <summary>Refuses a value the enum does not define.</summary>
        /// <param name="value">The value the names mapped to.</param>
        /// <returns>The value.</returns>
        private static TEnum Defined(TEnum value)
        {
            bool defined = IsFlags ? (Bits(value) & ~DeclaredBits) == 0 : Enum.IsDefined(value);
            return defined
                ? value
                : throw new JsonException(string.Create(
                    CultureInfo.InvariantCulture, $"The value is not a defined member of '{typeof(TEnum).Name}'."));
        }

        /// <summary>A value's bits, whatever the enum's underlying type.</summary>
        /// <param name="value">The value.</param>
        /// <returns>Its bits as an unsigned 64-bit number.</returns>
        private static ulong Bits(TEnum value)
        {
            // A signed underlying type is reinterpreted, not range-checked: only the bits matter.
            return Type.GetTypeCode(typeof(TEnum)) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                ? unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture))
                : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
    }
}
