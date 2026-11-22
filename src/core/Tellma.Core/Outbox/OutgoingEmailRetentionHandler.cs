// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §7.3: the nightly retention, after core.job-retention, as the system user. Each page
    // reads up to 500 ids of rows older than the retention by the database clock that the delete's
    // precondition accepts (settled, or unsettled with no live job), then deletes them through the
    // stack's DeleteByIdsAsync, so the delete's own rules run and the blob effect releases the
    // attachments for the sweep. Each page commits on its own and reports progress, until a page is
    // short. A row that changes between a page's read and its delete (an administrator's retry, or
    // their own delete) refuses the delete, so the page is read again, once, before the refusal
    // stands.
    /// <summary>Deletes the outgoing emails older than the outbox's retention that are settled or abandoned.</summary>
    /// <param name="pages">Reads each page and deletes it through the outgoing emails' stack.</param>
    /// <param name="options">The outbox's options: the retention.</param>
    /// <param name="metrics">The outbox's instruments.</param>
    /// <param name="loggers">Creates the outbox's logger.</param>
    [JobHandler(Key, LeaseSeconds = 600, RunsWhenReadOnly = true)]
    internal sealed class OutgoingEmailRetentionHandler(
        IOutboxRetentionPages pages,
        IOptions<OutboxOptions> options,
        OutboxMetrics metrics,
        ILoggerFactory loggers) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.outgoing-email-retention";

        /// <summary>The handler's built-in schedule: its id in the platform's reserved band.</summary>
        public const int ScheduleId = 7;

        /// <summary>The built-in schedule's expression: every day at 03:45, after the job retention.</summary>
        public const string Cron = "45 3 * * *";

        /// <summary>The resource key of the progress message, whose argument counts the emails deleted so far.</summary>
        public const string ProgressKey = "Outbox_RetentionProgress";

        /// <summary>The most emails one page deletes.</summary>
        public const int PageSize = 500;

        /// <summary>Reports a page read again.</summary>
        private readonly ILogger _logger = loggers.CreateLogger(OutboxLog.Category);

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            TimeSpan retention = options.Value.Retention;
            foreach (JobItem<Job> item in batch.Items)
            {
                // --- Pages until one is short; each read, deleted, counted and reported ---
                int total = 0;
                while (true)
                {
                    item.CancellationToken.ThrowIfCancellationRequested();
                    (int read, int deleted) = await PageAsync(retention, item.CancellationToken).ConfigureAwait(false);
                    total += deleted;
                    metrics.RecordRetained(deleted);
                    item.Progress.Report(
                        null,
                        new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal) { ["deleted"] = total }),
                        null);

                    // A full page that deleted nothing would read the same page again forever.
                    if (read < PageSize || deleted == 0)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>Reads one page of ids and deletes them, reading the page again once should a row change in between.</summary>
        /// <param name="retention">How long a row is kept.</param>
        /// <param name="cancellationToken">Cancels the read and the delete's rounds.</param>
        /// <returns>How many ids the last read returned, and how many rows the page deleted.</returns>
        private async Task<(int Read, int Deleted)> PageAsync(TimeSpan retention, CancellationToken cancellationToken)
        {
            IReadOnlyList<int> ids = await pages.ReadAsync(PageSize, retention, cancellationToken).ConfigureAwait(false);
            if (ids.Count == 0)
            {
                return (0, 0);
            }

            try
            {
                return (ids.Count, await pages.DeleteAsync(ids, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception refused) when (refused is ValidationException or NotFoundException)
            {
                OutboxLog.OutboxRetentionPageReread(_logger, refused, ids.Count);
            }

            // --- The page again, as it stands now; a second refusal fails the run ---
            ids = await pages.ReadAsync(PageSize, retention, cancellationToken).ConfigureAwait(false);
            return ids.Count == 0
                ? (0, 0)
                : (ids.Count, await pages.DeleteAsync(ids, cancellationToken).ConfigureAwait(false));
        }
    }
}
