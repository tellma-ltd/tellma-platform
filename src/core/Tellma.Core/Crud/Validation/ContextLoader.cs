// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.2: a DataLoader over the round's batch. Every load returns its ref at once and
    // registers the request; LoadAsync parks the caller until the scheduler dispatches the round.
    // Requests are deduplicated by structural key (the root, the key property, the filter tree in
    // order, the parameter values, the raw text), key values are unioned into one key-set table,
    // selects are unioned, and a key that answers "not found" is cached like any value.
    /// <summary>
    ///     The validation context loader of one operation: collects the loads its validators request,
    ///     merges them, composes each round's onto a batch, and answers repeated requests from what
    ///     earlier rounds loaded.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Keyed loads (<see cref="ByIds{T, TK}" />, <see cref="ByKey{T, TK}" />,
    ///         <see cref="ByParentIds{TChild, TK}" />) are entity queries the loader writes itself,
    ///         restricted to a key-set table of the requested values and never filtered by row-level
    ///         access. <see cref="Query{T}" />, <see cref="Rows" />, <see cref="Exists" /> and
    ///         <see cref="Count" /> compile their filters through the query engine;
    ///         <see cref="Visible{T}" /> conjoins the caller's read decision first.
    ///     </para>
    ///     <para>
    ///         A keyed load answers a dictionary: <see cref="ByIds{T, TK}" /> and
    ///         <see cref="ByKey{T, TK}" /> hold the keys found, a missing key being absent;
    ///         <see cref="ByParentIds{TChild, TK}" /> holds every requested parent, one without
    ///         children under an empty list. String keys compare ignoring case, as the database's
    ///         collation does.
    ///     </para>
    ///     <para>
    ///         A request made while a round is being dispatched joins the next round. Every member is
    ///         safe to call from concurrent validators.
    ///     </para>
    /// </remarks>
    internal sealed partial class ContextLoader : IContextLoader
    {
        /// <summary>Guards every member below, and the groups and caches they hold.</summary>
        private readonly Lock _gate = new();

        /// <summary>The entities' metadata.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The tenant's query schema and its map to the entities' metadata.</summary>
        private readonly QueryexSchemaMap _map;

        /// <summary>The scheduler that parks the callers of <see cref="LoadAsync" />.</summary>
        private readonly ValidationScheduler _scheduler;

        /// <summary>The caller's read decision on an entity, for <see cref="Visible{T}" />; null when the loader serves no visibility rule.</summary>
        private readonly Func<Type, AccessDecision>? _readDecisions;

        /// <summary>The groups the next round dispatches, by structural key, in request order.</summary>
        private readonly Dictionary<object, LoadGroup> _pending = [];

        /// <summary>The values earlier rounds loaded for value loads, by structural key.</summary>
        private readonly Dictionary<object, object?> _values = [];

        /// <summary>The entity lists earlier rounds loaded for queries, by structural key, with the select each was read under.</summary>
        private readonly Dictionary<string, (SelectSet Select, object Entities)> _queries = new(StringComparer.Ordinal);

        /// <summary>The keyed, children and ancestor caches, by the entity class and what they key on.</summary>
        private readonly Dictionary<(Type Entity, string Kind), object> _caches = [];

        /// <summary>Creates a loader.</summary>
        /// <param name="metadata">The entities' metadata.</param>
        /// <param name="map">The tenant's query schema and its map to the entities' metadata.</param>
        /// <param name="scheduler">The scheduler that parks the callers of <see cref="LoadAsync" />.</param>
        /// <param name="readDecisions">
        ///     The caller's read decision on an entity class, for <see cref="Visible{T}" />; null when the
        ///     loader serves no visibility rule, so a visible load is an authoring bug.
        /// </param>
        public ContextLoader(IEntityMetadataProvider metadata, QueryexSchemaMap map, ValidationScheduler scheduler, Func<Type, AccessDecision>? readDecisions = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(scheduler);
            _metadata = metadata;
            _map = map;
            _scheduler = scheduler;
            _readDecisions = readDecisions;
        }

        /// <summary>Whether a request waits for the next round.</summary>
        public bool HasPending
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count > 0;
                }
            }
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null)
            where TK : notnull
        {
            ArgumentNullException.ThrowIfNull(ids);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            return Keyed<T, TK>(metadata, metadata.Key, ids, SelectSet.Parse(select), nameof(ids));
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(string key, IReadOnlyList<TK> values, string? select = null)
            where TK : notnull
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(values);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            PropertyMetadata property = metadata.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"The entity {metadata.Name} has no property '{key}'.", nameof(key));

            // A value keys one row only when the property alone is unique; one unique per parent
            // keys a row per parent, which a dictionary by value cannot hold.
            bool unique = ReferenceEquals(property, metadata.Key)
                || metadata.UniqueIndexes.Any(index => index.Properties.Count == 1 && ReferenceEquals(index.Properties[0], property));
            return unique
                ? Keyed<T, TK>(metadata, property, values, SelectSet.Parse(select), nameof(values))
                : throw new ArgumentException(
                    $"The property {metadata.Name}.{property.Name} is not unique on its own, so a value does not key one row. Load by a property with a unique index of its own, or query the rows with a filter.",
                    nameof(key));
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
            where TK : notnull
        {
            ArgumentNullException.ThrowIfNull(parentIds);
            EntityMetadata metadata = _metadata.Get(typeof(TChild));
            PropertyMetadata parentKey = ParentKeyOf(metadata);
            KeySets.EnsureKeyType<TK>(parentKey, nameof(parentIds));
            TK[] keys = KeySets.Distinct(parentIds, nameof(parentIds));
            var selected = SelectSet.Parse(select);
            selected = selected.IsAll ? selected : selected.With(parentKey.Name);
            lock (_gate)
            {
                // --- Every parent an earlier round loaded under a covering select answers from the cache ---
                ChildrenCache<TChild, TK> cache = Cache(typeof(TChild), "children", static () => new ChildrenCache<TChild, TK>());
                TK[] missing = [.. keys.Where(key => !cache.Covers(key, selected))];
                if (missing.Length == 0)
                {
                    return new ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>>(cache.Answer(keys));
                }

                LoadGroup group = Pending((typeof(TChild), "children"), () => Open(typeof(ChildrenGroup<,>), [typeof(TChild), typeof(TK)], this, metadata, parentKey, cache));
                return ((IChildrenLoad<TChild, TK>)group).Add(keys, missing, selected);
            }
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            ArgumentNullException.ThrowIfNull(filter);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            return Entities<T>(metadata, filter, arguments, SelectSet.Parse(select));
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Visible<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            ArgumentNullException.ThrowIfNull(filter);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            var selected = SelectSet.Parse(select);
            AccessDecision decision = _readDecisions?.Invoke(typeof(T))
                ?? throw new InvalidOperationException(
                    $"A visible load of {metadata.Name} was requested, but this loader serves no read decisions. Visible loads are served inside an operation that resolved the caller's access.");

            // --- The caller's read decision: none reads nothing, a filter is conjoined, unrestricted adds nothing ---
            return decision.Outcome switch
            {
                AccessOutcome.Denied => new ContextRef<IReadOnlyList<T>>([]),
                AccessOutcome.Filtered => Entities<T>(metadata, FilterTree.And([filter, decision.Filter!]), arguments, selected),
                AccessOutcome.Unrestricted => Entities<T>(metadata, filter, arguments, selected),
                _ => throw new InvalidOperationException($"The read decision on {metadata.Name} has an unknown outcome, {decision.Outcome}."),
            };
        }

        /// <inheritdoc />
        public ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments = null)
        {
            ArgumentNullException.ThrowIfNull(spec);
            string key = QueryStatementKey.For("rows").Add(spec).Add(arguments).Build(QueryexLimits.Default).Text;
            return Value<QueryRowSet, QueryRowSet>(key, batch =>
            {
                BatchResult<RowPage> page = batch.Rows(spec, arguments, options: null);
                return () => page.Value.Rows;
            }, static rows => rows);
        }

        /// <inheritdoc />
        public ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments = null)
        {
            // An existence is a count capped at zero, so it shares its statement with that count.
            return Counted(root, filter, cap: 0, arguments, static count => count > 0);
        }

        /// <inheritdoc />
        public ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(cap);
            return Counted(root, filter, cap, arguments, static count => count);
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids)
            where TK : notnull
        {
            ArgumentNullException.ThrowIfNull(ids);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            TreeMetadata tree = metadata.Tree
                ?? throw new ArgumentException($"The entity {metadata.Name} is no tree, so its rows have no ancestors.", nameof(ids));
            KeySets.EnsureKeyType<TK>(metadata.Key, nameof(ids));
            TK[] keys = KeySets.Distinct(ids, nameof(ids));
            lock (_gate)
            {
                // --- Every id whose whole chain an earlier round loaded answers from the cache ---
                TreeCache<TK> cache = Cache(typeof(T), "ancestors", static () => new TreeCache<TK>());
                TK[] missing = [.. keys.Where(key => !cache.Covers(key))];
                if (missing.Length == 0)
                {
                    return new ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>>(cache.Answer(keys));
                }

                LoadGroup group = Pending((typeof(T), "ancestors"), () => new AncestorsGroup<TK>(_map.Descriptor(metadata), metadata.Key, tree.ParentKey, cache));
                return ((AncestorsGroup<TK>)group).Add(keys, missing);
            }
        }

        /// <inheritdoc />
        public ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(sql);
            options ??= SqlOptions.None;
            if (options.Writes.Count > 0)
            {
                throw new ArgumentException(
                    "A context load reads only, so its SqlOptions declare no writes: a validation round never writes. Write in the operation's persist batch instead.",
                    nameof(options));
            }

            SqlOptions read = options;
            SqlStatementKey key = new(sql.Format, sql.GetArguments(), read);
            return Value<RawResult, RawResult>(key, batch =>
            {
                // The text was checked where IContextLoader.Sql was called, and reaches the batch unchanged.
#pragma warning disable TELLMA0003
                BatchResult<RawResult> result = batch.Sql(sql, read);
#pragma warning restore TELLMA0003
                return () => result.Value;
            }, static result => result);
        }

        /// <inheritdoc />
        public Task LoadAsync()
        {
            // With nothing pending, every ref the caller holds is loaded already: no round is needed.
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return Task.CompletedTask;
                }
            }

            return _scheduler.ParkAsync();
        }

        /// <inheritdoc />
        public void Prime<T, TK>(T entity)
            where TK : notnull
        {
            ArgumentNullException.ThrowIfNull(entity);
            EntityMetadata metadata = _metadata.Get(typeof(T));
            KeySets.EnsureKeyType<TK>(metadata.Key, nameof(entity));
            TK key = metadata.Key.Getter(entity) is TK value
                ? value
                : throw new ArgumentException($"The {metadata.Name} has no key to prime it by.", nameof(entity));
            lock (_gate)
            {
                KeyedCacheOf<T, TK>(metadata.Key).Prime(key, entity);
            }
        }

        /// <summary>
        ///     Takes every pending request as one round: requests made from now on wait for the next.
        /// </summary>
        /// <returns>The round, empty when nothing was pending.</returns>
        public ContextRound BeginRound()
        {
            lock (_gate)
            {
                LoadGroup[] groups = [.. _pending.Values];
                _pending.Clear();
                return new ContextRound(this, groups);
            }
        }

        /// <summary>
        ///     Dispatches one round on its own batch: composes every pending request, executes the
        ///     batch and fills every ref; a round with nothing pending sends nothing.
        /// </summary>
        /// <param name="batch">A composing batch, which this call executes when the round loads anything.</param>
        /// <param name="cancellationToken">The operation's cancellation.</param>
        /// <returns>A task that completes when every ref of the round is filled.</returns>
        public async Task DispatchAsync(IDataBatch batch, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ContextRound round = BeginRound();
            if (round.IsEmpty)
            {
                return;
            }

            round.Compose(batch);
            _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            round.Complete();
        }

        /// <summary>Opens a load group over generic arguments the caller holds without the group's class constraint.</summary>
        /// <param name="definition">The group's open generic class.</param>
        /// <param name="arguments">Its type arguments.</param>
        /// <param name="parameters">Its constructor's arguments.</param>
        /// <returns>The group.</returns>
        private static LoadGroup Open(Type definition, Type[] arguments, params object?[] parameters)
        {
            // An entity class is always a class; a value type fails here, as the batch would refuse it.
            return (LoadGroup)Activator.CreateInstance(definition.MakeGenericType(arguments), parameters)!;
        }

        /// <summary>The parent key a child entity's collection nests it under.</summary>
        /// <param name="child">The child's metadata.</param>
        /// <returns>The parent key.</returns>
        /// <exception cref="ArgumentException">The entity is no child of any collection, or of several by different keys.</exception>
        private PropertyMetadata ParentKeyOf(EntityMetadata child)
        {
            PropertyMetadata[] keys = [.. _metadata.All
                .SelectMany(static parent => parent.Children)
                .Where(collection => ReferenceEquals(collection.Child, child))
                .Select(static collection => collection.ParentKey)
                .Distinct()];
            return keys.Length == 1
                ? keys[0]
                : throw new ArgumentException(
                    keys.Length == 0
                        ? $"The entity {child.Name} is no child of any collection, so it has no parent ids to load by."
                        : $"The entity {child.Name} nests under more than one parent key, so its parent ids are ambiguous.",
                    nameof(child));
        }

        /// <summary>The names of an entity's properties the query schema declares, which a load of every property selects.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <returns>The names, in declaration order.</returns>
        private IEnumerable<string> Declared(EntityMetadata metadata)
        {
            return _map.Declared(metadata).Select(static property => property.Name);
        }

        /// <summary>Registers a keyed load, answered from the cache for every key it already holds.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <typeparam name="TK">The key's type.</typeparam>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="property">The key property.</param>
        /// <param name="values">The requested values.</param>
        /// <param name="select">The select.</param>
        /// <param name="parameterName">The caller's parameter, for refusals.</param>
        /// <returns>The ref.</returns>
        private ContextRef<IReadOnlyDictionary<TK, T>> Keyed<T, TK>(
            EntityMetadata metadata, PropertyMetadata property, IReadOnlyList<TK> values, SelectSet select, string parameterName)
            where TK : notnull
        {
            KeySets.EnsureKeyType<TK>(property, parameterName);
            SelectSet keyed = select.IsAll ? select : select.With(property.Name);
            TK[] keys = KeySets.Distinct(values, parameterName);
            lock (_gate)
            {
                // --- Every key a round loaded, primed or found missing answers from the cache ---
                KeyedCache<T, TK> cache = KeyedCacheOf<T, TK>(property);
                TK[] missing = [.. keys.Where(key => !cache.Covers(key, keyed))];
                if (missing.Length == 0)
                {
                    return new ContextRef<IReadOnlyDictionary<TK, T>>(cache.Answer(keys));
                }

                LoadGroup group = Pending((typeof(T), "keyed:" + property.Name), () => Open(typeof(KeyedGroup<,>), [typeof(T), typeof(TK)], this, metadata, property, cache));
                return ((IKeyedLoad<T, TK>)group).Add(keys, missing, keyed);
            }
        }

        /// <summary>Registers an entity query, answered by an earlier round when one served a covering select.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="arguments">The filter's arguments, or null.</param>
        /// <param name="select">The select.</param>
        /// <returns>The ref.</returns>
        private ContextRef<IReadOnlyList<T>> Entities<T>(EntityMetadata metadata, FilterTree filter, QueryArguments? arguments, SelectSet select)
        {
            string key = QueryStatementKey.For("query").Add(typeof(T).AssemblyQualifiedName).Add(filter).Add(arguments).Build(QueryexLimits.Default).Text;
            lock (_gate)
            {
                // --- An earlier round served the same query under a covering select ---
                SelectSet wanted = select;
                if (_queries.TryGetValue(key, out (SelectSet Select, object Entities) served))
                {
                    if (served.Select.Covers(select))
                    {
                        return new ContextRef<IReadOnlyList<T>>((IReadOnlyList<T>)served.Entities);
                    }

                    // Read again under both selects, so what the cache holds only grows.
                    wanted = wanted.Union(served.Select);
                }

                LoadGroup group = Pending(key, () => Open(typeof(EntitiesGroup<>), [typeof(T)], this, metadata, key, filter, arguments));
                return ((IEntitiesLoad<T>)group).Add(wanted);
            }
        }

        /// <summary>Registers a capped count, which an existence shares.</summary>
        /// <typeparam name="TValue">The ref's value.</typeparam>
        /// <param name="root">The entity's name in the query schema.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="cap">The cap.</param>
        /// <param name="arguments">The filter's arguments, or null.</param>
        /// <param name="project">Projects the count onto the ref's value.</param>
        /// <returns>The ref.</returns>
        private ContextRef<TValue> Counted<TValue>(string root, FilterTree filter, int cap, QueryArguments? arguments, Func<int, TValue> project)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentNullException.ThrowIfNull(filter);
            EntityDescriptor entity = _map.Schema.FindEntity(root)
                ?? throw new ArgumentException($"The query schema has no entity '{root}'.", nameof(root));
            QuerySpec spec = new() { Root = entity, Select = entity.Key.Name, Filter = filter };
            string key = QueryStatementKey.For("count").Add(entity.Name).Add(filter).Add(arguments).Add(cap).Build(QueryexLimits.Default).Text;
            return Value(key, batch =>
            {
                BatchResult<int> count = batch.Count(spec, arguments, cap);
                return () => count.Value;
            }, project);
        }

        /// <summary>Registers a load whose value its structural key fully decides.</summary>
        /// <typeparam name="TLoaded">The loaded value.</typeparam>
        /// <typeparam name="TValue">The ref's value.</typeparam>
        /// <param name="key">The structural key.</param>
        /// <param name="compose">Appends the load to a batch and returns what reads its value once the batch ran.</param>
        /// <param name="project">Projects the loaded value onto the ref's value.</param>
        /// <returns>The ref.</returns>
        private ContextRef<TValue> Value<TLoaded, TValue>(object key, Func<IDataBatch, Func<TLoaded>> compose, Func<TLoaded, TValue> project)
        {
            lock (_gate)
            {
                // --- An earlier round served the same load ---
                if (_values.TryGetValue(key, out object? served))
                {
                    return new ContextRef<TValue>(project((TLoaded)served!));
                }

                LoadGroup group = Pending(key, () => new ValueGroup<TLoaded>(this, key, compose));
                return ((ValueGroup<TLoaded>)group).Add(project);
            }
        }

        /// <summary>The pending group of a structural key, opened when none is pending; called under the gate.</summary>
        /// <param name="key">The structural key.</param>
        /// <param name="open">Opens the group.</param>
        /// <returns>The group.</returns>
        private LoadGroup Pending(object key, Func<LoadGroup> open)
        {
            if (!_pending.TryGetValue(key, out LoadGroup? group))
            {
                group = open();
                _pending.Add(key, group);
            }

            return group;
        }

        /// <summary>A cache of the loader, created on first use; called under the gate.</summary>
        /// <typeparam name="TCache">The cache's class.</typeparam>
        /// <param name="entity">The entity class it caches rows of.</param>
        /// <param name="kind">What it keys on.</param>
        /// <param name="create">Creates it.</param>
        /// <returns>The cache.</returns>
        private TCache Cache<TCache>(Type entity, string kind, Func<TCache> create)
            where TCache : class
        {
            if (!_caches.TryGetValue((entity, kind), out object? cache))
            {
                cache = create();
                _caches.Add((entity, kind), cache);
            }

            return (TCache)cache;
        }

        /// <summary>The keyed cache of an entity class and a key property, created on first use; called under the gate.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <typeparam name="TK">The key's type.</typeparam>
        /// <param name="property">The key property.</param>
        /// <returns>The cache.</returns>
        private KeyedCache<T, TK> KeyedCacheOf<T, TK>(PropertyMetadata property)
            where TK : notnull
        {
            return Cache(typeof(T), "keyed:" + property.Name, static () => new KeyedCache<T, TK>());
        }
    }
}
