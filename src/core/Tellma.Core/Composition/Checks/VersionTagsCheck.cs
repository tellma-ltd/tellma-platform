// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Caching;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.version-tags</c>: every version tag the entities declare is a plain lower-case name,
    ///     <c>entity:</c> names come from cacheable entities alone and are unique, every user-level tag
    ///     names an int property of its entity, and no two names differ in case alone.
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
            return Task.FromResult(services.GetRequiredService<EntityMetadataSource>().Build.Provider is not null
                && services.GetService<IVersionTagRegistry>() is VersionTagRegistry registry
                ? registry.Problems
                : []);
        }
    }
}
