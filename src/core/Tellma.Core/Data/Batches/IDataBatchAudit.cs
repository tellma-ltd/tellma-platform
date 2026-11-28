// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     A test-mode check around every executed batch, resolved from the batch's scope when one is
    ///     registered: the fixture tier compares the tables change tracking saw written with the
    ///     batch's declared writes. Never registered in production.
    /// </summary>
    internal interface IDataBatchAudit
    {
        /// <summary>
        ///     Runs before the command is sent, on a connection of its own, so the round trip's
        ///     connection statistics stay the batch's.
        /// </summary>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>What the check needs afterwards.</returns>
        ValueTask<object?> BeforeAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Runs after the batch succeeded and committed, on a connection of its own, and throws
        ///     when the batch wrote what it did not declare.
        /// </summary>
        /// <param name="state">What <see cref="BeforeAsync" /> returned.</param>
        /// <param name="writtenTables">The batch's declared writes.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>A task that completes when the check passed.</returns>
        ValueTask AfterAsync(object? state, IReadOnlySet<TableName> writtenTables, CancellationToken cancellationToken);
    }
}
