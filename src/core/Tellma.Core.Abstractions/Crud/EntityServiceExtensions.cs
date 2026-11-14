// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The operations a stack serves by capability: the entity's base class or capability interface
    ///     is the constraint, so a stack without the capability has no such member at compile time.
    /// </summary>
    public static class EntityServiceExtensions
    {
        /// <summary>The built-in action that activates rows.</summary>
        private const string Activate = "activate";

        /// <summary>The built-in action that deactivates rows.</summary>
        private const string Deactivate = "deactivate";

        /// <summary>Activates rows of an activatable stack.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="options">What the action answers; null for the ids alone.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The target ids and, when asked, the rows read back.</returns>
        public static Task<EntitiesResult<TEntity>> ActivateAsync<TEntity, TKey>(
            this EntityService<TEntity, TKey> service,
            IReadOnlyList<TKey> ids,
            ActionOptions? options = null,
            CancellationToken cancellationToken = default)
            where TEntity : Entity<TKey>, IActivatable
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(service);
            return service.ExecuteActionAsync(Activate, ids, arguments: null, options ?? new ActionOptions(), cancellationToken);
        }

        /// <summary>Deactivates rows of an activatable stack.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="options">What the action answers; null for the ids alone.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The target ids and, when asked, the rows read back.</returns>
        public static Task<EntitiesResult<TEntity>> DeactivateAsync<TEntity, TKey>(
            this EntityService<TEntity, TKey> service,
            IReadOnlyList<TKey> ids,
            ActionOptions? options = null,
            CancellationToken cancellationToken = default)
            where TEntity : Entity<TKey>, IActivatable
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(service);
            return service.ExecuteActionAsync(Deactivate, ids, arguments: null, options ?? new ActionOptions(), cancellationToken);
        }

        /// <summary>Reads the children of tree rows, the roots, or both, as one row set.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="request">The parents and the clauses.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        public static Task<QueryRowSet> GetByParentIdsAsync<TEntity, TKey>(
            this EntityService<TEntity, TKey> service,
            ParentIdsRequest request,
            CancellationToken cancellationToken = default)
            where TEntity : TreeEntity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(request);
            return service.Pipeline.GetByParentIdsAsync(request, cancellationToken);
        }

        /// <summary>Deletes tree rows with all their descendants, all or nothing.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The deleted count, descendants included.</returns>
        public static Task<AffectedResult> DeleteWithDescendantsAsync<TEntity, TKey>(
            this EntityService<TEntity, TKey> service,
            IReadOnlyList<TKey> ids,
            CancellationToken cancellationToken = default)
            where TEntity : TreeEntity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(ids);
            return service.Pipeline.DeleteWithDescendantsAsync(ids, cancellationToken);
        }
    }
}
