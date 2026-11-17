// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Crud.Query;

namespace Tellma.Core.Crud
{
    // The factory and the sealed pipeline are spec 0015 §3.1 and §5.1.
    /// <summary>
    ///     Creates the platform's pipeline behind each entity service, bound to the service's hooks
    ///     and to its stack's descriptor. Scoped, like the services that take it.
    /// </summary>
    /// <param name="registry">The stack registry.</param>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="queries">The scope's query operation.</param>
    internal sealed class EntityPipelineFactory(StackRegistry registry, QuerySurfaces surfaces, QueryOperation queries) : IEntityPipelineFactory
    {
        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">No stack has the entity as its leaf.</exception>
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(behavior);

            // The service is constructing: the pipeline only keeps the hooks, and calls none of them now.
            StackDescriptor descriptor = registry.FindByLeaf(typeof(TEntity))
                ?? throw new InvalidOperationException(
                    $"No stack has the entity {typeof(TEntity).FullName} as its leaf, so its service has no pipeline. Contribute the stack with its service.");
            return new EntityPipeline<TEntity, TKey>(descriptor, behavior, surfaces, queries);
        }
    }
}
