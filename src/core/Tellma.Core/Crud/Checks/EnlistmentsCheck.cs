// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.enlistments</c>: every <see cref="Abstractions.Crud.IEnlists{TTarget}" /> a class of
    ///     the composition declares targets a stack that accepts enlisted writes, and the declared
    ///     pairs between stacks form no cycle; a self-pair, a stack's service enlisting its own entity
    ///     from an open frame, is not an edge of the graph.
    /// </summary>
    internal sealed class EnlistmentsCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Enlistments;

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
