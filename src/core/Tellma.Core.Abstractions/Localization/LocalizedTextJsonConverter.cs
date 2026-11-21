// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     Reads and writes a <see cref="LocalizedText" /> as a JSON array of exactly three elements:
    ///     the first a string, the others strings or <c>null</c>. The shape is the same under any
    ///     serializer options, since the converter writes the array itself.
    /// </summary>
    /// <remarks>
    ///     Reading refuses anything else with <see cref="JsonException" />: an object, an array of
    ///     another length, a first element that is not a string, or another element that is neither a
    ///     string nor <c>null</c>.
    /// </remarks>
    public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedText>
    {
        /// <summary>The message every refused shape carries.</summary>
        private const string ShapeMessage = "A localized text is a JSON array of three elements: a string, then two strings or nulls.";

        /// <inheritdoc />
        public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException(ShapeMessage);
            }

            // --- Exactly three slots, the first a string ---
            string text = ReadSlot(ref reader, required: true)!;
            string? text2 = ReadSlot(ref reader, required: false);
            string? text3 = ReadSlot(ref reader, required: false);

            // The serializer buffers the whole value before calling a converter, so running out of
            // tokens here means the array was cut short, never that more data is pending.
            return reader.Read() && reader.TokenType == JsonTokenType.EndArray
                ? new LocalizedText(text, text2, text3)
                : throw new JsonException(ShapeMessage);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartArray();
            writer.WriteStringValue(value.Text);
            WriteSlot(writer, value.Text2);
            WriteSlot(writer, value.Text3);
            writer.WriteEndArray();
        }

        /// <summary>Reads the next element of the array as one slot.</summary>
        /// <param name="reader">The reader, positioned before the element.</param>
        /// <param name="required">True for the first slot, which must be a string.</param>
        /// <returns>The slot's text, or <see langword="null" />.</returns>
        /// <exception cref="JsonException">The element is missing or of another kind.</exception>
        private static string? ReadSlot(ref Utf8JsonReader reader, bool required)
        {
            // A string in any slot; null in the second and third alone. GetString reads null as null.
            bool accepted = reader.Read()
                && (reader.TokenType == JsonTokenType.String || (!required && reader.TokenType == JsonTokenType.Null));

            return accepted ? reader.GetString() : throw new JsonException(ShapeMessage);
        }

        /// <summary>Writes one optional slot: its text, or <c>null</c>.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="text">The slot's text.</param>
        private static void WriteSlot(Utf8JsonWriter writer, string? text)
        {
            if (text is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(text);
            }
        }
    }
}
