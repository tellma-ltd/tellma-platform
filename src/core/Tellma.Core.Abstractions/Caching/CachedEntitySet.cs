// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The whole table of one cacheable entity as one tenant holds it: every row, inactive ones
    ///     included (consumers filter), in key order, and the same rows by id. Shared by every reader
    ///     of the tenant and never to be changed.
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <param name="Tag">The entity's tag the rows are current for.</param>
    /// <param name="LoadedAt">The instance clock when the rows were read.</param>
    /// <param name="Items">The rows, in key order.</param>
    /// <param name="ById">The rows by id.</param>
    public sealed record CachedEntitySet<TEntity>(
        VersionTag Tag, DateTimeOffset LoadedAt, IReadOnlyList<TEntity> Items, IReadOnlyDictionary<int, TEntity> ById)
        where TEntity : class
    {
        /// <summary>
        ///     The format version of the list's client-facing shape, part of every wire tag of a list.
        ///     Raise it whenever the shape changes, so every client refetches.
        /// </summary>
        public const int FormatVersion = 1;
    }
}
