// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Writes a <see cref="long" /> that a JavaScript number holds exactly as a JSON number, where
    ///     the platform's rules write every <see cref="long" /> as text: a byte count in the client
    ///     metadata, which the client compares with a file's size as a number.
    /// </summary>
    /// <remarks>
    ///     A value beyond JavaScript's safe integers, ±(2^53 − 1), is a <see cref="JsonException" />
    ///     either way, so no client ever reads a rounded value.
    /// </remarks>
    internal sealed class SafeIntegerJsonConverter : JsonConverter<long>
    {
        /// <summary>The largest integer a JavaScript number holds exactly, 2^53 − 1.</summary>
        private const long MaxSafeInteger = 9_007_199_254_740_991;

        /// <inheritdoc />
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value) && IsSafe(value)
                ? value
                : throw new JsonException("Expected a JSON number that a JavaScript number holds exactly.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (!IsSafe(value))
            {
                throw new JsonException($"The value {value.ToString(CultureInfo.InvariantCulture)} is beyond the integers a JavaScript number holds exactly.");
            }

            writer.WriteNumberValue(value);
        }

        /// <summary>Whether a JavaScript number holds a value exactly.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it does.</returns>
        private static bool IsSafe(long value)
        {
            return value is >= -MaxSafeInteger and <= MaxSafeInteger;
        }
    }
}
