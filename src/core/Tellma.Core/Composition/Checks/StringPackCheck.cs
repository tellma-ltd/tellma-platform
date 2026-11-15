// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Localization;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.string-pack</c>: no shared string resource key is declared by two composed
    ///     assemblies, so each key of the merged string packs, and of every label resolved against
    ///     them, has one owner.
    /// </summary>
    /// <remarks>Reads the composed assemblies' resources alone, so a build step without a deployment runs it too.</remarks>
    internal sealed class StringPackCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.StringPack;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // One problem per conflicting key and pair of resources, in the order composition met them.
            StringPackComposer composer = services.GetRequiredService<StringPackComposer>();
            IReadOnlyList<CompositionProblem> problems =
            [
                .. composer.Conflicts.Select(conflict => new CompositionProblem(
                    Name,
                    $"The shared string resource key '{conflict.Key}' is declared by both {conflict.First} and {conflict.Second}; the composed string pack holds one text per key.",
                    "Rename the key in one of the two resources. A label key starts with its entity's schema and name (Gl_Center_Name), so a key that names the declaring feature's own entity, or starts with the feature's own prefix, is that feature's alone.")),
            ];

            return Task.FromResult(problems);
        }
    }
}
