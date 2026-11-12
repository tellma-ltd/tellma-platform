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
    ///     Registers <see cref="ProvisioningStepContributionItem" />s: each step class once, closed over
    ///     the registered leaves when it is generic over substitutable entities, as a scoped service,
    ///     and recorded as a <see cref="ProvisioningStepRegistration" /> the step runner creates inside
    ///     the tenant scope it runs in. The same class contributed twice, by one feature or by two, is
    ///     a composition problem, because a step runs once per tenant and belongs to one feature; two
    ///     steps that share a name are the startup gate's to report, because a name is only known once
    ///     a step exists.
    /// </summary>
    internal sealed class ProvisioningStepContributionRealizer : IContributionRealizer<ProvisioningStepContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ProvisioningStepContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The shape of the contributed class ---
            Type? contributed = item.StepType;
            if (contributed is null
                || !contributed.IsClass
                || contributed.IsAbstract
                || contributed.ContainsGenericParameters
                || !typeof(ITenantProvisioningStep).IsAssignableFrom(contributed))
            {
                string described = contributed is null ? "no step type" : TypeDisplay.Qualified(contributed);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the provisioning step {described}, which is not a concrete, closed class implementing {nameof(ITenantProvisioningStep)}.",
                    "Contribute a provisioning step as a concrete class implementing ITenantProvisioningStep: contribution.ProvisioningStep<TStep>()."));
                return;
            }

            // --- A step generic over a pack's entity runs over the leaf the distribution registered ---
            Type stepType;
            try
            {
                stepType = CloseOverLeaves(contributed, context.Record.LeafOf);
            }
            catch (ArgumentException exception)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the provisioning step {TypeDisplay.Qualified(contributed)}, which cannot be closed over the registered leaves: {exception.Message}",
                    "Constrain the step's type parameters by the replaceable defaults it is written against, so every leaf satisfies them."));
                return;
            }

            // --- A class contributed again, by its own feature or another, is refused ---
            ProvisioningStepRegistration? earlier = context.Record.ProvisioningSteps.Find(existing => existing.StepType == stepType);
            if (earlier is not null)
            {
                string by = ReferenceEquals(earlier.Owner, owner) ? "already, once" : $"after feature '{earlier.Owner.Name}' did";
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the provisioning step {TypeDisplay.Qualified(stepType)} {by}; a step runs once per tenant, so one feature contributes it once.",
                    "Contribute each provisioning step class from one feature, once."));
                return;
            }

            // --- Scoped by its own class, recorded with its owner and its factory ---
            context.Services.TryAdd(ServiceDescriptor.Scoped(stepType, stepType));
            context.Record.ProvisioningSteps.Add(new ProvisioningStepRegistration(
                stepType,
                owner,
                services => (ITenantProvisioningStep)services.GetRequiredService(stepType)));
        }

        /// <summary>
        ///     A constructed generic step closed over the leaf of each type argument instead, as a
        ///     stack's service is; any other step as it is.
        /// </summary>
        /// <param name="stepType">The contributed step class.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it, else itself.</param>
        /// <returns>The step class to register.</returns>
        /// <exception cref="ArgumentException">A leaf does not satisfy a type parameter's constraints.</exception>
        internal static Type CloseOverLeaves(Type stepType, Func<Type, Type> leafOf)
        {
            if (!stepType.IsConstructedGenericType)
            {
                return stepType;
            }

            Type[] arguments = stepType.GetGenericArguments();
            Type[] leaves = [.. arguments.Select(leafOf)];
            return arguments.SequenceEqual(leaves) ? stepType : stepType.GetGenericTypeDefinition().MakeGenericType(leaves);
        }
    }
}
