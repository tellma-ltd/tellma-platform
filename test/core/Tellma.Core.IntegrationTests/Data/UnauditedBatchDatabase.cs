// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     A batch fixture database whose host runs without the change-tracking audit, so a test can
    ///     overlap batches that write: the audit attributes every change in a batch's window to that
    ///     batch, which holds only while one batch at a time writes.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class UnauditedBatchDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The database, with no audit.</summary>
        public DataBatchDatabase Database { get; } = new(sql, audit: false);

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
