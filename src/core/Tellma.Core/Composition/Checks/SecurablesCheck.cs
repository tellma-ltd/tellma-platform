// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Access;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Queryex;

namespace Tellma.Core.Composition.Checks
{
    // The rules are spec 0014 §4.5's, less the endpoint pairs, which the host's endpoint audit checks.
    /// <summary>
    ///     <c>core.securables</c>: composes the securable registry and reports what composition found
    ///     (malformed names, conflicting registrations, aliases and marks naming nothing registered),
    ///     then judges the composed securables: each belongs to a composed feature; an owner is a
    ///     registered resource, reached from a filter root, and owner chains end; and, against the
    ///     model, every filter root is a query entity, a child entity's securable declares its owner,
    ///     and the owner's navigation leads to the owner's filter root.
    /// </summary>
    /// <remarks>
    ///     A model that does not build is the entity-metadata check's to report; the filter roots are
    ///     then not judged.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class SecurablesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Securables;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- Building the registry: a contributor the container cannot build fails it here, once ---
            ISecurableRegistry? registry;
            try
            {
                registry = services.GetService<ISecurableRegistry>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(
                [
                    new CompositionProblem(
                        Name,
                        $"The securable registry could not be built: {exception.GetType().Name}: {exception.Message}",
                        "Fix the securable contributor the container fails to build; until it builds, no securable is registered."),
                ]);
            }

            if (registry is null)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
            }

            // --- What composing the registry found; a failed composition holds nothing more to judge ---
            List<CompositionProblem> problems = registry is SecurableRegistry composed ? [.. composed.Problems] : [];
            if (registry is SecurableRegistry { Failed: true })
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            // --- The composed securables on their own ---
            CheckFeatures(registry, problems);
            CheckOwners(registry, problems);
            CheckOwnerChains(registry, problems);

            // --- The filter roots, against the model ---
            if (services.GetService<EntityMetadataSource>()?.Build.Provider is IEntityMetadataProvider metadata
                && services.GetService<IQueryexSchemaProvider>() is IQueryexSchemaProvider schemas)
            {
                CheckFilterRoots(registry, metadata, schemas.GetSchema(MultilingualShape.All), problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Checks that every securable belongs to a composed feature, reporting each unknown feature once.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckFeatures(ISecurableRegistry registry, List<CompositionProblem> problems)
        {
            HashSet<string> composed = new(record.Features.Select(static feature => feature.Name), StringComparer.Ordinal);
            foreach (IGrouping<string, SecurableDescriptor> unknown in registry.All
                .Where(descriptor => !composed.Contains(descriptor.Feature))
                .GroupBy(static descriptor => descriptor.Feature, StringComparer.Ordinal))
            {
                int count = unknown.Count();
                string named = count == 1 ? $"The securable '{unknown.First()}'" : $"The securable '{unknown.First()}' and {count - 1} more";
                problems.Add(new CompositionProblem(
                    Name,
                    $"{named} belong{(count == 1 ? "s" : string.Empty)} to the feature '{unknown.Key}', which is not composed.",
                    "Name a composed feature, or name none, so the securable belongs to the feature that registers it."));
            }
        }

        /// <summary>Checks that every owner is a registered resource, reached from the securable's filter root.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckOwners(ISecurableRegistry registry, List<CompositionProblem> problems)
        {
            foreach (SecurableDescriptor securable in registry.All)
            {
                if (securable.Owner is not SecurableOwner owner)
                {
                    continue;
                }

                // The owner is a registered name; an alias would resolve a former name for one release only.
                if (registry.ForResource(owner.Resource).Count == 0)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{Described(securable)} declares the owner '{owner.Resource}', which is not a registered resource.",
                        "Name the registered resource whose grants reach the child, such as 'core.User', spelled as registered."));
                }

                // The owner's navigation starts at the securable's own filter root.
                if (securable.FilterRoot is null)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{Described(securable)} declares the owner '{owner.Resource}' but takes no filter root, so no navigation leads to the owner.",
                        "Give a child securable its own entity as its filter root, from which the owner's navigation starts."));
                }
            }
        }

        /// <summary>
        ///     Checks that every owner chain ends: following each securable to the same action of its
        ///     owner never comes back to it. Each cycle is reported once.
        /// </summary>
        /// <param name="registry">The registry.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckOwnerChains(ISecurableRegistry registry, List<CompositionProblem> problems)
        {
            // The evaluator decides a child's owner pair first, recursively; a pair whose owner pair
            // is not registered ends the chain.
            Dictionary<SecurableDescriptor, int> order = new(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < registry.All.Count; index++)
            {
                order[registry.All[index]] = index;
            }

            foreach (SecurableDescriptor start in registry.All.Where(static securable => securable.Owner is not null))
            {
                List<SecurableDescriptor> chain = [start];
                SecurableDescriptor? next = OwnerOf(registry, start);
                while (next is not null && !chain.Contains(next, ReferenceEqualityComparer.Instance))
                {
                    chain.Add(next);
                    next = OwnerOf(registry, next);
                }

                // A chain that comes back to its start is a cycle, reported from its first member in
                // the registry's order, so each cycle is reported once.
                if (ReferenceEquals(next, start) && chain.All(member => order[start] <= order[member]))
                {
                    string path = string.Join(" → ", chain.Append(start).Select(static member => $"'{member}'"));
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The owners of the securables form a cycle: {path}.",
                        "Break the cycle: an owner chain ends at a securable without an owner."));
                }
            }
        }

        /// <summary>
        ///     Checks the filter roots against the query schema: each is an entity of it, a child
        ///     entity's securable declares an owner, and an owner's navigation is a path of navigations
        ///     from the filter root to the owner's filter root.
        /// </summary>
        /// <param name="registry">The registry.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="schema">The query schema with every language twin.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckFilterRoots(ISecurableRegistry registry, IEntityMetadataProvider metadata, QueryexSchema schema, List<CompositionProblem> problems)
        {
            // A child entity is one some entity carries as a child collection: saved with its owner,
            // and reachable by a grant only through it.
            HashSet<string> children = new(
                metadata.All.SelectMany(static entity => entity.Children).Select(static collection => collection.Child.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (SecurableDescriptor securable in registry.All)
            {
                if (securable.FilterRoot is not string filterRoot)
                {
                    continue;
                }

                // --- The filter root is a query entity ---
                if (schema.FindEntity(filterRoot) is not EntityDescriptor root)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{Described(securable)} takes the filter root '{filterRoot}', which is not an entity of the query schema.",
                        "Name the query entity a grant's filter binds against, such as the resource's own entity name, or none for a securable that takes no filter."));
                    continue;
                }

                // --- A child is reached through its owner ---
                if (securable.Owner is not SecurableOwner owner)
                {
                    if (children.Contains(root.Name))
                    {
                        problems.Add(new CompositionProblem(
                            Name,
                            $"{Described(securable)} takes the filter root '{root.Name}', a child entity, without an owner.",
                            "Declare the owner whose grants reach the child and the navigation to it, such as new SecurableOwner(\"core.User\", \"User\"), or filter on a top-level entity."));
                    }

                    continue;
                }

                CheckNavigation(registry, securable, root, owner, problems);
            }
        }

        /// <summary>Checks that an owner's navigation leads from the securable's filter root to the owner's.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="securable">The child securable.</param>
        /// <param name="root">Its filter root's query entity.</param>
        /// <param name="owner">Its owner.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckNavigation(ISecurableRegistry registry, SecurableDescriptor securable, EntityDescriptor root, SecurableOwner owner, List<CompositionProblem> problems)
        {
            // --- Walk the path, one to-one navigation per segment ---
            EntityDescriptor reached = root;
            foreach (string segment in owner.Navigation.Split('.'))
            {
                NavigationDescriptor? navigation = reached.Navigations.FirstOrDefault(
                    candidate => string.Equals(candidate.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (navigation is null)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{Described(securable)} declares the owner navigation '{owner.Navigation}', which is not a path of navigations from '{root.Name}'.",
                        $"Name the to-one navigations from '{root.Name}' to the owner's entity, joined by dots, such as 'User'."));
                    return;
                }

                reached = navigation.Target;
            }

            // --- The path ends at the owner's filter root: the same action's, else the resource's ---
            IReadOnlyList<SecurableDescriptor> owned = registry.ForResource(owner.Resource);
            string? expected = owned.FirstOrDefault(candidate => string.Equals(candidate.Action, securable.Action, StringComparison.OrdinalIgnoreCase))?.FilterRoot
                ?? owned.Select(static candidate => candidate.FilterRoot).FirstOrDefault(static candidate => candidate is not null);
            if (expected is not null && !string.Equals(reached.Name, expected, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{Described(securable)} reaches '{reached.Name}' through '{owner.Navigation}', not '{expected}', the filter root of its owner '{owner.Resource}'.",
                    $"Name the navigations from '{root.Name}' that lead to '{expected}'."));
            }
        }

        /// <summary>The same action of a securable's owner, when registered.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="securable">The securable.</param>
        /// <returns>The owner pair, or null when the securable has no owner or the pair is not registered.</returns>
        private static SecurableDescriptor? OwnerOf(ISecurableRegistry registry, SecurableDescriptor securable)
        {
            return securable.Owner is SecurableOwner owner
                ? registry.ForResource(owner.Resource).FirstOrDefault(candidate => string.Equals(candidate.Action, securable.Action, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        /// <summary>How a problem names a securable: its pair and its feature.</summary>
        /// <param name="securable">The securable.</param>
        /// <returns>The description.</returns>
        private static string Described(SecurableDescriptor securable)
        {
            return $"The securable '{securable}' of the feature '{securable.Feature}'";
        }
    }
}
