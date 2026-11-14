// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The process-wide store of each user's latest <see cref="UserVersionTagSnapshot" />, keyed by
    ///     tenant and user and bounded by the configured number of entries with the least recently
    ///     used evicted first.
    /// </summary>
    public interface IUserVersionTagSnapshots
    {
        /// <summary>A user's latest snapshot.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The snapshot, or null until the user's first connect on this instance (or after its eviction).</returns>
        UserVersionTagSnapshot? Current(int tenantId, int userId);

        /// <summary>
        ///     Replaces a user's snapshot. Only the batch executor calls it, after every batch that
        ///     carried the caller's connect statements.
        /// </summary>
        /// <param name="snapshot">The snapshot.</param>
        void Replace(UserVersionTagSnapshot snapshot);
    }
}
