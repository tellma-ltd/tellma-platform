// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Provisioning;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.provisioning-steps</c>: every contributed provisioning step can be created, has a
    ///     name the tenant database's step record can hold, a non-negative order and a version from
    ///     1, and no two steps share a name.
    /// </summary>
    /// <remarks>
    ///     Reads the composition alone: each step is created once in the gate's scope, outside any
    ///     tenant, and only its name, order and version are read. Names are compared without regard
    ///     to case, because the step record's key column compares them that way.
    /// </remarks>
    /// <param name="record">The realised composition, which lists the contributed step classes.</param>
    internal sealed class ProvisioningStepsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.ProvisioningSteps;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            List<CompositionProblem> problems = [];
            List<(ITenantProvisioningStep Step, ContributedType Contributed)> steps = [];

            // --- Each step, created as its tenant scope would create it ---
            foreach (ContributedType contributed in record.ProvisioningSteps)
            {
                try
                {
                    steps.Add(((ITenantProvisioningStep)ActivatorUtilities.CreateInstance(services, contributed.Type), contributed));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The provisioning step {TypeDisplay.Qualified(contributed.Type)} of feature '{contributed.Owner.Name}' could not be created: {exception.GetType().Name}: {exception.Message}",
                        "Register every service the step's constructor takes, and keep the constructor from reaching a tenant: the step runs its work in RunAsync."));
                }
            }

            // --- Name, order and version of each ---
            foreach ((ITenantProvisioningStep step, ContributedType contributed) in steps)
            {
                string label = $"{TypeDisplay.Qualified(contributed.Type)} of feature '{contributed.Owner.Name}'";
                if (!ProvisioningStepNames.IsValid(step.Name))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The provisioning step {label} has the name '{step.Name}', which is not 1 to {ProvisioningStepNames.MaxLength} characters without control characters or surrounding white space.",
                        "Name the step '<feature>.<step>', such as 'gl.sample-centers'."));
                }

                if (step.Order < 0)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The provisioning step {label} has the order {step.Order}, which is negative.",
                        "Give the step an order in its band: 100 to 199 for a pack, 200 to 899 for a distribution."));
                }

                if (step.Version < 1)
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The provisioning step {label} has the version {step.Version}, which is below 1.",
                        "Start a step's version at 1 and raise it whenever the step must run again on every tenant."));
                }
            }

            // --- One step per name ---
            foreach (IGrouping<string, (ITenantProvisioningStep Step, ContributedType Contributed)> clash in steps
                .Where(static pair => ProvisioningStepNames.IsValid(pair.Step.Name))
                .GroupBy(static pair => pair.Step.Name, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1))
            {
                string types = string.Join(" and ", clash.Select(static pair => $"{TypeDisplay.Qualified(pair.Contributed.Type)} (feature '{pair.Contributed.Owner.Name}')"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The provisioning steps {types} share the name '{clash.Key}'.",
                    "Give each provisioning step a name of its own, prefixed with its feature's name."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
