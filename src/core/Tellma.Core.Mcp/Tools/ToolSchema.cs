// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>A tool's input schema and the argument names it declares.</summary>
    /// <param name="Schema">The JSON Schema 2020-12 document: a closed object.</param>
    /// <param name="Names">The argument names, which a call may not exceed.</param>
    internal sealed record ToolInput(JsonElement Schema, IReadOnlyList<string> Names);

    /// <summary>
    ///     Builds the tools' JSON schemas: JSON Schema 2020-12, every object closed with
    ///     <c>additionalProperties: false</c>, so a model is told exactly the arguments a tool takes.
    /// </summary>
    internal static class ToolSchema
    {
        /// <summary>The dialect every schema declares.</summary>
        public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>A tool's input: a closed object of the arguments given, in order.</summary>
        /// <param name="properties">The arguments and their schemas.</param>
        /// <param name="required">The arguments a call must give.</param>
        /// <returns>The input.</returns>
        public static ToolInput Input(IReadOnlyList<(string Name, JsonObject Schema)> properties, IReadOnlyList<string> required)
        {
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentNullException.ThrowIfNull(required);
            JsonObject schema = Closed(properties, required);
            schema.Insert(0, "$schema", Dialect);
            return new ToolInput(JsonSerializer.SerializeToElement(schema), [.. properties.Select(static property => property.Name)]);
        }

        /// <summary>A closed object schema.</summary>
        /// <param name="properties">The members and their schemas.</param>
        /// <param name="required">The members an instance must carry.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Closed(IReadOnlyList<(string Name, JsonObject Schema)> properties, IReadOnlyList<string> required)
        {
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentNullException.ThrowIfNull(required);
            JsonObject members = [];
            foreach ((string name, JsonObject schema) in properties)
            {
                members[name] = schema;
            }

            JsonObject result = new()
            {
                ["type"] = "object",
                ["properties"] = members,
            };
            if (required.Count > 0)
            {
                result["required"] = new JsonArray([.. required.Select(static name => (JsonNode)JsonValue.Create(name))]);
            }

            result["additionalProperties"] = false;
            return result;
        }

        /// <summary>A string.</summary>
        /// <param name="description">What it holds.</param>
        /// <returns>The schema.</returns>
        public static JsonObject String(string description)
        {
            return new JsonObject { ["type"] = "string", ["minLength"] = 1, ["description"] = description };
        }

        /// <summary>A string from a closed set.</summary>
        /// <param name="description">What it chooses.</param>
        /// <param name="values">The values, as a call spells them.</param>
        /// <param name="defaultValue">The value an absent argument means.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Enum(string description, IReadOnlyList<string> values, string defaultValue)
        {
            ArgumentNullException.ThrowIfNull(values);
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. values.Select(static value => (JsonNode)JsonValue.Create(value))]),
                ["default"] = defaultValue,
                ["description"] = description,
            };
        }

        /// <summary>An integer within a range.</summary>
        /// <param name="description">What it counts.</param>
        /// <param name="minimum">The smallest value.</param>
        /// <param name="maximum">The largest value; null for none.</param>
        /// <param name="defaultValue">The value an absent argument means; null for none.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Integer(string description, int minimum, int? maximum, int? defaultValue)
        {
            JsonObject schema = new() { ["type"] = "integer", ["minimum"] = minimum };
            if (maximum is int max)
            {
                schema["maximum"] = max;
            }

            if (defaultValue is int value)
            {
                schema["default"] = value;
            }

            schema["description"] = description;
            return schema;
        }

        /// <summary>A boolean, false when absent.</summary>
        /// <param name="description">What it switches.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Boolean(string description)
        {
            return new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = description };
        }

        /// <summary>A list of record ids: positive integers, or their digits as strings.</summary>
        /// <param name="description">Which records they name.</param>
        /// <param name="minimum">The fewest ids.</param>
        /// <param name="maximum">The most ids.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Ids(string description, int minimum, int maximum)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = new JsonArray("integer", "string"),
                    ["minimum"] = 1,
                    ["pattern"] = "^[1-9][0-9]*$",
                },
                ["minItems"] = minimum,
                ["maxItems"] = maximum,
                ["description"] = description,
            };
        }

        /// <summary>A list of non-empty strings.</summary>
        /// <param name="description">What they name.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Strings(string description)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["description"] = description,
            };
        }

        /// <summary>An object of JSON scalars keyed by name.</summary>
        /// <param name="description">What the values are for.</param>
        /// <returns>The schema.</returns>
        public static JsonObject Scalars(string description)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JsonObject { ["type"] = new JsonArray("string", "number", "boolean", "null") },
                ["description"] = description,
            };
        }
    }
}
