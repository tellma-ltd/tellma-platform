// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Where a batch's round trip gets its connection: a fresh one from the scope's provider, or a
    ///     factory handle's own.
    /// </summary>
    internal interface IBatchConnection
    {
        /// <summary>The tenant whose database the connection reaches.</summary>
        int TenantId { get; }

        /// <summary>Acquires an open connection for one round trip.</summary>
        /// <param name="cancellationToken">Cancels opening or waiting for the connection.</param>
        /// <returns>The open connection.</returns>
        ValueTask<SqlConnection> AcquireAsync(CancellationToken cancellationToken);

        /// <summary>Gives the connection back once the round trip is over.</summary>
        /// <param name="connection">The connection <see cref="AcquireAsync" /> returned.</param>
        /// <returns>A task that completes when it is released.</returns>
        ValueTask ReleaseAsync(SqlConnection connection);
    }
}
