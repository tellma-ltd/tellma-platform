// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Writes;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §14.4: the nightly retention, in three loops of 1,000-row pages, each page its own
    // maintenance round trip committed on its own, so a run cut short resumes where it stopped.
    // First the stop: a job that has waited for confirmation longer than the failed jobs are kept is
    // queued flagged, so its next claim records the stop, and its notice is withdrawn after the page.
    // Then the succeeded jobs past their retention, then the failed and cancelled ones; the rows
    // referencing a deleted job lose it through their keys. A job that has not ended is never
    // deleted. Progress is reported after every page.
    /// <summary>
    ///     Stops the jobs that waited too long for confirmation, and deletes the ended jobs past their
    ///     retention: succeeded jobs after one period, failed and cancelled jobs after a longer one.
    /// </summary>
    /// <param name="database">The tenant's database, which each page's maintenance batch comes from.</param>
    /// <param name="notifier">The scope's notifier, which withdraws the stopped jobs' notices.</param>
    /// <param name="options">The jobs' options: both retention periods.</param>
    /// <param name="foreignKeys">The model's foreign keys whose delete action writes their table, which name the tables a deleted job's references write.</param>
    [JobHandler(Key, LeaseSeconds = 600, RunsWhenReadOnly = true)]
    internal sealed class JobRetentionHandler(ITenantDatabase database, INotifier notifier, IOptions<JobsOptions> options, ForeignKeyActions foreignKeys) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.job-retention";

        /// <summary>The handler's built-in schedule: its id in the platform's reserved band.</summary>
        public const int ScheduleId = 1;

        /// <summary>The built-in schedule's expression: every day at 03:00, before the other retentions.</summary>
        public const string Cron = "0 3 * * *";

        /// <summary>The key of the progress message, whose arguments count the jobs stopped and deleted so far.</summary>
        public const string ProgressKey = "Jobs.RetentionProgress";

        /// <summary>The most withdrawals one notifier call carries.</summary>
        private const int WithdrawalsPerCall = 100;

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            JobsOptions settings = options.Value;
            IReadOnlySet<TableName> deleteWrites = DeleteWrites();
            foreach (JobItem<Job> item in batch.Items)
            {
                // --- The stop first, so a stopped job's next claim runs before its row ages out ---
                Counts counts = new();
                await StopAsync(item, settings.FailedRetention, counts).ConfigureAwait(false);

                // --- Then each band, until a page is short ---
                await BandAsync(item, succeeded: true, settings.SucceededRetention, deleteWrites, counts).ConfigureAwait(false);
                await BandAsync(item, succeeded: false, settings.FailedRetention, deleteWrites, counts).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     The tables a delete page writes: the job table, and every table the model's keys to it
        ///     write when a job is deleted (each job entity's and the schedule states', whose keys set
        ///     the job to null).
        /// </summary>
        /// <returns>The tables.</returns>
        private HashSet<TableName> DeleteWrites()
        {
            HashSet<TableName> writes = [JobStatements.Jobs];
            writes.UnionWith(foreignKeys.WrittenByDeleting([JobStatements.Jobs]));
            return writes;
        }

        /// <summary>Stops the jobs that waited for confirmation too long, a page at a time, withdrawing each page's notices.</summary>
        /// <param name="item">The job item, whose token cancels between pages and whose progress each page reports.</param>
        /// <param name="failedRetention">How long a job may wait for confirmation.</param>
        /// <param name="counts">The run's counts so far.</param>
        /// <returns>A task that completes when a page stops fewer jobs than a page holds.</returns>
        private async Task StopAsync(JobItem<Job> item, TimeSpan failedRetention, Counts counts)
        {
            while (true)
            {
                item.CancellationToken.ThrowIfCancellationRequested();

                // --- One page: its own maintenance round trip, committed on its own ---
                var page = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                BatchResult<IReadOnlyList<StoppedJob>> stopped = JobStatements.RetentionStop(page, failedRetention);
                _ = await page.ExecuteAsync(item.CancellationToken).ConfigureAwait(false);

                // --- The stopped jobs' notices; a page re-run after an ambiguous failure outputs none, and those age out ---
                // The page committed, and its rows left the stop's predicate, so no later run finds them
                // again: their withdrawals ignore a requested cancel (spec 0020 §6.6 cancels the item
                // token so a handler can finish what it committed), and the scope's token still guards
                // each round trip. The cancel is honoured at the next page.
                foreach (StoppedJob[] chunk in stopped.Value.Chunk(WithdrawalsPerCall))
                {
                    await notifier.WithdrawAsync([.. chunk.Select(static job => new WithdrawRequest(JobNotices.PendingConfirmationKey(job.Id)))], CancellationToken.None).ConfigureAwait(false);
                }

                counts.Stopped += stopped.Value.Count;
                Report(item, counts);
                if (stopped.Value.Count < JobStatements.RetentionPageSize)
                {
                    return;
                }
            }
        }

        /// <summary>Deletes one band a page at a time, until a page deletes fewer jobs than a page holds.</summary>
        /// <param name="item">The job item, whose token cancels between pages and whose progress each page reports.</param>
        /// <param name="succeeded">True for the succeeded jobs; false for the failed and cancelled ones.</param>
        /// <param name="retention">How long the band's jobs are kept after they ended.</param>
        /// <param name="writes">The tables a delete page writes.</param>
        /// <param name="counts">The run's counts so far.</param>
        /// <returns>A task that completes when the band is done.</returns>
        private async Task BandAsync(JobItem<Job> item, bool succeeded, TimeSpan retention, IReadOnlySet<TableName> writes, Counts counts)
        {
            while (true)
            {
                item.CancellationToken.ThrowIfCancellationRequested();

                // --- One page: its own maintenance round trip, committed on its own ---
                var page = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                BatchResult<int> deleted = JobStatements.RetentionPage(page, succeeded, retention, writes);
                _ = await page.ExecuteAsync(item.CancellationToken).ConfigureAwait(false);

                if (succeeded)
                {
                    counts.Succeeded += deleted.Value;
                }
                else
                {
                    counts.Ended += deleted.Value;
                }

                Report(item, counts);
                if (deleted.Value < JobStatements.RetentionPageSize)
                {
                    return;
                }
            }
        }

        /// <summary>Reports the run's counts so far.</summary>
        /// <param name="item">The job item.</param>
        /// <param name="counts">The counts.</param>
        private static void Report(JobItem<Job> item, Counts counts)
        {
            item.Progress.Report(
                null,
                new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["stopped"] = counts.Stopped,
                    ["succeeded"] = counts.Succeeded,
                    ["ended"] = counts.Ended,
                }),
                null);
        }

        /// <summary>The jobs one run stopped and deleted so far.</summary>
        private sealed class Counts
        {
            /// <summary>The jobs stopped while waiting for confirmation.</summary>
            public int Stopped { get; set; }

            /// <summary>The succeeded jobs deleted.</summary>
            public int Succeeded { get; set; }

            /// <summary>The failed and cancelled jobs deleted.</summary>
            public int Ended { get; set; }
        }
    }
}
