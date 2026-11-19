// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    /// <summary>What the outcome rules read of one item once its invocation ended.</summary>
    /// <param name="Attempts">The row's attempts, the claim's included.</param>
    /// <param name="ConfirmedKeys">The confirmation keys the row carries; null when none.</param>
    /// <param name="Reason">Why the item's token was cancelled; none when it was not.</param>
    /// <param name="Mark">The handler's explicit outcome; null when it left the item unmarked.</param>
    /// <param name="Observed">Whether the handler reported progress, a checkpoint or an outcome for the item.</param>
    /// <param name="StateJson">The item's last checkpoint, which the completion binds.</param>
    internal sealed record JobItemFacts(
        int Attempts,
        string? ConfirmedKeys,
        JobCancellationReason Reason,
        JobItemMark? Mark,
        bool Observed,
        string? StateJson);

    /// <summary>The handler options the rules apply: the attempts allowed and the backoff's base and ceiling.</summary>
    /// <param name="MaxAttempts">The attempts a job is allowed.</param>
    /// <param name="RetryBaseSeconds">The backoff's base, in seconds.</param>
    /// <param name="RetryMaxSeconds">The backoff's ceiling, in seconds.</param>
    internal sealed record JobRetryPolicy(int MaxAttempts, int RetryBaseSeconds, int RetryMaxSeconds)
    {
        /// <summary>The policy a handler declares.</summary>
        /// <param name="options">The handler's attribute.</param>
        /// <returns>The policy.</returns>
        public static JobRetryPolicy Of(JobHandlerAttribute options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return new(options.MaxAttempts, options.RetryBaseSeconds, options.RetryMaxSeconds);
        }
    }

    // Spec 0020 §3.4, in its order: a lost lease discards every outcome; a marked item keeps its
    // own; an unmarked item whose cancel was requested is cancelled; every other follows how the
    // invocation ended. Then a retry at the last allowed attempt fails, with its error or with
    // Jobs.AttemptsExhausted. Every outcome that records a code records the trace id of the
    // activity the invocation ran in.
    /// <summary>Maps each item of an ended invocation to the outcome its completion records.</summary>
    internal static class JobOutcomeRules
    {
        /// <summary>The outcomes of every item of a partition, in item order; null when any item's lease was lost, so nothing completes.</summary>
        /// <param name="items">The items, in claim order.</param>
        /// <param name="ending">How the invocation ended.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity; null when none was recorded.</param>
        /// <param name="random">The source of the backoff's jitter.</param>
        /// <returns>The outcomes, or null.</returns>
        public static IReadOnlyList<JobOutcome>? Decide(
            IReadOnlyList<JobItemFacts> items, JobEnding ending, JobRetryPolicy policy, string? traceId, Random random)
        {
            ArgumentNullException.ThrowIfNull(items);

            // --- Rule 1: a lost lease anywhere leaves the rows to the instance that took them ---
            return items.Any(static item => item.Reason == JobCancellationReason.LeaseLost)
                ? null
                : [.. items.Select(item => Decide(item, ending, policy, traceId, random))];
        }

        /// <summary>The outcome of one item whose lease holds.</summary>
        /// <param name="item">The item.</param>
        /// <param name="ending">How the invocation ended.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <param name="random">The source of the backoff's jitter.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="InvalidOperationException">The item's lease was lost, which only the whole partition can answer.</exception>
        public static JobOutcome Decide(JobItemFacts item, JobEnding ending, JobRetryPolicy policy, string? traceId, Random random)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(ending);
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(random);
            if (item.Reason == JobCancellationReason.LeaseLost)
            {
                throw new InvalidOperationException("An item whose lease was lost has no outcome: the partition's completion is not executed.");
            }

            // --- Rules 2 to 4, then the last allowed attempt ---
            JobOutcome outcome = item.Mark is JobItemMark mark
                ? Marked(item, mark, policy, traceId, random)
                : item.Reason == JobCancellationReason.CancelRequested
                    ? JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, traceId), item.StateJson)
                    : Unmarked(item, ending, policy, traceId, random);
            return Exhausted(outcome, item, policy, traceId);
        }

        /// <summary>
        ///     The outcome of one item of a completion whose handler statements failed: the writes were
        ///     discarded, so the item's mark no longer stands; a requested cancel still stops it, and
        ///     every other item follows the failure as an exception from the invocation.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="failure">How the completion failed: a validation or an unexpected failure.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <param name="random">The source of the backoff's jitter.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome AfterFailedCompletion(JobItemFacts item, JobEnding failure, JobRetryPolicy policy, string? traceId, Random random)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(failure);
            ArgumentNullException.ThrowIfNull(policy);
            JobOutcome outcome = item.Reason == JobCancellationReason.CancelRequested
                ? JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, traceId), item.StateJson)
                : Unmarked(item, failure, policy, traceId, random);
            return Exhausted(outcome, item, policy, traceId);
        }

        /// <summary>
        ///     The outcome of one item of a completion whose guard failed: the work runs again at once
        ///     under the fresh permissions, unless that was its last allowed attempt.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <returns>The outcome.</returns>
        public static JobOutcome AfterGuardFailure(JobItemFacts item, JobRetryPolicy policy, string? traceId)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(policy);

            // Spec 0020 §3.4 fails every retry at the last allowed attempt, this one included: a guard
            // that fails on that attempt (any permissions change, a role save among them) fails the
            // job with Jobs.AttemptsExhausted, as a stop after progress on it does. Read literally
            // until the spec says otherwise.
            return Exhausted(JobOutcome.Retry(0, null, item.StateJson), item, policy, traceId);
        }

        /// <summary>The outcome a handler's explicit mark records.</summary>
        /// <param name="item">The item.</param>
        /// <param name="mark">The mark.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <param name="random">The source of the backoff's jitter.</param>
        /// <returns>The outcome.</returns>
        private static JobOutcome Marked(JobItemFacts item, JobItemMark mark, JobRetryPolicy policy, string? traceId, Random random)
        {
            JobOutcomeError? error = mark.Error is null ? null : new JobOutcomeError(mark.Error.Code, mark.ErrorArgumentsJson, traceId);
            return mark.Kind switch
            {
                JobItemMarkKind.Succeeded => JobOutcome.Succeeded(item.StateJson),
                JobItemMarkKind.Retry => JobOutcome.Retry(
                    mark.RetryAfter is TimeSpan after ? JobBackoff.SuppliedSeconds(after) : Backoff(item, policy, random),
                    error,
                    item.StateJson),
                JobItemMarkKind.Failed => JobOutcome.Failed(error ?? throw new InvalidOperationException("A failure mark carries its error."), item.StateJson),
                _ => throw new InvalidOperationException($"The mark kind {mark.Kind} is not defined."),
            };
        }

        /// <summary>The outcome of an unmarked item, from how the invocation ended.</summary>
        /// <param name="item">The item.</param>
        /// <param name="ending">How the invocation ended.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <param name="random">The source of the backoff's jitter.</param>
        /// <returns>The outcome.</returns>
        private static JobOutcome Unmarked(JobItemFacts item, JobEnding ending, JobRetryPolicy policy, string? traceId, Random random)
        {
            return ending.Kind switch
            {
                JobEndingKind.Returned => JobOutcome.Succeeded(item.StateJson),
                JobEndingKind.Failed or JobEndingKind.Invalid => JobOutcome.Failed(Traced(ending.Error!, traceId), item.StateJson),
                JobEndingKind.ConfirmationRequired => Pending(item, ending, traceId),
                JobEndingKind.Cancelled => item.Observed ? JobOutcome.Retry(0, null, item.StateJson) : JobOutcome.Released(),
                JobEndingKind.Threw => JobOutcome.Retry(Backoff(item, policy, random), new JobOutcomeError(JobErrorCodes.Internal, null, traceId), item.StateJson),
                _ => throw new InvalidOperationException($"The ending kind {ending.Kind} is not defined."),
            };
        }

        /// <summary>
        ///     The outcome of warnings the job carries no consent for: it waits for its initiator, or
        ///     fails when one confirmation could not cover them.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="ending">The ending, which carries the warnings.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <returns>The outcome.</returns>
        private static JobOutcome Pending(JobItemFacts item, JobEnding ending, string? traceId)
        {
            IReadOnlyList<ConfirmationRequest> requests = ending.Confirmations!;
            if (PendingConfirmations.Confirmable(requests, item.ConfirmedKeys))
            {
                return JobOutcome.PendingConfirmation(PendingConfirmations.Serialize(requests), item.StateJson);
            }

            // The arguments name the distinct keys, as the API's confirm bound counts them.
            string arguments = string.Create(CultureInfo.InvariantCulture, $"{{\"count\":{PendingConfirmations.DistinctKeys(requests)}}}");
            return JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.TooManyConfirmations, arguments, traceId), item.StateJson);
        }

        /// <summary>A retry at or past the last allowed attempt fails, with its error or with the exhaustion code.</summary>
        /// <param name="outcome">The outcome so far.</param>
        /// <param name="item">The item.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="traceId">The trace id of the invocation's activity.</param>
        /// <returns>The outcome.</returns>
        private static JobOutcome Exhausted(JobOutcome outcome, JobItemFacts item, JobRetryPolicy policy, string? traceId)
        {
            if (outcome.Kind != JobOutcomeKind.Retry || item.Attempts < policy.MaxAttempts)
            {
                return outcome;
            }

            JobOutcomeError error = outcome.ErrorCode is string code
                ? new JobOutcomeError(code, outcome.ErrorArgumentsJson, outcome.ErrorTraceId)
                : new JobOutcomeError(JobErrorCodes.AttemptsExhausted, null, traceId);
            return JobOutcome.Failed(error, outcome.StateJson);
        }

        /// <summary>The backoff after the item's attempt.</summary>
        /// <param name="item">The item.</param>
        /// <param name="policy">The handler's retry policy.</param>
        /// <param name="random">The source of the jitter.</param>
        /// <returns>The delay, in seconds.</returns>
        private static int Backoff(JobItemFacts item, JobRetryPolicy policy, Random random)
        {
            // A row reaches the handler with at least one attempt; the floor keeps a corrupt count in range.
            return JobBackoff.Seconds(Math.Max(1, item.Attempts), policy.RetryBaseSeconds, policy.RetryMaxSeconds, random);
        }

        /// <summary>An error with the invocation's trace id attached.</summary>
        /// <param name="error">The error.</param>
        /// <param name="traceId">The trace id.</param>
        /// <returns>The error.</returns>
        private static JobOutcomeError Traced(JobOutcomeError error, string? traceId)
        {
            return error with { TraceId = traceId };
        }
    }
}
