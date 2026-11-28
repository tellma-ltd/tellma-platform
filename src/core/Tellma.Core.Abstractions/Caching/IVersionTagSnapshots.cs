// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The process-wide store of each tenant's latest <see cref="VersionTagSnapshot" />, bounded
    ///     by the configured number of entries with the least recently used tenant evicted first.
    ///     Every cache of the process validates against it.
    /// </summary>
    public interface IVersionTagSnapshots
    {
        /// <summary>The tenant's latest snapshot.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>
        ///     The snapshot; an empty one for a tenant this instance has not read (or has evicted),
        ///     which is what makes every cache read on a fresh instance load.
        /// </returns>
        VersionTagSnapshot Current(int tenantId);

        /// <summary>Replaces a tenant's snapshot. Only the batch executor calls it, after every tenant batch.</summary>
        /// <param name="snapshot">The snapshot, holding tenant-level names only.</param>
        void Replace(VersionTagSnapshot snapshot);
    }
}
