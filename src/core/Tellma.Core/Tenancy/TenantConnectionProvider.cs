// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Opens connections to the tenant the scope's request context is bound to. Keeps no tenant
    ///     and no string of its own: every call reads the scope's context and the registry's
    ///     snapshot.
    /// </summary>
    /// <param name="accessor">The scope's request context.</param>
    /// <param name="registry">The registry, whose snapshot holds the tenant.</param>
    /// <param name="connections">Opens the connection of a named tenant.</param>
    internal sealed class TenantConnectionProvider(
        IRequestContextAccessor accessor, ITenantRegistry registry, ITenantConnectionFactory connections)
        : ITenantConnectionProvider
    {
        /// <inheritdoc />
        public TenantInfo Tenant => registry.Get(BoundTenantId());

        /// <inheritdoc />
        public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            return connections.OpenAsync(BoundTenantId(), cancellationToken);
        }

        /// <summary>The id of the tenant the scope is bound to.</summary>
        /// <returns>The tenant id.</returns>
        /// <exception cref="InvalidOperationException">The scope is not bound to a tenant.</exception>
        private int BoundTenantId()
        {
            // The scope's tenant and no other: the provider never takes a tenant id from a caller.
            return accessor.Current.RequireTenant().Id;
        }
    }
}
