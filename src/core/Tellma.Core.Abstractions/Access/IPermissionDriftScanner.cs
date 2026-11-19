// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The scan is spec 0014 §9.
    /// <summary>
    ///     Finds a tenant's stored permissions that grant nothing under the running deployment and
    ///     the tenant's current schema: an unknown resource or action, a filter where none binds, an
    ///     unsupported query-language version, or a filter that no longer validates. One per scope,
    ///     used in the platform's own scope of the tenant.
    /// </summary>
    /// <remarks>
    ///     The scan reads every permission once, writes nothing and takes no lock. The unrestricted
    ///     wildcard pair never drifts but on a public role.
    /// </remarks>
    public interface IPermissionDriftScanner
    {
        /// <summary>Scans every stored permission of the scope's tenant.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The permissions that grant nothing, by permission id, one item each.</returns>
        /// <exception cref="InvalidOperationException">The scope is not the platform's own.</exception>
        Task<IReadOnlyList<PermissionDriftItem>> ScanAsync(CancellationToken cancellationToken = default);
    }
}
