// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The encoding of the ids a problem body carries as display data: a JSON number when the id
    ///     is a safe integer, one every client reads exactly, else a string of its digits.
    /// </summary>
    /// <remarks>
    ///     An exception carries no stack, so it cannot write its ids in a stack's key type. A client
    ///     shows these ids and never matches them to the rows it loaded, so a number serves an
    ///     <see cref="int" /> key, and the string keeps every digit of a <see cref="long" /> beyond a
    ///     double's exact range. Entity results write their ids in the key type instead.
    /// </remarks>
    public static class DisplayIds
    {
        /// <summary>The largest integer a double holds exactly, with every smaller one: 2^53 − 1.</summary>
        public const long MaxSafeInteger = 9_007_199_254_740_991;

        /// <summary>Whether an id lies within ±(2^53 − 1), so a JSON number carries it exactly to every client.</summary>
        /// <param name="id">The id.</param>
        /// <returns>True when the id is a safe integer.</returns>
        public static bool IsSafeInteger(long id)
        {
            return id is >= -MaxSafeInteger and <= MaxSafeInteger;
        }

        /// <summary>Writes an id as display data: a number when it is a safe integer, else a string.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="id">The id.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer" /> is null.</exception>
        public static void Write(Utf8JsonWriter writer, long id)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (IsSafeInteger(id))
            {
                writer.WriteNumberValue(id);
            }
            else
            {
                writer.WriteStringValue(id.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Reads an id written as display data: a JSON integer or a string of digits.</summary>
        /// <param name="reader">The reader, on the id's token.</param>
        /// <returns>The id.</returns>
        /// <exception cref="JsonException">The token is neither an integer nor a string of digits within range.</exception>
        internal static long Read(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long number))
            {
                return number;
            }

            Span<byte> buffer = stackalloc byte[ScalarText.MaxTokenLength];
            return reader.TokenType == JsonTokenType.String
                && ScalarText.TryRead(ref reader, buffer, out int length)
                && ScalarText.TryParseInt64(buffer[..length], out long parsed)
                ? parsed
                : throw new JsonException("An id is a JSON integer or a string of digits, within the range of a 64-bit integer.");
        }
    }

    /// <summary>Writes one problem-body id as <see cref="DisplayIds" /> encode it; reads a number or a string.</summary>
    internal sealed class DisplayIdJsonConverter : JsonConverter<long>
    {
        /// <inheritdoc />
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DisplayIds.Read(ref reader);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            DisplayIds.Write(writer, value);
        }
    }

    /// <summary>Writes a list of problem-body ids as <see cref="DisplayIds" /> encode them; reads numbers or strings.</summary>
    internal sealed class DisplayIdListJsonConverter : JsonConverter<IReadOnlyList<long>>
    {
        /// <inheritdoc />
        public override IReadOnlyList<long> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A list of ids is a JSON array.");
            }

            List<long> ids = [];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                ids.Add(DisplayIds.Read(ref reader));
            }

            return new ReadOnlyCollection<long>(ids);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, IReadOnlyList<long> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartArray();
            foreach (long id in value)
            {
                DisplayIds.Write(writer, id);
            }

            writer.WriteEndArray();
        }
    }
}
