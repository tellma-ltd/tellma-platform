// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The answer of every operation that returns rows in details shape: <c>get</c>,
    ///     <c>get-by-ids</c>, <c>all</c>, <c>save</c>, <c>activate</c>, <c>deactivate</c> and an
    ///     entity action without a result of its own.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ids" /> is in input order for a save and in request order for a get by ids, an
    ///     absent or hidden id omitted; <see cref="Entities" /> is parallel to it, or empty when the
    ///     caller asked for ids alone. Ids are written in the stack's key type: a JSON number for an
    ///     <see cref="int" /> key, a string for a <see cref="long" /> key.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class EntitiesResult<TEntity>
    {
        /// <summary>The rows' ids, in the operation's order.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<long> Ids
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Ids));
        }

        /// <summary>The rows in details shape, parallel to <see cref="Ids" />; empty when the caller asked for ids alone.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TEntity> Entities
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Entities));
        }

        /// <summary>The rows the entities point at, each restricted to its own entity's related projection; null when none.</summary>
        public RelatedEntities? Related { get; init; }

        /// <summary>The details extras the request included, by name; null when none.</summary>
        public IReadOnlyDictionary<string, JsonElement>? Extras { get; init; }
    }
}
