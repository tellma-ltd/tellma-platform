// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Opens connections to a tenant named explicitly, and to the catalog: the door of the
    ///     migrator, the reconcilers and every caller outside a tenant scope. Inside a tenant scope,
    ///     data access goes through <see cref="ITenantConnectionProvider" /> instead.
    /// </summary>
    /// <remarks>
    ///     A tenant's string is the one the registry's snapshot composed: the factory keeps no copy, so
    ///     an identical string always reaches the same SqlClient pool and a relocation invalidates one
    ///     thing. The strings are never logged. A singleton.
    /// </remarks>
    public interface ITenantConnectionFactory
    {
        /// <summary>The tenant's composed connection string, from the snapshot; never log it.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="TenantNotFoundException">The snapshot does not hold the tenant.</exception>
        /// <exception cref="TenantUnavailableException">The snapshot is unavailable.</exception>
        string GetConnectionString(int tenantId);

        /// <summary>Opens a connection to the tenant's database.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        /// <exception cref="TenantNotFoundException">The snapshot does not hold the tenant.</exception>
        /// <exception cref="TenantUnavailableException">The snapshot is unavailable.</exception>
        Task<SqlConnection> OpenAsync(int tenantId, CancellationToken cancellationToken = default);

        /// <summary>Opens a connection to the catalog database.</summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        Task<SqlConnection> OpenCatalogAsync(CancellationToken cancellationToken = default);
    }
}
