// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A side-effect component: statements every persist batch on the stack carries, and
    ///     best-effort work after it commits. The service's own hooks run first, then every component
    ///     in registration order.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    [CollectionService]
    public interface IPersistEffect<TEntity>
    {
        /// <summary>Appends the effect's statements to the persist batch; they commit or roll back with the operation.</summary>
        /// <param name="context">The persist context, of the operation's kind.</param>
        /// <returns>A task that completes when the statements are appended.</returns>
        Task ContributeAsync(PersistContext<TEntity> context);

        /// <summary>Runs best-effort work after the commit, outside any transaction. Does nothing by default.</summary>
        /// <param name="outcome">The committed operation's outcome, of its kind.</param>
        /// <returns>A task that completes when the work ran.</returns>
        Task AfterCommitAsync(PersistOutcome<TEntity> outcome)
        {
            return Task.CompletedTask;
        }
    }
}
