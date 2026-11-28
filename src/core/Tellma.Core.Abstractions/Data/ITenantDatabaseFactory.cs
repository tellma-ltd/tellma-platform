// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     Opens a tenant database outside any tenant scope: the migrator, and background handlers
    ///     that reach a tenant they were not scoped to. A singleton.
    /// </summary>
    public interface ITenantDatabaseFactory
    {
        /// <summary>Opens a handle bound to a tenant, with a connection to its database.</summary>
        /// <param name="tenantId">The tenant whose database to open.</param>
        /// <param name="services">
        ///     The caller's scope, which supplies the batch contributors and the scope's data-access
        ///     counters.
        /// </param>
        /// <param name="cancellationToken">Cancels opening the connection.</param>
        /// <returns>The handle, which the caller disposes.</returns>
        Task<ITenantDatabaseHandle> OpenAsync(int tenantId, IServiceProvider services, CancellationToken cancellationToken = default);
    }
}
