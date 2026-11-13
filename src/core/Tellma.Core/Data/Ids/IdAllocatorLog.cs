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
        /// <param name="dropped">How many buffered ids at or below the table's largest id were dropped.</param>
        [LoggerMessage(
            EventName = "IdAllocator.Healed",
            Level = LogLevel.Warning,
            Message = "The sequence {Sequence} of tenant {TenantId} was behind its table: it returned {First} while the table held {MaxId}. The gap was consumed with a reservation of {Consumed} ids, and {Dropped} buffered ids at or below the table's largest id were dropped.")]
        public static partial void Healed(ILogger logger, string sequence, int tenantId, long first, long maxId, long consumed, long dropped);

        /// <summary>
        ///     A refill found a sequence behind its table: an out-of-band insert wrote ids the sequence
        ///     had not issued. The refill kept only the ids above the table, and the buffer dropped its
        ///     ids at or below them; the next reservation that reads a range below the table heals.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="first">The first id of the range found behind.</param>
        /// <param name="maxId">The largest id the table held.</param>
        /// <param name="dropped">How many buffered ids at or below the table's largest id were dropped.</param>
        [LoggerMessage(
            EventName = "IdAllocator.BehindOnRefill",
            Level = LogLevel.Warning,
            Message = "A refill found the sequence {Sequence} of tenant {TenantId} behind its table: it returned {First} while the table held {MaxId}. Only the ids above the table were kept, and {Dropped} buffered ids at or below it were dropped.")]
        public static partial void BehindOnRefill(ILogger logger, string sequence, int tenantId, long first, long maxId, long dropped);

        /// <summary>
        ///     Ids a reservation or a take gave back were at or below the largest id a read found the
        ///     table holding, so they were discarded rather than buffered again.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sequence">The sequence.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="discarded">How many ids were discarded.</param>
        [LoggerMessage(
            EventName = "IdAllocator.StaleDiscarded",
            Level = LogLevel.Warning,
            Message = "{Discarded} ids of {Sequence} of tenant {TenantId} came back to the buffer at or below the largest id its table holds, and were discarded.")]
        public static partial void StaleDiscarded(ILogger logger, string sequence, int tenantId, long discarded);

        /// <summary>
        ///     A tenant's buffers were forgotten because the database behind it may change: it was
        ///     frozen for a relocation or a restore, left the freeze, or left the registry.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="dropped">How many buffered ids were dropped.</param>
        [LoggerMessage(
            EventName = "IdAllocator.TenantForgotten",
            Level = LogLevel.Information,
            Message = "The id buffers of tenant {TenantId} were forgotten, dropping {Dropped} ids: the database behind the tenant may change.")]
        public static partial void TenantForgotten(ILogger logger, int tenantId, long dropped);

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
