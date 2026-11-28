// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Buffers.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Api
{
    // The description and the fingerprint follow spec 0016 §3.10.

    /// <summary>
    ///     Describes the JSON contracts of an endpoint canonically, as the platform's JSON rules
    ///     write them, and fingerprints the description with the endpoint's contract revisions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A description is compact JSON with no insignificant whitespace and members in ordinal
    ///         order, so the same contracts give the same bytes in every process and on every
    ///         platform. It holds no assembly versions and no CLR names but the stable names of a
    ///         type or converter that writes itself, which the description cannot see into.
    ///     </para>
    ///     <list type="bullet">
    ///         <item>A scalar is its JSON kind and format: <c>"integer:int32"</c>, <c>"string:int64"</c>, <c>"string:decimal"</c>, <c>"string:date-time"</c>.</item>
    ///         <item>
    ///             A number written under a member's or a type's number handling says so:
    ///             <c>"integer:int32|string"</c> when it is written as a string, <c>"number:double|named"</c>
    ///             when named floating-point literals are allowed. Handling that only loosens reading
    ///             leaves the description as it is.
    ///         </item>
    ///         <item>An enum is <c>{ "enum": [ names ] }</c>, its wire names sorted, with <c>"flags": true</c> for a flags enum.</item>
    ///         <item>An object is <c>{ "object": { member: type } }</c>; a member that may be null or absent is <c>{ "nullable": type }</c>.</item>
    ///         <item>An array is <c>{ "array": type }</c>; a dictionary <c>{ "map": { "key": type, "value": type } }</c>.</item>
    ///         <item>A polymorphic type adds <c>"polymorphic": { "discriminator": name, "types": { value: type } }</c>.</item>
    ///         <item>A type met again inside itself is <c>{ "ref": n }</c>, <c>n</c> levels up.</item>
    ///         <item>A pre-serialized value (<see cref="PreSerializedJson{T}" />) is described as the type its bytes were serialized from.</item>
    ///         <item>
    ///             A row set is <c>"row-set"</c> (its columns describe themselves); an entities result
    ///             spells out its ids in the key type and its related projections, one per entity its
    ///             stack's details expansion reaches.
    ///         </item>
    ///     </list>
    /// </remarks>
    /// <param name="registry">The stacks, whose details expansions say which related projections an entities result carries.</param>
    /// <param name="metadata">The entity metadata the expansions walk.</param>
    /// <param name="options">The JSON rules the contracts are described under.</param>
    internal sealed class ContractDescriber(IStackRegistry registry, IEntityMetadataProvider metadata, JsonSerializerOptions options)
    {
        /// <summary>The fingerprint's length in base64url characters.</summary>
        public const int FingerprintLength = 12;

        /// <summary>The description of no body.</summary>
        private const string None = "none";

        /// <summary>The description of any JSON value.</summary>
        private const string Any = "any";

        /// <summary>The description of a row set, whose columns describe themselves.</summary>
        private const string RowSet = "row-set";

        /// <summary>The scalar types and their JSON kinds and formats under the platform's rules.</summary>
        private static readonly Dictionary<Type, string> Scalars = new()
        {
            [typeof(bool)] = "boolean",
            [typeof(byte)] = "integer:uint8",
            [typeof(short)] = "integer:int16",
            [typeof(int)] = "integer:int32",
            [typeof(long)] = "string:int64",
            [typeof(decimal)] = "string:decimal",
            [typeof(double)] = "number:double",
            [typeof(float)] = "number:single",
            [typeof(string)] = "string",
            [typeof(DateOnly)] = "string:date",
            [typeof(DateTime)] = "string:date-time",
            [typeof(DateTimeOffset)] = "string:date-time-offset",
            [typeof(TimeOnly)] = "string:time",
            [typeof(Guid)] = "string:uuid",
            [typeof(byte[])] = "string:base64",
            [typeof(object)] = Any,
            [typeof(JsonElement)] = Any,
            [typeof(JsonDocument)] = Any,
            [typeof(JsonNode)] = Any,
        };

        /// <summary>
        ///     The canonical description of an endpoint: its request and response contracts and its
        ///     contract revisions, as one compact JSON object.
        /// </summary>
        /// <param name="request">The body's type; null when the endpoint takes none.</param>
        /// <param name="response">The result's type; <c>typeof(void)</c> when it answers no body.</param>
        /// <param name="stackRevision">The contract revision of the stack the endpoint belongs to; 0 on a routed service.</param>
        /// <param name="surfaceRevision">The contract revision of the child query surface the endpoint belongs to; 0 elsewhere.</param>
        /// <param name="actionRevision">The contract revision of the action serving the endpoint; 0 for a standard operation.</param>
        /// <returns>The description.</returns>
        public string Describe(Type? request, Type response, int stackRevision, int surfaceRevision, int actionRevision)
        {
            ArgumentNullException.ThrowIfNull(response);

            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer))
            {
                // --- Members in ordinal order: request, response, revisions ---
                writer.WriteStartObject();
                writer.WritePropertyName("request");
                if (request is null)
                {
                    writer.WriteStringValue(None);
                }
                else
                {
                    Write(writer, request, [], options.NumberHandling);
                }

                writer.WritePropertyName("response");
                Write(writer, response, [], options.NumberHandling);

                writer.WriteStartObject("revisions");
                writer.WriteNumber("action", actionRevision);
                writer.WriteNumber("stack", stackRevision);
                writer.WriteNumber("surface", surfaceRevision);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>The fingerprint of a description: the first 12 characters of the base64url SHA-256 of its UTF-8 bytes.</summary>
        /// <param name="description">The description.</param>
        /// <returns>The fingerprint.</returns>
        public static string Fingerprint(string description)
        {
            ArgumentNullException.ThrowIfNull(description);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(description));
            return Base64Url.EncodeToString(hash)[..FingerprintLength];
        }

        /// <summary>Writes the description of one type.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="type">The type.</param>
        /// <param name="path">The object, array and map types being described around this one, outermost first.</param>
        /// <param name="handling">
        ///     The number handling the type is written under: the member's, else its declaring type's,
        ///     else the options'. A collection passes it on to its elements, as the serializer does.
        /// </param>
        private void Write(Utf8JsonWriter writer, Type type, List<Type> path, JsonNumberHandling handling)
        {
            // --- No body, a nullable value, a scalar, an enum ---
            if (type == typeof(void))
            {
                writer.WriteStringValue(None);
                return;
            }

            if (Nullable.GetUnderlyingType(type) is Type underlying)
            {
                WriteNullable(writer, underlying, path, handling);
                return;
            }

            if (Scalars.TryGetValue(type, out string? scalar))
            {
                writer.WriteStringValue(WithHandling(scalar, handling));
                return;
            }

            if (type.IsEnum)
            {
                WriteEnum(writer, type);
                return;
            }

            // --- The envelopes the platform's converters write ---
            if (type == typeof(QueryRowSet))
            {
                writer.WriteStringValue(RowSet);
                return;
            }

            if (type == typeof(RowPage))
            {
                WriteRowPage(writer);
                return;
            }

            if (type == typeof(RelatedEntities))
            {
                // Outside an entities result the bag's entities are unknown; inside one they are spelled out.
                writer.WriteStringValue("related");
                return;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EntitiesResult<>))
            {
                WriteEntitiesResult(writer, type.GetGenericArguments()[0], path);
                return;
            }

            // Bytes the platform serialized once have the shape of the type they were serialized from.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PreSerializedJson<>))
            {
                Write(writer, type.GetGenericArguments()[0], path, handling);
                return;
            }

            // --- A type met again inside itself: a reference by distance ---
            int index = path.LastIndexOf(type);
            if (index >= 0)
            {
                writer.WriteStartObject();
                writer.WriteNumber("ref", path.Count - index);
                writer.WriteEndObject();
                return;
            }

            // --- Everything else by its contract ---
            JsonTypeInfo contract = options.GetTypeInfo(type);
            path.Add(type);
            switch (contract.Kind)
            {
                case JsonTypeInfoKind.Object:
                    WriteObject(writer, contract, path);
                    break;
                case JsonTypeInfoKind.Enumerable:
                    writer.WriteStartObject();
                    writer.WritePropertyName("array");
                    Write(writer, contract.ElementType!, path, handling);
                    writer.WriteEndObject();
                    break;
                case JsonTypeInfoKind.Dictionary:
                    writer.WriteStartObject();
                    writer.WriteStartObject("map");
                    writer.WritePropertyName("key");
                    // A key is a property name, which number handling never touches.
                    Write(writer, contract.KeyType!, path, JsonNumberHandling.Strict);
                    writer.WritePropertyName("value");
                    Write(writer, contract.ElementType!, path, handling);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    break;
                case JsonTypeInfoKind.None:
                default:
                    // A type its own converter writes: named, since its shape cannot be seen.
                    writer.WriteStringValue("custom:" + TypeDisplay.Qualified(type));
                    break;
            }

            path.RemoveAt(path.Count - 1);
        }

        /// <summary>Writes <c>{ "nullable": type }</c>.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="type">The type that may be null.</param>
        /// <param name="path">The types being described around this one.</param>
        /// <param name="handling">The number handling the value is written under.</param>
        private void WriteNullable(Utf8JsonWriter writer, Type type, List<Type> path, JsonNumberHandling handling)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("nullable");
            Write(writer, type, path, handling);
            writer.WriteEndObject();
        }

        /// <summary>
        ///     A number scalar's description under its number handling: <c>|string</c> when it is
        ///     written as a string, <c>|named</c> when a floating-point number may be written as a
        ///     named literal. Every other scalar, and handling that only loosens reading, is unchanged.
        /// </summary>
        /// <param name="scalar">The scalar's description under the strict handling.</param>
        /// <param name="handling">The number handling it is written under.</param>
        /// <returns>The description.</returns>
        private static string WithHandling(string scalar, JsonNumberHandling handling)
        {
            bool integer = scalar.StartsWith("integer:", StringComparison.Ordinal);
            bool floating = scalar.StartsWith("number:", StringComparison.Ordinal);
            if (!integer && !floating)
            {
                // Strings, booleans and the platform's string-written numbers ignore number handling.
                return scalar;
            }

            if (handling.HasFlag(JsonNumberHandling.WriteAsString))
            {
                scalar += "|string";
            }

            if (floating && handling.HasFlag(JsonNumberHandling.AllowNamedFloatingPointLiterals))
            {
                scalar += "|named";
            }

            return scalar;
        }

        /// <summary>Writes an enum's wire names, sorted, and whether it is a set of flags.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="type">The enum.</param>
        private static void WriteEnum(Utf8JsonWriter writer, Type type)
        {
            // A Queryex type travels as the language's name for it, which its own converter writes.
            IEnumerable<string> names = type == typeof(QueryexType)
                ? QueryexTypeJsonConverter.Names
                : type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(static field => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name)
                    .Order(StringComparer.Ordinal);

            writer.WriteStartObject();
            writer.WriteStartArray("enum");
            foreach (string name in names)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            if (type.IsDefined(typeof(FlagsAttribute), inherit: false))
            {
                writer.WriteBoolean("flags", true);
            }

            writer.WriteEndObject();
        }

        /// <summary>Writes an object's members by JSON name, and its derived types when it is polymorphic.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="contract">The object's contract.</param>
        /// <param name="path">The types being described, this one last.</param>
        private void WriteObject(Utf8JsonWriter writer, JsonTypeInfo contract, List<Type> path)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("object");
            WriteMembers(writer, contract.Properties, path, contract.NumberHandling ?? options.NumberHandling);

            // --- A polymorphic base: each derived type under its discriminator ---
            if (contract.PolymorphismOptions is JsonPolymorphismOptions polymorphism)
            {
                writer.WriteStartObject("polymorphic");
                writer.WriteString("discriminator", polymorphism.TypeDiscriminatorPropertyName);
                writer.WriteStartObject("types");
                foreach (JsonDerivedType derived in polymorphism.DerivedTypes.OrderBy(static derived => Convert.ToString(derived.TypeDiscriminator, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal))
                {
                    writer.WritePropertyName(Convert.ToString(derived.TypeDiscriminator, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                    Write(writer, derived.DerivedType, path, options.NumberHandling);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        /// <summary>Writes <c>{ name: type }</c> for each member, in ordinal order of the JSON names.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="members">The members.</param>
        /// <param name="path">The types being described.</param>
        /// <param name="typeHandling">The declaring type's number handling, else the options'; a member's own overrides it.</param>
        private void WriteMembers(Utf8JsonWriter writer, IEnumerable<JsonPropertyInfo> members, List<Type> path, JsonNumberHandling typeHandling)
        {
            writer.WriteStartObject();

            // A member with neither a getter nor a setter is ignored but kept for a constructor
            // parameter of its name; it is never on the wire either way, so it is left out.
            foreach (JsonPropertyInfo member in members.Where(static member => member.Get is not null || member.Set is not null).OrderBy(static member => member.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(member.Name);

                // A member its own converter writes is named by the converter; a reference that may
                // be null, like a nullable value, is wrapped.
                bool nullable = !member.PropertyType.IsValueType && member.IsGetNullable;
                if (nullable)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("nullable");
                }

                if (member.CustomConverter is JsonConverter converter)
                {
                    writer.WriteStringValue("custom:" + TypeDisplay.Qualified(converter.GetType()));
                }
                else
                {
                    Write(writer, member.PropertyType, path, member.NumberHandling ?? typeHandling);
                }

                if (nullable)
                {
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>Writes the rows envelope: the rows, the capped count and its flag, and the ancestors.</summary>
        /// <param name="writer">The writer.</param>
        private static void WriteRowPage(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteStartObject("object");
            WriteNullableScalar(writer, "ancestors", RowSet);
            WriteNullableScalar(writer, "count", Scalars[typeof(int)]);
            WriteNullableScalar(writer, "countCapped", Scalars[typeof(bool)]);
            writer.WriteString("rows", RowSet);
            writer.WriteEndObject();
            writer.WriteEndObject();

            static void WriteNullableScalar(Utf8JsonWriter writer, string name, string scalar)
            {
                writer.WriteStartObject(name);
                writer.WriteString("nullable", scalar);
                writer.WriteEndObject();
            }
        }

        /// <summary>
        ///     Writes an entities result: its ids in the entity's key type, its entities, its extras,
        ///     and the related projection of each entity the entity's stack expands to.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="entity">The result's entity type.</param>
        /// <param name="path">The types being described.</param>
        private void WriteEntitiesResult(Utf8JsonWriter writer, Type entity, List<Type> path)
        {
            Type key = EntityShapes.KeyTypeOf(entity) ?? throw new InvalidOperationException(
                $"An entities result is written in its entity's key type, and {entity.Name} is not an entity.");

            writer.WriteStartObject();
            writer.WriteStartObject("object");

            writer.WritePropertyName("entities");
            writer.WriteStartObject();
            writer.WritePropertyName("array");
            Write(writer, entity, path, options.NumberHandling);
            writer.WriteEndObject();

            writer.WriteStartObject("extras");
            writer.WriteStartObject("nullable");
            writer.WriteStartObject("map");
            writer.WriteString("key", Scalars[typeof(string)]);
            writer.WriteString("value", Any);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartObject("ids");
            writer.WriteString("array", key == typeof(long) ? Scalars[typeof(long)] : Scalars[typeof(int)]);
            writer.WriteEndObject();

            writer.WriteStartObject("related");
            writer.WritePropertyName("nullable");
            WriteRelated(writer, entity, path);
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>
        ///     Writes the related projections an entity's details carry: for each entity its stack's
        ///     details expansion reaches, in ordinal order of the names, the projected members.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="entity">The entity type.</param>
        /// <param name="path">The types being described.</param>
        private void WriteRelated(Utf8JsonWriter writer, Type entity, List<Type> path)
        {
            if (registry.Stacks.FirstOrDefault(stack => stack.EntityType == entity) is not StackDescriptor stack)
            {
                // No stack expands the entity, so the bag's entities are not known.
                writer.WriteStringValue("related");
                return;
            }

            SortedDictionary<string, EntityMetadata> reached = new(StringComparer.Ordinal);
            foreach (string expansion in stack.DetailsExpand)
            {
                Reach(metadata.Get(stack.EntityType), expansion, reached);
            }

            writer.WriteStartObject();
            writer.WriteStartObject("related");
            foreach ((string name, EntityMetadata target) in reached)
            {
                JsonTypeInfo contract = options.GetTypeInfo(target.ClrType);
                writer.WriteStartObject(name);
                writer.WritePropertyName("array");
                writer.WriteStartObject();
                writer.WritePropertyName("object");
                WriteMembers(writer, RelatedEntitiesJsonConverter.ProjectedMembers(contract, StackDescriptorBuilder.RelatedSelect(target)), path, contract.NumberHandling ?? options.NumberHandling);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        /// <summary>Collects the entity of every navigation along one expansion path: <c>Parts.Gizmo</c> walks the collection, then reaches the gizmo.</summary>
        /// <param name="root">The stack's entity.</param>
        /// <param name="expansion">The dotted path of collections and navigations.</param>
        /// <param name="reached">Receives the entities reached, by name.</param>
        private static void Reach(EntityMetadata root, string expansion, SortedDictionary<string, EntityMetadata> reached)
        {
            EntityMetadata current = root;
            foreach (string segment in expansion.Split('.'))
            {
                if (current.Children.FirstOrDefault(child => child.Property == segment) is ChildCollectionMetadata collection)
                {
                    current = collection.Child;
                }
                else if (current.References.FirstOrDefault(reference => reference.Navigation == segment) is ReferenceMetadata reference)
                {
                    reached[reference.Target.Name] = reference.Target;
                    current = reference.Target;
                }
                else
                {
                    // The stack's checks refuse a path that names neither; nothing further is reached.
                    return;
                }
            }
        }
    }
}
