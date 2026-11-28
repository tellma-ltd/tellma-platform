// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The data layer's door to SQL inside a tenant scope: the connection to the tenant the scope's
    ///     request context is bound to. Scoped.
    /// </summary>
    /// <remarks>
    ///     The provider binds the tenant of the scope's request context, so it refuses a scope with no
    ///     tenant (<see cref="InvalidOperationException" />) rather than guess one. A batch is bound to
    ///     one tenant for its lifetime and never changes database.
    /// </remarks>
    public interface ITenantConnectionProvider
    {
        /// <summary>The scope's tenant, from the registry's snapshot.</summary>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        TenantInfo Tenant { get; }

        /// <summary>Opens a connection to the scope's tenant database.</summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default);
    }
}
