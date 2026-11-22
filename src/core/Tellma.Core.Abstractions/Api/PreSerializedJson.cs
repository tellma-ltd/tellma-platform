// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     A value of <typeparamref name="T" /> already serialized as UTF-8 JSON under the platform's
    ///     rules, which a result member writes as those bytes, unchanged: serving a document the
    ///     platform serialized once and caches never serializes it again.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only the platform builds one, from bytes it serialized itself under
    ///         <see cref="TellmaJson.Options" />, so the bytes are written without being validated
    ///         again. A contract description describes the member as <typeparamref name="T" />, the
    ///         shape its bytes have.
    ///     </para>
    ///     <para>
    ///         A JSON schema describes the member as any value. A result that nests a deep type, whose
    ///         own route describes it, holds it pre-serialized for that reason too: the web host's
    ///         OpenAPI document writes each schema under the HTTP nesting limit, and a schema nests
    ///         about twice as deep as the documents it describes.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The type the bytes are the JSON of.</typeparam>
    [JsonConverter(typeof(PreSerializedJsonConverterFactory))]
    public sealed class PreSerializedJson<T>
    {
        /// <summary>The bytes; shared with the cache they came from, so never changed.</summary>
        private readonly byte[] _utf8;

        /// <summary>Wraps bytes the platform serialized.</summary>
        /// <param name="utf8">The value's UTF-8 JSON; shared, never changed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="utf8" /> is null.</exception>
        internal PreSerializedJson(byte[] utf8)
        {
            _utf8 = utf8 ?? throw new ArgumentNullException(nameof(utf8));
        }

        /// <summary>Serializes a value under the platform's rules.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value's bytes.</returns>
        internal static PreSerializedJson<T> Of(T value)
        {
            return new PreSerializedJson<T>(JsonSerializer.SerializeToUtf8Bytes(value, TellmaJson.Options));
        }

        /// <summary>The value's UTF-8 JSON.</summary>
        public ReadOnlyMemory<byte> Utf8 => _utf8;

        /// <summary>Reads the value back from its bytes under the platform's rules.</summary>
        /// <returns>The value.</returns>
        /// <exception cref="JsonException">The bytes are the JSON <c>null</c>.</exception>
        public T Deserialize()
        {
            return JsonSerializer.Deserialize<T>(_utf8, TellmaJson.Options)
                ?? throw new JsonException($"The pre-serialized {typeof(T).Name} is the JSON null.");
        }
    }

    /// <summary>Creates the converter of a <see cref="PreSerializedJson{T}" />.</summary>
    internal sealed class PreSerializedJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            ArgumentNullException.ThrowIfNull(typeToConvert);
            return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(PreSerializedJson<>);
        }

        /// <inheritdoc />
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(typeToConvert);
            return (JsonConverter)Activator.CreateInstance(typeof(PreSerializedJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
        }
    }

    /// <summary>
    ///     Writes a <see cref="PreSerializedJson{T}" /> as its bytes, and reads one as the bytes of the
    ///     JSON value it meets, re-encoded under the platform's rules.
    /// </summary>
    /// <typeparam name="T">The type the bytes are the JSON of.</typeparam>
    internal sealed class PreSerializedJsonConverter<T> : JsonConverter<PreSerializedJson<T>>
    {
        /// <inheritdoc />
        public override PreSerializedJson<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // A client reading an answer keeps the value's own bytes; null never reaches a converter.
            using var document = JsonDocument.ParseValue(ref reader);
            return new PreSerializedJson<T>(JsonSerializer.SerializeToUtf8Bytes(document.RootElement, TellmaJson.Options));
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, PreSerializedJson<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            // The platform serialized the bytes under the same rules, so they are written as they are.
            writer.WriteRawValue(value.Utf8.Span, skipInputValidation: true);
        }
    }
}
