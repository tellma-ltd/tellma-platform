// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>The compositions the stack suite builds over its model.</summary>
    internal static class StackScenarios
    {
        /// <summary>
        ///     The gadget scenario: the gadget stacked with its generic service, a companion, a
        ///     validator, a details contributor and a named filter contributor, the gizmo and the
        ///     sprocket stacked with the default service, and a routed service.
        /// </summary>
        /// <param name="contribute">What the scenario adds to the feature.</param>
        /// <param name="compose">What the scenario adds to the composition.</param>
        /// <returns>The composed services.</returns>
        public static IServiceCollection Gadgets(Action<FeatureContribution>? contribute = null, Action<TellmaBuilder>? compose = null)
        {
            return TestComposition.Compose(tellma => Compose(tellma, contribute, compose));
        }

        /// <summary>Runs every startup check over the gadget scenario.</summary>
        /// <param name="contribute">What the scenario adds to the feature.</param>
        /// <param name="compose">What the scenario adds to the composition.</param>
        /// <param name="services">Registrations made before the composition, such as a host's own.</param>
        /// <returns>Every problem.</returns>
        public static Task<IReadOnlyList<CompositionProblem>> CheckAsync(
            Action<FeatureContribution>? contribute = null, Action<TellmaBuilder>? compose = null, IServiceCollection? services = null)
        {
            return TestComposition.CheckAsync(tellma => Compose(tellma, contribute, compose), services: services);
        }

        /// <summary>Runs every startup check over a feature stacking the gadget with a service, and nothing else stacked.</summary>
        /// <typeparam name="TService">The gadget's service.</typeparam>
        /// <param name="contribute">What the scenario adds to the feature.</param>
        /// <param name="compose">What the scenario adds to the composition.</param>
        /// <returns>Every problem.</returns>
        public static Task<IReadOnlyList<CompositionProblem>> CheckAsync<TService>(Action<FeatureContribution>? contribute = null, Action<TellmaBuilder>? compose = null)
            where TService : class
        {
            return TestComposition.CheckAsync(tellma =>
            {
                _ = tellma.AddFeature(StackModel.Feature<TService>(contribute));
                compose?.Invoke(tellma);
            });
        }

        /// <summary>Composes the gadget scenario.</summary>
        /// <param name="tellma">The builder.</param>
        /// <param name="contribute">What the scenario adds to the feature.</param>
        /// <param name="compose">What the scenario adds to the composition.</param>
        private static void Compose(TellmaBuilder tellma, Action<FeatureContribution>? contribute, Action<TellmaBuilder>? compose)
        {
            _ = tellma.AddFeature(StackModel.Feature<GadgetService<SmGadget>>(contribution =>
            {
                _ = contribution
                    .Entity<SmGizmo>()
                    .Entity<SmSprocket>()
                    .EntityCompanion<SmGadget, GadgetOperations<SmGadget>>()
                    .Validator<SmGadget>(typeof(GadgetRules<>))
                    .DetailsContributor<SmGadget>(typeof(GadgetDetails<>))
                    .NamedFilters<SmGadget>(typeof(GadgetFilters<>))
                    .ApiService<InboxService>();
                contribute?.Invoke(contribution);
            }));
            compose?.Invoke(tellma);
        }
    }
}
