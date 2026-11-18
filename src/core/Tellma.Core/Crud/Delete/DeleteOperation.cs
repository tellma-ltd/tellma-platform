// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Crud.Save;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Delete
{
    // The deletes are spec 0015 §10, each a unit of work the operation scope names and meters
    // (§16.1): by ids (§10.1), by query (§10.2) and with descendants (§10.3).
    /// <summary>
    ///     The deletes of a stack: by ids, by query and with descendants, each all or nothing, checked
    ///     under the caller's read filter and the Delete grant inside the transaction. Scoped.
    /// </summary>
    /// <param name="writes">The scope's write services.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="engine">The Queryex engine, which compiles a delete by query's filter before any round trip.</param>
    internal sealed class DeleteOperation(SaveOperation writes, CrudOperations operations, QueryexEngine engine)
    {
        /// <summary>Deletes rows by their ids, all or nothing.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="expectedStamps">The stamp each row was read with, parallel to the ids; null to skip the comparison.</param>
        /// <param name="cancellationToken">Cancels the validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The rows deleted.</returns>
        public Task<AffectedResult> DeleteByIdsAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(ids);
            return Run(
                stack,
                CrudOperationNames.Delete,
                ids.Count,
                () => new DeleteRun<TEntity, TKey>(writes, stack, behavior, CrudOperationNames.Delete, cancellationToken).ByIdsAsync(ids, expectedStamps),
                cancellationToken);
        }

        /// <summary>Deletes the rows a filter selects, capped, and only when they number what the caller confirmed.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The filter, its arguments and the confirmed count.</param>
        /// <param name="cancellationToken">Cancels the delete before it persists; a persist already sent completes regardless.</param>
        /// <returns>The rows deleted.</returns>
        public Task<AffectedResult> DeleteByQueryAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, DeleteByQueryRequest request, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(request);
            return Run(
                stack,
                CrudOperationNames.DeleteByQuery,
                Math.Max(request.ExpectedCount, 0),
                () => new DeleteRun<TEntity, TKey>(writes, stack, behavior, CrudOperationNames.DeleteByQuery, cancellationToken).ByQueryAsync(request, engine),
                cancellationToken);
        }

        /// <summary>Deletes tree roots with every descendant, all or nothing.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="ids">The roots' ids.</param>
        /// <param name="cancellationToken">Cancels the validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The rows deleted, descendants included.</returns>
        public Task<AffectedResult> DeleteWithDescendantsAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(ids);
            return Run(
                stack,
                CrudOperationNames.DeleteWithDescendants,
                ids.Count,
                () => new DeleteRun<TEntity, TKey>(writes, stack, behavior, CrudOperationNames.DeleteWithDescendants, cancellationToken).WithDescendantsAsync(ids),
                cancellationToken);
        }

        /// <summary>Runs a delete as a unit of work of the operation scope, metered in the lane its row count picks.</summary>
        /// <param name="stack">The stack.</param>
        /// <param name="operation">The operation's name.</param>
        /// <param name="rows">The rows the delete names or confirmed.</param>
        /// <param name="steps">The delete's steps.</param>
        /// <param name="cancellationToken">The caller's token, which tells an abort from a failure.</param>
        /// <returns>The rows deleted.</returns>
        private Task<AffectedResult> Run(StackDescriptor stack, string operation, int rows, Func<Task<AffectedResult>> steps, CancellationToken cancellationToken)
        {
            string lane = CrudMetrics.Lane(rows, writes.LargeBatchThreshold);
            return operations.RunAsync(stack.Resource, operation, source: null, lane, () => SaveOperation.TranslatedAsync(steps), cancellationToken);
        }
    }
}
