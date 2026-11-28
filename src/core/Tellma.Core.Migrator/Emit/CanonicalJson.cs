// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Migrator.Emit
{
    /// <summary>
    ///     Rewrites JSON in the emit's canonical form: every object's members in ordinal order of their
    ///     names, arrays in their own order, text escaped by the writer's encoder, no whitespace. The
    ///     same value always yields the same bytes, whatever order its writer chose.
    /// </summary>
    internal static class CanonicalJson
    {
        /// <summary>Writes a value in canonical form.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The value.</param>
        public static void Write(Utf8JsonWriter writer, JsonElement value)
        {
            ArgumentNullException.ThrowIfNull(writer);

            switch (value.ValueKind)
            {
                // --- An object: members by name, ordinal; a repeated name keeps its members' order ---
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (JsonProperty member in value.EnumerateObject().OrderBy(static member => member.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(member.Name);
                        Write(writer, member.Value);
                    }

                    writer.WriteEndObject();
                    break;

                // --- An array: its items in order ---
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        Write(writer, item);
                    }

                    writer.WriteEndArray();
                    break;

                // --- Text, re-escaped by the writer, so an escape the source chose never survives ---
                case JsonValueKind.String:
                    writer.WriteStringValue(value.GetString());
                    break;

                // --- Numbers, booleans and null, as written ---
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    value.WriteTo(writer);
                    break;
            }
        }
    }
}
