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

        /// <summary>The commit probe failed, so whether the ambiguous round trip committed is unknown.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The probe's exception.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "DataBatch.CommitProbeFailed",
            Level = LogLevel.Warning,
            Message = "The commit probe of {Operation} on tenant {TenantId} failed, so whether its round trip committed is unknown.")]
        public static partial void CommitProbeFailed(ILogger logger, Exception exception, string operation, int tenantId);

        /// <summary>An attempt of the commit probe failed transiently or lost its connection; the probe runs again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The attempt's failure.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="attempt">The failed attempt, from 1.</param>
        /// <param name="delayMs">The wait before the next attempt, in milliseconds.</param>
        [LoggerMessage(
            EventName = "DataBatch.CommitProbeRetrying",
            Level = LogLevel.Information,
            Message = "Attempt {Attempt} of the commit probe of {Operation} on tenant {TenantId} failed; it runs again in {DelayMs} ms.")]
        public static partial void CommitProbeRetrying(ILogger logger, Exception exception, string operation, int tenantId, int attempt, double delayMs);

        /// <summary>A round trip failed and is sent again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The attempt's failure.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="attempt">The failed attempt, from 1.</param>
        /// <param name="retryClass">Why it may be re-run: <c>reported</c> or <c>ambiguous</c>.</param>
        /// <param name="delayMs">The wait before the next attempt, in milliseconds.</param>
        [LoggerMessage(
            EventName = "DataBatch.Retrying",
            Level = LogLevel.Information,
            Message = "Attempt {Attempt} of a round trip of {Operation} on tenant {TenantId} failed ({RetryClass}); it is sent again in {DelayMs} ms.")]
        public static partial void Retrying(ILogger logger, Exception exception, string operation, int tenantId, int attempt, string retryClass, double delayMs);

        /// <summary>Recording what a failed batch read (its tags, its metering) failed; the batch's own failure surfaces.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The recording's exception.</param>
        /// <param name="operation">The scope's operation.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "DataBatch.FailureObservationFailed",
            Level = LogLevel.Warning,
            Message = "Recording the version tags of a failed batch of {Operation} on tenant {TenantId} failed; the batch's own failure surfaces.")]
        public static partial void FailureObservationFailed(ILogger logger, Exception exception, string operation, int tenantId);
    }
}
