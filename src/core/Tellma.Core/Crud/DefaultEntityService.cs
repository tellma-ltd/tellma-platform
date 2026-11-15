// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The service of a stack registered without one (<c>contribution.Entity&lt;TEntity&gt;()</c>):
    ///     the standard operations and no hook of its own, closed over the stack's leaf.
    /// </summary>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    internal sealed class DefaultEntityService<TEntity, TKey>(IEntityPipelineFactory factory) : EntityService<TEntity, TKey>(factory)
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>;
}
