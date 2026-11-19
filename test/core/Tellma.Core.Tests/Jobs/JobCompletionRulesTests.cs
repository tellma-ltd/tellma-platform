// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Jobs;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The rules a completion applies around the outcomes, with no database: the backoff's
    ///     bounds, the record of pending warnings and its caps, the notices an outcome raises and to
    ///     whom, and the translation of a failed completion's statement errors.
    /// </summary>
    public sealed class JobCompletionRulesTests
    {
        // --- The backoff ---

        [Theory]
        [InlineData(1, 30)]
        [InlineData(2, 60)]
        [InlineData(5, 480)]
        [InlineData(8, 3600)]
        [InlineData(40, 3600)]
        public void The_backoffs_ceiling_doubles_per_attempt_up_to_the_handlers_maximum(int attempts, int ceiling)
        {
            Assert.Equal(ceiling, JobBackoff.CeilingSeconds(attempts, 30, 3600));
        }

        [Fact]
        public void The_backoff_draws_every_delay_from_zero_to_the_ceiling()
        {
            Random random = new(11);

            int[] delays = [.. Enumerable.Range(0, 2_000).Select(_ => JobBackoff.Seconds(2, 30, 3600, random))];

            Assert.All(delays, static delay => Assert.InRange(delay, 0, 60));
            Assert.Contains(0, delays);
            Assert.Contains(60, delays);
        }

        [Fact]
        public void A_supplied_delay_rounds_up_to_the_next_second()
        {
            Assert.Equal(0, JobBackoff.SuppliedSeconds(TimeSpan.Zero));
            Assert.Equal(1, JobBackoff.SuppliedSeconds(TimeSpan.FromMilliseconds(1)));
            Assert.Equal(90, JobBackoff.SuppliedSeconds(TimeSpan.FromSeconds(90)));
        }

        // --- The pending warnings ---

        [Fact]
        public void The_pending_record_counts_every_warning_and_keeps_each_ones_first_hundred_ids()
        {
            ConfirmationRequest many = new("Sales.Backdated", [.. Enumerable.Range(1, 150).Select(static id => (long)id)], new Dictionary<string, object?>());

            string record = PendingConfirmations.Serialize([many, Warning("Inventory.Negative")]);

            using var parsed = JsonDocument.Parse(record);
            Assert.Equal(2, parsed.RootElement.GetProperty("count").GetInt32());
            JsonElement first = parsed.RootElement.GetProperty("items")[0];
            Assert.Equal("Sales.Backdated", first.GetProperty("key").GetString());
            Assert.Equal(JobLimits.MaxPendingConfirmationIds, first.GetProperty("ids").GetArrayLength());
            Assert.Equal(100, first.GetProperty("ids")[99].GetInt64());
        }

        [Fact]
        public void A_record_past_sixty_four_kilobytes_drops_items_from_the_end_but_never_the_first_of_a_key()
        {
            // Each item carries about 2 KB of arguments; 60 items of two keys pass the limit.
            Dictionary<string, object?> bulky = new(StringComparer.Ordinal) { ["text"] = new string('x', 2_000) };
            ConfirmationRequest[] requests =
            [
                .. Enumerable.Range(0, 59).Select(index => new ConfirmationRequest("Sales.Backdated", [index], bulky)),
                new ConfirmationRequest("Inventory.Negative", [1], bulky),
            ];

            string record = PendingConfirmations.Serialize(requests);

            using var parsed = JsonDocument.Parse(record);
            Assert.InRange(Encoding.UTF8.GetByteCount(record), 0, JobLimits.MaxPendingConfirmationsBytes);
            Assert.Equal(60, parsed.RootElement.GetProperty("count").GetInt32());
            string?[] keys = [.. parsed.RootElement.GetProperty("items").EnumerateArray().Select(static item => item.GetProperty("key").GetString())];
            Assert.Equal("Sales.Backdated", keys[0]);
            Assert.Equal("Inventory.Negative", keys[^1]);
            Assert.True(keys.Length < 60);
        }

        [Fact]
        public void Sixteen_distinct_keys_are_confirmable_and_seventeen_are_not()
        {
            ConfirmationRequest[] sixteen = [.. Enumerable.Range(0, 16).Select(static index => Warning($"Acme.Warning{index}"))];

            Assert.True(PendingConfirmations.Confirmable([.. sixteen, sixteen[0]], null));
            Assert.False(PendingConfirmations.Confirmable([.. sixteen, Warning("Acme.Warning16")], null));
            Assert.Equal(16, PendingConfirmations.DistinctKeys([.. sixteen, sixteen[3]]));
        }

        [Fact]
        public void The_confirmed_keys_split_on_commas_trimmed_and_without_empty_entries()
        {
            Assert.Equal(["Sales.Backdated", "Inventory.Negative"], PendingConfirmations.Split(" Sales.Backdated,, Inventory.Negative "));
            Assert.Empty(PendingConfirmations.Split(null));
        }

        // --- The notices ---

        [Fact]
        public void A_failure_raises_its_notice_to_the_requester_with_the_handler_key_and_the_code()
        {
            var failed = JobOutcome.Failed(new JobOutcomeError("Acme.Closed", null, null), null);

            NotificationRequest notice = JobNotices.Of(Job(requester: 9), failed, new JobHandlerAttribute("acme.ship"), [1, 2])!;

            Assert.Equal(JobNotices.FailedType, notice.Type);
            Assert.Equal([9], notice.RecipientIds);
            Assert.Equal(new JobFailedArguments("acme.ship", "Acme.Closed"), notice.Arguments);
            Assert.Equal((CoreResources.Job, 41), (notice.TargetResource, notice.TargetId));
            Assert.False(notice.InboxOnly);
        }

        [Fact]
        public void A_failure_without_a_requester_goes_to_the_administrators_and_stays_in_the_inbox_when_the_handler_says_so()
        {
            var failed = JobOutcome.Failed(new JobOutcomeError(JobErrorCodes.Internal, null, null), null);

            NotificationRequest notice = JobNotices.Of(Job(requester: null), failed, new JobHandlerAttribute("acme.ship") { InboxOnlyFailureNotices = true }, [1, 2])!;

            Assert.Equal([1, 2], notice.RecipientIds);
            Assert.True(notice.InboxOnly);
            Assert.Null(JobNotices.Of(Job(requester: null), failed, new JobHandlerAttribute("acme.ship"), []));
        }

        [Fact]
        public void A_wait_for_confirmation_raises_its_notice_once_per_job_with_the_warnings_count()
        {
            var pending = JobOutcome.PendingConfirmation(PendingConfirmations.Serialize([Warning("Sales.Backdated"), Warning("Sales.Backdated")]), null);

            NotificationRequest notice = JobNotices.Of(Job(requester: 9), pending, new JobHandlerAttribute("acme.ship"), [])!;

            Assert.Equal(JobNotices.PendingConfirmationType, notice.Type);
            Assert.Equal(new JobPendingConfirmationArguments("acme.ship", 2), notice.Arguments);
            Assert.Equal("core.job.pending-confirmation:41", notice.DedupKey);
        }

        [Fact]
        public void Every_other_outcome_raises_no_notice()
        {
            JobHandlerAttribute options = new("acme.ship");
            JobOutcome[] quiet =
            [
                JobOutcome.Succeeded(null),
                JobOutcome.Retry(5, new JobOutcomeError(JobErrorCodes.Internal, null, null), null),
                JobOutcome.Released(),
                JobOutcome.Cancelled(new JobOutcomeError(JobErrorCodes.Cancelled, null, null), null),
            ];

            Assert.All(quiet, outcome => Assert.Null(JobNotices.Of(Job(requester: 9), outcome, options, [1])));
            Assert.All(quiet, static outcome => Assert.False(JobNotices.Raises(outcome)));
        }

        [Fact]
        public void A_change_reaches_the_requester_alone()
        {
            Abstractions.Realtime.ClientEvent changed = JobNotices.Changed(41, 9);

            Assert.Equal(JobNotices.ChangedEvent, changed.Name);
            Assert.Equal([9], changed.UserIds);
            Assert.Equal(new JobChangedPayload(41), changed.Payload);
        }

        // --- A failed completion's statement errors ---

        [Fact]
        public void The_lease_fence_is_recognised_and_never_translated()
        {
            BatchAssertionFailedException fence = new(TellmaSqlErrors.Invariant, JobErrorCodes.LeaseLost, 0);

            Assert.True(JobCompletionFailures.IsLeaseLost(fence));
            Assert.Null(JobCompletionFailures.Translate(fence));
            Assert.False(JobCompletionFailures.IsLeaseLost(new BatchAssertionFailedException(TellmaSqlErrors.Invariant, "Acme.Other", 0)));
        }

        [Fact]
        public void A_handler_statements_failure_translates_at_the_root()
        {
            Assert.Equal(("Acme.Balanced", 0), RootError(new BatchAssertionFailedException(TellmaSqlErrors.Invariant, "Acme.Balanced", 3)));
            Assert.Equal(("Acme.Distro", 0), RootError(new BatchAssertionFailedException(TellmaSqlErrors.DistroMin, "Acme.Distro", 3)));
            Assert.Equal((ValidationCodes.TreeCycle, 0), RootError(new TreeCycleException(null, 3)));
            Assert.Equal((ValidationCodes.TreeTooDeep, 0), RootError(new TreeDepthExceededException(null, 3)));
            Assert.Equal((ValidationCodes.Unique, 0), RootError(new UniqueConstraintViolationException("IX_Acme", 3)));
            Assert.Equal((ValidationCodes.FkNotFound, 0), RootError(new ForeignKeyViolationException("FK_Acme", 3) { Conflict = ForeignKeyConflict.MissingTarget }));
            Assert.Equal((ValidationCodes.FkInUse, 0), RootError(new ForeignKeyViolationException("FK_Acme", 3) { Conflict = ForeignKeyConflict.StillReferenced }));

            ValidationError blob = Assert.Single(JobCompletionFailures.Translate(new BatchAssertionFailedException(TellmaSqlErrors.Invariant, ValidationCodes.BlobNotAttachable, 3))!.Errors);
            Assert.Equal("persist", blob.Arguments["reason"]);
        }

        [Fact]
        public void A_failure_no_row_translates_is_left_as_it_is()
        {
            Assert.Null(JobCompletionFailures.Translate(new BatchAssertionFailedException(TellmaSqlErrors.StaleVersionTag, "VersionTag.Stale", 0)));
        }

        /// <summary>A job of the shipment handler, its id 41.</summary>
        /// <param name="requester">The job's requester.</param>
        /// <returns>The job.</returns>
        private static Job Job(int? requester)
        {
            return new Job { Id = 41, HandlerKey = "acme.ship", RunAsUserId = 7, RequestedById = requester, Attempts = 1 };
        }

        /// <summary>A warning of a key over one row.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The warning.</returns>
        private static ConfirmationRequest Warning(string key)
        {
            return new ConfirmationRequest(key, [1], new Dictionary<string, object?>());
        }

        /// <summary>The code of the one error a failure translates to, and its path's depth.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The code and the depth.</returns>
        private static (string Code, int Depth) RootError(DataAccessException failure)
        {
            ValidationError error = Assert.Single(JobCompletionFailures.Translate(failure)!.Errors);
            return (error.Code, error.Path.Segments.Count);
        }
    }
}
