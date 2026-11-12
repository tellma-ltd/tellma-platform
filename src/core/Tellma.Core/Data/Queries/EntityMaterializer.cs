// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // The routing rules are spec 0012 §11.5's.
    /// <summary>
    ///     Turns an entity query's row sets into entities: a depth-1 column onto the statement's own
    ///     entity, a deeper column onto a related instance kept apart from it, a JSON column by its
    ///     name, and child rows onto their parents' collections.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A related instance is keyed by its entity's name and its id, deduplicated across every
    ///         row and statement of the query, and never assigned to a navigation property, so the
    ///         result has no cycles. It carries only the columns that were selected through it, which
    ///         its set's projection records.
    ///     </para>
    ///     <para>
    ///         A property no column reads keeps the value a fresh instance has. Thread-safe: it holds
    ///         only caches.
    ///     </para>
    /// </remarks>
    internal sealed class EntityMaterializer
    {
        /// <summary>The generic <see cref="RelatedEntities.Add{T}" />.</summary>
        private static readonly MethodInfo AddRelatedMethod = typeof(RelatedEntities).GetMethod(nameof(RelatedEntities.Add))!;

        /// <summary>A constructor per entity class.</summary>
        private readonly ConcurrentDictionary<Type, Func<object>> _factories = new();

        /// <summary>A collection setter per parent class and collection.</summary>
        private readonly ConcurrentDictionary<(Type Parent, string Property), Action<object, IReadOnlyList<object>>> _collections = new();

        /// <summary>The closed <see cref="RelatedEntities.Add{T}" /> per entity class.</summary>
        private readonly ConcurrentDictionary<Type, Action<RelatedEntities, string, IReadOnlyList<object>, IReadOnlySet<string>>> _adders = new();

        /// <summary>
        ///     Reads the instances of a statement's own entity from its rows, in row order, routing the
        ///     deeper columns to the related instances.
        /// </summary>
        /// <param name="map">The schema the statement compiled against.</param>
        /// <param name="entity">The statement's entity.</param>
        /// <param name="rows">The statement's rows.</param>
        /// <param name="related">The query's related instances, added to.</param>
        /// <returns>The instances, and the names of the properties the statement's own columns set.</returns>
        /// <exception cref="InvalidOperationException">A column's path or name matches nothing of the entity.</exception>
        public Materialized Read(QueryexSchemaMap map, EntityMetadata entity, QueryRowSet rows, RelatedCollector related)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(related);

            // --- Route every column: onto the entity, or onto the related instance of its path's prefix ---
            List<ColumnTarget> own = [];
            Dictionary<string, RelatedTarget> groups = new(StringComparer.OrdinalIgnoreCase);
            for (int column = 0; column < rows.Columns.Count; column++)
            {
                QueryColumn described = rows.Columns[column];
                if (described.Path is not { Count: > 0 } path)
                {
                    // A JSON column: no path, set by its column name.
                    PropertyMetadata json = map.JsonColumns(entity).FirstOrDefault(property => string.Equals(property.Column, described.Name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"The column '{described.Name}' has no path and is no JSON column of the entity {entity.Name}.");
                    own.Add(new ColumnTarget(json, Cells.Reader(rows, column)));
                    continue;
                }

                if (path.Count == 1)
                {
                    own.Add(new ColumnTarget(Property(entity, path[0]), Cells.Reader(rows, column)));
                    continue;
                }

                // --- A deeper column: resolve the prefix's entity through the schema's navigations ---
                string prefix = string.Join('.', path.Take(path.Count - 1));
                if (!groups.TryGetValue(prefix, out RelatedTarget? group))
                {
                    EntityDescriptor target = map.Descriptor(entity);
                    foreach (string segment in path.Take(path.Count - 1))
                    {
                        target = target.Navigations.FirstOrDefault(navigation => string.Equals(navigation.Name, segment, StringComparison.OrdinalIgnoreCase))?.Target
                            ?? throw new InvalidOperationException($"The column '{described.Name}' follows the navigation '{segment}', which the entity {target.Name} does not have.");
                    }

                    groups[prefix] = group = new RelatedTarget(map.Metadata(target));
                }

                PropertyMetadata property = Property(group.Entity, path[^1]);
                ColumnTarget routed = new(property, Cells.Reader(rows, column));
                if (string.Equals(property.Name, group.Entity.Key.Name, StringComparison.Ordinal))
                {
                    group.Key = routed;
                }

                group.Columns.Add(routed);
            }

            // A related instance is found by its id; a prefix whose key was not selected cannot be keyed.
            foreach ((string prefix, RelatedTarget group) in groups)
            {
                if (group.Key is null)
                {
                    throw new InvalidOperationException($"The related columns under '{prefix}' are selected without '{prefix}.{group.Entity.Key.Name}', which keys them.");
                }
            }

            // --- The rows ---
            Func<object> create = Factory(entity.ClrType);
            object[] instances = new object[rows.RowCount];
            for (int row = 0; row < rows.RowCount; row++)
            {
                object instance = create();
                foreach (ColumnTarget column in own)
                {
                    column.Assign(instance, row);
                }

                foreach (RelatedTarget group in groups.Values)
                {
                    // No key, no row: an optional step that found nothing.
                    if (group.Key!.Value(row) is not object id)
                    {
                        continue;
                    }

                    (object instance, HashSet<string> carried) target = related.Find(group.Entity, id, Factory(group.Entity.ClrType));
                    foreach (ColumnTarget column in group.Columns)
                    {
                        if (target.carried.Add(column.Property.Name))
                        {
                            column.Assign(target.instance, row);
                        }
                    }
                }

                instances[row] = instance;
            }

            return new Materialized(instances, own.Select(static column => column.Property.Name).ToHashSet(StringComparer.Ordinal));
        }

        /// <summary>
        ///     Stitches child instances onto their parents' collection, in the order the children
        ///     came; every parent gets the collection, an empty one when it has no children. A child
        ///     whose parent is not among the parents is dropped.
        /// </summary>
        /// <param name="parent">The parents' entity.</param>
        /// <param name="collection">The collection.</param>
        /// <param name="parents">The parents.</param>
        /// <param name="children">The children, in the child query's order.</param>
        public void Stitch(EntityMetadata parent, ChildCollectionMetadata collection, IReadOnlyList<object> parents, IReadOnlyList<object> children)
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(parents);
            ArgumentNullException.ThrowIfNull(children);

            // --- The children by their parent's key ---
            Dictionary<object, List<object>> byParent = [];
            foreach (object child in children)
            {
                if (collection.ParentKey.Getter(child) is not object parentKey)
                {
                    continue;
                }

                if (!byParent.TryGetValue(parentKey, out List<object>? siblings))
                {
                    byParent[parentKey] = siblings = [];
                }

                siblings.Add(child);
            }

            // --- Each parent's collection ---
            foreach (object instance in parents)
            {
                Action<object, IReadOnlyList<object>> assign = _collections.GetOrAdd((instance.GetType(), collection.Property), static key => CollectionSetter(key.Parent, key.Property));
                object? key = parent.Key.Getter(instance);
                assign(instance, key is not null && byParent.TryGetValue(key, out List<object>? stitched) ? stitched : []);
            }
        }

        /// <summary>
        ///     Adds whole instances of an entity to the related instances, such as a tree page's
        ///     ancestors, each carrying the given columns.
        /// </summary>
        /// <param name="related">The query's related instances.</param>
        /// <param name="entity">The instances' entity.</param>
        /// <param name="instances">The instances.</param>
        /// <param name="carried">The property names each carries.</param>
        public static void AddWhole(RelatedCollector related, EntityMetadata entity, IReadOnlyList<object> instances, IReadOnlySet<string> carried)
        {
            ArgumentNullException.ThrowIfNull(related);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(instances);
            ArgumentNullException.ThrowIfNull(carried);
            foreach (object instance in instances)
            {
                if (entity.Key.Getter(instance) is object id)
                {
                    related.Merge(entity, id, instance, carried);
                }
            }
        }

        /// <summary>The related instances gathered so far, as typed sets with their projections.</summary>
        /// <param name="related">The query's related instances.</param>
        /// <returns>The sets.</returns>
        public RelatedEntities Build(RelatedCollector related)
        {
            ArgumentNullException.ThrowIfNull(related);
            RelatedEntities sets = new();
            foreach (RelatedCollector.EntitySet set in related.Sets)
            {
                // The projection is what every instance carries.
                HashSet<string> projection = new(set.Instances.Values.First().Carried, StringComparer.Ordinal);
                foreach ((object _, HashSet<string> carried) in set.Instances.Values)
                {
                    projection.IntersectWith(carried);
                }

                Action<RelatedEntities, string, IReadOnlyList<object>, IReadOnlySet<string>> add = _adders.GetOrAdd(set.Entity.ClrType, static type => Adder(type));
                add(sets, set.Entity.Name, [.. set.Instances.Values.Select(static entry => entry.Instance)], projection);
            }

            return sets;
        }

        /// <summary>The property of an entity a path segment names.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The property.</returns>
        private static PropertyMetadata Property(EntityMetadata entity, string name)
        {
            return entity.Properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The column '{name}' names no property of the entity {entity.Name}; a materialised column is a mapped property.");
        }

        /// <summary>Compiles the setter of a child collection: a new list of the property's element type, filled and assigned.</summary>
        /// <param name="parent">The parent class.</param>
        /// <param name="property">The collection's property name.</param>
        /// <returns>The setter.</returns>
        private static Action<object, IReadOnlyList<object>> CollectionSetter(Type parent, string property)
        {
            PropertyInfo info = parent.GetProperty(property)
                ?? throw new InvalidOperationException($"The class {parent.Name} has no collection property {property}.");
            Type element = info.PropertyType.IsGenericType ? info.PropertyType.GetGenericArguments()[0] : typeof(object);
            Type list = typeof(List<>).MakeGenericType(element);
            return (instance, children) =>
            {
                var filled = (IList)Activator.CreateInstance(list, children.Count)!;
                foreach (object child in children)
                {
                    _ = filled.Add(child);
                }

                info.SetValue(instance, filled);
            };
        }

        /// <summary>The closed <see cref="RelatedEntities.Add{T}" /> of a class, taking untyped instances.</summary>
        /// <param name="type">The class.</param>
        /// <returns>The adder.</returns>
        private static Action<RelatedEntities, string, IReadOnlyList<object>, IReadOnlySet<string>> Adder(Type type)
        {
            MethodInfo add = AddRelatedMethod.MakeGenericMethod(type);
            return (sets, name, instances, projection) =>
            {
                var typed = Array.CreateInstance(type, instances.Count);
                for (int index = 0; index < instances.Count; index++)
                {
                    typed.SetValue(instances[index], index);
                }

                _ = add.Invoke(sets, [name, typed, projection]);
            };
        }

        /// <summary>The compiled parameterless constructor of an entity class.</summary>
        /// <param name="type">The class.</param>
        /// <returns>The constructor.</returns>
        private Func<object> Factory(Type type)
        {
            return _factories.GetOrAdd(type, static type => Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(type), typeof(object))).Compile());
        }

        /// <summary>One column routed onto a property: reads the row's cell and converts it to the property's type.</summary>
        /// <param name="property">The property.</param>
        /// <param name="read">Reads the column's cell of a row; null for SQL <c>NULL</c>.</param>
        private sealed class ColumnTarget(PropertyMetadata property, Func<int, object?> read)
        {
            /// <summary>Converts a cell to the property's type.</summary>
            private readonly Func<object, object?> _convert = Cells.Converter(property.ClrType);

            /// <summary>The property.</summary>
            public PropertyMetadata Property { get; } = property;

            /// <summary>Reads the column's cell of a row.</summary>
            public Func<int, object?> Value { get; } = read;

            /// <summary>Sets the property from a row's cell; a null cell leaves a non-nullable value type at its default.</summary>
            /// <param name="instance">The instance.</param>
            /// <param name="row">The row.</param>
            public void Assign(object instance, int row)
            {
                object? value = Value(row);
                if (value is null)
                {
                    if (!Property.ClrType.IsValueType || Nullable.GetUnderlyingType(Property.ClrType) is not null)
                    {
                        Property.Setter(instance, null);
                    }

                    return;
                }

                Property.Setter(instance, _convert(value));
            }
        }

        /// <summary>The columns of one related path prefix, and the one that keys its instances.</summary>
        /// <param name="entity">The prefix's entity.</param>
        private sealed class RelatedTarget(EntityMetadata entity)
        {
            /// <summary>The prefix's entity.</summary>
            public EntityMetadata Entity { get; } = entity;

            /// <summary>The prefix's columns.</summary>
            public List<ColumnTarget> Columns { get; } = [];

            /// <summary>The column of the entity's key, once routed.</summary>
            public ColumnTarget? Key { get; set; }
        }

        /// <summary>Reads typed cells and converts them to property types.</summary>
        internal static class Cells
        {
            /// <summary>A reader of one column's cells, boxing each typed value; null for SQL <c>NULL</c>.</summary>
            /// <param name="rows">The row set.</param>
            /// <param name="column">The column.</param>
            /// <returns>The reader.</returns>
            public static Func<int, object?> Reader(QueryRowSet rows, int column)
            {
                Array buffer = rows.GetBuffer(column);
                return buffer switch
                {
                    int[] values => row => rows.IsNull(column, row) ? null : values[row],
                    long[] values => row => rows.IsNull(column, row) ? null : values[row],
                    short[] values => row => rows.IsNull(column, row) ? null : values[row],
                    byte[] values => row => rows.IsNull(column, row) ? null : values[row],
                    decimal[] values => row => rows.IsNull(column, row) ? null : values[row],
                    double[] values => row => rows.IsNull(column, row) ? null : values[row],
                    bool[] values => row => rows.IsNull(column, row) ? null : values[row],
                    string?[] values => row => rows.IsNull(column, row) ? null : values[row],
                    Guid[] values => row => rows.IsNull(column, row) ? null : values[row],
                    DateOnly[] values => row => rows.IsNull(column, row) ? null : values[row],
                    DateTime[] values => row => rows.IsNull(column, row) ? null : values[row],
                    DateTimeOffset[] values => row => rows.IsNull(column, row) ? null : values[row],
                    _ => row => rows.IsNull(column, row) ? null : buffer.GetValue(row),
                };
            }

            /// <summary>
            ///     A converter from a cell to a property type: an enum from its name, a date from a
            ///     date and time, another number type through its invariant conversion, and anything
            ///     already of the type as it is.
            /// </summary>
            /// <param name="propertyType">The property's type.</param>
            /// <returns>The converter.</returns>
            public static Func<object, object?> Converter(Type propertyType)
            {
                Type target = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
                if (target.IsEnum)
                {
                    // Stored by member name (the model's enum convention).
                    return value => value is string name ? Enum.Parse(target, name, ignoreCase: false) : Enum.ToObject(target, value);
                }

                return value => target.IsInstanceOfType(value) ? value
                    : target == typeof(DateOnly) && value is DateTime moment ? DateOnly.FromDateTime(moment)
                    : Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>One statement's instances, and the names of the properties its own columns set on each.</summary>
    /// <param name="Instances">The instances, in row order.</param>
    /// <param name="Carried">The names of the properties set from the statement's own columns.</param>
    internal sealed record Materialized(IReadOnlyList<object> Instances, IReadOnlySet<string> Carried);

    /// <summary>
    ///     The related instances one entity query gathers: per entity, each instance by its id with the
    ///     property names it carries, in the order first met.
    /// </summary>
    internal sealed class RelatedCollector
    {
        /// <summary>The sets, by entity name.</summary>
        private readonly Dictionary<string, EntitySet> _sets = new(StringComparer.Ordinal);

        /// <summary>The sets, in the order first met.</summary>
        public IEnumerable<EntitySet> Sets => _sets.Values;

        /// <summary>The instance of an entity with an id, created when it is new.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">The id.</param>
        /// <param name="create">Creates a fresh instance.</param>
        /// <returns>The instance and the property names it carries so far.</returns>
        public (object Instance, HashSet<string> Carried) Find(EntityMetadata entity, object id, Func<object> create)
        {
            EntitySet set = SetOf(entity);
            if (!set.Instances.TryGetValue(id, out (object Instance, HashSet<string> Carried) entry))
            {
                entry = (create(), new HashSet<string>(StringComparer.Ordinal));
                set.Instances.Add(id, entry);
            }

            return entry;
        }

        /// <summary>
        ///     Adds a whole instance; when one with its id is already held, the columns the held one
        ///     lacks are copied onto it.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="id">The id.</param>
        /// <param name="instance">The instance.</param>
        /// <param name="carried">The property names it carries.</param>
        public void Merge(EntityMetadata entity, object id, object instance, IReadOnlySet<string> carried)
        {
            EntitySet set = SetOf(entity);
            if (!set.Instances.TryGetValue(id, out (object Instance, HashSet<string> Carried) held))
            {
                set.Instances.Add(id, (instance, new HashSet<string>(carried, StringComparer.Ordinal)));
                return;
            }

            foreach (PropertyMetadata property in entity.Properties)
            {
                if (carried.Contains(property.Name) && held.Carried.Add(property.Name))
                {
                    property.Setter(held.Instance, property.Getter(instance));
                }
            }
        }

        /// <summary>The set of an entity, created when it is new.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The set.</returns>
        private EntitySet SetOf(EntityMetadata entity)
        {
            if (!_sets.TryGetValue(entity.Name, out EntitySet? set))
            {
                _sets[entity.Name] = set = new EntitySet(entity);
            }

            return set;
        }

        /// <summary>One entity's related instances.</summary>
        /// <param name="entity">The entity.</param>
        internal sealed class EntitySet(EntityMetadata entity)
        {
            /// <summary>The entity.</summary>
            public EntityMetadata Entity { get; } = entity;

            /// <summary>Each instance with the property names it carries, by id, in the order first met.</summary>
            public OrderedDictionary<object, (object Instance, HashSet<string> Carried)> Instances { get; } = [];
        }
    }
}
