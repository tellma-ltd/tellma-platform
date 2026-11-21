// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     One check the startup gate runs before the host serves traffic. Any package registers its
    ///     checks as services of this type; the gate runs every one of them inside one service scope
    ///     and fails startup with every problem they report, all at once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A check reports problems rather than throwing. Each problem's
    ///         <see cref="CompositionProblem.Fix" /> states the remedy as the code or configuration to
    ///         add. A check that throws is reported as a problem of its own.
    ///     </para>
    ///     <para>
    ///         A check that needs a store and finds it unreachable reports nothing: reachability is a
    ///         readiness concern, never a startup failure, so an outage cannot turn into a crash loop.
    ///     </para>
    /// </remarks>
    [CollectionService]
    public interface IStartupCheck
    {
        /// <summary>The check's name in diagnostics, such as <c>core.options</c>.</summary>
        string Name { get; }

        /// <summary>
        ///     True when the check reads deployment configuration, options, the host environment, the
        ///     catalog, a database or a key store; false when it reads the composition alone. A build
        ///     step that realises the composition without a deployment runs only the checks for which
        ///     this is false.
        /// </summary>
        bool RequiresInfrastructure { get; }

        /// <summary>Runs the check.</summary>
        /// <param name="services">The services of the scope the gate runs its checks in.</param>
        /// <param name="cancellationToken">Cancels the check when the host stops starting.</param>
        /// <returns>The problems found; empty when the check passes.</returns>
        Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken);
    }
}
