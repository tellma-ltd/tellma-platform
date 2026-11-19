// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Client
{
    // Spec 0029 §2.2: an action's arguments travel twice in the client metadata, as a JSON Schema of
    // the arguments record and as one field per top-level member, which the generated dialog renders.
    // Both read the members as the platform's JSON rules serialize them, so a field's name is the
    // wire's and the schema describes what the endpoint reads.
    /// <summary>The arguments of a stack's action as the client metadata describes them.</summary>
    internal static class ActionArguments
    {
        /// <summary>The text of a <see cref="long" />, which the platform's rules write as a string: an optional minus sign and digits.</summary>
        private const string IntegerPattern = "^-?[0-9]+$";

        /// <summary>The text of a <see cref="decimal" />, which the platform's rules write as a string: an integer with optional fraction digits, never an exponent.</summary>
        private const string DecimalPattern = "^-?[0-9]+(\\.[0-9]+)?$";

        /// <summary>The type an action's arguments bind to: an entity action's arguments record, an API action's request body.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The type, or null when the action takes none.</returns>
        public static Type? TypeOf(ActionDescriptor action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return action switch
            {
                EntityActionDescriptor entity => entity.ArgumentsType,
                ApiActionDescriptor api => api.RequestType,
                _ => null,
            };
        }

        /// <summary>
        ///     The top-level members of an arguments type as the platform's JSON rules serialize them,
        ///     in serialization order: each property the wire carries, with its wire name.
        /// </summary>
        /// <param name="type">The arguments type.</param>
        /// <returns>The members; none for a type that is no JSON object.</returns>
        public static IReadOnlyList<Member> Members(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            JsonTypeInfo info = TellmaJson.Options.GetTypeInfo(type);
            return info.Kind != JsonTypeInfoKind.Object
                ? []
                : [.. info.Properties
                    .Where(static property => property.AttributeProvider is PropertyInfo)
                    .Select(static property => new Member(property, (PropertyInfo)property.AttributeProvider!))];
        }

        /// <summary>Whether a member's value may be empty: a nullable value type, or a reference type its annotations say may be null.</summary>
        /// <param name="property">The member's property.</param>
        /// <returns>True when it may be empty; an unannotated reference type may not.</returns>
        public static bool IsNullable(PropertyInfo property)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (property.PropertyType.IsValueType)
            {
                return Nullable.GetUnderlyingType(property.PropertyType) is not null;
            }

            // The context caches per instance and is not thread safe, so each call makes its own.
            return new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
        }

        /// <summary>
        ///     Whether a member must be given: the platform's JSON rules refuse a body without it (a C#
        ///     <see langword="required" /> member, or one marked <see cref="JsonRequiredAttribute" />),
        ///     its value may not be empty, or it is marked <see cref="RequiredAttribute" />.
        /// </summary>
        /// <param name="member">The member.</param>
        /// <returns>True when it must be given.</returns>
        public static bool IsRequired(Member member)
        {
            ArgumentNullException.ThrowIfNull(member);
            return member.Json.IsRequired
                || !IsNullable(member.Property)
                || member.Property.GetCustomAttribute<RequiredAttribute>(inherit: true) is not null;
        }

        /// <summary>
        ///     A member's type facts: its data type, the named enum it takes its values from, the entity
        ///     whose id it holds, and whether it is a list of either. A list of anything else, a record
        ///     and any type the client has no scalar for are JSON.
        /// </summary>
        /// <param name="member">The member.</param>
        /// <param name="enums">The composition's C# enums, which name an enum member's values.</param>
        /// <returns>The type facts.</returns>
        public static ClientValueDescriptor Value(Member member, NamedEnums enums)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(enums);
            PropertyInfo property = member.Property;
            bool nullable = IsNullable(property);

            // --- The scalar a list holds, or the member's own type ---
            Type? element = ClientDataTypes.ElementOf(property.PropertyType);
            Type scalar = element ?? property.PropertyType;
            string? target = property.GetCustomAttribute<EntityRefAttribute>(inherit: true)?.Resource;
            string? valueSet = property.GetCustomAttribute<ValueSetAttribute>(inherit: true)?.Name;
            string? enumName = valueSet ?? (NamedEnums.Unwrap(scalar) is Type enumType ? enums.Find(enumType)?.Name : null);
            ClientDataType? type = valueSet is null ? ClientDataTypes.Of(scalar) : ClientDataType.Enum;

            // --- A list is a value only of enum values or of ids; anything else is JSON ---
            bool array = element is not null && type is not null && (enumName is not null || target is not null);
            if (type is not ClientDataType known || (element is not null && !array))
            {
                return new ClientValueDescriptor(ClientDataType.Json, nullable, Enum: null, MaxLength: null, Scale: null, Long: false, Target: null, Array: false);
            }

            int? maxLength = known == ClientDataType.String ? MaxLength(property) : null;
            return new ClientValueDescriptor(known, nullable, enumName, maxLength, Scale: null, ClientDataTypes.IsLong(scalar), target, array);
        }

        /// <summary>
        ///     The JSON Schema (2020-12) of an arguments type as the platform's JSON rules read it: a
        ///     <see cref="long" /> or <see cref="decimal" /> as its decimal text, an enum as its wire
        ///     names, a date-time as text, and each object's required members listed.
        /// </summary>
        /// <param name="type">The arguments type.</param>
        /// <returns>The schema.</returns>
        public static JsonElement Schema(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            JsonSchemaExporterOptions options = new()
            {
                TreatNullObliviousAsNonNullable = true,
                TransformSchemaNode = Transform,
            };

            JsonNode node = TellmaJson.Options.GetJsonSchemaAsNode(type, options);
            using var document = JsonDocument.Parse(node.ToJsonString());
            return document.RootElement.Clone();
        }

        /// <summary>
        ///     Rewrites the exporter's node for the types the platform's converters write, which the
        ///     exporter cannot see into, and states each object's required members and each text
        ///     member's length.
        /// </summary>
        /// <param name="context">Where in the schema the node is.</param>
        /// <param name="node">The exporter's node.</param>
        /// <returns>The node to keep.</returns>
        private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
        {
            // --- The platform's converters: a type's own schema, null admitted for a nullable value type ---
            Type type = context.TypeInfo.Type;
            Type? underlying = Nullable.GetUnderlyingType(type);
            Type scalar = underlying ?? type;
            bool nullable = underlying is not null;
            if (Converted(scalar, nullable) is JsonObject converted)
            {
                node = converted;
            }

            if (node is not JsonObject schema)
            {
                return node;
            }

            // --- An object: every member that must be given is required, and nothing else ---
            // The list replaces the exporter's own, so the schema and the fields agree on every member.
            if (context.TypeInfo.Kind == JsonTypeInfoKind.Object && schema["properties"] is JsonObject)
            {
                string[] required = [.. Members(type)
                    .Where(IsRequired)
                    .Select(static member => member.Json.Name)];
                if (required.Length > 0)
                {
                    schema["required"] = new JsonArray([.. required.Select(static name => (JsonNode)JsonValue.Create(name))]);
                }
                else
                {
                    _ = schema.Remove("required");
                }
            }

            // --- A text member: its declared length ---
            if (context.PropertyInfo?.AttributeProvider is PropertyInfo property
                && scalar == typeof(string)
                && MaxLength(property) is int maxLength)
            {
                schema["maxLength"] = maxLength;
            }

            return schema;
        }

        /// <summary>The schema of a type the platform's converters write, or null for any other type.</summary>
        /// <param name="scalar">The type, a nullable's underlying type for a nullable.</param>
        /// <param name="nullable">Whether the value may be null.</param>
        /// <returns>The schema, or null.</returns>
        private static JsonObject? Converted(Type scalar, bool nullable)
        {
            JsonNode Text()
            {
                return nullable ? new JsonArray("string", "null") : JsonValue.Create("string");
            }

            if (scalar.IsEnum)
            {
                JsonArray values = [.. Enum.GetNames(scalar).Select(name => (JsonNode?)JsonValue.Create(NamedEnums.WireName(scalar, name)))];
                if (nullable)
                {
                    values.Add(null);
                }

                return new JsonObject { ["type"] = Text(), ["enum"] = values };
            }

            return scalar switch
            {
                _ when scalar == typeof(long) => new JsonObject { ["type"] = Text(), ["pattern"] = IntegerPattern },
                _ when scalar == typeof(decimal) => new JsonObject { ["type"] = Text(), ["pattern"] = DecimalPattern },
                _ when scalar == typeof(DateTimeOffset) => new JsonObject { ["type"] = Text(), ["format"] = "date-time" },
                _ when scalar == typeof(DateTime) || scalar == typeof(TimeOnly) => new JsonObject { ["type"] = Text() },
                _ => null,
            };
        }

        /// <summary>A text member's declared length: its <see cref="MaxLengthAttribute" />, else its <see cref="StringLengthAttribute" />.</summary>
        /// <param name="property">The member's property.</param>
        /// <returns>The length, or null when none is declared.</returns>
        private static int? MaxLength(PropertyInfo property)
        {
            // A bare MaxLength declares the largest length the store allows, -1, which is no length.
            return property.GetCustomAttribute<MaxLengthAttribute>(inherit: true) is { Length: > 0 } maxLength
                ? maxLength.Length
                : property.GetCustomAttribute<StringLengthAttribute>(inherit: true)?.MaximumLength;
        }

        /// <summary>One top-level member of an arguments type.</summary>
        /// <param name="Json">The member as the platform's JSON rules serialize it.</param>
        /// <param name="Property">The member's property.</param>
        public sealed record Member(JsonPropertyInfo Json, PropertyInfo Property);
    }
}
