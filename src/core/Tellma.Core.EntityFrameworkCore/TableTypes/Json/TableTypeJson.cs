// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Json
{
    /// <summary>
    ///     Canonical JSON serialization for table-type metadata. The canonical form — fixed property
    ///     order (declaration order of the records), camelCase names, no whitespace, nulls omitted —
    ///     makes <b>string equality equivalent to definition equality</b>, which is what lets the
    ///     migrations differ compare definition annotations verbatim without re-deriving anything.
    /// </summary>
    public static class TableTypeJson
    {
        /// <summary>Serializes a <see cref="TableTypeDefinition" /> to its canonical JSON form.</summary>
        /// <param name="definition">The definition to serialize.</param>
        /// <returns>The canonical JSON string.</returns>
        public static string Serialize(TableTypeDefinition definition)
        {
            return JsonSerializer.Serialize(definition, TableTypeJsonContext.Default.TableTypeDefinition);
        }

        /// <summary>Deserializes a <see cref="TableTypeDefinition" /> from its canonical JSON form.</summary>
        /// <param name="json">The canonical JSON produced by <see cref="Serialize(TableTypeDefinition)" />.</param>
        /// <returns>The deserialized definition.</returns>
        public static TableTypeDefinition DeserializeDefinition(string json)
        {
            return JsonSerializer.Deserialize(json, TableTypeJsonContext.Default.TableTypeDefinition)
                ?? throw new InvalidOperationException($"Cannot deserialize a table-type definition from '{json}'.");
        }

        /// <summary>Serializes the built-in table types configuration to its canonical JSON form.</summary>
        /// <param name="configuration">The configuration to serialize.</param>
        /// <returns>The canonical JSON string.</returns>
        public static string Serialize(BuiltInTableTypesConfiguration configuration)
        {
            return JsonSerializer.Serialize(configuration, TableTypeJsonContext.Default.BuiltInTableTypesConfiguration);
        }

        /// <summary>Deserializes the built-in table types configuration from its canonical JSON form.</summary>
        /// <param name="json">The canonical JSON produced by <see cref="Serialize(BuiltInTableTypesConfiguration)" />.</param>
        /// <returns>The deserialized configuration.</returns>
        public static BuiltInTableTypesConfiguration DeserializeBuiltIn(string json)
        {
            return JsonSerializer.Deserialize(json, TableTypeJsonContext.Default.BuiltInTableTypesConfiguration)
                ?? throw new InvalidOperationException($"Cannot deserialize a built-in table-types configuration from '{json}'.");
        }

        /// <summary>Serializes a standalone table-type configuration to its canonical JSON form.</summary>
        /// <param name="configuration">The configuration to serialize.</param>
        /// <returns>The canonical JSON string.</returns>
        public static string Serialize(StandaloneTableTypeConfiguration configuration)
        {
            return JsonSerializer.Serialize(configuration, TableTypeJsonContext.Default.StandaloneTableTypeConfiguration);
        }

        /// <summary>Deserializes a standalone table-type configuration from its canonical JSON form.</summary>
        /// <param name="json">The canonical JSON produced by <see cref="Serialize(StandaloneTableTypeConfiguration)" />.</param>
        /// <returns>The deserialized configuration.</returns>
        public static StandaloneTableTypeConfiguration DeserializeStandalone(string json)
        {
            return JsonSerializer.Deserialize(json, TableTypeJsonContext.Default.StandaloneTableTypeConfiguration)
                ?? throw new InvalidOperationException($"Cannot deserialize a standalone table-type configuration from '{json}'.");
        }

        /// <summary>Serializes a list of grant principals to its canonical JSON form.</summary>
        /// <param name="principals">The database principals.</param>
        /// <returns>The canonical JSON string.</returns>
        public static string SerializeGrants(IReadOnlyList<string> principals)
        {
            return JsonSerializer.Serialize(principals, TableTypeJsonContext.Default.IReadOnlyListString);
        }

        /// <summary>Deserializes a list of grant principals from its canonical JSON form.</summary>
        /// <param name="json">The canonical JSON produced by <see cref="SerializeGrants" />.</param>
        /// <returns>The deserialized principals.</returns>
        public static IReadOnlyList<string> DeserializeGrants(string json)
        {
            return JsonSerializer.Deserialize(json, TableTypeJsonContext.Default.IReadOnlyListString)
                ?? throw new InvalidOperationException($"Cannot deserialize grant principals from '{json}'.");
        }
    }

    /// <summary>
    ///     Source-generated <see cref="JsonSerializerContext" /> for the canonical table-type JSON.
    ///     Property order follows record declaration order; changing either the records or these
    ///     options changes the canonical form and therefore diffs every existing definition —
    ///     treat both as a breaking change to the snapshot contract.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(TableTypeDefinition))]
    [JsonSerializable(typeof(BuiltInTableTypesConfiguration))]
    [JsonSerializable(typeof(StandaloneTableTypeConfiguration))]
    [JsonSerializable(typeof(IReadOnlyList<string>))]
    internal sealed partial class TableTypeJsonContext : JsonSerializerContext
    {
    }
}
