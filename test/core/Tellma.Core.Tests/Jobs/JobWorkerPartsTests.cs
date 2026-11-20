// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The worker's smaller parts with no database: the batch slots and the wake of tenants refused
    ///     one, the lease owner's text, the drain held below the host's shutdown timeout, the gap
    ///     notices, what a poll found, the queue latency of a first claim, and the composition of the
    ///     worker by its option.
    /// </summary>
    public sealed class JobWorkerPartsTests : IDisposable
    {
        /// <summary>The worker tests' handlers.</summary>
        private readonly BatchHarness _harness = new(compose: WorkerTestModel.Compose);

        /// <summary>The handlers' registry.</summary>
        private JobHandlerRegistry Registry => _harness.Services.GetRequiredService<JobHandlerRegistry>();

        // --- The batch slots ---

        [Fact]
        public void A_key_runs_at_most_its_concurrency_per_tenant_and_only_started_batches_take_the_instances_slots()
        {
            TenantPollerWake wake = new(NullLoggerFactory.Instance);
            JobConcurrency slots = new(maxParallelBatches: 3, wake);
            JobHandlerRegistration audit = Handler(WorkerTestModel.AuditKey);
            JobHandlerRegistration recompute = Handler(WorkerTestModel.RecomputeKey);

            // --- The audit key runs two at once per tenant; a reservation not started holds no instance slot ---
            using JobSlotReservation? first = slots.TryReserve(1, audit);
            using JobSlotReservation? second = slots.TryReserve(1, audit);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Null(slots.TryReserve(1, audit));
            Assert.Equal(3, slots.Free);

            // --- Three started batches fill the instance: no key of any tenant is reserved then ---
            first!.Start();
            second!.Start();
            using JobSlotReservation? other = slots.TryReserve(2, recompute);
            other!.Start();
            Assert.Equal(0, slots.Free);
            Assert.Null(slots.TryReserve(3, recompute));

            // --- A release frees both slots once, however often it is disposed; a start after it takes nothing ---
            first.Dispose();
            first.Dispose();
            first.Start();
            Assert.Equal(1, slots.Free);
            Assert.Equal(1, slots.Running(1, WorkerTestModel.AuditKey));

            // --- A claim that leased nothing frees its key's slot, never an instance slot ---
            using JobSlotReservation? empty = slots.TryReserve(3, recompute);
            empty!.Dispose();
            Assert.Equal((1, 0), (slots.Free, slots.Running(3, WorkerTestModel.RecomputeKey)));
        }

        [Fact]
        public void A_key_refused_at_its_limit_wakes_its_tenant_only_once_one_of_its_own_batches_ended()
        {
            TenantPollerWake wake = new(NullLoggerFactory.Instance);
            JobConcurrency slots = new(maxParallelBatches: 4, wake);
            int woken = 0;
            using IDisposable subscription = wake.Subscribe(1, () => woken++);
            JobHandlerRegistration audit = Handler(WorkerTestModel.AuditKey);
            JobHandlerRegistration recompute = Handler(WorkerTestModel.RecomputeKey);

            // --- Tenant 1's recompute batch runs; its next poll is refused the key ---
            JobSlotReservation held = slots.TryReserve(1, recompute)!;
            held.Start();
            Assert.Null(slots.TryReserve(1, recompute));

            // --- Neither its own empty claim nor another tenant's batch ending wakes it ---
            slots.TryReserve(1, audit)!.Dispose();
            JobSlotReservation another = slots.TryReserve(2, recompute)!;
            another.Start();
            another.Dispose();
            Assert.Equal(0, woken);

            // --- Its recompute batch ending does, once ---
            held.Dispose();
            Assert.Equal(1, woken);
            JobSlotReservation again = slots.TryReserve(1, recompute)!;
            again.Start();
            again.Dispose();
            Assert.Equal(1, woken);
        }

        [Fact]
        public void A_tenant_refused_an_instance_slot_is_woken_one_per_freed_slot_first_refused_first()
        {
            TenantPollerWake wake = new(NullLoggerFactory.Instance);
            JobConcurrency slots = new(maxParallelBatches: 1, wake);
            int woken1 = 0, woken2 = 0;
            using IDisposable one = wake.Subscribe(1, () => woken1++);
            using IDisposable two = wake.Subscribe(2, () => woken2++);
            JobHandlerRegistration audit = Handler(WorkerTestModel.AuditKey);
            JobHandlerRegistration recompute = Handler(WorkerTestModel.RecomputeKey);

            // --- The instance's one slot is taken; tenant 1 is refused twice, then tenant 2 ---
            JobSlotReservation held = slots.TryReserve(3, recompute)!;
            held.Start();
            Assert.Null(slots.TryReserve(1, audit));
            Assert.Null(slots.TryReserve(1, recompute));
            Assert.Null(slots.TryReserve(2, audit));

            // --- Each freed slot wakes the tenant refused first, once ---
            held.Dispose();
            Assert.Equal((1, 0), (woken1, woken2));
            JobSlotReservation next = slots.TryReserve(1, audit)!;
            next.Start();
            next.Dispose();
            Assert.Equal((1, 1), (woken1, woken2));

            // --- A release with no refusal since wakes nobody ---
            JobSlotReservation last = slots.TryReserve(3, recompute)!;
            last.Start();
            last.Dispose();
            Assert.Equal((1, 1), (woken1, woken2));
        }

        [Fact]
        public void Polls_planned_in_one_instant_may_overshoot_the_bound_and_no_slot_is_free_until_they_fall_below_it()
        {
            TenantPollerWake wake = new(NullLoggerFactory.Instance);
            JobConcurrency slots = new(maxParallelBatches: 1, wake);
            int woken = 0;
            using IDisposable subscription = wake.Subscribe(3, () => woken++);
            JobHandlerRegistration audit = Handler(WorkerTestModel.AuditKey);

            // --- Two tenants planned while the slot was free; both claims leased rows and start ---
            JobSlotReservation first = slots.TryReserve(1, audit)!;
            JobSlotReservation second = slots.TryReserve(2, audit)!;
            first.Start();
            second.Start();
            Assert.Equal((2, 0), (slots.Started, slots.Free));
            Assert.Null(slots.TryReserve(3, audit));

            // --- The first to end leaves the instance at its bound: no slot is free, nobody is woken ---
            first.Dispose();
            Assert.Equal((0, 0), (slots.Free, woken));

            // --- The second frees the slot and wakes the tenant refused ---
            second.Dispose();
            Assert.Equal((1, 1), (slots.Free, woken));
        }

        // --- The lease owner ---

        [Theory]
        [InlineData("a1b2c3", "WEB01", 41, "a1b2c3/41")]
        [InlineData(null, "WEB01", 41, "WEB01/41")]
        [InlineData("  ", "WEB01", 41, "WEB01/41")]
        public void The_lease_owner_is_the_instance_id_else_the_machine_and_the_process(string? instance, string machine, int process, string expected)
        {
            Assert.Equal(expected, JobLeaseOwner.Of(instance, machine, process));
        }

        [Fact]
        public void A_long_instance_id_is_shortened_so_the_owner_fits_its_column_with_the_process_id_whole()
        {
            string owner = JobLeaseOwner.Of(new string('x', 300), "WEB01", 123456);

            Assert.Equal(128, owner.Length);
            Assert.EndsWith("/123456", owner, StringComparison.Ordinal);
        }

        // --- The drain against the host's shutdown timeout ---

        [Theory]
        [InlineData(true, 4, 30, true)]
        [InlineData(true, 30, 30, false)]
        [InlineData(true, 31, 30, false)]
        [InlineData(false, 31, 30, true)]
        public void The_drain_must_end_before_the_host_s_shutdown_timeout_when_the_worker_runs(bool enabled, int drainSeconds, int shutdownSeconds, bool valid)
        {
            JobsHostValidation validation = new(Options.Create(new HostOptions { ShutdownTimeout = TimeSpan.FromSeconds(shutdownSeconds) }));

            ValidateOptionsResult result = validation.Validate(null, new JobsOptions { Enabled = enabled, DrainTimeout = TimeSpan.FromSeconds(drainSeconds) });

            Assert.Equal(valid, result.Succeeded);
            if (!valid)
            {
                Assert.Contains("Tellma:Jobs:DrainTimeout", result.FailureMessage, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_composed_options_refuse_a_drain_as_long_as_the_shutdown_timeout()
        {
            Dictionary<string, string?> values = new(TestComposition.SoundConfiguration) { ["Tellma:Jobs:Enabled"] = "true", ["Tellma:Jobs:DrainTimeout"] = "00:00:30" };
            using ServiceProvider services = TestComposition.Compose(WorkerTestModel.Compose, TestComposition.Configuration(values)).BuildServiceProvider();

            OptionsValidationException refused = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<JobsOptions>>().Value);
            Assert.Contains(refused.Failures, static failure => failure.Contains("shutdown timeout", StringComparison.Ordinal));
        }

        // --- The gap notices ---

        [Fact]
        public void A_gap_notifies_the_administrators_in_requests_within_the_recipient_cap_and_each_requester_of_their_own_held_jobs()
        {
            DateTimeOffset now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);
            JobGapCheck gap = new(now.AddDays(-2), now, TimeSpan.FromHours(6), [new(101, 2), new(null, 5), new(102, 1)]);

            NotificationRequest[] requests = [.. JobNotices.Gap(gap, [1, 2, 3, 2], maxRecipientsPerRequest: 2)];

            // --- The gap notice: every held job counted, the administrators in requests of at most two ---
            NotificationRequest[] gapNotices = [.. requests.Where(static request => request.Type == JobNotices.GapType)];
            Assert.Equal([[1, 2], [3]], gapNotices.Select(static request => request.RecipientIds.ToArray()));
            Assert.All(gapNotices, request =>
            {
                Assert.Equal(new SchedulerGapArguments(now.AddDays(-2), now, 8), request.Arguments);
                Assert.Equal(JobNotices.GapType, request.DedupKey);
                Assert.Null(request.TargetResource);
                Assert.Equal(DuplicatePolicy.Suppress, request.OnDuplicate);
            });

            // --- One held notice per requester with their count; the rows with no requester only in the gap notice ---
            NotificationRequest[] held = [.. requests.Where(static request => request.Type == JobNotices.HeldType)];
            Assert.Equal([(101, 2), (102, 1)], held.Select(static request => (Assert.Single(request.RecipientIds), ((JobHeldArguments)request.Arguments!).Count)));
            Assert.All(held, static request => Assert.Equal(JobNotices.HeldType, request.DedupKey));
        }

        [Fact]
        public void The_gap_and_held_notices_are_warnings_that_toast_with_no_target()
        {
            Assert.Equal(new NotificationTypeDescriptor(JobNotices.HeldType, JobNotices.Category, Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning, Toast: true), JobNotices.HeldDescriptor);
            Assert.Equal(new NotificationTypeDescriptor(JobNotices.GapType, JobNotices.Category, Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning, Toast: true), JobNotices.GapDescriptor);
            Assert.Equal("core.job.pending-confirmation:42", JobNotices.PendingConfirmationKey(42));
        }

        // --- What a poll found ---

        [Fact]
        public void A_poll_is_full_when_a_claim_or_the_tick_filled_its_batch_and_found_some_when_anything_was_leased()
        {
            JobPollPlan plan = new(1, ReadOnly: false, Stopping: false, [new JobPollClaimRequest(Handler(WorkerTestModel.AuditKey), null)], SampleBacklog: false, SampleOrphans: false, "host-a/41");

            Assert.Equal(JobPollFinding.Nothing, FakeJobPoll.Result(plan, static _ => 0).Finding);
            Assert.Equal(JobPollFinding.Some, FakeJobPoll.Result(plan, static _ => 1).Finding);
            Assert.Equal(JobPollFinding.Full, FakeJobPoll.Result(plan, static _ => 2).Finding);
            Assert.Equal(JobPollFinding.Some, (FakeJobPoll.Result(plan, static _ => 0) with { Tick = new Tick(3, Full: false) }).Finding);
            Assert.Equal(JobPollFinding.Full, (FakeJobPoll.Result(plan, static _ => 0) with { Tick = new Tick(50, Full: true) }).Finding);
        }

        // --- The queue latency ---

        [Fact]
        public void The_queue_latency_is_recorded_for_a_first_claim_only_and_never_below_zero()
        {
            DateTimeOffset now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

            // --- First claimed now: started is the lease's start ---
            Assert.Equal(TimeSpan.FromSeconds(90), JobsMetrics.QueueLatency(new Job { DueAt = now.AddSeconds(-90), StartedAt = now, LeaseExpiresAt = now.AddSeconds(300) }, 300));
            Assert.Equal(TimeSpan.Zero, JobsMetrics.QueueLatency(new Job { DueAt = now.AddSeconds(2), StartedAt = now, LeaseExpiresAt = now.AddSeconds(300) }, 300));

            // --- Claimed again: its start is an earlier claim's ---
            Assert.Null(JobsMetrics.QueueLatency(new Job { DueAt = now.AddSeconds(-90), StartedAt = now.AddSeconds(-400), LeaseExpiresAt = now.AddSeconds(300) }, 300));
        }

        // --- The composition ---

        [Theory]
        [InlineData(null, true)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        public void The_worker_is_composed_unless_its_option_is_configured_off(string? enabled, bool composed)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection([new("Tellma:Jobs:Enabled", enabled)]).Build();

            Assert.Equal(composed, JobsComposition.Enabled(configuration));
        }

        [Fact]
        public void The_worker_is_one_singleton_behind_the_hosted_service_and_the_tenant_state_listener()
        {
            using ServiceProvider services = TestComposition.Compose(WorkerTestModel.Compose, TestComposition.Configuration(TestComposition.Sound(("Tellma:Jobs:Enabled", "true"))))
                .BuildServiceProvider();
            JobWorker worker = services.GetRequiredService<JobWorker>();

            Assert.Same(worker, services.GetServices<IHostedService>().OfType<JobWorker>().Single());
            Assert.Same(worker, services.GetServices<ITenantStateListener>().OfType<JobWorker>().Single());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A handler's registration.</summary>
        /// <param name="key">Its key.</param>
        /// <returns>The registration.</returns>
        private JobHandlerRegistration Handler(string key)
        {
            return Registry.TryFind(key, out JobHandlerRegistration? handler) ? handler : throw new InvalidOperationException($"No handler {key}.");
        }

        /// <summary>A tick's round, as a poll reads it.</summary>
        /// <param name="Leased">The schedules leased.</param>
        /// <param name="Full">Whether the tick leased its most.</param>
        private sealed record Tick(int Leased, bool Full) : IScheduleTickRound
        {
            /// <inheritdoc />
            public Task RunStep2Async(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
