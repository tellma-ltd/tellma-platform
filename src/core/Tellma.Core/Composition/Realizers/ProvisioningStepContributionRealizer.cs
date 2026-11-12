// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="ProvisioningStepContributionItem" />s: each step class once, as a
    ///     scoped <see cref="ITenantProvisioningStep" />, so the step runner creates it inside the
    ///     tenant scope it runs in. The same class contributed twice is registered once, for the
    ///     feature realised first; two classes that share a step name are the startup gate's to
    ///     report, because a name is only known once a step exists.
    /// </summary>
    internal sealed class ProvisioningStepContributionRealizer : IContributionRealizer<ProvisioningStepContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ProvisioningStepContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The shape of the contributed class ---
            Type? stepType = item.StepType;
            if (stepType is null
                || !stepType.IsClass
                || stepType.IsAbstract
                || stepType.ContainsGenericParameters
                || !typeof(ITenantProvisioningStep).IsAssignableFrom(stepType))
            {
                string described = stepType is null ? "no step type" : TypeDisplay.Qualified(stepType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the provisioning step {described}, which is not a concrete, closed class implementing {nameof(ITenantProvisioningStep)}.",
                    "Contribute a provisioning step as a concrete class implementing ITenantProvisioningStep: contribution.ProvisioningStep<TStep>()."));
                return;
            }

            // --- Once per class, scoped, recorded with its owner ---
            if (!context.Record.ProvisioningSteps.Exists(existing => existing.Type == stepType))
            {
                context.Record.ProvisioningSteps.Add(new ContributedType(stepType, owner));
                context.Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ITenantProvisioningStep), stepType));
            }
        }
    }
}
