// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The leases an instance holds, with no database: the tokens of a claim (item, batch and
    ///     scope) and the reasons each cancellation gives, the stop of a lost lease and of the host,
    ///     the rows leaving their lease, the progress buffer a renewal carries, the verdicts a renewal
    ///     reaches on the leases it carried, and the partitions of a claim by run-as user and consent.
    /// </summary>
    public sealed class JobLeasesTests : IDisposable
    {
        /// <summary>The tenant every lease is tracked for.</summary>
        private const int TenantId = 1;

        /// <summary>The job tests' model and handlers.</summary>
        private readonly BatchHarness _harness = new(compose: JobTestModel.Compose);

        /// <summary>The clock the leases stamp their claims and renewals with.</summary>
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

        /// <summary>The leases under test.</summary>
        private readonly JobLeases _leases;

        /// <summary>Creates the leases on the fake clock.</summary>
        public JobLeasesTests()
        {
            _leases = new JobLeases(_time);
        }

        /// <summary>The running test's token, standing in for the host's.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The arguments-only handler's registration.</summary>
        private JobHandlerRegistration Handler => _harness.Services.GetRequiredService<JobHandlerRegistry>().TryFind("acme.recompute-balances", out JobHandlerRegistration? handler)
            ? handler
            : throw new InvalidOperationException("The test model registers the handler.");

        // --- Tokens and reasons ---

        [Fact]
        public void A_requested_cancel_stops_its_item_alone_and_names_why_and_the_batch_once_every_item_stopped()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobItem<Job> first = Bound(lease, 1);
            JobItem<Job> second = Bound(lease, 2);

            lease.RequestCancel(1);
            Assert.True(first.CancellationToken.IsCancellationRequested);
            Assert.Equal(JobCancellationReason.CancelRequested, first.CancellationReason);
            Assert.False(second.CancellationToken.IsCancellationRequested);
            Assert.False(lease.ScopeToken.IsCancellationRequested);

            // --- The last item's cancel stops the batch, never the scope ---
            lease.RequestCancel(2);
            Assert.True(second.CancellationToken.IsCancellationRequested);
            Assert.False(lease.ScopeToken.IsCancellationRequested);
        }

        [Fact]
        public void A_reason_given_before_the_item_was_bound_reaches_it_once_bound()
        {
            using JobLease lease = Track([Row(1)], Token);

            lease.Slot(1)!.Cancel(JobCancellationReason.CancelRequested);
            JobItem<Job> item = Bound(lease, 1);

            Assert.Equal(JobCancellationReason.CancelRequested, item.CancellationReason);
            Assert.True(item.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public void A_lost_lease_stops_every_item_and_the_scope_and_replaces_any_other_reason()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobItem<Job> first = Bound(lease, 1);
            JobItem<Job> second = Bound(lease, 2);
            lease.RequestCancel(1);

            lease.Lose();

            Assert.True(lease.IsLost);
            Assert.True(lease.ScopeToken.IsCancellationRequested);
            Assert.Equal([JobCancellationReason.LeaseLost, JobCancellationReason.LeaseLost], new[] { first, second }.Select(static item => item.CancellationReason));
        }

        [Fact]
        public void The_hosts_stop_stops_every_item_and_the_scope_with_its_own_reason()
        {
            using CancellationTokenSource host = new();
            using JobLease lease = Track([Row(1)], host.Token);
            JobItem<Job> item = Bound(lease, 1);

            host.Cancel();

            Assert.True(lease.IsStopping);
            Assert.False(lease.IsLost);
            Assert.True(lease.ScopeToken.IsCancellationRequested);
            Assert.Equal(JobCancellationReason.HostStopping, item.CancellationReason);
        }

        [Fact]
        public void A_host_already_stopping_stops_a_claim_as_soon_as_it_is_tracked()
        {
            using CancellationTokenSource host = new();
            host.Cancel();

            using JobLease lease = Track([Row(1)], host.Token);

            Assert.True(lease.IsStopping);
            Assert.True(lease.Slot(1)!.Token.IsCancellationRequested);
        }

        // --- Rows leaving their lease ---

        [Fact]
        public void A_lease_leaves_the_tenants_leases_once_its_last_row_left()
        {
            JobLease lease = Track([Row(1), Row(2)], Token);

            lease.Leave([1]);
            Assert.Same(lease, Assert.Single(_leases.InFlight(TenantId)));
            Assert.Null(lease.Slot(1));

            lease.Leave([2]);
            Assert.Empty(_leases.InFlight(TenantId));

            // Left twice, disposed twice: neither throws.
            lease.Leave([2]);
            lease.Dispose();
        }

        [Fact]
        public void A_cancel_of_a_row_that_left_its_lease_is_ignored()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobSlot slot = lease.Slot(1)!;

            lease.Leave([1]);
            lease.RequestCancel(1);
            slot.Cancel(JobCancellationReason.LeaseLost);

            Assert.False(lease.Slot(2)!.Token.IsCancellationRequested);
        }

        // --- The progress buffer ---

        [Fact]
        public void The_buffer_keeps_the_last_value_of_each_column_and_a_message_with_its_arguments()
        {
            using JobLease lease = Track([Row(1)], Token);
            JobSlot slot = lease.Slot(1)!;
            Assert.Null(slot.Buffered());

            slot.Buffer(20, "Acme_Reading", /*lang=json,strict*/ """{"page":1}""", null);
            slot.Buffer(null, null, null, /*lang=json,strict*/ """{"page":2}""");
            slot.Buffer(40, "Acme_Writing", null, null);

            (Abstractions.TableTypes.JobProgressList row, long version) = slot.Buffered()!.Value;
            Assert.Equal((1, 40, "Acme_Writing", null, /*lang=json,strict*/ """{"page":2}"""), (row.Id, row.ProgressPercent, row.ProgressKey, row.ProgressArgumentsJson, row.StateJson));
            Assert.Equal(3, version);
            Assert.Equal(1, slot.StateVersion);
        }

        [Fact]
        public void A_carried_buffer_empties_unless_a_report_arrived_after_it_was_taken()
        {
            using JobLease lease = Track([Row(1)], Token);
            JobSlot slot = lease.Slot(1)!;
            slot.Buffer(20, null, null, null);
            long taken = slot.Buffered()!.Value.Version;

            // --- A later report keeps the buffer for the next renewal ---
            slot.Buffer(30, null, null, null);
            slot.Carried(taken, 20, _time.GetTimestamp());
            Assert.Equal((byte)30, slot.Buffered()!.Value.Row.ProgressPercent);

            // --- Carried at its version, it empties ---
            slot.Carried(slot.Buffered()!.Value.Version, 30, _time.GetTimestamp());
            Assert.Null(slot.Buffered());
        }

        // --- The renewal schedule, the jumps and the abandonment ---

        [Fact]
        public void A_lease_is_due_a_renewal_a_fifth_of_its_length_after_its_claim_and_after_each_renewal()
        {
            using JobLease lease = Track([Row(1)], Token);
            long claimed = _time.GetTimestamp();

            // The default lease is 300 s, so a renewal is due every 60 s.
            Assert.Equal(TimeSpan.FromSeconds(60), lease.RenewalInterval);
            _time.Advance(TimeSpan.FromSeconds(59));
            Assert.False(lease.IsRenewalDue);
            _time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(lease.IsRenewalDue);

            // --- A renewal counts from when it was sent; an older one never moves the schedule back ---
            long sent = _time.GetTimestamp();
            _time.Advance(TimeSpan.FromSeconds(10));
            lease.Renewed(sent);
            lease.Renewed(claimed);
            Assert.Equal(TimeSpan.FromSeconds(10), lease.SinceRenewal);
            _time.Advance(TimeSpan.FromSeconds(49));
            Assert.False(lease.IsRenewalDue);
            _time.Advance(TimeSpan.FromSeconds(1));
            Assert.True(lease.IsRenewalDue);
        }

        [Fact]
        public void Once_the_final_renewal_bound_a_lease_a_renewal_extends_it_only_to_the_end_that_renewal_set()
        {
            using JobLease lease = Track([Row(1)], Token);
            Assert.Equal(lease.LeaseSeconds, lease.RenewalSeconds);

            // --- Bound to thirty seconds: a renewal ten seconds on extends the lease by the twenty left ---
            lease.BindToStop(_time.GetTimestamp(), leaseSeconds: 30);
            Assert.Equal(30, lease.RenewalSeconds);
            _time.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(20, lease.RenewalSeconds);

            // --- A second binding changes nothing, and past the end a renewal extends it by a second ---
            lease.BindToStop(_time.GetTimestamp(), leaseSeconds: 300);
            _time.Advance(TimeSpan.FromSeconds(25));
            Assert.Equal(1, lease.RenewalSeconds);
        }

        [Fact]
        public void A_lease_keeps_the_end_its_final_renewal_wrote_only_once_bound_to_the_hosts_stop()
        {
            using JobLease lease = Track([Row(1)], Token);
            Assert.False(lease.KeepsStopEnd);

            // --- Unbound, there is no final end to keep ---
            _ = Assert.Throws<InvalidOperationException>(lease.KeepStopEnd);
            Assert.False(lease.KeepsStopEnd);

            // --- Bound, then written by the final renewal: later renewals keep that end ---
            lease.BindToStop(_time.GetTimestamp(), leaseSeconds: 30);
            Assert.False(lease.KeepsStopEnd);
            lease.KeepStopEnd();
            Assert.True(lease.KeepsStopEnd);
        }

        [Fact]
        public void A_jump_of_five_points_from_the_carried_percent_renews_at_most_once_every_five_seconds()
        {
            using JobLease lease = Track([Row(1)], Token);
            JobSlot slot = lease.Slot(1)!;

            // --- Four points is no jump; five is ---
            Assert.False(slot.TryJump(4, _time));
            Assert.True(slot.TryJump(5, _time));

            // --- Another jump within five seconds waits, however far the percent moved ---
            _time.Advance(TimeSpan.FromSeconds(4.9));
            Assert.False(slot.TryJump(60, _time));
            _time.Advance(TimeSpan.FromSeconds(0.1));
            Assert.True(slot.TryJump(60, _time));

            // --- A renewal that carried a percent moves both the base and the clock of the next jump ---
            _time.Advance(TimeSpan.FromSeconds(10));
            slot.Carried(slot.BufferVersion, 60, _time.GetTimestamp());
            _time.Advance(TimeSpan.FromSeconds(5));
            Assert.False(slot.TryJump(64, _time));
            Assert.True(slot.TryJump(65, _time));
        }

        [Fact]
        public void The_cancel_clock_starts_once_every_item_had_its_cancel_requested_and_an_abandoned_lease_is_no_longer_carried()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            Assert.Null(lease.SinceCancelled);

            // --- One item's cancel leaves the other running: no clock, and the batch token stands ---
            lease.RequestCancel(1);
            Assert.Null(lease.SinceCancelled);
            Assert.True(lease.Slot(1)!.Token.IsCancellationRequested);
            Assert.False(lease.Slot(2)!.Token.IsCancellationRequested);

            // --- The last item's cancel starts it; it counts from then ---
            _time.Advance(TimeSpan.FromSeconds(30));
            lease.RequestCancel(2);
            _time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(TimeSpan.FromSeconds(5), lease.SinceCancelled);
            lease.RequestCancel(2);
            Assert.Equal(TimeSpan.FromSeconds(5), lease.SinceCancelled);

            // --- Abandoned once, and left out of every renewal from then on ---
            Assert.True(lease.Abandon());
            Assert.False(lease.Abandon());
            Assert.True(lease.IsAbandoned);
            Assert.Empty(JobRenewal.Carried(_leases.InFlight(TenantId)));
        }

        [Fact]
        public void The_cancel_clock_starts_when_the_last_item_whose_cancel_was_not_requested_leaves_the_lease()
        {
            using JobLease lease = Track([Row(1), Row(2), Row(3)], Token);
            lease.RequestCancel(1);
            lease.Leave([2]);
            Assert.Null(lease.SinceCancelled);

            // --- Item 3 completes: only the cancelled item is left, so the handler has nothing more to run ---
            lease.Leave([3]);
            Assert.Equal(TimeSpan.Zero, lease.SinceCancelled);
        }

        [Fact]
        public void A_lost_lease_is_reported_once_and_the_instance_lists_its_leases_by_tenant()
        {
            using JobLease mine = Track([Row(1)], Token);
            using JobLease other = _leases.Track(TenantId + 1, Handler, Guid.NewGuid(), [Row(2)], Token);

            Assert.True(mine.TryReportLoss());
            Assert.False(mine.TryReportLoss());
            Assert.Equal([TenantId, TenantId + 1], _leases.Tenants().Order());
            Assert.Equal(2, _leases.All().Count);
        }

        // --- The renewal's verdicts ---

        [Fact]
        public void A_renewal_carries_the_leases_with_a_row_left_to_judge_and_leaves_out_those_completing_or_lost()
        {
            using JobLease running = Track([Row(1)], Token);
            using JobLease partly = Track([Row(2), Row(3)], Token);
            using JobLease completing = Track([Row(4), Row(5)], Token);
            using JobLease lost = Track([Row(6)], Token);

            // --- One partition of a claim completing keeps its lease carried; a claim wholly completing does not ---
            partly.Slot(2)!.BeginCompleting();
            completing.Slot(4)!.BeginCompleting();
            completing.Slot(5)!.BeginCompleting();
            lost.Lose();

            Assert.Equal([running, partly], JobRenewal.Carried(_leases.InFlight(TenantId)));
        }

        [Fact]
        public void A_renewal_that_returned_every_row_under_the_claims_token_keeps_the_lease()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobItem<Job> item = Bound(lease, 1);

            IReadOnlyList<JobLease> lost = JobRenewal.Judge([lease], Renewed(lease, 1, 2));

            Assert.Empty(lost);
            Assert.False(lease.IsLost);
            Assert.False(lease.ScopeToken.IsCancellationRequested);
            Assert.False(item.CancellationToken.IsCancellationRequested);
        }

        [Fact]
        public void A_row_the_renewal_did_not_return_loses_its_claims_whole_lease_and_no_other()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            using JobLease other = Track([Row(3)], Token);
            JobItem<Job> first = Bound(lease, 1);
            JobItem<Job> second = Bound(lease, 2);

            IReadOnlyList<JobLease> lost = JobRenewal.Judge([lease, other], [.. Renewed(lease, 2), .. Renewed(other, 3)]);

            Assert.Same(lease, Assert.Single(lost));
            Assert.True(lease.IsLost);
            Assert.True(lease.ScopeToken.IsCancellationRequested);
            Assert.Equal([JobCancellationReason.LeaseLost, JobCancellationReason.LeaseLost], new[] { first, second }.Select(static item => item.CancellationReason));
            Assert.False(other.IsLost);
            Assert.False(other.ScopeToken.IsCancellationRequested);
        }

        [Fact]
        public void A_row_the_renewal_returned_under_another_token_loses_the_lease()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobItem<Job> item = Bound(lease, 2);

            // The row was claimed again elsewhere, so the renewal reads it under the new claim's token.
            IReadOnlyList<JobLease> lost = JobRenewal.Judge([lease], [new RenewedJob(1, lease.Token, null), new RenewedJob(2, Guid.NewGuid(), null)]);

            Assert.Same(lease, Assert.Single(lost));
            Assert.True(lease.ScopeToken.IsCancellationRequested);
            Assert.Equal(JobCancellationReason.LeaseLost, item.CancellationReason);
        }

        [Fact]
        public void A_cancel_the_renewal_returned_stops_its_item_alone_and_never_the_scope()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            JobItem<Job> first = Bound(lease, 1);
            JobItem<Job> second = Bound(lease, 2);

            IReadOnlyList<JobLease> lost = JobRenewal.Judge([lease], [new RenewedJob(1, lease.Token, DateTimeOffset.UtcNow), new RenewedJob(2, lease.Token, null)]);

            Assert.Empty(lost);
            Assert.False(lease.IsLost);
            Assert.True(first.CancellationToken.IsCancellationRequested);
            Assert.Equal(JobCancellationReason.CancelRequested, first.CancellationReason);
            Assert.False(second.CancellationToken.IsCancellationRequested);
            Assert.False(lease.ScopeToken.IsCancellationRequested);
        }

        [Fact]
        public void A_row_whose_completion_is_being_sent_is_not_judged()
        {
            using JobLease lease = Track([Row(1), Row(2)], Token);
            using JobLease completing = Track([Row(3)], Token);
            JobItem<Job> second = Bound(lease, 2);

            // --- One partition's rows are completing and already ended; another partition still runs ---
            lease.Slot(1)!.BeginCompleting();
            completing.Slot(3)!.BeginCompleting();
            IReadOnlyList<JobLease> lost = JobRenewal.Judge([lease, completing], Renewed(lease, 2));

            Assert.Empty(lost);
            Assert.False(lease.IsLost);
            Assert.False(completing.IsLost);
            Assert.False(second.CancellationToken.IsCancellationRequested);

            // --- A cancel requested of a completing row stops nothing: its completion already decided it ---
            _ = JobRenewal.Judge([lease], [new RenewedJob(1, lease.Token, DateTimeOffset.UtcNow), .. Renewed(lease, 2)]);
            Assert.False(lease.Slot(1)!.Token.IsCancellationRequested);
        }

        // --- Partitions ---

        [Fact]
        public void A_claim_partitions_by_run_as_user_and_consent_in_claim_order()
        {
            ClaimedJob[] rows =
            [
                Row(1, runAs: 7),
                Row(2, runAs: 8),
                Row(3, runAs: 7, confirmed: "Sales.Backdated"),
                Row(4, runAs: 7),
                Row(5, runAs: 7, confirmed: "Inventory.Negative, Sales.Backdated"),
                Row(6, runAs: 7, confirmed: "Sales.Backdated,Inventory.Negative"),
                Row(7, runAs: 8),
            ];

            IReadOnlyList<IReadOnlyList<ClaimedJob>> partitions = JobRunner.Partition(rows);

            // The consent is compared as a set of keys, however it was spelled.
            Assert.Equal(
                [[1, 4], [2, 7], [3], [5, 6]],
                partitions.Select(static partition => partition.Select(static row => row.Job.Id).ToArray()));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A claimed row of the arguments-only handler.</summary>
        /// <param name="id">The job's id.</param>
        /// <param name="runAs">The run-as user.</param>
        /// <param name="confirmed">The confirmed keys it carries.</param>
        /// <returns>The row.</returns>
        private static ClaimedJob Row(int id, int runAs = 7, string? confirmed = null)
        {
            return new ClaimedJob(new Job { Id = id, HandlerKey = "acme.recompute-balances", RunAsUserId = runAs, Attempts = 1 }, null, confirmed, Guid.Empty);
        }

        /// <summary>Tracks a claim of rows under a fresh token.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="host">The host's token.</param>
        /// <returns>The lease.</returns>
        private JobLease Track(IReadOnlyList<ClaimedJob> rows, CancellationToken host)
        {
            return _leases.Track(TenantId, Handler, Guid.NewGuid(), rows, host);
        }

        /// <summary>The rows a renewal returned for a lease, under its token and with no cancel requested.</summary>
        /// <param name="lease">The lease.</param>
        /// <param name="ids">The jobs returned.</param>
        /// <returns>The rows.</returns>
        private static RenewedJob[] Renewed(JobLease lease, params int[] ids)
        {
            return [.. ids.Select(id => new RenewedJob(id, lease.Token, null))];
        }

        /// <summary>An item bound to its slot, as the runner binds it before invoking the handler.</summary>
        /// <param name="lease">The lease.</param>
        /// <param name="jobId">The job.</param>
        /// <returns>The item.</returns>
        private static JobItem<Job> Bound(JobLease lease, int jobId)
        {
            JobSlot slot = lease.Slot(jobId)!;
            JobItem<Job> item = new() { Job = slot.Row.Job, Item = slot.Row.Job, CancellationToken = slot.Token, Progress = new NoProgress() };
            slot.Bind(item.TrySetCancellationReason);
            return item;
        }

        /// <summary>A progress sink that records nothing.</summary>
        private sealed class NoProgress : IJobProgress
        {
            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(Abstractions.Data.IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }
}
