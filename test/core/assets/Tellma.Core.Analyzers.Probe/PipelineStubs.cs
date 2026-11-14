// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Entities;

// The service pipeline's contract types TELLMA0005 matches by name, declared here until the
// contract ships them; delete this file when it does. They take the contract's namespace, which is
// what the rule matches, rather than the folder's.
#pragma warning disable IDE0130
namespace Tellma.Core.Abstractions.Crud
#pragma warning restore IDE0130
{
    /// <summary>Stands in for the entity service base.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TKey">The key.</typeparam>
    public abstract class EntityService<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>Stands in for the front-door save.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>A task.</returns>
        public Task SaveAsync(IReadOnlyList<TEntity> entities)
        {
            return Task.CompletedTask;
        }

        /// <summary>Stands in for the validation hook.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>A task.</returns>
        protected virtual Task ValidateAsync(IReadOnlyList<TEntity> entities)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Stands in for the entity service over an <see cref="int" /> key.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    public abstract class EntityService<TEntity> : EntityService<TEntity, int>
        where TEntity : Entity<int>;
}
