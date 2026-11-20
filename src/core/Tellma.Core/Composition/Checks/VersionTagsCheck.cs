// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Caching;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Data.Writes;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.version-tags</c>: every version tag the entities declare is a plain lower-case name,
    ///     <c>entity:</c> names come from cacheable entities alone and are unique, every user-level tag
    ///     names an int property of its entity, no two names differ in case alone, and every table a
    ///     user-level tag names can tell its users when a key's <c>ON DELETE</c> action writes it.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone. A model that fails <c>core.entity-metadata</c> has no entities
    ///     to read, so this check then reports nothing and leaves the failure to that check.
    /// </remarks>
    internal sealed class VersionTagsCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.VersionTags;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // The registry the host resolves is built from the same metadata, so its problems are the
            // composition's; a registry a feature put in its place reports through its own checks.
            if (services.GetRequiredService<EntityMetadataSource>().Build.Provider is not { } metadata
                || services.GetService<IVersionTagRegistry>() is not VersionTagRegistry registry)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
            }

            // --- Then the keys' actions over the tables a sound registry's user-level rules name ---
            return Task.FromResult(registry.Problems.Count > 0 || services.GetService<ForeignKeyActions>() is not { } keys
                ? registry.Problems
                : keys.UserTagProblems(metadata, registry));
        }
    }
}
