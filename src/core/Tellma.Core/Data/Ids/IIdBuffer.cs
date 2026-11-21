// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Ids
{
    // The save pipeline decides immediate against deferred before any batch exists (spec 0015 §6.4):
    // the validators of an immediate reservation start before round trip 1 composes, so the probe
    // cannot ride a batch the way IIdAllocator.Reserve does.
    /// <summary>
    ///     The allocator's buffer, probed without a batch: a reservation the buffer covers in full,
    ///     for a caller that must know before any batch composes whether its ids are in hand.
    /// </summary>
    internal interface IIdBuffer
    {
        /// <summary>
        ///     Reserves the ids a payload's new rows need from the buffers of a tenant's database alone:
        ///     the reservation is immediate, or none is made and the buffers keep every id. A buffer
        ///     filled from another database than the one the tenant lives in now is dropped, never
        ///     served.
        /// </summary>
        /// <param name="database">The tenant's database, which names the tenant and where its database lives now.</param>
        /// <param name="entityType">The payload's root entity.</param>
        /// <param name="rows">The payload's roots, children included through their collections.</param>
        /// <returns>The immediate reservation; null when some table's buffer holds fewer ids than its new rows.</returns>
        /// <exception cref="ArgumentException">
        ///     The database is not one the platform composed, two new rows of a table share a temporary
        ///     id, or a row is null or of another type.
        /// </exception>
        IdReservation? ReserveBuffered(ITenantDatabase database, Type entityType, IReadOnlyList<object> rows);
    }
}
