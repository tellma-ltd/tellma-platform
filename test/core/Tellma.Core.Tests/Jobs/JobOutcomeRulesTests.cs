// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Jobs;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The outcome engine: how an invocation's end classifies, then each item's outcome in the
    ///     rules' order (a lost lease, a mark, a requested cancel, the end), a retry at the last
    ///     allowed attempt, the outcomes after a failed completion and after a failed guard, and the
    ///     trace id every recorded error carries.
    /// </summary>
    public sealed class JobOutcomeRulesTests
    {
        /// <summary>The trace id of the invocation's activity.</summary>
        private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

        /// <summary>A checkpoint the item holds.</summary>
        private const string State = /*lang=json,strict*/ """{"page":3}""";

        /// <summary>The handler's retry policy: five attempts, a 30-second base, a one-hour ceiling.</summary>
        private static readonly JobRetryPolicy Policy = new(MaxAttempts: 5, RetryBaseSeconds: 30, RetryMaxSeconds: 3600);

        // --- How the invocation ended ---

        [Fact]
        public void A_return_a_job_failure_a_validation_failure_and_an_unconfirmed_warning_each_classify_as_themselves()
        {
            Assert.Same(JobEnding.Returned, JobEnding.Of(null, cancelled: false, hostStopping: false));

            var failed = JobEnding.Of(new JobFailedException(new JobError("Acme.Closed", new Dictionary<string, object?> { ["period"] = "2026-10" })), false, false);
            Assert.Equal(JobEndingKind.Failed, failed.Kind);
            Assert.Equal(new JobOutcomeError("Acme.Closed", /*lang=json,strict*/ """{"period":"2026-10"}""", null), failed.Error);

            var invalid = JobEnding.Of(Invalid("Fk.NotFound", "Unique"), false, false);
            Assert.Equal(JobEndingKind.Invalid, invalid.Kind);
            Assert.Equal("Fk.NotFound", invalid.Error!.Code);

            ConfirmationRequest warning = new("Sales.Backdated", [4], new Dictionary<string, object?>());
            var required = JobEnding.Of(new ConfirmationRequiredException([warning]), false, false);
            Assert.Equal(JobEndingKind.ConfirmationRequired, required.Kind);
            Assert.Equal([warning], required.Confirmations);
        }

        [Fact]
        public void A_cancellation_is_the_workers_only_when_a_token_it_handed_out_was_cancelled()
        {
            // A library's own timeout surfaces as a cancellation too, and is an ordinary failure.
            Assert.Equal(JobEndingKind.Cancelled, JobEnding.Of(new OperationCanceledException(), cancelled: true, hostStopping: false).Kind);
            Assert.Equal(JobEndingKind.Cancelled, JobEnding.Of(new TaskCanceledException(), cancelled: true, hostStopping: false).Kind);
            Assert.Equal(JobEndingKind.Threw, JobEnding.Of(new OperationCanceledException(), cancelled: false, hostStopping: false).Kind);
        }

        [Fact]
        public void Once_the_host_is_stopping_any_exception_ends_the_invocation_as_its_stop()
        {
            Assert.Equal(JobEndingKind.Cancelled, JobEnding.Of(new InvalidOperationException("The connection broke."), cancelled: false, hostStopping: true).Kind);
            Assert.Equal(JobEndingKind.Threw, JobEnding.Of(new InvalidOperationException("The connection broke."), cancelled: false, hostStopping: false).Kind);

            // A handler's own verdict is still its verdict.
            Assert.Equal(JobEndingKind.Failed, JobEnding.Of(new JobFailedException(new JobError("Acme.Closed")), cancelled: false, hostStopping: true).Kind);
        }

        [Fact]
        public void A_validation_failure_records_its_first_error_with_its_arguments()
        {
            ValidationException invalid = new(
            [
                new ValidationError(ValidationPath.Root.Property("Name"), "MaxLength", new Dictionary<string, object?> { ["maxLength"] = 50 }),
                new ValidationError(ValidationPath.Root, "Unique", new Dictionary<string, object?>()),
            ]);

            Assert.Equal(new JobOutcomeError("MaxLength", /*lang=json,strict*/ """{"maxLength":50}""", null), JobEnding.Invalid(invalid).Error);
        }

        [Fact]
        public void A_validation_code_too_long_for_the_job_table_records_the_internal_error_and_arguments_too_long_record_none()
        {
            Assert.Equal(new JobOutcomeError(JobErrorCodes.Internal, null, null), JobEnding.Invalid(Invalid(new string('X', JobLimits.MaxKeyLength + 1))).Error);

            ValidationException verbose = new([new ValidationError(ValidationPath.Root, "Acme.Verbose", new Dictionary<string, object?> { ["text"] = new string('x', 5_000) })]);
            Assert.Equal(new JobOutcomeError("Acme.Verbose", null, null), JobEnding.Invalid(verbose).Error);
        }

        // --- Rule 1: a lost lease ---

        [Fact]
        public void A_lost_lease_on_any_item_discards_every_outcome()
        {
            IReadOnlyList<JobOutcome>? outcomes = JobOutcomeRules.Decide(
                [Item(), Item(reason: JobCancellationReason.LeaseLost), Item(mark: JobItemMark.Succeeded)],
                JobEnding.Returned,
                Policy,
                TraceId,
                new Random(1));

            Assert.Null(outcomes);
        }

        // --- Rule 2: a mark stands ---

        [Fact]
        public void A_marked_item_keeps_its_mark_however_the_invocation_ended_and_whatever_its_cancel()
        {
            JobOutcome outcome = Decide(Item(mark: JobItemMark.Succeeded, reason: JobCancellationReason.CancelRequested), JobEnding.Unexpected(new InvalidOperationException()));

            Assert.Equal(JobOutcome.Succeeded(State), outcome);
        }

        [Fact]
        public void A_retry_mark_waits_the_delay_it_supplied_rounded_up_to_a_second_and_records_its_error_with_the_trace()
        {
            JobError error = new("Acme.Busy", new Dictionary<string, object?> { ["queue"] = "east" });
            JobItemMark mark = new(JobItemMarkKind.Retry, TimeSpan.FromSeconds(12.2), error, /*lang=json,strict*/ """{"queue":"east"}""");

            JobOutcome outcome = Decide(Item(mark: mark), JobEnding.Returned);

            Assert.Equal(JobOutcome.Retry(13, new JobOutcomeError("Acme.Busy", /*lang=json,strict*/ """{"queue":"east"}""", TraceId), State), outcome);
        }

        [Fact]
        public void A_retry_mark_without_a_delay_waits_the_handlers_backoff()
        {
            JobItemMark mark = new(JobItemMarkKind.Retry, null, null, null);

            // Third attempt: the ceiling is 30 × 2² = 120 seconds.
            int[] delays = [.. Enumerable.Range(0, 200).Select(seed => Decide(Item(attempts: 3, mark: mark), JobEnding.Returned, new Random(seed)).RetryAfterSeconds!.Value)];

            Assert.All(delays, static delay => Assert.InRange(delay, 0, 120));
            Assert.True(delays.Distinct().Count() > 10);
        }

        [Fact]
        public void A_failure_mark_records_its_error_with_the_trace()
        {
            JobItemMark mark = new(JobItemMarkKind.Failed, null, new JobError("Acme.Closed"), null);

            Assert.Equal(JobOutcome.Failed(new JobOutcomeError("Acme.Closed", null, TraceId), State), Decide(Item(mark: mark), JobEnding.Returned));
        }

        // --- Rule 3: a requested cancel ---

        [Fact]
        public void An_unmarked_item_whose_cancel_was_requested_is_cancelled_however_the_invocation_ended()
        {
            var cancelled = JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, TraceId), State);

            Assert.Equal(cancelled, Decide(Item(reason: JobCancellationReason.CancelRequested), JobEnding.Returned));
            Assert.Equal(cancelled, Decide(Item(reason: JobCancellationReason.CancelRequested), JobEnding.Unexpected(new InvalidOperationException())));
            Assert.Equal(cancelled, Decide(Item(reason: JobCancellationReason.CancelRequested, observed: true), JobEnding.Of(new OperationCanceledException(), true, false)));
        }

        [Fact]
        public void In_a_batch_one_cancelled_item_is_cancelled_while_the_others_follow_their_marks_and_the_end()
        {
            JobItemMark retry = new(JobItemMarkKind.Retry, TimeSpan.FromMinutes(1), null, null);

            IReadOnlyList<JobOutcome>? outcomes = JobOutcomeRules.Decide(
                [Item(), Item(reason: JobCancellationReason.CancelRequested), Item(mark: retry)],
                JobEnding.Returned,
                Policy,
                TraceId,
                new Random(1));

            Assert.NotNull(outcomes);
            Assert.Equal(
                [JobOutcomeKind.Succeeded, JobOutcomeKind.Cancelled, JobOutcomeKind.Retry],
                outcomes.Select(static outcome => outcome.Kind));
            Assert.Equal(60, outcomes[2].RetryAfterSeconds);
        }

        // --- Rule 4: how the invocation ended ---

        [Fact]
        public void An_unmarked_item_of_a_handler_that_returned_succeeds_with_its_checkpoint()
        {
            Assert.Equal(JobOutcome.Succeeded(State), Decide(Item(), JobEnding.Returned));
        }

        [Fact]
        public void An_unmarked_item_of_a_handler_that_failed_or_was_refused_fails_with_the_error_and_the_trace()
        {
            var failed = JobEnding.Of(new JobFailedException(new JobError("Acme.Closed")), false, false);
            Assert.Equal(JobOutcome.Failed(new JobOutcomeError("Acme.Closed", null, TraceId), State), Decide(Item(), failed));

            Assert.Equal(JobOutcome.Failed(new JobOutcomeError("Unique", null, TraceId), State), Decide(Item(), JobEnding.Invalid(Invalid("Unique"))));
        }

        [Fact]
        public void A_stopped_invocation_releases_an_item_the_handler_never_touched_and_retries_one_it_did_at_once()
        {
            var stopped = JobEnding.Of(new OperationCanceledException(), cancelled: true, hostStopping: false);

            Assert.Equal(JobOutcome.Released(), Decide(Item(observed: false), stopped));
            Assert.Equal(JobOutcome.Retry(0, null, State), Decide(Item(observed: true), stopped));
        }

        [Fact]
        public void An_unexpected_exception_retries_the_item_after_the_backoff_with_the_internal_error_and_the_trace()
        {
            JobOutcome outcome = Decide(Item(attempts: 1), JobEnding.Unexpected(new InvalidOperationException("Boom.")), new Random(7));

            Assert.Equal(JobOutcomeKind.Retry, outcome.Kind);
            Assert.InRange(outcome.RetryAfterSeconds!.Value, 0, 30);
            Assert.Equal((JobErrorCodes.Internal, null, TraceId), (outcome.ErrorCode, outcome.ErrorArgumentsJson, outcome.ErrorTraceId));
            Assert.Equal(State, outcome.StateJson);
        }

        // --- Confirmations ---

        [Fact]
        public void An_unconfirmed_warning_leaves_the_item_waiting_with_the_warnings_it_raised()
        {
            ConfirmationRequest warning = new("Sales.Backdated", [4, 5], new Dictionary<string, object?> { ["days"] = 3 });

            JobOutcome outcome = Decide(Item(), JobEnding.Of(new ConfirmationRequiredException([warning]), false, false));

            Assert.Equal(JobOutcomeKind.PendingConfirmation, outcome.Kind);
            Assert.Equal(0, outcome.RetryAfterSeconds);
            Assert.Equal(/*lang=json,strict*/ """{"count":1,"items":[{"key":"Sales.Backdated","ids":[4,5],"arguments":{"days":3}}]}""", outcome.PendingConfirmationsJson);
            Assert.Null(outcome.ErrorCode);
        }

        [Fact]
        public void Warnings_of_more_than_sixteen_keys_fail_the_item_with_their_distinct_count()
        {
            ConfirmationRequest[] warnings = [.. Enumerable.Range(0, 17).Select(static index => new ConfirmationRequest($"Acme.Warning{index}", [1], new Dictionary<string, object?>()))];

            JobOutcome outcome = Decide(Item(), JobEnding.Of(new ConfirmationRequiredException([.. warnings, warnings[0]]), false, false));

            Assert.Equal(JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.TooManyConfirmations, /*lang=json,strict*/ """{"count":17}""", TraceId), State), outcome);
        }

        [Fact]
        public void Warnings_whose_keys_with_the_confirmed_ones_pass_two_thousand_characters_fail_the_item()
        {
            ConfirmationRequest warning = new("Acme." + new string('w', 59), [1], new Dictionary<string, object?>());
            string confirmed = string.Join(',', Enumerable.Range(0, 29).Select(static index => $"Acme.Confirmed{index:D2}" + new string('c', 50)));

            JobOutcome outcome = Decide(Item(confirmedKeys: confirmed), JobEnding.Of(new ConfirmationRequiredException([warning]), false, false));

            Assert.Equal(JobOutcomeKind.Failed, outcome.Kind);
            Assert.Equal(JobErrorCodes.TooManyConfirmations, outcome.ErrorCode);
        }

        // --- The last allowed attempt ---

        [Fact]
        public void A_retry_at_the_last_allowed_attempt_fails_with_its_own_error_or_as_exhausted()
        {
            JobOutcome threw = Decide(Item(attempts: 5), JobEnding.Unexpected(new InvalidOperationException()));
            Assert.Equal(JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.Internal, null, TraceId), State), threw);

            JobItemMark bare = new(JobItemMarkKind.Retry, null, null, null);
            Assert.Equal(JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.AttemptsExhausted, null, TraceId), State), Decide(Item(attempts: 5, mark: bare), JobEnding.Returned));

            // One attempt earlier, the retry stands.
            Assert.Equal(JobOutcomeKind.Retry, Decide(Item(attempts: 4, mark: bare), JobEnding.Returned).Kind);
        }

        [Fact]
        public void A_release_is_never_exhausted_since_it_gives_its_attempt_back()
        {
            var stopped = JobEnding.Of(new OperationCanceledException(), cancelled: true, hostStopping: false);

            Assert.Equal(JobOutcome.Released(), Decide(Item(attempts: 9), stopped));
        }

        // --- After a failed completion and after a failed guard ---

        [Fact]
        public void After_a_failed_completion_the_marks_are_discarded_with_the_work_and_each_item_follows_the_failure()
        {
            var invalid = JobEnding.Invalid(Invalid("Fk.NotFound"));
            var cancelled = JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, TraceId), State);

            Assert.Equal(JobOutcome.Failed(new JobOutcomeError("Fk.NotFound", null, TraceId), State), JobOutcomeRules.AfterFailedCompletion(Item(mark: JobItemMark.Succeeded), invalid, Policy, TraceId, new Random(1)));
            Assert.Equal(cancelled, JobOutcomeRules.AfterFailedCompletion(Item(reason: JobCancellationReason.CancelRequested), invalid, Policy, TraceId, new Random(1)));

            JobOutcome internalError = JobOutcomeRules.AfterFailedCompletion(Item(), JobEnding.Unexpected(new InvalidOperationException()), Policy, TraceId, new Random(1));
            Assert.Equal((JobOutcomeKind.Retry, JobErrorCodes.Internal), (internalError.Kind, internalError.ErrorCode));
        }

        [Fact]
        public void After_a_failed_guard_every_item_runs_again_at_once_unless_it_used_its_last_attempt()
        {
            Assert.Equal(JobOutcome.Retry(0, null, State), JobOutcomeRules.AfterGuardFailure(Item(mark: JobItemMark.Succeeded), Policy, TraceId));
            Assert.Equal(
                JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.AttemptsExhausted, null, TraceId), State),
                JobOutcomeRules.AfterGuardFailure(Item(attempts: 5), Policy, TraceId));
        }

        [Fact]
        public void The_retry_policy_is_the_handlers_attribute()
        {
            JobHandlerAttribute options = new("acme.ship") { MaxAttempts = 3, RetryBaseSeconds = 10, RetryMaxSeconds = 90 };

            Assert.Equal(new JobRetryPolicy(3, 10, 90), JobRetryPolicy.Of(options));
        }

        /// <summary>Decides one item's outcome under the test policy and trace.</summary>
        /// <param name="item">The item.</param>
        /// <param name="ending">How the invocation ended.</param>
        /// <param name="random">The jitter's source; a fixed seed when null.</param>
        /// <returns>The outcome.</returns>
        private static JobOutcome Decide(JobItemFacts item, JobEnding ending, Random? random = null)
        {
            return JobOutcomeRules.Decide(item, ending, Policy, TraceId, random ?? new Random(1));
        }

        /// <summary>An item's facts, on its first attempt with a checkpoint, unmarked, uncancelled and untouched by default.</summary>
        /// <param name="attempts">The attempts, this one included.</param>
        /// <param name="mark">The handler's mark.</param>
        /// <param name="reason">Why its token was cancelled.</param>
        /// <param name="observed">Whether the handler reported on it.</param>
        /// <param name="confirmedKeys">The keys the job carries as confirmed.</param>
        /// <returns>The facts.</returns>
        private static JobItemFacts Item(
            int attempts = 1, JobItemMark? mark = null, JobCancellationReason reason = JobCancellationReason.None, bool observed = false, string? confirmedKeys = null)
        {
            return new JobItemFacts(attempts, confirmedKeys, reason, mark, observed, State);
        }

        /// <summary>A validation failure of root errors with the codes given and no arguments.</summary>
        /// <param name="codes">The codes.</param>
        /// <returns>The failure.</returns>
        private static ValidationException Invalid(params string[] codes)
        {
            return new ValidationException([.. codes.Select(static code => new ValidationError(ValidationPath.Root, code, new Dictionary<string, object?>()))]);
        }
    }
}
