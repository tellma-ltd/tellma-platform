// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Opens connections to an explicitly named tenant from the registry's composed string, and to
    ///     the catalog. Keeps no string of its own.
    /// </summary>
    /// <param name="registry">The registry, whose snapshot holds every tenant's string.</param>
    /// <param name="catalog">The catalog connection.</param>
    internal sealed class TenantConnectionFactory(ITenantRegistry registry, CatalogConnection catalog) : ITenantConnectionFactory
    {
        /// <inheritdoc />
        public string GetConnectionString(int tenantId)
        {
            return registry.Get(tenantId).ConnectionString;
        }

        /// <inheritdoc />
        public async Task<SqlConnection> OpenAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            SqlConnection connection = new(GetConnectionString(tenantId));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public Task<SqlConnection> OpenCatalogAsync(CancellationToken cancellationToken = default)
        {
            return catalog.OpenAsync(cancellationToken);
        }
    }
}
