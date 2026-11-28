// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    /// <summary>
    ///     The query surfaces of the composition's stacks and child query surfaces, the plain surface
    ///     of every other mapped entity a row source reads as a lookup target or a dependent, and
    ///     what the traversal rule knows of every entity a path can reach, built once on first use
    ///     with no tenant scope. A singleton.
    /// </summary>
    /// <param name="registry">The stack registry.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    internal sealed class QuerySurfaces(IStackRegistry registry, IEntityMetadataProvider metadata)
    {
        /// <summary>The names the search convention reads when a root declares no searchable column, in order.</summary>
        private static readonly string[] ConventionColumns = ["Name", "Name2", "Name3", "Code"];

        /// <summary>The surfaces and the facts, built on first use.</summary>
        private readonly Lazy<Contents> _contents = new(
            () => Build(registry ?? throw new ArgumentNullException(nameof(registry)), metadata ?? throw new ArgumentNullException(nameof(metadata))),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>A stack's surface, by resource.</summary>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The surface; null when no stack has the resource.</returns>
        public QuerySurface? Stack(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return _contents.Value.Stacks.GetValueOrDefault(resource);
        }

        /// <summary>A child query surface, by resource.</summary>
        /// <param name="resource">The surface's resource.</param>
        /// <returns>The surface; null when no child query surface has the resource.</returns>
        public QuerySurface? Child(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return _contents.Value.Children.GetValueOrDefault(resource);
        }

        /// <summary>
        ///     The surface a statement rooted on an entity plans on: the entity's stack's, else its
        ///     child query surface's, else a plain surface with no named filter, the search
        ///     convention, the key as its default select and the default ceilings.
        /// </summary>
        /// <param name="entity">The entity's name, ignoring case.</param>
        /// <returns>The surface; null for an entity the tenant model does not map.</returns>
        public QuerySurface? Root(string entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return _contents.Value.Roots.GetValueOrDefault(entity);
        }

        /// <summary>The metadata of a mapped entity, by its name, ignoring case.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The metadata; null for an entity the tenant model does not map.</returns>
        public EntityMetadata? Entity(string entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return _contents.Value.Entities.GetValueOrDefault(entity);
        }

        /// <summary>What the traversal rule knows of an entity, by its name, ignoring case.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <returns>The facts; null for an entity the tenant model does not map.</returns>
        public TraversalFacts? Facts(string entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            return _contents.Value.Facts.GetValueOrDefault(entity);
        }

        /// <summary>Builds the surfaces and the facts.</summary>
        /// <param name="registry">The stack registry.</param>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The contents.</returns>
        private static Contents Build(IStackRegistry registry, IEntityMetadataProvider metadata)
        {
            // --- The stacks' surfaces ---
            Dictionary<string, QuerySurface> stacks = new(StringComparer.Ordinal);
            foreach (StackDescriptor stack in registry.Stacks)
            {
                EntityMetadata entity = metadata.Get(stack.EntityType);
                IReadOnlyList<(string Property, SearchKind Kind)> searchable = stack.SearchableProperties.Count > 0
                    ? stack.SearchableProperties
                    : Convention(entity);
                stacks[stack.Resource] = new QuerySurface(
                    stack.Resource,
                    isChild: false,
                    stack.DefaultSelect,
                    stack.DefaultOrderBy,
                    searchable,
                    entity.DisplayName,
                    stack.KeyType,
                    entity.IsActivatable,
                    isTree: entity.Tree is not null,
                    stack.NamedFilters,
                    stack.Limits);
            }

            // --- The child query surfaces': the convention is already the descriptor's; no tree and no named filter ---
            Dictionary<string, QuerySurface> children = new(StringComparer.Ordinal);
            foreach (ChildQueryDescriptor child in registry.ChildQueries)
            {
                EntityMetadata entity = metadata.Get(child.EntityType);
                children[child.Resource] = new QuerySurface(
                    child.Resource,
                    isChild: true,
                    child.DefaultSelect,
                    child.DefaultOrderBy,
                    child.SearchableProperties,
                    entity.DisplayName,
                    Nullable.GetUnderlyingType(entity.Key.ClrType) ?? entity.Key.ClrType,
                    entity.IsActivatable,
                    isTree: false,
                    new Dictionary<string, FilterTree>(StringComparer.Ordinal),
                    child.Limits);
            }

            // --- Every mapped entity's projection, and the owner navigation of a child query surface;
            // the entity by its name, and the surface a statement rooted on it plans on ---
            Dictionary<string, TraversalFacts> facts = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, EntityMetadata> entities = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, QuerySurface> roots = new(StringComparer.OrdinalIgnoreCase);
            foreach (EntityMetadata entity in metadata.All)
            {
                _ = entities.TryAdd(entity.Name, entity);
                _ = roots.TryAdd(entity.Name, stacks.GetValueOrDefault(entity.Name) ?? children.GetValueOrDefault(entity.Name) ?? Plain(entity));

                ChildQueryDescriptor? child = registry.FindChildQuery(entity.Name);
                IReadOnlySet<string> related = registry.Find(entity.Name)?.RelatedSelect
                    ?? child?.RelatedSelect
                    ?? StackDescriptorBuilder.RelatedSelect(entity);
                _ = facts.TryAdd(entity.Name, new TraversalFacts(new HashSet<string>(related, StringComparer.OrdinalIgnoreCase), child?.OwnerNavigation));
            }

            return new Contents(stacks, children, facts, entities, roots);
        }

        /// <summary>The plain surface of an entity no stack and no child query surface serves.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The surface.</returns>
        private static QuerySurface Plain(EntityMetadata entity)
        {
            return new QuerySurface(
                entity.Name,
                isChild: false,
                entity.Key.Name,
                defaultOrderBy: null,
                Convention(entity),
                entity.DisplayName,
                Nullable.GetUnderlyingType(entity.Key.ClrType) ?? entity.Key.ClrType,
                entity.IsActivatable,
                isTree: false,
                new Dictionary<string, FilterTree>(StringComparer.Ordinal),
                StackLimits.Default);
        }

        /// <summary>The search convention: the string properties named <c>Name</c>, <c>Name2</c>, <c>Name3</c> and <c>Code</c>, each by contains.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The columns, in the convention's order.</returns>
        internal static List<(string Property, SearchKind Kind)> Convention(EntityMetadata entity)
        {
            return [.. ConventionColumns
                .Where(column => entity.Properties.Any(property => property.Name == column && property.ClrType == typeof(string)))
                .Select(static column => (column, SearchKind.Contains))];
        }

        /// <summary>The surfaces and the facts.</summary>
        /// <param name="Stacks">The stacks' surfaces, by resource.</param>
        /// <param name="Children">The child query surfaces, by resource.</param>
        /// <param name="Facts">The facts of every mapped entity, by name, ignoring case.</param>
        /// <param name="Entities">The metadata of every mapped entity, by name, ignoring case.</param>
        /// <param name="Roots">The surface a statement rooted on each mapped entity plans on, by name, ignoring case.</param>
        private sealed record Contents(
            Dictionary<string, QuerySurface> Stacks,
            Dictionary<string, QuerySurface> Children,
            Dictionary<string, TraversalFacts> Facts,
            Dictionary<string, EntityMetadata> Entities,
            Dictionary<string, QuerySurface> Roots);
    }

    /// <summary>What the traversal rule knows of one entity.</summary>
    /// <param name="RelatedSelect">The columns a path may read when the caller's read of the entity is not unrestricted, ignoring case.</param>
    /// <param name="OwnerNavigation">The navigation to the owner's row when the entity is a child query surface; null otherwise.</param>
    internal sealed record TraversalFacts(IReadOnlySet<string> RelatedSelect, string? OwnerNavigation);
}
