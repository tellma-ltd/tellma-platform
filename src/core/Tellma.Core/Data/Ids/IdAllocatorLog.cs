// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Data.Ids
{
    /// <summary>The id allocator's log messages.</summary>
    internal static partial class IdAllocatorLog
    {
        /// <summary>
        ///     A sequence was found behind its table at reservation and the gap was consumed: an
        ///     out-of-band insert wrote ids the sequence had not issued.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="first">The first id of the range found behind.</param>
        /// <param name="maxId">The largest id the table held.</param>
        /// <param name="consumed">How many ids the healing reservation asked for.</param>
        [LoggerMessage(
            EventName = "IdAllocator.Healed",
            Level = LogLevel.Warning,
            Message = "The sequence {Sequence} of tenant {TenantId} was behind its table: it returned {First} while the table held {MaxId}. The gap was consumed with a reservation of {Consumed} ids.")]
        public static partial void Healed(ILogger logger, string sequence, int tenantId, long first, long maxId, long consumed);

        /// <summary>A duplicate primary key surfaced on a table the buffer serves, so its buffered ranges were dropped.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="dropped">How many buffered ids were dropped.</param>
        [LoggerMessage(
            EventName = "IdAllocator.BufferDropped",
            Level = LogLevel.Warning,
            Message = "A duplicate primary key surfaced on the table of {Sequence} of tenant {TenantId}; the {Dropped} buffered ids were dropped, so the next reservation reads the table afresh.")]
        public static partial void BufferDropped(ILogger logger, string sequence, int tenantId, long dropped);
    }
}
