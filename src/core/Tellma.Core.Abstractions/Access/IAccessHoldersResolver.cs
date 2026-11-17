// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Access
{
    // The resolver is spec 0014 §5.1 and §10 (access/holders); its round trip is §7.5's.
    /// <summary>
    ///     Answers "who holds this securable" across the tenant, in one round trip on the scope's
    ///     caller's behalf. Each user's grants compose exactly as their own decision would, so "who
    ///     holds this" and "can they" never disagree. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The holders are the active users, other than the system user, with a current membership
    ///         of an active role that is not public, whose grants on the securable or on its owner
    ///         allow it. Public roles and bespoke criteria reach every user alike, so they are
    ///         reported once, as <see cref="AccessHolders.Everyone" />.
    ///     </para>
    ///     <para>
    ///         Only users the caller may read are listed, at most the configured maximum, by display
    ///         name; every holder is counted. A pair the registry does not hold answers with no holder
    ///         and one problem naming no stored permission, without a round trip.
    ///     </para>
    /// </remarks>
    public interface IAccessHoldersResolver
    {
        /// <summary>Resolves who holds a securable.</summary>
        /// <param name="resource">The securable's resource, or a former name of it.</param>
        /// <param name="action">The securable's action.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The holders.</returns>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        /// <exception cref="StaleContextException">The caller's permissions moved again after the batch was recomposed once.</exception>
        /// <exception cref="InvalidOperationException">The caller's filter on the users exceeds the compile ceilings: a configuration problem of the caller's roles, logged with them.</exception>
        Task<AccessHolders> ResolveAsync(string resource, string action, CancellationToken cancellationToken = default);
    }
}
