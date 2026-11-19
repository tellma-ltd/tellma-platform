// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Settings
{
    // The related reads are spec 0013 §6.2's: for every key with References, the rows whose ids its
    // value holds, one entity query per referenced type, restricted to those ids and projected to
    // the target's related select. Spec 0012 offers no non-generic entity query, so the generic is
    // closed over each type once and cached. No row-level filter applies, as to the related entities
    // of a details read; an id whose row is gone is absent, and the editor shows it bare.
    /// <summary>Composes the related reads of the settings editor's reference keys and fills the related entities from them.</summary>
    /// <param name="schemas">The query schemas, whose shape decides which multilingual twins a projection holds.</param>
    /// <param name="stacks">The stack registry, whose stacks state each target's related select.</param>
    /// <param name="metadata">The entity metadata, which resolves a referenced type to its leaf.</param>
    internal sealed class SettingsRelatedReads(IQueryexSchemaProvider schemas, IStackRegistry stacks, IEntityMetadataProvider metadata)
    {
        /// <summary>The generic composer, closed over each target leaf and key type on first use.</summary>
        private static readonly MethodInfo ComposeOfMethod = typeof(SettingsRelatedReads).GetMethod(nameof(ComposeOf), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The settings service's generic related read is missing.");

        /// <summary>The closed composers, by target leaf and key type.</summary>
        private static readonly ConcurrentDictionary<(Type Entity, Type Key), Composer> Composers = new();

        /// <summary>One target's composer: binds the ids, appends the query, and returns what fills the related entities.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="entity">The target leaf.</param>
        /// <param name="projection">The projected columns, key first.</param>
        /// <param name="ids">The ids, as read from the values.</param>
        /// <returns>What fills the related entities after execution.</returns>
        private delegate Action<RelatedEntities> Composer(IDataBatch batch, EntityMetadata entity, IReadOnlySet<string> projection, IReadOnlyCollection<long> ids);

        /// <summary>
        ///     Appends one read per referenced type of the ids the given values hold. A value holds an
        ///     id as a number or as an array of numbers; anything else holds none.
        /// </summary>
        /// <param name="batch">The batch.</param>
        /// <param name="shape">The multilingual shape the batch's schema follows.</param>
        /// <param name="values">The reference keys' values.</param>
        /// <returns>What fills the related entities once the batch executed.</returns>
        /// <exception cref="InvalidOperationException">The query schema declares no entity of a referenced leaf.</exception>
        public Action<RelatedEntities> Compose(IDataBatch batch, MultilingualShape shape, IEnumerable<(SettingKey Key, JsonElement Value)> values)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(values);

            // --- The ids by target leaf, in the order the keys name the leaves ---
            Dictionary<EntityMetadata, HashSet<long>> byTarget = [];
            List<EntityMetadata> order = [];
            foreach ((SettingKey key, JsonElement value) in values)
            {
                if (key.References is not Type references)
                {
                    continue;
                }

                EntityMetadata target = metadata.Get(references);
                if (!byTarget.TryGetValue(target, out HashSet<long>? ids))
                {
                    ids = [];
                    byTarget.Add(target, ids);
                    order.Add(target);
                }

                Collect(value, ids);
            }

            // --- One query per target that holds an id, of an int or long key ---
            QueryexSchema? schema = null;
            List<Action<RelatedEntities>> readers = [];
            foreach (EntityMetadata target in order)
            {
                HashSet<long> ids = byTarget[target];
                Type keyType = target.Key.ClrType;
                if (ids.Count == 0 || (keyType != typeof(int) && keyType != typeof(long)))
                {
                    continue;
                }

                schema ??= schemas.GetSchema(shape);
                IReadOnlySet<string> projection = Projection(target, schema);
                Composer composer = Composers.GetOrAdd(
                    (target.ClrType, keyType),
                    static pair => ComposeOfMethod.MakeGenericMethod(pair.Entity, pair.Key).CreateDelegate<Composer>());
                readers.Add(composer(batch, target, projection, ids));
            }

            return related =>
            {
                foreach (Action<RelatedEntities> read in readers)
                {
                    read(related);
                }
            };
        }

        /// <summary>The ids a value holds: a whole number, or the whole numbers of an array.</summary>
        /// <param name="value">The value.</param>
        /// <param name="ids">Receives the ids.</param>
        private static void Collect(JsonElement value, HashSet<long> ids)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetInt64(out long id):
                    _ = ids.Add(id);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out long element))
                        {
                            _ = ids.Add(element);
                        }
                    }

                    break;
                case JsonValueKind.Undefined:
                case JsonValueKind.Object:
                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                default:
                    // A fractional or oversized number, or any other kind, holds no id.
                    break;
            }
        }

        /// <summary>One target's read: its ids bound, its rows queried with no filter, then added to the related entities.</summary>
        /// <typeparam name="TEntity">The target leaf.</typeparam>
        /// <typeparam name="TKey">Its key type.</typeparam>
        /// <param name="batch">The batch.</param>
        /// <param name="entity">The target leaf's metadata.</param>
        /// <param name="projection">The projected columns, key first.</param>
        /// <param name="ids">The ids.</param>
        /// <returns>What fills the related entities after execution.</returns>
        private static Action<RelatedEntities> ComposeOf<TEntity, TKey>(IDataBatch batch, EntityMetadata entity, IReadOnlySet<string> projection, IReadOnlyCollection<long> ids)
            where TEntity : class
            where TKey : struct, IBinaryInteger<TKey>
        {
            // An id outside the key's range names no row.
            TKey[] keys = [.. ids.Where(static id => long.CreateSaturating(TKey.CreateSaturating(id)) == id).Select(static id => TKey.CreateChecked(id))];
            if (keys.Length == 0)
            {
                return static _ => { };
            }

            SqlIdentifier listed = KeyTables.Bind(batch, keys);
            BatchResult<EntityQueryResult<TEntity>> rows = batch.Query(new EntityQuery<TEntity>
            {
                Select = string.Join(", ", projection),
                Restrictions = [new KeySetRestriction(entity.Key.Name, listed.Name)],
                OrderBy = entity.Key.Name,
            });

            return related =>
            {
                IReadOnlyList<TEntity> found = rows.Value.Entities;
                if (found.Count > 0)
                {
                    related.Add(entity.Name, found, projection);
                }
            };
        }

        /// <summary>The columns of a target's related select its schema entity declares, its key first.</summary>
        /// <param name="entity">The target leaf.</param>
        /// <param name="schema">The query schema.</param>
        /// <returns>The columns.</returns>
        /// <exception cref="InvalidOperationException">The schema declares no entity of the leaf.</exception>
        private OrderedNameSet Projection(EntityMetadata entity, QueryexSchema schema)
        {
            EntityDescriptor descriptor = schema.FindEntity(entity.Name)
                ?? throw new InvalidOperationException($"The query schema declares no entity {entity.Name}, which a setting key references, so its related rows cannot be read.");
            IReadOnlySet<string> related = stacks.Find(entity.Name)?.RelatedSelect
                ?? stacks.FindChildQuery(entity.Name)?.RelatedSelect
                ?? StackDescriptorBuilder.RelatedSelect(entity);

            // A multilingual twin the tenant's languages leave out is not in the schema.
            return new OrderedNameSet([entity.Key.Name, .. related.Where(column => SchemaLookup.Property(descriptor, column) is not null)]);
        }
    }
}
