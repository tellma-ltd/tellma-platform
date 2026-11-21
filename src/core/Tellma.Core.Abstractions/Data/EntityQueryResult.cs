// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>The materialised result of an <see cref="EntityQuery{TEntity}" />.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="Entities">The entities, in the query's order, with their loaded collections.</param>
    /// <param name="Related">The related entities the selected navigations reach, by entity.</param>
    public sealed record EntityQueryResult<TEntity>(IReadOnlyList<TEntity> Entities, RelatedEntities Related)
        where TEntity : class;
}
