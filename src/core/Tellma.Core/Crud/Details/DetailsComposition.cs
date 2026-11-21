// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Details
{
    // The plan is spec 0015 §5.3: the statements it appends, the extras' name rules, the record
    // permissions read after the batch, and the result in request order.
    /// <summary>
    ///     One composition of a details plan on one batch: the handles of its statements, the extras
    ///     its producers added, and the reads behind the record permissions. After the batch it
    ///     assembles the result: the visible rows in request order, what they point at, and each
    ///     requested extra serialised.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    internal sealed class DetailsComposition<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The batch the plan composes on.</summary>
        private readonly IDataBatch _batch;

        /// <summary>The service's hooks, which judge each row's abilities.</summary>
        private readonly IEntityBehavior<TEntity, TKey> _behavior;

        /// <summary>The request.</summary>
        private readonly DetailsRequest _request;

        /// <summary>The extras the request includes, by name.</summary>
        private readonly HashSet<string> _requested;

        /// <summary>The ids in request order, distinct; null when every row is read.</summary>
        private readonly IReadOnlyList<TKey>? _ids;

        /// <summary>The handle of the main-ids statement, whose rows are the visible ids; null on a read over given ids.</summary>
        private readonly BatchResult<RowPage>? _main;

        /// <summary>The handle of the entity query.</summary>
        private readonly BatchResult<EntityQueryResult<TEntity>> _entities;

        /// <summary>The grouped reads of what each collection level points at.</summary>
        private readonly List<(DetailsTargets Targets, BatchResult<RowPage> Rows)> _targets = [];

        /// <summary>The extras the producers added, by name, each producing its value after the batch.</summary>
        private readonly Dictionary<string, Func<IReadOnlyList<TEntity>, JsonElement>> _extras = new(StringComparer.Ordinal);

        /// <summary>What turns rows read into related entities.</summary>
        private readonly DetailsMaterials _materials;

        /// <summary>The record permissions' abilities and their reads; null when the extra is not requested.</summary>
        private List<(RecordAbility Ability, BatchResult<RowPage>? Reach)>? _abilities;

        /// <summary>Creates a composition and appends the rows' statements: the main ids, the entity query, and each collection level's targets.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The request.</param>
        /// <param name="ids">The ids in request order, distinct; null when every row is read.</param>
        /// <param name="root">The stack's schema entity.</param>
        /// <param name="readFilter">The caller's row filter on the stack's read; null when unrestricted.</param>
        /// <param name="idsSource">The table of the ids already visible, as a save's read-back passes; null to read them under the filter.</param>
        /// <param name="projection">What the read selects.</param>
        /// <param name="materials">What turns rows read into related entities.</param>
        public DetailsComposition(
            IDataBatch batch,
            IEntityBehavior<TEntity, TKey> behavior,
            DetailsRequest request,
            IReadOnlyList<TKey>? ids,
            EntityDescriptor root,
            FilterTree? readFilter,
            SqlIdentifier? idsSource,
            DetailsProjection projection,
            DetailsMaterials materials)
        {
            _batch = batch;
            _behavior = behavior;
            _request = request;
            _requested = new HashSet<string>(request.Include, StringComparer.Ordinal);
            _ids = ids;
            _materials = materials;
            string key = root.Key.Name;

            // --- 1. The main ids: row-level security decides visibility here and nowhere else ---
            if (idsSource is null)
            {
                IReadOnlyList<KeySetRestriction> restrictions = ids is null ? [] : [new KeySetRestriction(key, KeyTables.Bind(batch, ids).Name)];
                _main = batch.Rows(
                    new QuerySpec { Root = root, Select = key, Filter = readFilter, OrderBy = ids is null ? key : null, Restrictions = restrictions },
                    arguments: null,
                    new RowQueryOptions { CaptureKeys = true });
                idsSource = KeyTables.Of(batch).KeysOf(_main)
                    ?? throw new InvalidOperationException("The main-ids statement captured no keys, so the details plan has no visible ids to restrict on.");
            }

            IdsSource = idsSource;

            // --- 2. The rows, every collection, and the related projection along the expansion ---
            _entities = batch.Query(new EntityQuery<TEntity>
            {
                Select = projection.RootSelect,
                AllProperties = true,
                Restrictions = [new KeySetRestriction(key, IdsSource.Name)],
                Children = projection.Children,
            });
            foreach (DetailsTargets targets in projection.Targets)
            {
                BatchResult<RowPage> rows = batch.Rows(
                    new QuerySpec { Root = targets.Root, Select = targets.Select, Aggregate = true, Restrictions = [new KeySetRestriction(targets.KeyPath, IdsSource.Name)] },
                    arguments: null,
                    options: null);
                _targets.Add((targets, rows));
            }
        }

        /// <summary>The table of the visible ids, which every extra joins.</summary>
        public SqlIdentifier IdsSource { get; }

        /// <summary>Whether an extra is already added under a name.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when added.</returns>
        public bool Has(string name)
        {
            return _extras.ContainsKey(name);
        }

        /// <summary>A producer's sink: the plan's statements, held to the names the producer declares.</summary>
        /// <param name="producer">The producer's class, for the refusals.</param>
        /// <param name="declared">The extras the producer declares.</param>
        /// <returns>The sink.</returns>
        public IDetailsPlanSink<TEntity> SinkFor(Type producer, IReadOnlySet<string> declared)
        {
            return new ProducerSink(this, producer, declared);
        }

        /// <summary>
        ///     Appends the reads of the record permissions: one per ability whose grant is filtered,
        ///     over the visible ids under the grant's filter.
        /// </summary>
        /// <param name="abilities">Each ability the caller is granted, with its decision's row filter when the grant is filtered.</param>
        public void AddPermissions(IReadOnlyList<(RecordAbility Ability, FilterTree? Filter)> abilities)
        {
            ArgumentNullException.ThrowIfNull(abilities);
            _abilities = [];
            foreach ((RecordAbility ability, FilterTree? filter) in abilities)
            {
                BatchResult<RowPage>? reach = filter is null
                    ? null
                    : _batch.Rows(
                        new QuerySpec { Root = RootOf(), Select = KeyName(), Filter = filter, Restrictions = [new KeySetRestriction(KeyName(), IdsSource.Name)] },
                        arguments: null,
                        options: null);
                _abilities.Add((ability, reach));
            }
        }

        /// <summary>
        ///     Assembles the result after the batch: the visible rows in request order, what they and
        ///     their collections point at, and each requested extra in request order.
        /// </summary>
        /// <returns>The result.</returns>
        public EntitiesResult<TEntity> Complete()
        {
            // --- The visible rows, in request order ---
            EntityQueryResult<TEntity> loaded = _entities.Value;
            Dictionary<TKey, TEntity> byId = [];
            foreach (TEntity entity in loaded.Entities)
            {
                byId[entity.Id] = entity;
            }

            // The requested ids keep their order; with none, the main ids' key order, else the rows'.
            // An id the main ids did not admit has no row: the entity query reads the visible ids only.
            IEnumerable<TKey> order = _ids
                ?? (_main is not null ? Keys(_main.Value.Rows) : loaded.Entities.Select(static entity => entity.Id));
            List<TEntity> visible = [];
            foreach (TKey id in order)
            {
                if (byId.TryGetValue(id, out TEntity? entity))
                {
                    visible.Add(entity);
                }
            }

            // --- What the rows point at, and what their collections point at, each entity once ---
            RelatedEntities related = Related(loaded.Related);

            // --- The extras, in request order ---
            OrderedDictionary<string, JsonElement> extras = new(StringComparer.Ordinal);
            foreach (string name in _request.Include.Distinct(StringComparer.Ordinal))
            {
                if (string.Equals(name, StackDescriptorBuilder.PermissionsExtra, StringComparison.Ordinal))
                {
                    extras[name] = Serialize(Permissions(visible));
                }
                else if (_extras.TryGetValue(name, out Func<IReadOnlyList<TEntity>, JsonElement>? produce))
                {
                    extras[name] = produce(visible);
                }
            }

            return new EntitiesResult<TEntity>
            {
                Ids = [.. visible.Select(static entity => long.CreateChecked(entity.Id))],
                Entities = visible,
                Related = related,
                Extras = _request.Include.Count == 0 ? null : extras,
            };
        }

        /// <summary>The keys of a row set's first column.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The keys, in row order.</returns>
        private static List<TKey> Keys(QueryRowSet rows)
        {
            List<TKey> keys = new(rows.RowCount);
            Array buffer = rows.GetBuffer(0);
            for (int row = 0; row < rows.RowCount; row++)
            {
                if (!rows.IsNull(0, row))
                {
                    keys.Add(buffer switch
                    {
                        int[] ints => TKey.CreateChecked(ints[row]),
                        long[] longs => TKey.CreateChecked(longs[row]),
                        _ => TKey.CreateChecked(Convert.ToInt64(buffer.GetValue(row), CultureInfo.InvariantCulture)),
                    });
                }
            }

            return keys;
        }

        /// <summary>Serialises an extra's value as the wire writes it.</summary>
        /// <typeparam name="TValue">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON.</returns>
        private static JsonElement Serialize<TValue>(TValue value)
        {
            return JsonSerializer.SerializeToElement(value, TellmaJson.Options);
        }

        /// <summary>The related entities of the rows and of their collections, each entity once with the columns all its sources carried.</summary>
        /// <param name="rows">The related entities the entity query gathered from the rows.</param>
        /// <returns>The related entities.</returns>
        private RelatedEntities Related(RelatedEntities rows)
        {
            if (_targets.Count == 0)
            {
                return rows;
            }

            RelatedCollector collector = new();
            foreach (RelatedEntitySet set in rows.Sets.Values)
            {
                EntityMaterializer.AddWhole(collector, _materials.Metadata.Get(set.EntityType), set.Entities, set.Projection);
            }

            foreach ((DetailsTargets targets, BatchResult<RowPage> read) in _targets)
            {
                // The level's own instances are empty shells: every column of the grouped read is a related one.
                _ = _materials.Materializer.Read(_materials.Map, targets.Child, read.Value.Rows, collector);
            }

            return _materials.Materializer.Build(collector);
        }

        /// <summary>
        ///     The record permissions of the visible rows: per row, each granted ability its filter
        ///     does not reach, then each remaining ability the service's rule refuses.
        /// </summary>
        /// <param name="visible">The visible rows, in request order.</param>
        /// <returns>One entry per visible row, in request order.</returns>
        private List<RecordPermissions<TKey>> Permissions(List<TEntity> visible)
        {
            // --- Which visible rows each filtered grant reaches ---
            List<(RecordAbility Ability, HashSet<TKey>? Reached)> abilities = [];
            foreach ((RecordAbility ability, BatchResult<RowPage>? reach) in _abilities ?? [])
            {
                abilities.Add((ability, reach is null ? null : [.. Keys(reach.Value.Rows)]));
            }

            // --- Per row: outside the filter first, then the rule ---
            List<RecordPermissions<TKey>> permissions = new(visible.Count);
            foreach (TEntity row in visible)
            {
                Dictionary<string, string> refused = new(StringComparer.Ordinal);
                foreach ((RecordAbility ability, HashSet<TKey>? reached) in abilities)
                {
                    if (reached is not null && !reached.Contains(row.Id))
                    {
                        refused[ability.Name] = RecordAbilities.OutsideFilter;
                    }
                    else if (_behavior.RefuseOnRecord(row, ability.Name) is string reason)
                    {
                        refused[ability.Name] = reason;
                    }
                }

                permissions.Add(new RecordPermissions<TKey>(row.Id, refused));
            }

            return permissions;
        }

        /// <summary>The stack's schema entity, which every keyed read of the plan is rooted at.</summary>
        /// <returns>The entity.</returns>
        private EntityDescriptor RootOf()
        {
            return _materials.Root;
        }

        /// <summary>The stack's key name.</summary>
        /// <returns>The name.</returns>
        private string KeyName()
        {
            return _materials.Root.Key.Name;
        }

        /// <summary>Records an extra under a name, by the name rules every producer is held to.</summary>
        /// <param name="producer">The producer's class.</param>
        /// <param name="declared">The extras it declares.</param>
        /// <param name="name">The extra's name.</param>
        /// <param name="produce">Produces the value after the batch.</param>
        /// <exception cref="InvalidOperationException">The name is not requested, not declared by the producer, or already added.</exception>
        private void Add(Type producer, IReadOnlySet<string> declared, string name, Func<IReadOnlyList<TEntity>, JsonElement> produce)
        {
            if (!declared.Contains(name))
            {
                throw new InvalidOperationException(
                    $"{producer.Name} adds the details extra '{name}', which its [DetailsExtras] does not declare. Declare every extra a class adds.");
            }

            if (!_requested.Contains(name))
            {
                throw new InvalidOperationException(
                    $"{producer.Name} adds the details extra '{name}', which the request does not include. Add only the extras the plan's request includes.");
            }

            if (!_extras.TryAdd(name, produce))
            {
                throw new InvalidOperationException($"{producer.Name} adds the details extra '{name}' a second time. Add each extra once.");
            }
        }

        /// <summary>Refuses raw text that declares a write: every statement of a details plan is a read.</summary>
        /// <param name="producer">The producer's class.</param>
        /// <param name="options">The statement's options.</param>
        /// <exception cref="InvalidOperationException">The statement declares a write.</exception>
        private static void RequireRead(Type producer, SqlOptions options)
        {
            if (options.Writes.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{producer.Name} appends raw text that declares writes to a details plan. A details plan reads only; declare no written table.");
            }
        }

        /// <summary>A producer's view of the plan: the plan's statements, held to the names the producer declares.</summary>
        /// <param name="owner">The composition.</param>
        /// <param name="producer">The producer's class.</param>
        /// <param name="declared">The extras it declares.</param>
        private sealed class ProducerSink(DetailsComposition<TEntity, TKey> owner, Type producer, IReadOnlySet<string> declared) : IDetailsPlanSink<TEntity>
        {
            /// <inheritdoc />
            public void AddExtra(string name, FormattableString sql, SqlOptions? options)
            {
                options ??= SqlOptions.None;
                RequireRead(producer, options);
                if (options.ResultSets < 1)
                {
                    throw new InvalidOperationException(
                        $"{producer.Name} adds the details extra '{name}' as raw text that declares no result set. Declare the result sets it returns; the first is the extra.");
                }

                BatchResult<RawResult>? read = null;
                owner.Add(producer, declared, name, _ => Serialize(read!.Value.ResultSets[0]));
#pragma warning disable TELLMA0003
                read = owner._batch.Sql(sql, options);
#pragma warning restore TELLMA0003
            }

            /// <inheritdoc />
            public void AddExtra(string name, QuerySpec spec, QueryArguments? arguments)
            {
                BatchResult<RowPage>? read = null;
                owner.Add(producer, declared, name, _ => Serialize(read!.Value.Rows));
                read = owner._batch.Rows(spec, arguments, options: null);
            }

            /// <inheritdoc />
            public BatchResult<RowPage> AddRead(QuerySpec spec, QueryArguments? arguments)
            {
                return owner._batch.Rows(spec, arguments, options: null);
            }

            /// <inheritdoc />
            public BatchResult<RawResult> AddRead(FormattableString sql, SqlOptions? options)
            {
                options ??= SqlOptions.None;
                RequireRead(producer, options);
#pragma warning disable TELLMA0003
                return owner._batch.Sql(sql, options);
#pragma warning restore TELLMA0003
            }

            /// <inheritdoc />
            public void AddComputedExtra<TValue>(string name, Func<IReadOnlyList<TEntity>, TValue> compute)
            {
                owner.Add(producer, declared, name, visible => Serialize(compute(visible)));
            }
        }
    }

    /// <summary>What a details composition turns rows read into related entities with.</summary>
    /// <param name="Map">The tenant schema's map.</param>
    /// <param name="Root">The stack's schema entity.</param>
    /// <param name="Materializer">The entity materializer.</param>
    /// <param name="Metadata">The tenant model's entity metadata.</param>
    internal sealed record DetailsMaterials(QueryexSchemaMap Map, EntityDescriptor Root, EntityMaterializer Materializer, IEntityMetadataProvider Metadata);
}
