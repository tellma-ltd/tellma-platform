// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;

namespace Tellma.Core.Testing.Crud
{
    /// <summary>
    ///     A distribution's host under conformance test, over a tenant database of its own: the
    ///     composition the stacks run in, the callers the suite seeds, and the request scopes it runs
    ///     their operations in. The suite reads everything else, the stacks included, from the
    ///     composition.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A harness owns its tenant database, and no other suite writes it while the suite runs,
    ///         so a measured round trip or instrument is the operation's own. The suite creates its
    ///         own rows and never deletes rows it did not create.
    ///     </para>
    ///     <para>
    ///         Callers connect through the host's ordinary prologue, so the row-level rules, the tag
    ///         re-runs and the deny re-checks run as a request's do.
    ///     </para>
    /// </remarks>
    public interface IStackConformanceHarness
    {
        /// <summary>The host's root services: the stack registry, the securable registry, the entity metadata and the meter factory.</summary>
        IServiceProvider Services { get; }

        /// <summary>
        ///     Seeds an active member of the tenant who holds exactly <paramref name="grants" />,
        ///     through one role of its own, with the tenant's permissions tag moved so the next connect
        ///     reads the grants.
        /// </summary>
        /// <param name="grants">The role's permissions.</param>
        /// <param name="cancellationToken">The test's cancellation.</param>
        /// <returns>The caller.</returns>
        Task<StackConformanceCaller> SeedCallerAsync(IReadOnlyList<StackGrant> grants, CancellationToken cancellationToken);

        /// <summary>
        ///     A request scope of the tenant, connected as <paramref name="caller" /> and completed as a
        ///     request's is, ready for a stack's service to run in. The suite disposes it.
        /// </summary>
        /// <param name="caller">The caller.</param>
        /// <param name="cancellationToken">The test's cancellation.</param>
        /// <returns>The scope.</returns>
        Task<AsyncServiceScope> ScopeAsync(StackConformanceCaller caller, CancellationToken cancellationToken);

        /// <summary>
        ///     The round trips the database driver counted on every connection the scope opened, so the
        ///     suite can hold the platform's own count to what reached the server; null when the
        ///     harness does not observe the driver.
        /// </summary>
        /// <param name="scope">A scope <see cref="ScopeAsync" /> created.</param>
        /// <returns>The round trips so far; null when not observed.</returns>
        long? ServerRoundTrips(IServiceProvider scope);
    }
}
