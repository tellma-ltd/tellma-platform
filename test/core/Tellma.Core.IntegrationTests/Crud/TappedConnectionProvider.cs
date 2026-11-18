// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     A scope's tenant connection provider that remembers the connection it opened last, so a test
    ///     can close it under a batch the scope is running, as a connection lost on the network would be.
    /// </summary>
    /// <param name="inner">The provider it opens connections through.</param>
    internal sealed class TappedConnectionProvider(ITenantConnectionProvider inner) : ITenantConnectionProvider
    {
        /// <summary>The connection opened last; null before the first.</summary>
        public SqlConnection? Last { get; private set; }

        /// <inheritdoc />
        public TenantInfo Tenant => inner.Tenant;

        /// <inheritdoc />
        public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            SqlConnection connection = await inner.OpenAsync(cancellationToken);
            Last = connection;
            return connection;
        }
    }
}
