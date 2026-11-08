// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Composition.Realizers;

namespace Tellma.Core.Composition
{
    /// <summary>The host-agnostic composition root.</summary>
    public static class TellmaServiceCollectionExtensions
    {
        /// <summary>
        ///     Composes a Tellma distribution into <paramref name="services" />: runs the composition
        ///     method, validates the feature graph, realises every feature's contribution in dependency
        ///     order and installs the startup gate as the first hosted service.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Every problem the composition has is reported at once, in one
        ///         <see cref="TellmaCompositionException" /> thrown from this call, before the host is
        ///         built: a requirement no added feature satisfies, two features with one name, a
        ///         cycle, a feature type added twice, an options type bound to two paths, an invalid
        ///         slug, an item no realizer handles, and every problem a realizer reports.
        ///     </para>
        ///     <para>
        ///         Problems that need the built host (invalid options, a missing service, the
        ///         environment) are the startup gate's: it runs every registered
        ///         <see cref="IStartupCheck" /> before the host serves traffic and throws them, again
        ///         all at once.
        ///     </para>
        /// </remarks>
        /// <param name="services">The host's services.</param>
        /// <param name="slug">The distribution slug: 1 to 15 lowercase letters, digits or hyphens, starting with a letter.</param>
        /// <param name="configuration">The host's configuration.</param>
        /// <param name="environment">The host's environment.</param>
        /// <param name="compose">The distribution's composition method.</param>
        /// <returns><paramref name="services" />, for chaining.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="TellmaCompositionException">The composition has problems; every one is listed.</exception>
        /// <exception cref="InvalidOperationException">The services already hold a Tellma composition.</exception>
        public static IServiceCollection AddTellma(
            this IServiceCollection services,
            string slug,
            IConfiguration configuration,
            IHostEnvironment environment,
            Action<TellmaBuilder> compose)
        {
            return AddTellma(services, slug, configuration, environment, compose, ContributionRealizerSet.CreateBuiltIn(), IcuProbe.IsRunningIcu);
        }

        /// <summary>
        ///     <see cref="AddTellma(IServiceCollection, string, IConfiguration, IHostEnvironment, Action{TellmaBuilder})" />
        ///     with the realizers and the ICU probe supplied, so the suites can add test item kinds and
        ///     simulate a process without ICU.
        /// </summary>
        /// <param name="services">The host's services.</param>
        /// <param name="slug">The distribution slug.</param>
        /// <param name="configuration">The host's configuration.</param>
        /// <param name="environment">The host's environment.</param>
        /// <param name="compose">The distribution's composition method.</param>
        /// <param name="realizers">The realizers of this composition.</param>
        /// <param name="icuProbe">Reports whether the process runs ICU.</param>
        /// <returns><paramref name="services" />, for chaining.</returns>
        internal static IServiceCollection AddTellma(
            IServiceCollection services,
            string slug,
            IConfiguration configuration,
            IHostEnvironment environment,
            Action<TellmaBuilder> compose,
            ContributionRealizerSet realizers,
            Func<bool> icuProbe)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(slug);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(compose);

            CompositionPipeline pipeline = new(slug, services, configuration, environment, realizers, icuProbe);

            // The graph gate: nothing is realised over a graph that cannot be ordered.
            IReadOnlyList<CompositionProblem> problems = pipeline.Collect(compose);
            if (problems.Count > 0)
            {
                throw new TellmaCompositionException(problems);
            }

            problems = pipeline.Realise();
            return problems.Count > 0 ? throw new TellmaCompositionException(problems) : services;
        }
    }
}
