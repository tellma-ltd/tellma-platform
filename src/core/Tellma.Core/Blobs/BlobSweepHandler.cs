// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Blobs
{
    // spec 0017 §7.1: the sweep. Each round claims a batch of due rows on the database clock, deletes
    // their objects, then deletes the rows whose objects are gone; a round shorter than the batch size
    // ends the run, which then purges the store's incomplete artefacts once. Objects go before rows,
    // the mirror of staging's row before bytes, so no object is ever left without a row that leads the
    // sweep to it. A failure is metered and leaves its rows claimed for the sweep after their reclaim
    // instant; it never fails the run.
    /// <summary>
    ///     Deletes the objects and rows of staged blobs past their deadline and of released blobs, then
    ///     removes the store's incomplete artefacts.
    /// </summary>
    /// <remarks>
    ///     The store is resolved when the sweep runs, so a host composed without one fails its startup
    ///     check rather than the container's validation.
    /// </remarks>
    /// <param name="database">The tenant's database, which each round's maintenance batches come from.</param>
    /// <param name="services">The job scope's services, which resolve the blob store.</param>
    /// <param name="accessor">The job scope's request context, which names the tenant.</param>
    /// <param name="options">The sweep's batch size and reclaim span.</param>
    /// <param name="metrics">The sweep instruments.</param>
    /// <param name="time">The clock of the purge's cut-off.</param>
    /// <param name="logger">The sweep's log.</param>
    [JobHandler(Key, BatchSize = 1, LeaseSeconds = 600, MaxAttempts = 3, MaxConcurrency = 1, RunsWhenReadOnly = true)]
    internal sealed class BlobSweepHandler(
        ITenantDatabase database,
        IServiceProvider services,
        IRequestContextAccessor accessor,
        IOptions<BlobOptions> options,
        BlobMaintenanceMetrics metrics,
        TimeProvider time,
        ILogger<BlobSweepHandler> logger) : IJobHandler
    {
        /// <summary>The handler's key, which its built-in schedule names.</summary>
        public const string Key = "core.blob-sweep";

        /// <summary>The handler's built-in schedule: its id in the platform's reserved band.</summary>
        public const int ScheduleId = 4;

        /// <summary>The built-in schedule's expression: every 15 minutes.</summary>
        public const string Cron = "*/15 * * * *";

        /// <summary>The key of the progress message, whose arguments count the rows claimed and the objects deleted so far.</summary>
        public const string ProgressKey = "Blob.SweepProgress";

        /// <summary>The most rows whose objects the sweep deletes at once when it deletes them row by row.</summary>
        private const int RowDeleteParallelism = 8;

        /// <summary>How old an incomplete artefact must be before the sweep removes it: a day, far beyond any write in flight.</summary>
        public static TimeSpan PurgeAge { get; } = TimeSpan.FromHours(24);

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            int tenantId = accessor.Current.RequireTenant().Id;
            IBlobStore store = services.GetRequiredService<IBlobStore>();
            BlobOptions settings = options.Value;

            foreach (JobItem<Job> item in batch.Items)
            {
                CancellationToken cancellationToken = item.CancellationToken;
                long claimedTotal = 0;
                long objectsTotal = 0;
                while (true)
                {
                    // --- Between rounds: the job's cancellation ---
                    cancellationToken.ThrowIfCancellationRequested();

                    // --- The claim: its own maintenance round trip, committed as it ends ---
                    var claim = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                    claim.TransactionMode = TransactionMode.None;
                    BatchResult<IReadOnlyList<ClaimedBlob>> claimed = BlobStatements.Claim(claim, settings.SweepBatchSize, settings.SweepReclaimAfter);
                    _ = await claim.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    IReadOnlyList<ClaimedBlob> rows = claimed.Value;

                    // --- The objects, then the rows whose objects are gone ---
                    Round round = rows.Count == 0
                        ? new Round(0, 0, 0)
                        : await SweepAsync(store, tenantId, rows, cancellationToken).ConfigureAwait(false);
                    claimedTotal += rows.Count;
                    objectsTotal += round.Objects;
                    if (rows.Count > 0)
                    {
                        BlobMaintenanceLog.SweepBatch(logger, rows.Count, round.Deleted, round.Failed);
                    }

                    item.Progress.Report(
                        null,
                        new JobMessage(ProgressKey, new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["claimed"] = claimedTotal,
                            ["deleted"] = objectsTotal,
                        }),
                        null);

                    // --- A short round: nothing more is due ---
                    if (rows.Count < settings.SweepBatchSize)
                    {
                        break;
                    }
                }

                // --- Once per run: the store's incomplete artefacts ---
                await PurgeAsync(store, tenantId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Deletes one round's objects and then the rows whose objects are gone, metering both.</summary>
        /// <param name="store">The blob store.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="rows">The rows the round claimed; not empty.</param>
        /// <param name="cancellationToken">The job's cancellation.</param>
        /// <returns>The round's counts.</returns>
        private async Task<Round> SweepAsync(IBlobStore store, int tenantId, IReadOnlyList<ClaimedBlob> rows, CancellationToken cancellationToken)
        {
            // --- Every row's names; a row whose names cannot be formed (staging never writes one) is left claimed ---
            List<(ClaimedBlob Row, string[] Names)> named = new(rows.Count);
            int failed = 0;
            foreach (ClaimedBlob row in rows)
            {
                string[]? names = BlobObjectNames.Of(row.StorageKey, row.Variants);
                if (names is null)
                {
                    failed++;
                }
                else
                {
                    named.Add((row, names));
                }
            }

            // --- The objects: one call, and row by row when it fails, so one bad object holds back only its row ---
            List<(ClaimedBlob Row, string[] Names)> gone;
            try
            {
                await store.DeleteAsync(tenantId, [.. named.SelectMany(static entry => entry.Names)], cancellationToken).ConfigureAwait(false);
                gone = named;
            }
            catch (BlobStoreException)
            {
                gone = await DeleteRowByRowAsync(store, tenantId, named, cancellationToken).ConfigureAwait(false);
                failed += named.Count - gone.Count;
            }

            // --- The rows whose objects are gone ---
            IReadOnlyList<int> deleted = gone.Count == 0 ? [] : await DeleteRowsAsync([.. gone.Select(static entry => entry.Row.Id)]).ConfigureAwait(false);
            failed += gone.Count - deleted.Count;

            // --- The meters: deletions by the state each row was claimed from, then the failures ---
            HashSet<int> deletedIds = [.. deleted];
            foreach (IGrouping<BlobState, ClaimedBlob> group in gone.Select(static entry => entry.Row).Where(row => deletedIds.Contains(row.Id)).GroupBy(static row => row.PreviousState))
            {
                metrics.Deleted(group.Key, group.Count());
            }

            metrics.Failed(failed);
            return new Round(deleted.Count, failed, gone.Sum(static entry => entry.Names.Length));
        }

        /// <summary>Deletes each row's objects on its own, after the round's one call failed.</summary>
        /// <param name="store">The blob store.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="named">The rows and their names.</param>
        /// <param name="cancellationToken">The job's cancellation.</param>
        /// <returns>The rows whose objects are gone, in their claim order.</returns>
        private async Task<List<(ClaimedBlob Row, string[] Names)>> DeleteRowByRowAsync(
            IBlobStore store, int tenantId, List<(ClaimedBlob Row, string[] Names)> named, CancellationToken cancellationToken)
        {
            bool[] done = new bool[named.Count];
            BlobStoreException? first = null;
            await Parallel.ForEachAsync(
                Enumerable.Range(0, named.Count),
                new ParallelOptions { MaxDegreeOfParallelism = RowDeleteParallelism, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    try
                    {
                        await store.DeleteAsync(tenantId, named[index].Names, token).ConfigureAwait(false);
                        done[index] = true;
                    }
                    catch (BlobStoreException exception)
                    {
                        _ = Interlocked.CompareExchange(ref first, exception, null);
                    }
                }).ConfigureAwait(false);

            List<(ClaimedBlob Row, string[] Names)> gone = [.. named.Where((_, index) => done[index])];
            if (first is not null)
            {
                BlobMaintenanceLog.SweepStoreFailed(logger, first, BlobTelemetryTagValues.StoreDelete, named.Count - gone.Count);
            }

            return gone;
        }

        /// <summary>
        ///     Deletes the rows whose objects are gone, and row by row when a row is still referenced,
        ///     so that row alone is retained.
        /// </summary>
        /// <remarks>
        ///     The delete runs to the end whatever a requested cancel of the job: the objects are
        ///     already gone, and a row left behind would only be claimed again. A lost lease or the
        ///     host's stop still ends the scope, and the rows it left are claimed again after their
        ///     reclaim instant, deleting objects already gone.
        /// </remarks>
        /// <param name="ids">The rows; not empty.</param>
        /// <returns>The ids deleted.</returns>
        private async Task<IReadOnlyList<int>> DeleteRowsAsync(IReadOnlyList<int> ids)
        {
            try
            {
                return await DeleteAsync(ids).ConfigureAwait(false);
            }
            catch (ForeignKeyViolationException)
            {
                // The impossible case of spec 0017 §7.1: find the referenced rows one by one.
                List<int> deleted = new(ids.Count);
                foreach (int id in ids)
                {
                    try
                    {
                        deleted.AddRange(await DeleteAsync([id]).ConfigureAwait(false));
                    }
                    catch (ForeignKeyViolationException exception)
                    {
                        BlobMaintenanceLog.SweepRowRetained(logger, exception, id);
                    }
                }

                return deleted;
            }

            // One round trip of the row delete.
            async Task<IReadOnlyList<int>> DeleteAsync(IReadOnlyList<int> some)
            {
                var round = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                BatchResult<IReadOnlyList<int>> result = BlobStatements.DeleteSwept(round, [.. some]);
                _ = await round.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
                return result.Value;
            }
        }

        /// <summary>Removes the store's incomplete artefacts older than a day; a failure is metered and logged, never thrown.</summary>
        /// <param name="store">The blob store.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">The job's cancellation.</param>
        /// <returns>A task that completes when the purge ended.</returns>
        private async Task PurgeAsync(IBlobStore store, int tenantId, CancellationToken cancellationToken)
        {
            try
            {
                await store.PurgeIncompleteAsync(tenantId, time.GetUtcNow() - PurgeAge, cancellationToken).ConfigureAwait(false);
            }
            catch (BlobStoreException exception)
            {
                BlobMaintenanceLog.SweepStoreFailed(logger, exception, BlobTelemetryTagValues.StorePurge, 0);
                metrics.Failed(1);
            }
        }

        /// <summary>One round's counts.</summary>
        /// <param name="Deleted">The rows deleted.</param>
        /// <param name="Failed">The rows left claimed.</param>
        /// <param name="Objects">The objects deleted.</param>
        private readonly record struct Round(int Deleted, int Failed, int Objects);
    }
}
