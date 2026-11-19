// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc;
using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §2.3 and §4.2: the tree operations are the service's extension operations, so they
    // need the tree constraint the standard class does not carry; the projector closes this class
    // only over a stack whose capabilities name a tree.

    /// <summary>The web endpoints of a tree stack's tree operations.</summary>
    /// <typeparam name="TEntity">The stack's entity leaf, a tree entity.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    internal static class TreeEndpoints<TEntity, TKey>
        where TEntity : TreeEntity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The path a list of ids binds as.</summary>
        private const string IdsPath = "ids";

        /// <summary>The stack's <c>get-by-parent-ids</c>: the children of the parents, the roots, or both.</summary>
        /// <param name="request">The parents and the clauses.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>A page of the rows alone: no count and no ancestors.</returns>
        public static async Task<RowPage> GetByParentIdsAsync(
            [FromBody] ParentIdsRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            QueryRowSet rows = await service.GetByParentIdsAsync(request, cancellationToken).ConfigureAwait(false);

            // The service answers a bare row set; the wire's result is a page of rows alone.
            return new RowPage(rows, Count: null, Ancestors: null, CountCap: null);
        }

        /// <summary>The stack's <c>delete-with-descendants</c>: the rows and every row below them, deleted.</summary>
        /// <param name="request">The ids.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>How many rows were deleted, descendants included.</returns>
        public static Task<AffectedResult> DeleteWithDescendantsAsync(
            [FromBody] IdsRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            IReadOnlyList<TKey> ids = WireIds.ToKeys<TKey>(request.Ids, IdsPath);
            return service.DeleteWithDescendantsAsync(ids, cancellationToken);
        }
    }
}
