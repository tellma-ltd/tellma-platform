// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.stack-actions</c>: every <see cref="Abstractions.Crud.EntityActionAttribute" /> and
    ///     <see cref="Abstractions.Crud.ApiActionAttribute" /> method conforms: on the class its kind
    ///     belongs to, with a conforming signature and arguments record, a well-formed name that is
    ///     unique on its stack or service and avoids the standard segments the stack projects, and a
    ///     securable a routed service's action names. Every stack companion is attached to a stack and
    ///     declares an action, every routed service names its route, and every
    ///     <see cref="Abstractions.Crud.EntityRefAttribute" /> on an action's arguments names a stack
    ///     that serves queries.
    /// </summary>
    internal sealed class StackActionsCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.StackActions;

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
