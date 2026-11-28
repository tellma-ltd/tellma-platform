// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Blobs
{
    /// <summary>Source-generated log messages of the blob sweep and reconcile jobs.</summary>
    /// <remarks>
    ///     The events carry ids, kinds and counts, never storage names or file contents; the tenant
    ///     rides the log scope of the job.
    /// </remarks>
    internal static partial class BlobMaintenanceLog
    {
        /// <summary>One sweep batch ended.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="claimed">The rows the batch claimed.</param>
        /// <param name="deleted">The rows it deleted, objects and row.</param>
        /// <param name="failed">The rows it left claimed because their objects or row could not be deleted.</param>
        [LoggerMessage(
            EventName = "BlobSweepBatch",
            Level = LogLevel.Information,
            Message = "A blob sweep batch claimed {Claimed} blobs, deleted {Deleted} and left {Failed} for a later sweep.")]
        public static partial void SweepBatch(ILogger logger, int claimed, int deleted, int failed);

        /// <summary>
        ///     A swept row could not be deleted because a row still references it; it stays claimed and
        ///     the sweep moves on.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The database's refusal.</param>
        /// <param name="id">The blob's id.</param>
        [LoggerMessage(
            EventName = "BlobSweepRowRetained",
            Level = LogLevel.Warning,
            Message = "The blob {Id} was not deleted because a row still references it; its objects are already gone.")]
        public static partial void SweepRowRetained(ILogger logger, Exception exception, int id);

        /// <summary>
        ///     The store failed a sweep's call; the rows it concerned stay claimed for a later sweep.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The store's failure.</param>
        /// <param name="operation">The store operation: delete or purge.</param>
        /// <param name="rows">The rows left claimed; zero for a purge.</param>
        [LoggerMessage(
            EventName = "BlobSweepStoreFailed",
            Level = LogLevel.Warning,
            Message = "The blob store failed the sweep's {Operation}; {Rows} blobs stay claimed for a later sweep.")]
        public static partial void SweepStoreFailed(ILogger logger, Exception exception, string operation, int rows);

        /// <summary>
        ///     The reconcile released committed blobs that no owner row references: a path that bypassed
        ///     the save pipeline removed or rewrote their owner rows.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="kind">The blobs' kind.</param>
        /// <param name="count">How many were released.</param>
        [LoggerMessage(
            EventName = "BlobReconcileReleased",
            Level = LogLevel.Warning,
            Message = "The blob reconcile released {Count} committed blobs of kind {Kind} that no owner row references.")]
        public static partial void ReconcileReleased(ILogger logger, string kind, int count);
    }
}
