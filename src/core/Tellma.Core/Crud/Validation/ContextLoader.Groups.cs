// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Validation
{
    /// <summary>The load groups a round dispatches and the caches later rounds are answered from.</summary>
    internal sealed partial class ContextLoader
    {
        /// <summary>A keyed load, seen by a caller whose entity class carries no class constraint.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <typeparam name="TK">The key's type.</typeparam>
        private interface IKeyedLoad<T, TK>
            where TK : notnull
        {
            /// <summary>Adds a request; called under the loader's gate.</summary>
            /// <param name="keys">Every key requested.</param>
            /// <param name="missing">The keys the cache does not answer.</param>
            /// <param name="select">The request's select.</param>
            /// <returns>The request's ref.</returns>
            ContextRef<IReadOnlyDictionary<TK, T>> Add(TK[] keys, TK[] missing, SelectSet select);
        }

        /// <summary>A children load, seen by a caller whose entity class carries no class constraint.</summary>
        /// <typeparam name="TChild">The child class.</typeparam>
        /// <typeparam name="TK">The parent key's type.</typeparam>
        private interface IChildrenLoad<TChild, TK>
            where TK : notnull
        {
            /// <summary>Adds a request; called under the loader's gate.</summary>
            /// <param name="keys">Every parent id requested.</param>
            /// <param name="missing">The parent ids the cache does not answer.</param>
            /// <param name="select">The request's select.</param>
            /// <returns>The request's ref.</returns>
            ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> Add(TK[] keys, TK[] missing, SelectSet select);
        }

        /// <summary>An entity query, seen by a caller whose entity class carries no class constraint.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        private interface IEntitiesLoad<T>
        {
            /// <summary>Adds a request; called under the loader's gate.</summary>
            /// <param name="select">The request's select.</param>
            /// <returns>The request's ref.</returns>
            ContextRef<IReadOnlyList<T>> Add(SelectSet select);
        }

        /// <summary>The requests of one structural key that one round serves with one load.</summary>
        internal abstract class LoadGroup
        {
            /// <summary>Appends the group's statements to a batch; called again when the round is composed again.</summary>
            /// <param name="batch">The composing batch.</param>
            /// <returns>Reads the group's results once the batch has run.</returns>
            public abstract Action Compose(IDataBatch batch);

            /// <summary>Caches what the group read and fills its requests' refs; called under the loader's gate after every read.</summary>
            public abstract void Finish();
        }

        /// <summary>A load whose structural key fully decides its value: a rows query, a count, a raw statement.</summary>
        /// <typeparam name="TLoaded">The loaded value.</typeparam>
        /// <param name="loader">The loader, whose cache takes the value.</param>
        /// <param name="key">The structural key.</param>
        /// <param name="compose">Appends the load to a batch and returns what reads its value once the batch ran.</param>
        private sealed class ValueGroup<TLoaded>(ContextLoader loader, object key, Func<IDataBatch, Func<TLoaded>> compose) : LoadGroup
        {
            /// <summary>Each request's completion.</summary>
            private readonly List<Action<TLoaded>> _targets = [];

            /// <summary>Reads the value from the batch the round was last composed on.</summary>
            private Func<TLoaded>? _read;

            /// <summary>The value read.</summary>
            private TLoaded? _value;

            /// <summary>Adds a request; called under the loader's gate.</summary>
            /// <typeparam name="TValue">The ref's value.</typeparam>
            /// <param name="project">Projects the loaded value onto the ref's value.</param>
            /// <returns>The request's ref.</returns>
            public ContextRef<TValue> Add<TValue>(Func<TLoaded, TValue> project)
            {
                ContextRef<TValue> target = new();
                _targets.Add(value => target.Complete(project(value)));
                return target;
            }

            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                _read = compose(batch);
                return () => _value = _read();
            }

            /// <inheritdoc />
            public override void Finish()
            {
                loader._values[key] = _value;
                foreach (Action<TLoaded> target in _targets)
                {
                    target(_value!);
                }
            }
        }

        /// <summary>The keyed requests of one entity class and key property: one entity query restricted to the union of their keys.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <typeparam name="TK">The key's type.</typeparam>
        /// <param name="loader">The loader.</param>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="property">The key property.</param>
        /// <param name="cache">The cache the keys are answered from.</param>
        private sealed class KeyedGroup<T, TK>(ContextLoader loader, EntityMetadata metadata, PropertyMetadata property, KeyedCache<T, TK> cache)
            : LoadGroup, IKeyedLoad<T, TK>
            where T : class
            where TK : notnull
        {
            /// <summary>Each request's ref and keys.</summary>
            private readonly List<(ContextRef<IReadOnlyDictionary<TK, T>> Target, TK[] Keys)> _requests = [];

            /// <summary>The keys to load, in first-requested order.</summary>
            private readonly List<TK> _load = [];

            /// <summary>The keys to load, for lookups.</summary>
            private readonly HashSet<TK> _loading = new(KeySets.Comparer<TK>());

            /// <summary>The union of the requests' selects.</summary>
            private SelectSet? _select;

            /// <summary>The entities read.</summary>
            private IReadOnlyList<T>? _loaded;

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, T>> Add(TK[] keys, TK[] missing, SelectSet select)
            {
                _select = _select?.Union(select) ?? select;
                foreach (TK key in missing)
                {
                    if (_loading.Add(key))
                    {
                        _load.Add(key);

                        // A key read again keeps what it was read under, so the cache only grows.
                        if (cache.SelectOf(key) is SelectSet earlier)
                        {
                            _select = _select.Union(earlier);
                        }
                    }
                }

                ContextRef<IReadOnlyDictionary<TK, T>> target = new();
                _requests.Add((target, keys));
                return target;
            }

            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                SqlIdentifier table = KeySets.Table(batch, _load);
                BatchResult<EntityQueryResult<T>> result = batch.Query(new EntityQuery<T>
                {
                    Select = _select!.Render(loader.Declared(metadata)),
                    AllProperties = _select.IsAll,
                    Restrictions = [new KeySetRestriction(property.Name, table.Name)],
                    OrderBy = metadata.Key.Name,
                });
                return () => _loaded = result.Value.Entities;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                // --- The rows found, then every key that found none, cached as missing ---
                HashSet<TK> found = new(KeySets.Comparer<TK>());
                foreach (T entity in _loaded!)
                {
                    if (property.Getter(entity) is TK key)
                    {
                        cache.Store(key, entity, _select!);
                        _ = found.Add(key);
                    }
                }

                foreach (TK key in _load.Where(key => !found.Contains(key)))
                {
                    cache.Store(key, null, SelectSet.All);
                }

                foreach ((ContextRef<IReadOnlyDictionary<TK, T>> target, TK[] keys) in _requests)
                {
                    target.Complete(cache.Answer(keys));
                }
            }
        }

        /// <summary>The children requests of one child class: one entity query restricted to the union of their parent ids.</summary>
        /// <typeparam name="TChild">The child class.</typeparam>
        /// <typeparam name="TK">The parent key's type.</typeparam>
        /// <param name="loader">The loader.</param>
        /// <param name="metadata">The child's metadata.</param>
        /// <param name="parentKey">The child's parent key.</param>
        /// <param name="cache">The cache the parents are answered from.</param>
        private sealed class ChildrenGroup<TChild, TK>(ContextLoader loader, EntityMetadata metadata, PropertyMetadata parentKey, ChildrenCache<TChild, TK> cache)
            : LoadGroup, IChildrenLoad<TChild, TK>
            where TChild : class
            where TK : notnull
        {
            /// <summary>Each request's ref and parent ids.</summary>
            private readonly List<(ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> Target, TK[] Keys)> _requests = [];

            /// <summary>The parent ids to load, in first-requested order.</summary>
            private readonly List<TK> _load = [];

            /// <summary>The parent ids to load, for lookups.</summary>
            private readonly HashSet<TK> _loading = new(KeySets.Comparer<TK>());

            /// <summary>The union of the requests' selects.</summary>
            private SelectSet? _select;

            /// <summary>The children read.</summary>
            private IReadOnlyList<TChild>? _loaded;

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> Add(TK[] keys, TK[] missing, SelectSet select)
            {
                _select = _select?.Union(select) ?? select;
                foreach (TK key in missing)
                {
                    if (_loading.Add(key))
                    {
                        _load.Add(key);
                        if (cache.SelectOf(key) is SelectSet earlier)
                        {
                            _select = _select.Union(earlier);
                        }
                    }
                }

                ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> target = new();
                _requests.Add((target, keys));
                return target;
            }

            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                SqlIdentifier table = KeySets.Table(batch, _load);
                BatchResult<EntityQueryResult<TChild>> result = batch.Query(new EntityQuery<TChild>
                {
                    Select = _select!.Render(loader.Declared(metadata)),
                    AllProperties = _select.IsAll,
                    Restrictions = [new KeySetRestriction(parentKey.Name, table.Name)],
                    OrderBy = metadata.Key.Name,
                });
                return () => _loaded = result.Value.Entities;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                // --- Each parent's children in key order; a parent with none holds an empty list ---
                Dictionary<TK, List<TChild>> byParent = new(KeySets.Comparer<TK>());
                foreach (TK key in _load)
                {
                    byParent[key] = [];
                }

                foreach (TChild child in _loaded!)
                {
                    if (parentKey.Getter(child) is TK key && byParent.TryGetValue(key, out List<TChild>? children))
                    {
                        children.Add(child);
                    }
                }

                foreach ((TK key, List<TChild> children) in byParent)
                {
                    cache.Store(key, children, _select!);
                }

                foreach ((ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> target, TK[] keys) in _requests)
                {
                    target.Complete(cache.Answer(keys));
                }
            }
        }

        /// <summary>The query requests of one structural key: one entity query under the union of their selects.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <param name="loader">The loader, whose cache takes the entities.</param>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="key">The structural key.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="arguments">The filter's arguments, or null.</param>
        private sealed class EntitiesGroup<T>(ContextLoader loader, EntityMetadata metadata, string key, FilterTree filter, QueryArguments? arguments)
            : LoadGroup, IEntitiesLoad<T>
            where T : class
        {
            /// <summary>The requests' refs.</summary>
            private readonly List<ContextRef<IReadOnlyList<T>>> _targets = [];

            /// <summary>The union of the requests' selects.</summary>
            private SelectSet? _select;

            /// <summary>The entities read.</summary>
            private IReadOnlyList<T>? _loaded;

            /// <inheritdoc />
            public ContextRef<IReadOnlyList<T>> Add(SelectSet select)
            {
                _select = _select?.Union(select) ?? select;
                ContextRef<IReadOnlyList<T>> target = new();
                _targets.Add(target);
                return target;
            }

            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                BatchResult<EntityQueryResult<T>> result = batch.Query(new EntityQuery<T>
                {
                    Select = _select!.Render(loader.Declared(metadata)),
                    AllProperties = _select.IsAll,
                    Filter = filter,
                    Arguments = arguments,
                    OrderBy = metadata.Key.Name,
                });
                return () => _loaded = result.Value.Entities;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                loader._queries[key] = (_select!, _loaded!);
                foreach (ContextRef<IReadOnlyList<T>> target in _targets)
                {
                    target.Complete(_loaded!);
                }
            }
        }

        /// <summary>The ancestor requests of one tree: one rows query over the ids' keys and parents, ancestors included.</summary>
        /// <typeparam name="TK">The key's type.</typeparam>
        /// <param name="root">The tree's entity in the query schema.</param>
        /// <param name="key">The tree's key.</param>
        /// <param name="parentKey">The tree's parent key.</param>
        /// <param name="cache">The cache the chains are answered from.</param>
        private sealed class AncestorsGroup<TK>(EntityDescriptor root, PropertyMetadata key, PropertyMetadata parentKey, TreeCache<TK> cache) : LoadGroup
            where TK : notnull
        {
            /// <summary>Each request's ref and ids.</summary>
            private readonly List<(ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Target, TK[] Keys)> _requests = [];

            /// <summary>The ids to load, in first-requested order.</summary>
            private readonly List<TK> _load = [];

            /// <summary>The ids to load, for lookups.</summary>
            private readonly HashSet<TK> _loading = new(KeySets.Comparer<TK>());

            /// <summary>The page read: the ids' rows and their ancestors' rows.</summary>
            private RowPage? _loaded;

            /// <summary>Adds a request; called under the loader's gate.</summary>
            /// <param name="keys">Every id requested.</param>
            /// <param name="missing">The ids the cache does not answer.</param>
            /// <returns>The request's ref.</returns>
            public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Add(TK[] keys, TK[] missing)
            {
                foreach (TK id in missing)
                {
                    if (_loading.Add(id))
                    {
                        _load.Add(id);
                    }
                }

                ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> target = new();
                _requests.Add((target, keys));
                return target;
            }

            /// <inheritdoc />
            public override Action Compose(IDataBatch batch)
            {
                // The ids' (Id, ParentId) rows, and those of every node above them, in one statement.
                SqlIdentifier table = KeySets.Table(batch, _load);
                QuerySpec spec = new()
                {
                    Root = root,
                    Select = $"{key.Name}, {parentKey.Name}",
                    Restrictions = [new KeySetRestriction(key.Name, table.Name)],
                };
                BatchResult<RowPage> result = batch.Rows(spec, arguments: null, new RowQueryOptions { IncludeAncestors = true });
                return () => _loaded = result.Value;
            }

            /// <inheritdoc />
            public override void Finish()
            {
                // --- Every node read joins the cache; an id that read no row is missing ---
                Store(_loaded!.Rows);
                if (_loaded.Ancestors is QueryRowSet ancestors)
                {
                    Store(ancestors);
                }

                foreach (TK id in _load)
                {
                    cache.MarkMissing(id);
                }

                foreach ((ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> target, TK[] keys) in _requests)
                {
                    target.Complete(cache.Answer(keys));
                }
            }

            /// <summary>Caches the nodes of a row set of (key, parent key) rows.</summary>
            /// <param name="rows">The rows.</param>
            private void Store(QueryRowSet rows)
            {
                Array ids = rows.GetBuffer(0);
                Array parents = rows.GetBuffer(1);
                for (int row = 0; row < rows.RowCount; row++)
                {
                    TK id = KeySets.Convert<TK>(ids.GetValue(row)!);
                    bool root = rows.IsNull(1, row);
                    cache.Store(id, root ? default : KeySets.Convert<TK>(parents.GetValue(row)!), root);
                }
            }
        }

        /// <summary>
        ///     What earlier rounds loaded of one entity class by one key property: each key's row and the
        ///     select it was read under, a missing key's absence, and the payload rows primed.
        /// </summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <typeparam name="TK">The key's type.</typeparam>
        private sealed class KeyedCache<T, TK>
            where TK : notnull
        {
            /// <summary>Each key's entry.</summary>
            private readonly Dictionary<TK, Entry> _entries = new(KeySets.Comparer<TK>());

            /// <summary>Whether a key is answered under a select: primed, missing, or read under a covering select.</summary>
            /// <param name="key">The key.</param>
            /// <param name="select">The select.</param>
            /// <returns>True when it is.</returns>
            public bool Covers(TK key, SelectSet select)
            {
                return _entries.TryGetValue(key, out Entry? entry) && (entry.Primed || entry.Value is null || entry.Select.Covers(select));
            }

            /// <summary>The select a key's row was read under, or null when none was read.</summary>
            /// <param name="key">The key.</param>
            /// <returns>The select.</returns>
            public SelectSet? SelectOf(TK key)
            {
                return _entries.TryGetValue(key, out Entry? entry) && entry.Value is not null ? entry.Select : null;
            }

            /// <summary>Caches a key's row, or its absence; a primed row is kept.</summary>
            /// <param name="key">The key.</param>
            /// <param name="value">The row, or null when the key found none.</param>
            /// <param name="select">The select it was read under.</param>
            public void Store(TK key, T? value, SelectSet select)
            {
                if (!(_entries.TryGetValue(key, out Entry? entry) && entry.Primed))
                {
                    _entries[key] = new Entry(value, select, Primed: false);
                }
            }

            /// <summary>Caches an in-memory row, which answers every select and is never replaced by a read.</summary>
            /// <param name="key">The key.</param>
            /// <param name="value">The row.</param>
            public void Prime(TK key, T value)
            {
                _entries[key] = new Entry(value, SelectSet.All, Primed: true);
            }

            /// <summary>The rows of the keys the cache holds a row for.</summary>
            /// <param name="keys">The keys.</param>
            /// <returns>The rows by key; a missing key is absent.</returns>
            public Dictionary<TK, T> Answer(IEnumerable<TK> keys)
            {
                Dictionary<TK, T> answer = new(KeySets.Comparer<TK>());
                foreach (TK key in keys)
                {
                    if (_entries.TryGetValue(key, out Entry? entry) && entry.Value is T value)
                    {
                        answer[key] = value;
                    }
                }

                return answer;
            }

            /// <summary>One key's entry.</summary>
            /// <param name="Value">The row, or null for a key that found none.</param>
            /// <param name="Select">The select it was read under.</param>
            /// <param name="Primed">Whether it is an in-memory row.</param>
            private sealed record Entry(T? Value, SelectSet Select, bool Primed);
        }

        /// <summary>What earlier rounds loaded of one child class by parent id: each parent's children and the select they were read under.</summary>
        /// <typeparam name="TChild">The child class.</typeparam>
        /// <typeparam name="TK">The parent key's type.</typeparam>
        private sealed class ChildrenCache<TChild, TK>
            where TK : notnull
        {
            /// <summary>Each parent's children and select.</summary>
            private readonly Dictionary<TK, (IReadOnlyList<TChild> Children, SelectSet Select)> _entries = new(KeySets.Comparer<TK>());

            /// <summary>Whether a parent's children were read under a covering select.</summary>
            /// <param name="key">The parent id.</param>
            /// <param name="select">The select.</param>
            /// <returns>True when they were.</returns>
            public bool Covers(TK key, SelectSet select)
            {
                return _entries.TryGetValue(key, out (IReadOnlyList<TChild> Children, SelectSet Select) entry) && entry.Select.Covers(select);
            }

            /// <summary>The select a parent's children were read under, or null when none were read.</summary>
            /// <param name="key">The parent id.</param>
            /// <returns>The select.</returns>
            public SelectSet? SelectOf(TK key)
            {
                return _entries.TryGetValue(key, out (IReadOnlyList<TChild> Children, SelectSet Select) entry) ? entry.Select : null;
            }

            /// <summary>Caches a parent's children.</summary>
            /// <param name="key">The parent id.</param>
            /// <param name="children">The children, in key order.</param>
            /// <param name="select">The select they were read under.</param>
            public void Store(TK key, IReadOnlyList<TChild> children, SelectSet select)
            {
                _entries[key] = (children, select);
            }

            /// <summary>The children of each parent.</summary>
            /// <param name="keys">The parent ids, each cached.</param>
            /// <returns>The children by parent id.</returns>
            public Dictionary<TK, IReadOnlyList<TChild>> Answer(IEnumerable<TK> keys)
            {
                Dictionary<TK, IReadOnlyList<TChild>> answer = new(KeySets.Comparer<TK>());
                foreach (TK key in keys)
                {
                    answer[key] = _entries.TryGetValue(key, out (IReadOnlyList<TChild> Children, SelectSet Select) entry) ? entry.Children : [];
                }

                return answer;
            }
        }

        /// <summary>What earlier rounds loaded of one tree: each node's parent, and the ids that found no node.</summary>
        /// <typeparam name="TK">The key's type.</typeparam>
        private sealed class TreeCache<TK>
            where TK : notnull
        {
            /// <summary>Each node's parent; a root has none.</summary>
            private readonly Dictionary<TK, (bool IsRoot, TK? Parent)> _nodes = new(KeySets.Comparer<TK>());

            /// <summary>The ids that found no node.</summary>
            private readonly HashSet<TK> _missing = new(KeySets.Comparer<TK>());

            /// <summary>Whether an id's whole chain is known: the id is missing, or every node up to a root is cached.</summary>
            /// <param name="key">The id.</param>
            /// <returns>True when it is.</returns>
            public bool Covers(TK key)
            {
                if (_missing.Contains(key))
                {
                    return true;
                }

                HashSet<TK> seen = new(KeySets.Comparer<TK>());
                TK current = key;
                while (_nodes.TryGetValue(current, out (bool IsRoot, TK? Parent) node))
                {
                    // A revisited node is a cycle in the stored rows: the chain is as known as it gets.
                    if (node.IsRoot || !seen.Add(current))
                    {
                        return true;
                    }

                    current = node.Parent!;
                }

                return false;
            }

            /// <summary>Caches a node.</summary>
            /// <param name="key">The node's id.</param>
            /// <param name="parent">Its parent's id; ignored for a root.</param>
            /// <param name="isRoot">Whether it is a root.</param>
            public void Store(TK key, TK? parent, bool isRoot)
            {
                _nodes[key] = (isRoot, parent);
                _ = _missing.Remove(key);
            }

            /// <summary>Marks an id that found no node; an id cached as a node is left as it is.</summary>
            /// <param name="key">The id.</param>
            public void MarkMissing(TK key)
            {
                if (!_nodes.ContainsKey(key))
                {
                    _ = _missing.Add(key);
                }
            }

            /// <summary>The ancestor chain of each id that is a node: its parent first, up to its root.</summary>
            /// <param name="keys">The ids.</param>
            /// <returns>The chains by id; an id that found no node is absent, a root's chain is empty.</returns>
            public Dictionary<TK, IReadOnlyList<TK>> Answer(IEnumerable<TK> keys)
            {
                Dictionary<TK, IReadOnlyList<TK>> answer = new(KeySets.Comparer<TK>());
                foreach (TK key in keys)
                {
                    if (!_nodes.ContainsKey(key))
                    {
                        continue;
                    }

                    // --- Climb until a root, a node the rows do not reach, or a revisit ---
                    List<TK> chain = [];
                    HashSet<TK> seen = new(KeySets.Comparer<TK>()) { key };
                    TK current = key;
                    while (_nodes.TryGetValue(current, out (bool IsRoot, TK? Parent) node) && !node.IsRoot && seen.Add(node.Parent!))
                    {
                        chain.Add(node.Parent!);
                        current = node.Parent!;
                    }

                    answer[key] = chain;
                }

                return answer;
            }
        }

        /// <summary>The key types a load accepts, their comparison, and their key-set tables.</summary>
        private static class KeySets
        {
            /// <summary>The comparison of a key type: strings ignore case, as the database's collation does.</summary>
            /// <typeparam name="TK">The key's type.</typeparam>
            /// <returns>The comparer.</returns>
            public static IEqualityComparer<TK> Comparer<TK>()
            {
                return typeof(TK) == typeof(string) ? (IEqualityComparer<TK>)StringComparer.OrdinalIgnoreCase : EqualityComparer<TK>.Default;
            }

            /// <summary>Refuses a key type that is not the property's, or that no key-set table holds.</summary>
            /// <typeparam name="TK">The key's type.</typeparam>
            /// <param name="property">The key property.</param>
            /// <param name="parameterName">The caller's parameter.</param>
            /// <exception cref="ArgumentException">The type is refused.</exception>
            public static void EnsureKeyType<TK>(PropertyMetadata property, string parameterName)
            {
                Type held = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (held != typeof(TK))
                {
                    throw new ArgumentException($"The property {property.Name} holds {held.Name} values, so its keys are {held.Name}, not {typeof(TK).Name}.", parameterName);
                }

                if (!(held == typeof(int) || held == typeof(long) || held == typeof(short) || held == typeof(byte)
                    || held == typeof(Guid) || held == typeof(string) || held == typeof(DateOnly)))
                {
                    throw new ArgumentException($"The property {property.Name} holds {held.Name} values, which no key-set table holds; a load keys on integers, GUIDs, strings or dates.", parameterName);
                }
            }

            /// <summary>The distinct keys of a request, in order.</summary>
            /// <typeparam name="TK">The key's type.</typeparam>
            /// <param name="values">The requested keys.</param>
            /// <param name="parameterName">The caller's parameter.</param>
            /// <returns>The keys.</returns>
            /// <exception cref="ArgumentException">A key is null.</exception>
            public static TK[] Distinct<TK>(IReadOnlyList<TK> values, string parameterName)
            {
                return values.Any(static value => value is null)
                    ? throw new ArgumentException("A load's keys hold no null.", parameterName)
                    : [.. values.Distinct(Comparer<TK>())];
            }

            /// <summary>Appends the key-set table holding a load's keys.</summary>
            /// <typeparam name="TK">The key's type.</typeparam>
            /// <param name="batch">The composing batch.</param>
            /// <param name="keys">The keys, distinct.</param>
            /// <returns>The table, whose <c>Id</c> column holds the keys.</returns>
            public static SqlIdentifier Table<TK>(IDataBatch batch, IReadOnlyList<TK> keys)
            {
                return keys switch
                {
                    IReadOnlyList<int> ints => batch.Tvp([.. ints.Select(static id => new IdList { Id = id })]),
                    IReadOnlyList<long> longs => batch.Tvp([.. longs.Select(static id => new BigIdList { Id = id })]),
                    IReadOnlyList<short> shorts => batch.Tvp([.. shorts.Select(static id => new IdList { Id = id })]),
                    IReadOnlyList<byte> bytes => batch.Tvp([.. bytes.Select(static id => new IdList { Id = id })]),
                    IReadOnlyList<Guid> guids => batch.Tvp([.. guids.Select(static id => new GuidList { Id = id })]),
                    IReadOnlyList<string> strings => batch.Tvp([.. strings.Select(static id => new StringList { Id = id })]),
                    IReadOnlyList<DateOnly> dates => batch.Tvp([.. dates.Select(static id => new DateList { Id = id })]),
                    _ => throw new ArgumentException($"No key-set table holds {typeof(TK).Name} keys.", nameof(keys)),
                };
            }

            /// <summary>A key read from a row set's buffer, as the key's type.</summary>
            /// <typeparam name="TK">The key's type.</typeparam>
            /// <param name="value">The value read.</param>
            /// <returns>The key.</returns>
            public static TK Convert<TK>(object value)
            {
                return value is TK key ? key : (TK)System.Convert.ChangeType(value, typeof(TK), CultureInfo.InvariantCulture);
            }
        }
    }
}
