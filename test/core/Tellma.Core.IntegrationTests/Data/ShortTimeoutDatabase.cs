// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     A batch fixture database whose host times every command out after one second, so a test
    ///     can make a round trip time out without waiting the default thirty seconds.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class ShortTimeoutDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The command timeout of the host's batches.</summary>
        public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(1);

        /// <summary>The database, with the host's short timeout.</summary>
        public DataBatchDatabase Database { get; } = new(sql, CommandTimeout);

        /// <inheritdoc />
        public ValueTask InitializeAsync()
        {
            return Database.InitializeAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return Database.DisposeAsync();
        }
    }
}
