// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="ApiServiceContributionItem" />s, in realisation order, for the stack
    ///     composition, which registers each routed service and projects its actions. The same class
    ///     contributed again is recorded once.
    /// </summary>
    /// <remarks>
    ///     Only the item's shape is judged here; the route and the actions are the startup gate's to
    ///     report.
    /// </remarks>
    internal sealed class ApiServiceContributionRealizer : IContributionRealizer<ApiServiceContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ApiServiceContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The shape of the item ---
            if (item.ServiceType is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } serviceType)
            {
                string described = item.ServiceType is null ? "no service type" : TypeDisplay.Qualified(item.ServiceType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the routed service {described}, which is not a concrete, closed class.",
                    "Contribute a routed service as a concrete class marked [ApiRoute]: contribution.ApiService<TService>()."));
                return;
            }

            // --- Recorded once per class, with the first feature that contributed it ---
            if (!context.Record.ApiServices.Exists(existing => existing.ServiceType == serviceType))
            {
                context.Record.ApiServices.Add(new ContributedApiService(serviceType, owner));
            }
        }
    }
}
