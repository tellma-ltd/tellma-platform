// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes <see cref="RelatedEntities" /> as entity name to array:
    ///     <c>{ "core.User": [ { "id": 1, "name": "Administrator", "imageId": null } ] }</c>, the sets in
    ///     ordinal order of their names. Write-only: related entities are never read from JSON.
    /// </summary>
    /// <remarks>
    ///     Each set is written through one contract per entity type and projection: the type's own
    ///     members restricted to the set's projection, with their JSON names, converters and number
    ///     handling, and with a null member written as <c>null</c>, so every instance of a set has the
    ///     same members and a client tells a null value from a column the projection leaves out.
    /// </remarks>
    internal sealed class RelatedEntitiesJsonConverter : JsonConverter<RelatedEntities>
    {
        /// <summary>The most projected contracts kept per options instance; past it a contract is built per use.</summary>
        private const int MaxCachedContracts = 1024;

        /// <summary>The projected contracts, per options instance, by entity type and projection.</summary>
        private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<(Type Type, string Projection), JsonTypeInfo>> Contracts = [];

        /// <inheritdoc />
        public override RelatedEntities Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Related entities are written to JSON only; they are never read from it.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, RelatedEntities value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(options);

            writer.WriteStartObject();
            foreach ((string name, RelatedEntitySet set) in value.Sets.OrderBy(static set => set.Key, StringComparer.Ordinal))
            {
                // One contract lookup per set: every instance of a set is of its one type.
                JsonTypeInfo contract = ContractOf(set.EntityType, set.Projection, options);
                writer.WriteStartArray(name);
                foreach (object entity in set.Entities)
                {
                    JsonSerializer.Serialize(writer, entity, contract);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        /// <summary>
        ///     The members of an entity's contract a projection keeps, in the contract's order: each
        ///     member whose CLR property the projection names. A projected column the contract does
        ///     not serialize (a column marked never to travel) is left out.
        /// </summary>
        /// <param name="contract">The entity type's contract.</param>
        /// <param name="projection">The CLR property names the projection keeps.</param>
        /// <returns>The kept members.</returns>
        internal static IEnumerable<JsonPropertyInfo> ProjectedMembers(JsonTypeInfo contract, IReadOnlySet<string> projection)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(projection);

            // A member's CLR name comes from the property behind it; a member with none (a contract a
            // custom resolver built) is matched by its JSON name under the naming policy instead. A
            // member with no getter is never written (an ignored one kept for a constructor parameter).
            JsonNamingPolicy? policy = contract.Options.PropertyNamingPolicy;
            return contract.Properties.Where(member => member.Get is not null && (member.AttributeProvider is MemberInfo clr
                ? projection.Contains(clr.Name)
                : projection.Any(name => string.Equals(policy?.ConvertName(name) ?? name, member.Name, StringComparison.Ordinal))));
        }

        /// <summary>The projected contract of an entity type, cached per options instance.</summary>
        /// <param name="type">The entity type.</param>
        /// <param name="projection">The CLR property names the projection keeps.</param>
        /// <param name="options">The options.</param>
        /// <returns>The contract.</returns>
        private static JsonTypeInfo ContractOf(Type type, IReadOnlySet<string> projection, JsonSerializerOptions options)
        {
            ConcurrentDictionary<(Type, string), JsonTypeInfo> cache = Contracts.GetValue(options, static _ => new());
            (Type, string) key = (type, string.Join(",", projection.Order(StringComparer.Ordinal)));
            if (cache.TryGetValue(key, out JsonTypeInfo? cached))
            {
                return cached;
            }

            JsonTypeInfo contract = Build(type, projection, options);
            return cache.Count < MaxCachedContracts ? cache.GetOrAdd(key, contract) : contract;
        }

        /// <summary>
        ///     Builds the projected contract: a fresh object contract over the type holding a copy of
        ///     each kept member, written even when null.
        /// </summary>
        /// <param name="type">The entity type.</param>
        /// <param name="projection">The CLR property names the projection keeps.</param>
        /// <param name="options">The options.</param>
        /// <returns>The contract.</returns>
        private static JsonTypeInfo Build(Type type, IReadOnlySet<string> projection, JsonSerializerOptions options)
        {
            JsonTypeInfo full = options.GetTypeInfo(type);
            if (full.Kind != JsonTypeInfoKind.Object)
            {
                throw new JsonException($"The related set of {type.Name} cannot be written: the type does not serialize as an object.");
            }

            // The type's own number handling is the default of every member that sets none.
            var projected = JsonTypeInfo.CreateJsonTypeInfo(type, options);
            projected.NumberHandling = full.NumberHandling;
            foreach (JsonPropertyInfo member in ProjectedMembers(full, projection))
            {
                JsonPropertyInfo copy = projected.CreateJsonPropertyInfo(member.PropertyType, member.Name);
                copy.Get = member.Get;
                copy.CustomConverter = member.CustomConverter;
                copy.NumberHandling = member.NumberHandling;
                copy.AttributeProvider = member.AttributeProvider;

                // A projected member is always present: null is a value, absence a column left out.
                copy.ShouldSerialize = static (_, _) => true;
                projected.Properties.Add(copy);
            }

            return projected;
        }
    }
}
