// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Creates the writer of an <see cref="EntitiesResult{TEntity}" />, which writes the ids in the
    ///     entity's key type: a JSON number for an <see cref="int" /> key, a string for a
    ///     <see cref="long" /> key. Write-only: a result is never read from JSON.
    /// </summary>
    internal sealed class EntitiesResultJsonConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            ArgumentNullException.ThrowIfNull(typeToConvert);
            return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(EntitiesResult<>);
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">The result's entity type is not an entity, so it has no key type.</exception>
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(typeToConvert);
            Type entity = typeToConvert.GetGenericArguments()[0];
            Type key = KeyTypeOf(entity) ?? throw new NotSupportedException(
                $"An entities result is written in its entity's key type, and {entity.Name} is not an entity.");

            return (JsonConverter)Activator.CreateInstance(typeof(EntitiesResultJsonConverter<>).MakeGenericType(entity), args: [key == typeof(long)])!;
        }

        /// <summary>The key type of an entity class: the argument of the keyed base it derives from.</summary>
        /// <param name="type">The class.</param>
        /// <returns>The key type, or null when the class is not an entity.</returns>
        private static Type? KeyTypeOf(Type type)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
                {
                    return current.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }

    /// <summary>
    ///     Writes one entity's <see cref="EntitiesResult{TEntity}" />:
    ///     <c>{ "ids": [ … ], "entities": [ … ], "related": { … }, "extras": { … } }</c>, <c>related</c>
    ///     and <c>extras</c> absent when null.
    /// </summary>
    /// <remarks>
    ///     Each entity is written by its runtime type, so a leaf's own members travel even when the
    ///     result is typed over a class it derives from. The extras are written as they are: each was
    ///     serialized under the same rules, so a <c>permissions</c> extra already carries its ids in the
    ///     key type.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="longKeys">Whether the entity's key is a <see cref="long" />, written as a string.</param>
    internal sealed class EntitiesResultJsonConverter<TEntity>(bool longKeys) : JsonConverter<EntitiesResult<TEntity>>
    {
        /// <inheritdoc />
        public override EntitiesResult<TEntity> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("An entities result is written to JSON only; it is never read from it.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, EntitiesResult<TEntity> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(options);

            writer.WriteStartObject();

            // --- The ids, in the key type ---
            writer.WriteStartArray("ids");
            if (longKeys)
            {
                var text = (JsonConverter<long>)options.GetConverter(typeof(long));
                foreach (long id in value.Ids)
                {
                    text.Write(writer, id, options);
                }
            }
            else
            {
                foreach (long id in value.Ids)
                {
                    writer.WriteNumberValue(id);
                }
            }

            writer.WriteEndArray();

            // --- The entities, each by its runtime type: the declared contract unless a row is of a derived leaf ---
            writer.WriteStartArray("entities");
            JsonTypeInfo declared = options.GetTypeInfo(typeof(TEntity));
            foreach (TEntity entity in value.Entities)
            {
                if (entity is null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                Type runtime = entity.GetType();
                JsonSerializer.Serialize(writer, entity, runtime == declared.Type ? declared : options.GetTypeInfo(runtime));
            }

            writer.WriteEndArray();

            // --- The related rows and the extras, when present ---
            if (value.Related is RelatedEntities related)
            {
                writer.WritePropertyName("related");
                JsonSerializer.Serialize(writer, related, options.GetTypeInfo(typeof(RelatedEntities)));
            }

            if (value.Extras is IReadOnlyDictionary<string, JsonElement> extras)
            {
                writer.WriteStartObject("extras");
                foreach ((string name, JsonElement extra) in extras)
                {
                    writer.WritePropertyName(name);
                    extra.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }
}
