// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;

namespace Tellma.Core.Abstractions.Entities
{
    /// <summary>
    ///     The keyed base of every entity: one surrogate primary key, drawn from the table's own
    ///     sequence. The platform offers <see cref="int" /> keys by default and <see cref="long" /> keys
    ///     for large tables; no other key type is offered, and the startup gate refuses one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On the wire, <c>Id = 0</c> means a new row, a negative id is a temporary id unique within
    ///         one payload (the allocator rewrites it, and every self-typed foreign key and child parent
    ///         key that names it, before anything else reads the payload), and a positive id addresses
    ///         an existing row. The table refuses a stored id that is not positive, and its sequence
    ///         starts at 1000, leaving 1 to 999 for well-known seeded rows.
    ///     </para>
    ///     <para>
    ///         System-written entities (jobs, notifications, blobs) derive from this base directly,
    ///         carry their own timestamps and are never written through a save.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TKey">The key type: <see cref="int" /> or <see cref="long" />.</typeparam>
    public abstract class Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>
        ///     The primary key. Server-owned: the allocator assigns it to a new row, and a client value
        ///     on an existing row only addresses that row.
        /// </summary>
        public TKey Id { get; set; }
    }
}
