// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
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

        /// <summary>The leases under test.</summary>
        private readonly JobLeases _leases = new();

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
            slot.Carried(taken);
            Assert.Equal((byte)30, slot.Buffered()!.Value.Row.ProgressPercent);

            // --- Carried at its version, it empties ---
            slot.Carried(slot.Buffered()!.Value.Version);
            Assert.Null(slot.Buffered());
        }

        // --- The renewal's verdicts ---

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
