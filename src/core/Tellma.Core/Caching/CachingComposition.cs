// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Composition;

namespace Tellma.Core.Caching
{
    /// <summary>
    ///     Registers what the caches need beyond <see cref="CoreFeature" />'s contribution: the cache
    ///     options and the version tags' instruments.
    /// </summary>
    internal static class CachingComposition
    {
        /// <summary>Registers the options and the instruments.</summary>
        /// <param name="services">The composition's services.</param>
        /// <param name="options">The options registrar, whose bindings the startup gate validates.</param>
        public static void Register(IServiceCollection services, OptionsRegistrar options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            // --- The options, through the gate's validation ---
            options.Bind<CacheOptions>(CacheOptions.SectionPath, CoreFeature.FeatureName);

            // --- The instruments; AddMetrics is idempotent and supplies the meter factory ---
            _ = services.AddMetrics();
            _ = services.AddLogging();
            _ = services.AddSingleton<VersionTagMetrics>();
        }
    }
}
