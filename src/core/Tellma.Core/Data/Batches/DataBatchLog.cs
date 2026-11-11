// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Data.Batches
{
    /// <summary>Source-generated log messages of the batch executor.</summary>
    /// <remarks>
    ///     The batch text reaches logs through <see cref="SlowRoundTrip" /> alone and is never a span
    ///     attribute. Parameter values are never logged.
    /// </remarks>
    internal static partial class DataBatchLog
    {
        /// <summary>A round trip took longer than the slow threshold; carries the full text.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="attempt">The attempt, from 1.</param>
        /// <param name="durationMs">The duration in milliseconds.</param>
        /// <param name="statements">The statements that carry text.</param>
        /// <param name="tableRows">The rows bound through table-valued parameters.</param>
        /// <param name="rowsRead">The rows read.</param>
        /// <param name="text">The batch text.</param>
        [LoggerMessage(
            EventName = "DataBatch.SlowRoundTrip",
            Level = LogLevel.Information,
            Message = "A {Purpose} round trip of {Operation} on tenant {TenantId} took {DurationMs} ms (attempt {Attempt}, {Statements} statements, {TableRows} table rows bound, {RowsRead} rows read):\n{Text}")]
        public static partial void SlowRoundTrip(
            ILogger logger, int tenantId, string operation, string purpose, int attempt, double durationMs, int statements, long tableRows, long rowsRead, string text);

        /// <summary>A commit callback threw; the batch had committed, so the failure is logged, never thrown.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The callback's exception.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "DataBatch.OnCommittedFailed",
            Level = LogLevel.Error,
            Message = "A commit callback of {Operation} on tenant {TenantId} failed after its batch committed.")]
        public static partial void OnCommittedFailed(ILogger logger, Exception exception, string operation, int tenantId);

        /// <summary>Rolling back an explicit transaction after a failure failed too; the original failure surfaces.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The rollback's exception.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "DataBatch.RollbackFailed",
            Level = LogLevel.Warning,
            Message = "Rolling back the explicit transaction of {Operation} on tenant {TenantId} failed after the batch failed.")]
        public static partial void RollbackFailed(ILogger logger, Exception exception, string operation, int tenantId);
    }
}
