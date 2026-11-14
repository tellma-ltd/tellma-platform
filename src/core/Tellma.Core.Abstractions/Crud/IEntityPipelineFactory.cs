// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Creates the pipeline behind an entity service, bound to the service's hooks. A scoped service
    ///     the runtime registers once; a service subclass passes it through to the base constructor.
    ///     Not for distributions to implement: a startup check refuses any registration that is not the
    ///     platform's.
    /// </summary>
    public interface IEntityPipelineFactory
    {
        /// <summary>Creates a pipeline bound to a service's hooks.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="behavior">The service's hooks.</param>
        /// <returns>The pipeline, one per service instance.</returns>
        IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>;
    }
}
