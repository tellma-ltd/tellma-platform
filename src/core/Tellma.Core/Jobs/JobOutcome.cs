// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Jobs
{
    /// <summary>What a completion records on one job.</summary>
    internal enum JobOutcomeKind
    {
        /// <summary>The job succeeded.</summary>
        Succeeded,

        /// <summary>The job is queued again after a delay, its attempt counted.</summary>
        Retry,

        /// <summary>The job is queued again at once, its attempt uncounted: the handler never touched it.</summary>
        Released,

        /// <summary>The job failed.</summary>
        Failed,

        /// <summary>The job was stopped.</summary>
        Cancelled,

        /// <summary>The job waits for its initiator to confirm the warnings it raised.</summary>
        PendingConfirmation,
    }

    /// <summary>
    ///     The outcome a completion records on one job: its status, when it runs again, the error
    ///     group, the checkpoint and the warnings it waits on, as the completion statement binds them.
    /// </summary>
    /// <param name="Kind">What the completion records.</param>
    /// <param name="RetryAfterSeconds">How long until the job is due again; null to keep its due time.</param>
    /// <param name="ErrorCode">The error's code; null when the outcome records none.</param>
    /// <param name="ErrorArgumentsJson">The error's arguments as JSON; null when none.</param>
    /// <param name="ErrorTraceId">The trace id of the activity that recorded the error; null when none.</param>
    /// <param name="StateJson">The checkpoint to keep; null to keep the stored one.</param>
    /// <param name="PendingConfirmationsJson">The warnings the job waits on; set on a pending confirmation alone.</param>
    internal sealed record JobOutcome(
        JobOutcomeKind Kind,
        int? RetryAfterSeconds,
        string? ErrorCode,
        string? ErrorArgumentsJson,
        string? ErrorTraceId,
        string? StateJson,
        string? PendingConfirmationsJson)
    {
        /// <summary>The job's status once the completion recorded the outcome.</summary>
        public JobStatus Status => Kind switch
        {
            JobOutcomeKind.Succeeded => JobStatus.Succeeded,
            JobOutcomeKind.Retry or JobOutcomeKind.Released => JobStatus.Pending,
            JobOutcomeKind.Failed => JobStatus.Failed,
            JobOutcomeKind.Cancelled => JobStatus.Cancelled,
            JobOutcomeKind.PendingConfirmation => JobStatus.PendingConfirmation,
            _ => throw new InvalidOperationException($"The outcome kind {Kind} is not defined."),
        };

        /// <summary>Whether the outcome queues the job again at once: a retry with no delay.</summary>
        public bool RunsAgainNow => Kind == JobOutcomeKind.Retry && RetryAfterSeconds == 0;

        /// <summary>A success, keeping a checkpoint.</summary>
        /// <param name="stateJson">The last checkpoint.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome Succeeded(string? stateJson)
        {
            return new(JobOutcomeKind.Succeeded, null, null, null, null, stateJson, null);
        }

        /// <summary>A retry after a delay, with or without an error.</summary>
        /// <param name="afterSeconds">The delay, in seconds.</param>
        /// <param name="error">The error; null for none.</param>
        /// <param name="stateJson">The last checkpoint.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome Retry(int afterSeconds, JobOutcomeError? error, string? stateJson)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(afterSeconds);
            return new(JobOutcomeKind.Retry, afterSeconds, error?.Code, error?.ArgumentsJson, error?.TraceId, stateJson, null);
        }

        /// <summary>A release: the job is queued again with its attempt uncounted and its checkpoint as stored.</summary>
        /// <returns>The outcome.</returns>
        public static JobOutcome Released()
        {
            return new(JobOutcomeKind.Released, null, null, null, null, null, null);
        }

        /// <summary>A failure.</summary>
        /// <param name="error">The error.</param>
        /// <param name="stateJson">The last checkpoint.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome Failed(JobOutcomeError error, string? stateJson)
        {
            ArgumentNullException.ThrowIfNull(error);
            return new(JobOutcomeKind.Failed, null, error.Code, error.ArgumentsJson, error.TraceId, stateJson, null);
        }

        /// <summary>A stop.</summary>
        /// <param name="error">The reason: the requested cancel, or the missing entity.</param>
        /// <param name="stateJson">The last checkpoint.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome Cancelled(JobOutcomeError error, string? stateJson)
        {
            ArgumentNullException.ThrowIfNull(error);
            return new(JobOutcomeKind.Cancelled, null, error.Code, error.ArgumentsJson, error.TraceId, stateJson, null);
        }

        /// <summary>A wait for confirmation: due now, with no error and the warnings recorded.</summary>
        /// <param name="pendingConfirmationsJson">The warnings.</param>
        /// <param name="stateJson">The last checkpoint.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome PendingConfirmation(string pendingConfirmationsJson, string? stateJson)
        {
            ArgumentException.ThrowIfNullOrEmpty(pendingConfirmationsJson);
            return new(JobOutcomeKind.PendingConfirmation, 0, null, null, null, stateJson, pendingConfirmationsJson);
        }

        /// <summary>The outcome as a row of the completion statement's table.</summary>
        /// <param name="jobId">The job's id.</param>
        /// <returns>The row.</returns>
        public JobOutcomeList ToRow(int jobId)
        {
            return new JobOutcomeList
            {
                Id = jobId,
                Status = Status,
                Released = Kind == JobOutcomeKind.Released,
                RetryAfterSeconds = RetryAfterSeconds,
                ErrorCode = ErrorCode,
                ErrorArgumentsJson = ErrorArgumentsJson,
                ErrorTraceId = ErrorTraceId,
                StateJson = StateJson,
                PendingConfirmationsJson = PendingConfirmationsJson,
            };
        }
    }

    /// <summary>The error group an outcome records: the code, its arguments and the trace id, written together.</summary>
    /// <param name="Code">The error's code, a resource key.</param>
    /// <param name="ArgumentsJson">Its arguments as JSON; null when none.</param>
    /// <param name="TraceId">The trace id of the activity that recorded it; null when no activity was recorded.</param>
    internal sealed record JobOutcomeError(string Code, string? ArgumentsJson, string? TraceId);
}
