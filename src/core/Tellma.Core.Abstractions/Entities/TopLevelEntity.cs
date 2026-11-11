// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     An entity that is saved and addressed on its own, with the four audit columns. Every audit
    ///     column is server-owned and stamped server-side, once per batch, from the database clock.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ModifiedAt" /> is the concurrency token: a save reads it as the stamp the
    ///         client expects, and only user-visible mutations (a save, an update action, an invite)
    ///         stamp it. Bookkeeping never touches the entity row. No table carries a
    ///         <c>rowversion</c> column.
    ///     </para>
    ///     <para>
    ///         The two user columns are foreign keys to <c>core.Users</c>, and both timestamps are
    ///         <c>datetimeoffset(7)</c> instants written at offset zero.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
    public abstract class TopLevelEntity<TKey> : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>When the row was created; server-owned, <c>datetimeoffset(7)</c>.</summary>
        public DateTimeOffset CreatedAt { get; set; }

        /// <summary>The user who created the row; server-owned, a foreign key to <c>core.Users</c>.</summary>
        public int CreatedById { get; set; }

        /// <summary>
        ///     When the row was last changed by a user-visible mutation; server-owned,
        ///     <c>datetimeoffset(7)</c>, and the row's concurrency token.
        /// </summary>
        public DateTimeOffset ModifiedAt { get; set; }

        /// <summary>The user who last changed the row; server-owned, a foreign key to <c>core.Users</c>.</summary>
        public int ModifiedById { get; set; }
    }

    /// <summary>A top-level entity with an <see cref="int" /> key, the default.</summary>
    public abstract class TopLevelEntity : TopLevelEntity<int>;
}
