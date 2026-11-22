// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Blobs
{
    // spec 0017 §7.2: the weekly backstop for owner rows removed outside the pipeline. Each registered
    // kind is reconciled by its own maintenance round trip over the kind's owner table and column, in
    // the order of the kinds' names; a released count is metered and warned of, because every non-zero
    // count points at a path that bypassed the save pipeline. Progress is reported after each kind.
    /// <summary>
    ///     Releases the committed blobs that no owner row references, so the sweep reclaims them.
    /// </summary>
    /// <param name="database">The tenant's database, which each kind's maintenance batch comes from.</param>
    /// <param name="kinds">The registered kinds, with their owner tables and columns.</param>
    /// <param name="metrics">The reconcile instrument.</param>
    /// <param name="logger">The reconcile's log.</param>
    [JobHandler(Key, BatchSize = 1, LeaseSeconds = 900, MaxAttempts = 3, MaxConcurrency = 1, RunsWhenReadOnly = true)]
    internal sealed class BlobReconcileHandler(
        ITenantDatabase database,
        IBlobKindRegistry kinds,
        BlobMaintenanceMetrics metrics,
        ILogger<BlobReconcileHandler> logger) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.blob-reconcile";

        /// <summary>The handler's built-in schedule: its id in the platform's reserved band.</summary>
        public const int ScheduleId = 5;

        /// <summary>The built-in schedule's expression: every Saturday at 02:00.</summary>
        public const string Cron = "0 2 * * 6";

        /// <summary>The key of the progress message, whose arguments count the kinds reconciled and the blobs released so far.</summary>
        public const string ProgressKey = "Blob.ReconcileProgress";

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // --- Each kind once, in the order of their names ---
            BlobKindDescriptor[] all = [.. kinds.All.OrderBy(static kind => kind.Kind, StringComparer.Ordinal)];
            foreach (JobItem<Job> item in batch.Items)
            {
                int released = 0;
                for (int index = 0; index < all.Length; index++)
                {
                    item.CancellationToken.ThrowIfCancellationRequested();

                    // --- One kind: its own maintenance round trip ---
                    BlobKindDescriptor kind = all[index];
                    var round = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                    BatchResult<int> result = BlobStatements.Reconcile(round, kind);
                    _ = await round.ExecuteAsync(item.CancellationToken).ConfigureAwait(false);

                    // --- A released count is a path that bypassed the pipeline: meter and warn ---
                    if (result.Value > 0)
                    {
                        metrics.Released(kind.Kind, result.Value);
                        BlobMaintenanceLog.ReconcileReleased(logger, kind.Kind, result.Value);
                    }

                    released += result.Value;
                    int reconciled = index + 1;
                    item.Progress.Report(
                        (byte)(reconciled * JobLimits.MaxProgressPercent / all.Length),
                        new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["reconciled"] = reconciled,
                            ["total"] = all.Length,
                            ["released"] = released,
                        }),
                        null);
                }
            }
        }
    }
}
