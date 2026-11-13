// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;

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

        /// <summary>The tenant's multilingual shape, which picks the query schema the batch's query statements compile against.</summary>
        /// <exception cref="InvalidOperationException">The connection has no tenant settings to read it from.</exception>
        MultilingualShape Shape { get; }

        /// <summary>The context values the batch's query statements bind: today, now, the user and the time zone.</summary>
        /// <exception cref="InvalidOperationException">The connection has no request context to read them from.</exception>
        QueryexContextValues Context { get; }

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
