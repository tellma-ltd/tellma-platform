// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes a Queryex type as the language names it, its member name without the <c>Qx</c>
    ///     prefix (<c>Date</c>, <c>Numeric</c>), the name every row set's columns carry too, and reads
    ///     back only those names, without regard to case.
    /// </summary>
    /// <remarks>
    ///     The prefix exists only because several of the language's type names are also platform type
    ///     names in C#; the wire never carries it. A number, a prefixed name and an undefined value are
    ///     each a <see cref="JsonException" />, the ordinary binding error.
    /// </remarks>
    internal sealed class QueryexTypeJsonConverter : JsonConverter<QueryexType>
    {
        /// <summary>The wire name of each type.</summary>
        private static readonly FrozenDictionary<QueryexType, string> NamesByType = Enum.GetValues<QueryexType>()
            .ToFrozenDictionary(static type => type, static type => type.ToString()[2..]);

        /// <summary>Each type by its wire name, without regard to case.</summary>
        private static readonly FrozenDictionary<string, QueryexType> TypesByName = NamesByType
            .ToFrozenDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>Every wire name, sorted ordinally.</summary>
        public static IReadOnlyList<string> Names { get; } = [.. NamesByType.Values.Order(StringComparer.Ordinal)];

        /// <summary>A type's wire name.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name (<c>Date</c>).</returns>
        /// <exception cref="ArgumentOutOfRangeException">The language defines no such type.</exception>
        public static string NameOf(QueryexType type)
        {
            return NamesByType.TryGetValue(type, out string? name)
                ? name
                : throw new ArgumentOutOfRangeException(nameof(type), type, "The query language defines no such type.");
        }

        /// <inheritdoc />
        public override QueryexType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType == JsonTokenType.String
                ? Parse(reader.GetString())
                : throw new JsonException("A query type is written as its name, such as \"Date\".");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, QueryexType value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(Named(value));
        }

        /// <inheritdoc />
        public override QueryexType ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Parse(reader.GetString());
        }

        /// <inheritdoc />
        public override void WriteAsPropertyName(Utf8JsonWriter writer, QueryexType value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WritePropertyName(Named(value));
        }

        /// <summary>A type by its wire name.</summary>
        /// <param name="name">The name as read.</param>
        /// <returns>The type.</returns>
        /// <exception cref="JsonException">The language names no such type.</exception>
        private static QueryexType Parse(string? name)
        {
            return name is not null && TypesByName.TryGetValue(name, out QueryexType type)
                ? type
                : throw new JsonException($"'{name}' is not a query type; the types are {string.Join(", ", Names)}.");
        }

        /// <summary>A type's wire name, for a value being written.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name.</returns>
        /// <exception cref="JsonException">The language defines no such type.</exception>
        private static string Named(QueryexType type)
        {
            return NamesByType.TryGetValue(type, out string? name)
                ? name
                : throw new JsonException($"The query type {(int)type} is not one the language defines.");
        }
    }
}
