// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Details
{
    // The projection is spec 0015 §5.3 step 2 with spec 0016's related projection; what children
    // point at is loaded through a grouped read per collection and leading navigation, keyed to the
    // visible ids, because an entity query selects declared columns only below its root (spec 0012
    // §5.2); one read per navigation groups by that navigation's targets alone.
    /// <summary>
    ///     What a stack's details read selects under one tenant schema: every mapped property of the
    ///     root, every child collection at any depth, and the related projection of every navigation
    ///     its details expansion follows, the root's on its own select and each collection's on
    ///     grouped reads of its own, one per leading navigation. Built once per stack and schema. A
    ///     singleton.
    /// </summary>
    /// <param name="registry">The stack registry, which holds each entity's related projection.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    internal sealed class DetailsProjections(IStackRegistry registry, IEntityMetadataProvider metadata)
    {
        /// <summary>The projections built, by stack and schema instance.</summary>
        private readonly ConcurrentDictionary<(StackDescriptor Stack, QueryexSchema Schema), DetailsProjection> _built = new();

        /// <summary>The projection of a stack's details read under a tenant schema.</summary>
        /// <param name="stack">The stack.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="declared">The root's properties the schema declares, in declaration order.</param>
        /// <returns>The projection.</returns>
        public DetailsProjection For(StackDescriptor stack, QueryexSchema schema, IReadOnlyList<PropertyMetadata> declared)
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(declared);
            return _built.GetOrAdd((stack, schema), static (key, state) => state.Self.Build(key.Stack, key.Schema, state.Declared), (Self: this, Declared: declared));
        }

        /// <summary>Builds a stack's projection under a schema.</summary>
        /// <param name="stack">The stack.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="declared">The root's declared properties.</param>
        /// <returns>The projection.</returns>
        private DetailsProjection Build(StackDescriptor stack, QueryexSchema schema, IReadOnlyList<PropertyMetadata> declared)
        {
            EntityMetadata entity = metadata.Get(stack.EntityType);
            EntityDescriptor root = schema.FindEntity(entity.Name)
                ?? throw new InvalidOperationException($"The tenant's schema has no entity for the stack {stack.Resource}.");

            // --- The root's own columns, then every collection at any depth, parents first ---
            SelectItems rootSelect = new(declared.Select(static property => property.Name));
            List<string> children = [];
            Collections(entity, prefix: string.Empty, children);

            // --- The expansion: each path's leading collections pick its level, its navigations what is read there ---
            Dictionary<string, Level> levels = new(StringComparer.Ordinal);
            foreach (string path in stack.DetailsExpand)
            {
                Expand(path.Split('.', StringSplitOptions.TrimEntries), entity, root, stack.Limits.MaxExpandDepth, schema, rootSelect, levels);
            }

            // --- Each level below the root: one grouped read per leading navigation, so each reads the
            // distinct targets of that navigation alone, however many rows point at them ---
            List<DetailsTargets> targets = [];
            foreach ((string collection, Level level) in levels)
            {
                foreach (SelectItems select in level.Navigations.Values.Where(static select => select.Count > 0))
                {
                    targets.Add(new DetailsTargets(collection, level.Child, level.Root, level.KeyPath, string.Join(", ", select)));
                }
            }

            return new DetailsProjection(string.Join(", ", rootSelect), children, targets);
        }

        /// <summary>Adds an expansion path's projection to the root's select or to the grouped read of its level's leading navigation.</summary>
        /// <param name="segments">The path's segments.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="root">The stack's schema entity.</param>
        /// <param name="maxDepth">The deepest a path's navigations reach.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="rootSelect">The root's select.</param>
        /// <param name="levels">The levels below the root, by collection path.</param>
        private void Expand(
            string[] segments, EntityMetadata entity, EntityDescriptor root, int maxDepth, QueryexSchema schema, SelectItems rootSelect, Dictionary<string, Level> levels)
        {
            // --- The leading collections: the level the path's navigations start from ---
            EntityMetadata at = entity;
            List<ChildCollectionMetadata> walked = [];
            int index = 0;
            for (; index < segments.Length; index++)
            {
                if (at.Children.FirstOrDefault(child => string.Equals(child.Property, segments[index], StringComparison.Ordinal)) is not ChildCollectionMetadata collection)
                {
                    break;
                }

                walked.Add(collection);
                at = collection.Child;
            }

            // --- The navigations from there, up to the depth ceiling; a collection past a navigation ends the path ---
            List<ReferenceMetadata> navigations = [];
            for (; index < segments.Length && navigations.Count < maxDepth; index++)
            {
                if (at.References.FirstOrDefault(reference => string.Equals(reference.Navigation, segments[index], StringComparison.Ordinal)) is not ReferenceMetadata reference)
                {
                    break;
                }

                navigations.Add(reference);
                at = reference.Target;
            }

            if (navigations.Count == 0)
            {
                return;
            }

            // --- Where the select items go: the root's own select, or the grouped read of the level's leading navigation ---
            SelectItems select;
            EntityDescriptor from;
            if (walked.Count == 0)
            {
                select = rootSelect;
                from = root;
            }
            else
            {
                string key = string.Join('.', walked.Select(static collection => collection.Property));
                if (!levels.TryGetValue(key, out Level? level))
                {
                    EntityMetadata child = walked[^1].Child;
                    if (schema.FindEntity(child.Name) is not EntityDescriptor childRoot || KeyPath(walked) is not string keyPath)
                    {
                        return;
                    }

                    levels[key] = level = new Level(child, childRoot, keyPath);
                }

                select = level.For(navigations[0].Navigation);
                from = level.Root;
            }

            // --- Each navigation's projection, and the key of the next step so its target links to it ---
            EntityDescriptor? target = from;
            for (int step = 0; step < navigations.Count && target is not null; step++)
            {
                string prefix = string.Join('.', navigations.Take(step + 1).Select(static reference => reference.Navigation));
                target = SchemaLookup.Navigation(target, navigations[step].Navigation)?.Target;
                if (target is null)
                {
                    break;
                }

                foreach (string column in Projected(navigations[step].Target, target))
                {
                    select.Add($"{prefix}.{column}");
                }

                if (step + 1 < navigations.Count && SchemaLookup.Property(target, navigations[step + 1].ForeignKey.Name) is not null)
                {
                    select.Add($"{prefix}.{navigations[step + 1].ForeignKey.Name}");
                }
            }
        }

        /// <summary>The columns of an entity's related projection its schema entity declares, its key first.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="descriptor">Its schema entity.</param>
        /// <returns>The columns.</returns>
        private IEnumerable<string> Projected(EntityMetadata entity, EntityDescriptor descriptor)
        {
            IReadOnlySet<string> related = registry.Find(entity.Name)?.RelatedSelect
                ?? registry.FindChildQuery(entity.Name)?.RelatedSelect
                ?? StackDescriptorBuilder.RelatedSelect(entity);
            yield return entity.Key.Name;
            foreach (string column in related)
            {
                // A multilingual twin the tenant's languages leave out is not in the schema.
                if (!string.Equals(column, entity.Key.Name, StringComparison.Ordinal) && SchemaLookup.Property(descriptor, column) is not null)
                {
                    yield return column;
                }
            }
        }

        /// <summary>
        ///     The path from a collection's rows to the root's key: each level's navigation to its
        ///     parent, then the first level's parent key.
        /// </summary>
        /// <param name="walked">The collections from the root down.</param>
        /// <returns>The path; null when a level has no navigation to its parent.</returns>
        private static string? KeyPath(List<ChildCollectionMetadata> walked)
        {
            List<string> segments = [];
            for (int level = walked.Count - 1; level > 0; level--)
            {
                ChildCollectionMetadata collection = walked[level];
                if (collection.Child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name) is not ReferenceMetadata parent)
                {
                    return null;
                }

                segments.Add(parent.Navigation);
            }

            segments.Add(walked[0].ParentKey.Name);
            return string.Join('.', segments);
        }

        /// <summary>Lists an entity's child collections at any depth, each as its dotted path, parents first.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="prefix">The path to the entity's collections.</param>
        /// <param name="paths">Receives the paths.</param>
        private static void Collections(EntityMetadata entity, string prefix, List<string> paths)
        {
            foreach (ChildCollectionMetadata collection in entity.Children)
            {
                string path = prefix + collection.Property;
                paths.Add(path);
                Collections(collection.Child, path + ".", paths);
            }
        }

        /// <summary>One level below the root: its rows' entity and what each of their leading navigations reads.</summary>
        /// <param name="child">The level's entity.</param>
        /// <param name="root">Its schema entity.</param>
        /// <param name="keyPath">The path from its rows to the root's key.</param>
        private sealed class Level(EntityMetadata child, EntityDescriptor root, string keyPath)
        {
            /// <summary>The level's entity.</summary>
            public EntityMetadata Child { get; } = child;

            /// <summary>Its schema entity, each grouped read's root.</summary>
            public EntityDescriptor Root { get; } = root;

            /// <summary>The path from its rows to the root's key.</summary>
            public string KeyPath { get; } = keyPath;

            /// <summary>Each grouped read's items, by the leading navigation it reads through, in first-seen order.</summary>
            public OrderedDictionary<string, SelectItems> Navigations { get; } = new(StringComparer.Ordinal);

            /// <summary>The grouped read of a leading navigation, added on first use.</summary>
            /// <param name="navigation">The navigation.</param>
            /// <returns>Its items.</returns>
            public SelectItems For(string navigation)
            {
                if (!Navigations.TryGetValue(navigation, out SelectItems? select))
                {
                    Navigations[navigation] = select = new SelectItems([]);
                }

                return select;
            }
        }

        /// <summary>Select items in first-added order, a repeat ignored whatever its case.</summary>
        private sealed class SelectItems : IEnumerable<string>
        {
            /// <summary>The items, in order.</summary>
            private readonly List<string> _items = [];

            /// <summary>The items held, ignoring case.</summary>
            private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Creates the items.</summary>
            /// <param name="items">The first items.</param>
            public SelectItems(IEnumerable<string> items)
            {
                foreach (string item in items)
                {
                    Add(item);
                }
            }

            /// <summary>The number of items.</summary>
            public int Count => _items.Count;

            /// <summary>Adds an item unless it is held.</summary>
            /// <param name="item">The item.</param>
            public void Add(string item)
            {
                if (_held.Add(item))
                {
                    _items.Add(item);
                }
            }

            /// <inheritdoc />
            public IEnumerator<string> GetEnumerator()
            {
                return _items.GetEnumerator();
            }

            /// <inheritdoc />
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    /// <summary>What a stack's details read selects under one tenant schema.</summary>
    /// <param name="RootSelect">The root's select: every declared property, then the related projection of each navigation the root's expansion follows.</param>
    /// <param name="Children">Every child collection at any depth, dotted, parents first.</param>
    /// <param name="Targets">One grouped read per collection level and leading navigation its rows point through.</param>
    internal sealed record DetailsProjection(string RootSelect, IReadOnlyList<string> Children, IReadOnlyList<DetailsTargets> Targets);

    /// <summary>The grouped read of the distinct related entities one collection level points at through one leading navigation.</summary>
    /// <param name="Collection">The collection's dotted path from the root.</param>
    /// <param name="Child">The level's entity.</param>
    /// <param name="Root">Its schema entity, the read's root.</param>
    /// <param name="KeyPath">The path from the level's rows to the root's key, which the visible ids restrict.</param>
    /// <param name="Select">The read's items: the related projections reached through the navigation, which group it.</param>
    internal sealed record DetailsTargets(string Collection, EntityMetadata Child, EntityDescriptor Root, string KeyPath, string Select);
}
