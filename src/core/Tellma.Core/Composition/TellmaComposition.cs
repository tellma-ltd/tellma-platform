// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Composition.Realizers;

namespace Tellma.Core.Composition
{
    /// <summary>Validates a composition without a host.</summary>
    public static class TellmaComposition
    {
        /// <summary>
        ///     Runs a composition without a host and returns every problem the composition root would
        ///     throw: the graph gate's and, when the graph is sound, those of realising its items. No
        ///     service provider is built, no hosted service runs and nothing is read but
        ///     <paramref name="configuration" />.
        /// </summary>
        /// <remarks>
        ///     The composition runs in the environment named by <paramref name="configuration" />'s
        ///     <c>environment</c> key, as a host would read it, else Production. Startup checks are
        ///     not run: they need the built host.
        /// </remarks>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="configuration">The configuration the composition reads.</param>
        /// <param name="compose">The distribution's composition method.</param>
        /// <returns>Every problem found; empty for a sound composition.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static IReadOnlyList<CompositionProblem> Validate(string slug, IConfiguration configuration, Action<TellmaBuilder> compose)
        {
            ArgumentNullException.ThrowIfNull(slug);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(compose);

            CompositionPipeline pipeline = new(
                slug,
                new ServiceCollection(),
                configuration,
                new HostFreeEnvironment(configuration),
                ContributionRealizerSet.CreateBuiltIn(),
                IcuProbe.IsRunningIcu);

            IReadOnlyList<CompositionProblem> problems = pipeline.Collect(compose);
            return problems.Count > 0 ? problems : pipeline.Realise();
        }

        /// <summary>The environment a host-free composition runs in: named by configuration, with no content files.</summary>
        private sealed class HostFreeEnvironment : IHostEnvironment
        {
            /// <summary>Reads the environment name the way a host does, defaulting to Production.</summary>
            /// <param name="configuration">The configuration.</param>
            public HostFreeEnvironment(IConfiguration configuration)
            {
                string? name = configuration[HostDefaults.EnvironmentKey];
                EnvironmentName = string.IsNullOrWhiteSpace(name) ? Environments.Production : name;
                ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
                ContentRootPath = Directory.GetCurrentDirectory();
            }

            /// <inheritdoc />
            public string EnvironmentName { get; set; }

            /// <inheritdoc />
            public string ApplicationName { get; set; }

            /// <inheritdoc />
            public string ContentRootPath { get; set; }

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
