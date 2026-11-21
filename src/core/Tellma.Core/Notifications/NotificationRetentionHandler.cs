// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Notifications
{
    // The handler is spec 0021 §7: two bands, read rows aged from when they were read and unread
    // rows aged from when they were created, each deleted a page per maintenance round trip until a
    // page comes back short. Each page commits on its own, so a run cut short resumes where it
    // stopped, and progress is reported after every page. Retention reads neither preferences nor
    // types: a row of a removed type ages out like any other.
    /// <summary>Deletes inbox rows past their retention: read rows after one period, unread rows after a longer one.</summary>
    /// <param name="database">The tenant's database, which each page's maintenance batch comes from.</param>
    /// <param name="options">The notifications' options: the page size and both retentions.</param>
    /// <param name="metrics">The notifications' instruments.</param>
    [JobHandler(Key, LeaseSeconds = 600, RunsWhenReadOnly = true)]
    internal sealed class NotificationRetentionHandler(ITenantDatabase database, IOptions<NotificationsOptions> options, NotificationsMetrics metrics) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.notification-retention";

        /// <summary>The resource key of the progress message, whose arguments are the rows deleted so far in each band.</summary>
        public const string ProgressKey = "Notifications_RetentionProgress";

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            NotificationsOptions settings = options.Value;
            foreach (JobItem<Job> item in batch.Items)
            {
                // --- The read band, then the unread band, each until a page is short ---
                int read = await BandAsync(item, read: true, settings, deletedRead: 0).ConfigureAwait(false);
                _ = await BandAsync(item, read: false, settings, deletedRead: read).ConfigureAwait(false);
            }
        }

        /// <summary>Deletes one band a page at a time, until a page deletes fewer rows than a page holds.</summary>
        /// <param name="item">The job item, whose token cancels between pages and whose progress each page reports.</param>
        /// <param name="read">True for the read band; false for the unread band.</param>
        /// <param name="settings">The page size and both retentions.</param>
        /// <param name="deletedRead">The read band's total so far, for the unread band's progress.</param>
        /// <returns>The rows the band deleted.</returns>
        private async Task<int> BandAsync(JobItem<Job> item, bool read, NotificationsOptions settings, int deletedRead)
        {
            int total = 0;
            while (true)
            {
                item.CancellationToken.ThrowIfCancellationRequested();

                // --- One page: its own maintenance round trip, committed on its own ---
                var page = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                BatchResult<int> deleted = NotificationDeliveryStatements.RetentionPage(page, read, settings.RetentionPageSize, settings.ReadRetention, settings.UnreadRetention);
                _ = await page.ExecuteAsync(item.CancellationToken).ConfigureAwait(false);

                // --- The count, the instrument, and the progress ---
                total += deleted.Value;
                metrics.RecordRetained(read ? NotificationsMetrics.ReadBand : NotificationsMetrics.UnreadBand, deleted.Value);
                item.Progress.Report(
                    null,
                    new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["read"] = read ? total : deletedRead,
                        ["unread"] = read ? 0 : total,
                    }),
                    null);

                if (deleted.Value < settings.RetentionPageSize)
                {
                    return total;
                }
            }
        }
    }
}
