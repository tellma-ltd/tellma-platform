// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.stack-components</c>: every validator, persist effect, details contributor and
    ///     named filter contributor closes over the leaf of each stack it applies to and implements
    ///     its contract there; a persist effect is attached to an entity a stack owns; a details
    ///     contributor declares each extra once and no producer declares the platform's
    ///     <c>permissions</c>; every named filter contributor can be created from the root provider
    ///     and declares well-formed names, each once across the stack's contributors, with a tree.
    /// </summary>
    internal sealed class StackComponentsCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.StackComponents;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];
            _ = StackCheckSupport.Registry(services, Name, problems);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
