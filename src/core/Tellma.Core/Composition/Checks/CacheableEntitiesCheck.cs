// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Caching;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Composition.Checks
{
    // The rules are spec 0013 §4.2's.
    /// <summary>
    ///     <c>core.cacheable-entities</c>: every entity marked <see cref="CacheableAttribute" /> is a
    ///     top-level entity with an int key (the cached list is keyed by an int id), declares no child
    ///     collection, has a resolved row limit between 1 and <see cref="CacheOptions.EntityMaxRowsCeiling" />,
    ///     and has a read securable without a row filter, since one cached list is shared by every reader.
    /// </summary>
    /// <remarks>
    ///     A model that does not build is the entity-metadata check's to report, and limits that do
    ///     not validate are the options check's; the row limits are then not judged. An entity whose
    ///     read securable is not registered is not judged on its filter.
    /// </remarks>
    internal sealed class CacheableEntitiesCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.CacheableEntities;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            if (services.GetRequiredService<EntityMetadataSource>().Build.Provider is not IEntityMetadataProvider metadata)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            CacheOptions? limits = Limits(services);
            ISecurableRegistry? securables = Securables(services);
            foreach (EntityMetadata entity in metadata.All.Where(static entity => entity.IsCacheable).OrderBy(static entity => entity.Name, StringComparer.Ordinal))
            {
                Check(entity, limits, securables, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Checks one cacheable entity.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="limits">The cache limits, or null when they do not validate.</param>
        /// <param name="securables">The securable registry, or null when none is composed or it does not build.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void Check(EntityMetadata entity, CacheOptions? limits, ISecurableRegistry? securables, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(problems);

            // --- The shape: a top-level entity with an int key, no child collection ---
            if (!typeof(TopLevelEntity<int>).IsAssignableFrom(entity.ClrType))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The cacheable entity {entity.Name} ({entity.ClrType.Name}) is not a top-level entity with an int key; a cached list is keyed by an int id.",
                    $"Derive {entity.ClrType.Name} from TopLevelEntity, or remove [Cacheable]."));
            }

            if (entity.Children.Count > 0)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The cacheable entity {entity.Name} declares the child collection {entity.Children[0].Property}; a cached list holds flat rows.",
                    $"Remove [Cacheable] from {entity.ClrType.Name}, or its child collections."));
            }

            // --- The row limit, resolved against the configured default ---
            if (limits is not null)
            {
                int maxRows = CacheableEntityStore.ResolveMaxRows(entity, limits);
                if (maxRows < 1 || maxRows > limits.EntityMaxRowsCeiling)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cacheable entity {entity.Name} has a row limit of {maxRows}, outside 1 to {limits.EntityMaxRowsCeiling}; a list that large is not reference data."),
                        $"Set [Cacheable(MaxRows = ...)] on {entity.ClrType.Name} between 1 and {nameof(CacheOptions.EntityMaxRowsCeiling)}, leave it 0 for {nameof(CacheOptions.EntityMaxRowsDefault)}, or remove [Cacheable]."));
                }
            }

            // --- The read securable: no row filter; one not registered is not judged ---
            if (securables?.Find(entity.Name, AccessActions.Read) is SecurableDescriptor read && read.FilterRoot is not null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The cacheable entity {entity.Name} has a Read securable filtered through {read.FilterRoot}; a cached list is shared by every reader, so a row filter on it would be ignored.",
                    $"Register the Read securable of {entity.Name} without a filter root, or remove [Cacheable]."));
            }
        }

        /// <summary>The cache limits, or null when they do not validate: the options check reports those.</summary>
        /// <param name="services">The host's services.</param>
        /// <returns>The limits.</returns>
        private static CacheOptions? Limits(IServiceProvider services)
        {
            try
            {
                return services.GetService<IOptions<CacheOptions>>()?.Value;
            }
            catch (OptionsValidationException)
            {
                return null;
            }
        }

        /// <summary>The securable registry, or null when none is composed or it does not build: the securables check reports that.</summary>
        /// <param name="services">The host's services.</param>
        /// <returns>The registry.</returns>
        private static ISecurableRegistry? Securables(IServiceProvider services)
        {
            try
            {
                return services.GetService<ISecurableRegistry>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return null;
            }
        }
    }
}
