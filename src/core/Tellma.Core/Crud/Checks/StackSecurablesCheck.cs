// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Access;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.stack-securables</c>, the securables audit over the stack registry: every action a
    ///     stack's standard operations require, every entity action's <c>Action</c>, every non-null
    ///     API action securable of a stack, a companion or a routed service, and every child query
    ///     surface's read is a registered securable, so no operation is reachable that no grant can
    ///     name.
    /// </summary>
    /// <remarks>
    ///     A securable registry that does not compose is the securables check's to report; nothing is
    ///     then judged.
    /// </remarks>
    internal sealed class StackSecurablesCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.StackSecurables;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            if (StackCheckSupport.Registry(services, Name, problems) is not StackRegistry { Failed: false } stacks)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            ISecurableRegistry? securables;
            try
            {
                securables = services.GetService<ISecurableRegistry>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The securables check reports a registry the container cannot build.
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            if (securables is null or SecurableRegistry { Failed: true })
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            Audit(stacks, securables, problems);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Reports every securable the stacks require that the registry does not hold.</summary>
        /// <param name="stacks">The stack registry.</param>
        /// <param name="securables">The securable registry.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void Audit(IStackRegistry stacks, ISecurableRegistry securables, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(stacks);
            ArgumentNullException.ThrowIfNull(securables);
            ArgumentNullException.ThrowIfNull(problems);
            foreach (StackDescriptor stack in stacks.Stacks)
            {
                foreach ((string action, _) in StackSecurableContributor.Securables(stack))
                {
                    Require(securables, stack.Resource, action, $"the stack of {stack.Resource}", problems);
                }

                foreach (ApiActionDescriptor action in stack.Actions.OfType<ApiActionDescriptor>())
                {
                    if (action.Securable is SecurableRef securable)
                    {
                        Require(securables, securable.Resource, securable.Action, $"the action '{action.Name}' of {stack.Resource}", problems);
                    }
                }
            }

            foreach (ApiServiceDescriptor service in stacks.Services)
            {
                foreach (ApiActionDescriptor action in service.Actions)
                {
                    if (action.Securable is SecurableRef securable)
                    {
                        Require(securables, securable.Resource, securable.Action, $"the action '{action.Name}' of the routed service '{service.Route}'", problems);
                    }
                }
            }

            foreach (ChildQueryDescriptor child in stacks.ChildQueries)
            {
                Require(securables, child.Resource, AccessActions.Read, $"the child query surface of {child.Resource}", problems);
            }
        }

        /// <summary>Reports a securable the registry does not hold.</summary>
        /// <param name="securables">The securable registry.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="requiredBy">What requires it, for the problem.</param>
        /// <param name="problems">Receives the problem.</param>
        private void Require(ISecurableRegistry securables, string resource, string action, string requiredBy, List<CompositionProblem> problems)
        {
            if (securables.Find(resource, action) is null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The securable '{resource}:{action}', which {requiredBy} requires, is not registered, so no grant can name it.",
                    "Keep the stack registry's securable contributor composed; register a routed service's securable with contribution.Securables(...)."));
            }
        }
    }
}
