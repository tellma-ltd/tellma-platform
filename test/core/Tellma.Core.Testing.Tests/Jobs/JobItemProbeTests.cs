// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Testing.Jobs;

namespace Tellma.Core.Testing.Tests.Jobs
{
    /// <summary>
    ///     The job probe as a handler's own test uses it: a sample handler resumed from a seeded
    ///     checkpoint, retried, succeeded with a notification, and stopped on a requested cancel.
    /// </summary>
    public class JobItemProbeTests
    {
        private readonly CountingHandler _handler = new();

        [Fact]
        public async Task A_fresh_item_is_marked_for_a_retry_with_its_error()
        {
            JobItem<Job> item = JobItemProbe.ForJob(NewJob(), cancellationToken: TestContext.Current.CancellationToken);

            await _handler.ExecuteAsync(Batch(item));

            Assert.Equal(
                new JobItemOutcome(JobItemOutcomeKind.Retry, TimeSpan.FromSeconds(1), new JobError("Count.NotYet")),
                JobItemProbe.OutcomeOf(item));
            Assert.Empty(JobItemProbe.SuccessNotificationsOf(item));
        }

        [Fact]
        public async Task An_item_resumed_from_its_checkpoint_succeeds_and_queues_its_notification()
        {
            JobItem<Job> item = JobItemProbe.ForJob(NewJob(), checkpoint: new Checkpoint(2), cancellationToken: TestContext.Current.CancellationToken);

            await _handler.ExecuteAsync(Batch(item));

            Assert.Equal(new JobItemOutcome(JobItemOutcomeKind.Succeeded, null, null), JobItemProbe.OutcomeOf(item));
            Assert.Equal("acme.count.done", Assert.Single(JobItemProbe.SuccessNotificationsOf(item)).Type);
        }

        [Fact]
        public async Task A_requested_cancel_stops_the_handler_and_leaves_the_item_unmarked()
        {
            using CancellationTokenSource source = new();
            JobItem<Job> item = JobItemProbe.ForJob(NewJob(), cancellationToken: source.Token);

            // The worker records the reason before it cancels the token.
            Assert.True(JobItemProbe.SetCancellationReason(item, JobCancellationReason.CancelRequested));
            await source.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _handler.ExecuteAsync(Batch(item)));
            Assert.Equal(JobCancellationReason.CancelRequested, item.CancellationReason);
            Assert.Null(JobItemProbe.OutcomeOf(item));
        }

        [Fact]
        public void A_lost_lease_replaces_another_reason_and_none_is_refused()
        {
            JobItem<Job> item = JobItemProbe.ForJob(NewJob(), cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(JobItemProbe.SetCancellationReason(item, JobCancellationReason.HostStopping));
            Assert.True(JobItemProbe.SetCancellationReason(item, JobCancellationReason.LeaseLost));
            Assert.False(JobItemProbe.SetCancellationReason(item, JobCancellationReason.CancelRequested));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => JobItemProbe.SetCancellationReason(item, JobCancellationReason.None));
        }

        [Fact]
        public void A_checkpoint_is_stored_under_the_platforms_rules_and_can_be_cleared()
        {
            Job job = NewJob();
            Export export = new() { Id = 7 };
            JobItem<Export> item = JobItemProbe.ForItem(job, export, checkpoint: new Checkpoint(5), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Same(export, item.Item);
            Assert.Equal(new Checkpoint(5), item.State<Checkpoint>());

            // Stored as the worker stores it: camelCase members under the platform's rules.
            Assert.Equal(["done"], item.State<Dictionary<string, int>>()!.Keys);

            JobItemProbe.SetCheckpoint(item, null);
            Assert.Null(item.State<Checkpoint>());
            Assert.Null(JobItemProbe.OutcomeOf(item));
        }

        /// <summary>A job as a claim would return it.</summary>
        /// <returns>The job.</returns>
        private static Job NewJob()
        {
            return new Job { Id = 1, HandlerKey = "acme.count", RunAsUserId = 1 };
        }

        /// <summary>A batch of one item over a batch the sample handler never touches.</summary>
        /// <param name="item">The item.</param>
        /// <returns>The batch.</returns>
        private static JobBatch<Job> Batch(JobItem<Job> item)
        {
            return new JobBatch<Job> { Items = [item], Batch = DispatchProxy.Create<IDataBatch, UnusedBatch>() };
        }

        /// <summary>A handler's checkpoint: how many runs are done.</summary>
        /// <param name="Done">The runs done.</param>
        public sealed record Checkpoint(int Done);

        /// <summary>A row an entity-backed handler would process.</summary>
        public sealed class Export
        {
            /// <summary>The row's id.</summary>
            public int Id { get; init; }
        }

        /// <summary>
        ///     A sample handler that needs three runs: each run before the third retries after as many
        ///     seconds as runs are done, and the third succeeds and notifies.
        /// </summary>
        private sealed class CountingHandler : IJobHandler
        {
            /// <inheritdoc />
            public Task ExecuteAsync(JobBatch<Job> batch)
            {
                foreach (JobItem<Job> item in batch.Items)
                {
                    item.CancellationToken.ThrowIfCancellationRequested();
                    int done = (item.State<Checkpoint>()?.Done ?? 0) + 1;
                    if (done < 3)
                    {
                        item.Retry(TimeSpan.FromSeconds(done), new JobError("Count.NotYet"));
                        continue;
                    }

                    item.NotifyOnSuccess(new NotificationRequest("acme.count.done", [item.Job.RunAsUserId], null, null, null, null, null));
                    item.Succeed();
                }

                return Task.CompletedTask;
            }
        }
    }

    /// <summary>A batch whose every member throws; the sample handler holds a batch without using it.</summary>
    public class UnusedBatch : DispatchProxy
    {
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new NotSupportedException($"The sample handler uses no batch, yet {targetMethod?.Name} was called.");
        }
    }
}
