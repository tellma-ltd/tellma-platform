// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>How a cache read was answered; also the value of the requests counter's outcome tag.</summary>
    public enum CacheOutcome
    {
        /// <summary>The entry was present and carried the current tag: no round trip.</summary>
        Hit,

        /// <summary>No entry was present: the value was loaded and stored.</summary>
        Miss,

        /// <summary>An entry was present under another tag: the value was loaded and replaced it.</summary>
        Stale,

        /// <summary>
        ///     A cacheable list exceeded its row limit: nothing was stored and no value is returned,
        ///     and the consumer reads the table through an ordinary query.
        /// </summary>
        Oversized,

        /// <summary>
        ///     The kind's tag row is missing from the database: the value was loaded but not stored,
        ///     and every read loads until the migrator seeds the row.
        /// </summary>
        Uncached,
    }
}
