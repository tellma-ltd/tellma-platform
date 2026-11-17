// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Access
{
    // The directory is spec 0014 §10.
    /// <summary>The recipients of a tenant's administrative notices. One per scope.</summary>
    /// <remarks>
    ///     The directory checks no permission of the scope's caller: it serves the platform's own
    ///     notices. A caller that would hand its result to a user decides that user's access to it
    ///     first.
    /// </remarks>
    public interface IAdministratorDirectory
    {
        /// <summary>
        ///     The tenant's administrators: active individuals, invited or joined, with a membership of
        ///     the administrator role that holds now; ordered by id, at most twenty.
        /// </summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The users' ids.</returns>
        /// <exception cref="TenantNotFoundException">The scope's caller is not, or no longer, an active user of the tenant.</exception>
        Task<IReadOnlyList<int>> GetAdministratorIdsAsync(CancellationToken cancellationToken = default);
    }
}
