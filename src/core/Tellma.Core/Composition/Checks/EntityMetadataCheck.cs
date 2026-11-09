// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.entity-metadata</c>: builds the tenant model and its entity metadata, and reports every
    ///     way the model breaks the entity contract, all at once. Warnings (a referenced entity without a
    ///     natural key) are logged and never fail startup.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone, so a build step without a deployment runs it too. It is the first
    ///     use of the model in a host, which therefore fails before serving traffic, rather than on its
    ///     first request, when a declaration cannot be built.
    /// </remarks>
    internal sealed class EntityMetadataCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.EntityMetadata;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            EntityMetadataBuild build = services.GetRequiredService<EntityMetadataSource>().Build;
            ILogger logger = services.GetService<ILoggerFactory>()?.CreateLogger<EntityMetadataCheck>() ?? NullLogger<EntityMetadataCheck>.Instance;
            foreach (EntityMetadataWarning warning in build.Warnings)
            {
                EntityMetadataLog.Warning(logger, warning.Entity, warning.Message);
            }

            return Task.FromResult(build.Problems);
        }
    }
}
