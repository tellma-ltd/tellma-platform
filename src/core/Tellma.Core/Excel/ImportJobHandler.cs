// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.4-§12.6: a background import runs as its initiator, in the job frame the worker
    // opens around the handler, in two passes over a local copy of the file its Import row owns.
    // Pass 1 parses the whole file with no database and checks what the /start route could not;
    // pass 2 saves the rows from the checkpoint on, in chunks that each commit alone with the next
    // checkpoint on their persist batch, so an attempt that ends anywhere resumes where the last
    // committed chunk left off and never applies a row twice. The outcome, a success, a stop or the
    // errors, is the Import row's update, enlisted with the job frame so it commits with the job.
    /// <summary>
    ///     The handler of a background import: parses the file an import row owns, saves its rows in
    ///     resumable chunks, and records the outcome on the row.
    /// </summary>
    /// <param name="services">The job scope, whose operations of the imported resource are resolved by its name.</param>
    /// <param name="imports">Reads the row's file and saves each chunk.</param>
    /// <param name="importer">Parses the file.</param>
    /// <param name="contexts">Builds the parse's context from the job's localization.</param>
    /// <param name="host">The job frame, which the Import row's update is enlisted with.</param>
    /// <param name="options">The codec's options: the chunk size, the atomic ceiling and the errors reported.</param>
    /// <param name="metrics">The codec's instruments.</param>
    /// <param name="loggers">The logger factory.</param>
    [JobHandler(
        ExcelJobs.ImportHandlerKey,
        BatchSize = 1,
        LeaseSeconds = 600,
        CommitsInSteps = true,
        ResultEntity = CoreResources.Import,
        ResultVia = nameof(Import.JobId),
        MaxAttempts = 3)]
    internal sealed class ImportJobHandler(
        IServiceProvider services,
        ExcelImports imports,
        IExcelImporter importer,
        ExcelContextFactory contexts,
        IOpenWriteHost host,
        IOptions<ExcelOptions> options,
        ExcelMetrics metrics,
        ILoggerFactory loggers) : IEntityJobHandler<Import>, IEnlists<Import>
    {
        /// <summary>The codec's options.</summary>
        private readonly ExcelOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

        /// <summary>The codec's logger.</summary>
        private readonly ILogger _logger = (loggers ?? throw new ArgumentNullException(nameof(loggers))).CreateLogger(ExcelLog.Category);

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Import> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            JobItem<Import> item = batch.Single;

            // --- An item handed over already stopped: no pass runs, the last checkpoint is the outcome ---
            if (item.CancellationReason == JobCancellationReason.CancelRequested)
            {
                string? stack = ExcelJobTargets.Import(services, item.Arguments<ImportJobArguments>().Resource)?.ExportTarget.Entity.Name;
                await StopAsync(item, session: null, stack, chunks: null).ConfigureAwait(false);
                return;
            }

            ThrowIfInterrupted(item);
            ImportJobArguments arguments = item.Arguments<ImportJobArguments>();
            LocalizationContext localization = contexts.Localize(arguments.Culture);
            using IDisposable culture = LocalizationScope.Enter(localization);
            await RunAsync(item, arguments, contexts.Create(localization)).ConfigureAwait(false);
        }

        /// <summary>Throws when the worker took the item back: its lease was lost or its host is stopping, so the next attempt resumes it.</summary>
        /// <param name="item">The job's item.</param>
        /// <exception cref="OperationCanceledException">The item's lease was lost or its host is stopping.</exception>
        private static void ThrowIfInterrupted(JobItem<Import> item)
        {
            if (item.CancellationReason is JobCancellationReason.LeaseLost or JobCancellationReason.HostStopping)
            {
                throw new OperationCanceledException("The import's attempt was interrupted; the next attempt resumes from its checkpoint.", item.CancellationToken);
            }
        }

        /// <summary>The ceiling an atomic import crossed, as the rows against it and its value; null within every ceiling.</summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="limits">The stack's limits.</param>
        /// <param name="maxAtomicRows">The atomic import's own row ceiling.</param>
        /// <returns>The rows and the ceiling, or null.</returns>
        private static (long Rows, long MaxRows)? AtomicCeiling(ImportSession session, StackLimits limits, int maxAtomicRows)
        {
            // Every row at every depth against the atomic ceiling, the parent rows against the parent
            // ceiling, then every row against the one save's.
            long total = session.Sheets.Sum(static sheet => (long)sheet.Rows.Count);
            long parents = session.Order.Count;
            int parentCeiling = Math.Min(limits.MaxSaveCount, limits.MaxIds);
            return total > maxAtomicRows ? (total, maxAtomicRows)
                : parents > parentCeiling ? (parents, parentCeiling)
                : total > limits.MaxRowsPerSave ? (total, limits.MaxRowsPerSave)
                : null;
        }

        /// <summary>The two passes, then the outcome on the Import row.</summary>
        /// <param name="item">The job's item.</param>
        /// <param name="arguments">The job's arguments.</param>
        /// <param name="context">The parse's context.</param>
        /// <returns>A task that completes once the outcome is enlisted and the item marked, or left for a requested stop.</returns>
        private async Task RunAsync(JobItem<Import> item, ImportJobArguments arguments, ExcelContext context)
        {
            Import row = item.Item;
            StartImportRequest request = arguments.Request;
            IExcelImportTarget target = ExcelJobTargets.Import(services, arguments.Resource)
                ?? throw new InvalidOperationException($"The resource {arguments.Resource} of the import {row.Id} is no longer importable.");
            EntityMetadata entity = target.ExportTarget.Entity;
            string mode = ExcelMetrics.ModeOf(request.Mode);
            long started = Stopwatch.GetTimestamp();

            ImportSession? session = null;
            string? fileName = null;
            int lookups = 0;
            int chunks = 0;
            bool counted = false;
            try
            {
                // --- Pass 1: the whole file parsed, with no database, then what /start could not check ---
                item.Progress.Report(percent: null, new JobMessage(ExcelJobs.ImportParsing), state: null);
                (FileStream workbook, fileName) = await imports.OpenCommittedAsync(row.FileId ?? throw new InvalidOperationException($"The import {row.Id} owns no file."), item.CancellationToken).ConfigureAwait(false);
                await using (workbook.ConfigureAwait(false))
                {
                    session = await target.ParseAsync(importer, workbook, request, context, item.CancellationToken).ConfigureAwait(false);
                }

                if (request.Atomic && AtomicCeiling(session, target.Limits, _options.MaxAtomicImportRows) is (long rows, long maxRows))
                {
                    throw ImportFaults.TooLargeForAtomic(rows, maxRows);
                }

                ImportCheckpointState? checkpoint = item.State<ImportCheckpointState>();
                if (checkpoint is not null && !string.Equals(checkpoint.OrderHash, session.OrderHash, StringComparison.Ordinal))
                {
                    throw ImportFaults.ResumeMismatch(checkpoint.OrderHash, session.OrderHash);
                }

                if (checkpoint is { NextRow: > 0 })
                {
                    metrics.ImportResumed(entity.Name);
                    ExcelLog.ImportResumed(_logger, row.Id, checkpoint.NextRow);
                }

                // --- Pass 2: the chunks from the checkpoint on, each its own transaction ---
                int next = checkpoint?.NextRow ?? 0;
                int last = session.Order.Count - 1;
                int parentCeiling = Math.Min(target.Limits.MaxSaveCount, target.Limits.MaxIds);
                chunks = CommittedChunks(session, request.Atomic, target.Limits, next);
                counted = true;
                while (next <= last)
                {
                    // A requested stop takes effect between chunks, so the chunk in flight commits.
                    if (item.CancellationReason == JobCancellationReason.CancelRequested)
                    {
                        await StopAsync(item, session, entity.Name, chunks).ConfigureAwait(false);
                        return;
                    }

                    ThrowIfInterrupted(item);
                    int from = next;
                    int to = request.Atomic ? last : session.ChunkEnd(from, _options.ImportChunkRows, parentCeiling, target.Limits.MaxRowsPerSave);
                    item.Progress.Report(ExcelJobs.Percent(from, session.Order.Count), new JobMessage(ExcelJobs.ImportResolving), state: null);

                    // The chunk's round trips ride the job scope's token, which a requested stop leaves running.
                    ImportSession chunked = session;
                    (_, int read) = await imports.SaveChunkAsync(
                        target.Service,
                        entity,
                        session,
                        from,
                        to,
                        item.State<ImportCheckpointState>()?.CommittedNewIds ?? new Dictionary<long, long>(),
                        () => item.Progress.Report(percent: null, new JobMessage(ExcelJobs.ImportValidating), state: null),
                        (persist, chunk) => Checkpoint(item, chunked, from, to, persist, chunk),
                        row.Id,
                        CancellationToken.None).ConfigureAwait(false);

                    lookups += read;
                    chunks++;
                    next = to + 1;
                    ExcelLog.ImportChunkCommitted(_logger, row.Id, next, to - from + 1);
                }

                // --- The outcome: the committed checkpoint's counts and runs across every attempt ---
                ImportCheckpointState? final = item.State<ImportCheckpointState>();
                ImportOutcome outcome = new(
                    final?.Inserted ?? 0,
                    final?.Updated ?? 0,
                    final?.ChildrenInserted ?? 0,
                    final?.ChildrenUpdated ?? 0,
                    final?.ChildrenDeleted ?? 0,
                    final?.CommittedRowRanges ?? [],
                    session.Warnings);
                await RecordAsync(row, Json(outcome), session.Order.Count, errorCount: 0).ConfigureAwait(false);
                if (item.Job.RequestedById is int requester)
                {
                    item.NotifyOnSuccess(new NotificationRequest(
                        ExcelJobs.ImportCompletedType,
                        [requester],
                        new ImportCompletedArguments(fileName, session.Order.Count, ErrorCount: 0),
                        CoreResources.Import,
                        row.Id,
                        ActorUserId: null,
                        DedupKey: null));
                }

                item.Succeed();
                TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
                metrics.Imported(entity.Name, request.Mode, background: true, session.Order.Count, elapsed.TotalSeconds, lookups);
                metrics.ImportChunks(entity.Name, chunks);
                ExcelLog.ImportCompleted(_logger, entity.Name, mode, outcome.Inserted, outcome.Updated, (long)elapsed.TotalMilliseconds);
            }
            catch (Exception failure) when (item.CancellationReason == JobCancellationReason.CancelRequested && Refusal(failure) is null)
            {
                // A requested stop that cancelled the file's copy or its parse, or that landed while
                // a chunk ran which then asked for confirmation or threw: the chunk rolled back, so the
                // stop records the last committed checkpoint instead of retrying or waiting.
                await StopAsync(item, session, entity.Name, counted ? chunks : null).ConfigureAwait(false);
            }
            catch (Exception failure) when (Refusal(failure) is ImportException refused)
            {
                // --- The errors, with the runs earlier chunks committed; the worker's notice reports the failure ---
                ImportCheckpointState? committed = item.State<ImportCheckpointState>();
                ImportFailure result = new([.. refused.Errors.Take(_options.MaxReportedErrors)], refused.TotalErrors, committed?.CommittedRowRanges ?? []);
                await RecordAsync(row, Json(result), session?.Order.Count ?? row.RowCount, refused.TotalErrors).ConfigureAwait(false);
                item.Fail(new JobError(ExcelErrorCodes.ImportFailed));
                metrics.ImportRefused(entity.Name, refused.Errors);
                RecordChunks(entity.Name, counted ? chunks : null, committed);
                ExcelLog.ImportFailed(_logger, entity.Name, mode, refused.TotalErrors, refused.Errors[0].Code);
            }
        }

        /// <summary>
        ///     The import errors a failure of the run stands for: its own, or one error placed nowhere
        ///     in the workbook for a refusal of the request or of the caller's access, which names no
        ///     row; null for any other failure, which the worker records or retries.
        /// </summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The errors, or null.</returns>
        private static ImportException? Refusal(Exception failure)
        {
            return failure switch
            {
                ImportException refused => refused,
                ForbiddenException or BadRequestException => new ImportException(
                    [new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, ((TellmaException)failure).Code, ((TellmaException)failure).Arguments)],
                    totalErrors: 1,
                    committedRowRanges: []),
                _ => null,
            };
        }

        /// <summary>
        ///     Appends a chunk's checkpoint to its persist batch: the last committed checkpoint plus
        ///     the chunk, its new rows' ids kept only while a later row still names them.
        /// </summary>
        /// <param name="item">The job's item.</param>
        /// <param name="session">The parsed import.</param>
        /// <param name="from">The chunk's first position.</param>
        /// <param name="to">The chunk's last position.</param>
        /// <param name="batch">The chunk's persist batch.</param>
        /// <param name="chunk">The chunk as resolved for this save, whose entities carry the ids the save assigned.</param>
        private static void Checkpoint(JobItem<Import> item, ImportSession session, int from, int to, IDataBatch batch, ImportChunk chunk)
        {
            // The state becomes current only once the chunk commits, so a re-save or a rolled-back
            // chunk builds from the same last committed checkpoint and never counts a row twice.
            ImportCheckpointState? previous = item.State<ImportCheckpointState>();
            IReadOnlySet<long> later = session.ReferencedFrom(to + 1);
            Dictionary<long, long> ids = [];
            foreach ((long sheetId, long id) in previous?.CommittedNewIds ?? new Dictionary<long, long>())
            {
                if (later.Contains(sheetId))
                {
                    ids[sheetId] = id;
                }
            }

            foreach ((long sheetId, long id) in session.LaterReferencedIds(from, to, chunk.Entities))
            {
                ids[sheetId] = id;
            }

            ImportChunkFacts facts = chunk.Facts;
            ImportCheckpointState state = new(
                to + 1,
                session.OrderHash,
                (previous?.Inserted ?? 0) + facts.Inserted,
                (previous?.Updated ?? 0) + facts.Updated,
                (previous?.ChildrenInserted ?? 0) + facts.ChildrenInserted,
                (previous?.ChildrenUpdated ?? 0) + facts.ChildrenUpdated,
                (previous?.ChildrenDeleted ?? 0) + facts.ChildrenDeleted,
                session.CommittedRanges(previous?.CommittedRowRanges ?? [], from, to),
                ids);
            item.Progress.Append(batch, ExcelJobs.Percent(to + 1, session.Order.Count), new JobMessage(ExcelJobs.ImportSaving), state);
        }

        /// <summary>
        ///     The chunks earlier attempts committed up to a checkpoint's next row: one for an atomic
        ///     import, else the chunk boundaries replayed from the first row.
        /// </summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="atomic">Whether the import saves as one chunk.</param>
        /// <param name="limits">The stack's limits.</param>
        /// <param name="nextRow">The checkpoint's next row; zero when none committed.</param>
        /// <returns>The chunks.</returns>
        private int CommittedChunks(ImportSession session, bool atomic, StackLimits limits, int nextRow)
        {
            // The checkpoint's fixed shape keeps no chunk count, so the boundaries are replayed under
            // the current chunk size, the same pure function of the order each attempt used; a size
            // changed between attempts makes this the count the current size would have committed.
            int parentCeiling = Math.Min(limits.MaxSaveCount, limits.MaxIds);
            int count = 0;
            for (int from = 0; from < nextRow; count++)
            {
                from = (atomic ? session.Order.Count - 1 : session.ChunkEnd(from, _options.ImportChunkRows, parentCeiling, limits.MaxRowsPerSave)) + 1;
            }

            return count;
        }

        /// <summary>
        ///     Records the chunks an ended or stopped import committed, when they are known: counted
        ///     once the file was parsed and checked, or none because no checkpoint committed.
        /// </summary>
        /// <param name="entity">The imported entity's name, which tags the count; null when the resource is no longer importable.</param>
        /// <param name="chunks">The chunks committed across the attempts; null when they were not counted.</param>
        /// <param name="committed">The last committed checkpoint; null when none.</param>
        private void RecordChunks(string? entity, int? chunks, ImportCheckpointState? committed)
        {
            // Counting earlier attempts' chunks needs the parse; an import that ended before it is
            // recorded only when no chunk committed, so the histogram never holds a guessed zero.
            if (entity is not null && (chunks is not null || committed is not { NextRow: > 0 }))
            {
                metrics.ImportChunks(entity, chunks ?? 0);
            }
        }

        /// <summary>
        ///     Records a requested stop on the Import row: the last committed checkpoint's counts and
        ///     runs, marked stopped. The item is left unmarked, so the worker completes it cancelled.
        /// </summary>
        /// <param name="item">The job's item.</param>
        /// <param name="session">The parsed import; null when the stop came before the parse.</param>
        /// <param name="entity">The imported entity's name, which tags the chunk count; null when the resource is no longer importable.</param>
        /// <param name="chunks">The chunks committed across the attempts; null when they were not counted.</param>
        /// <returns>A task that completes once the update is enlisted.</returns>
        private async Task StopAsync(JobItem<Import> item, ImportSession? session, string? entity, int? chunks)
        {
            ImportCheckpointState? committed = item.State<ImportCheckpointState>();
            RecordChunks(entity, chunks, committed);

            ImportOutcome stopped = new(
                committed?.Inserted ?? 0,
                committed?.Updated ?? 0,
                committed?.ChildrenInserted ?? 0,
                committed?.ChildrenUpdated ?? 0,
                committed?.ChildrenDeleted ?? 0,
                committed?.CommittedRowRanges ?? [],
                session?.Warnings ?? [],
                Stopped: true);
            // A stop before the parse keeps the row count an earlier attempt recorded.
            await RecordAsync(item.Item, Json(stopped), session?.Order.Count ?? item.Item.RowCount, errorCount: 0).ConfigureAwait(false);
        }

        /// <summary>
        ///     Enlists the Import row's outcome with the job frame, checked against the stamp the claim
        ///     delivered, so it commits with the job's completion.
        /// </summary>
        /// <param name="row">The Import row as the claim delivered it.</param>
        /// <param name="resultJson">The outcome or the errors.</param>
        /// <param name="rowCount">The parent rows the file holds; null when it was not parsed.</param>
        /// <param name="errorCount">The errors found.</param>
        /// <returns>A task that completes once the update is on the completion batch.</returns>
        private async Task RecordAsync(Import row, string resultJson, int? rowCount, int errorCount)
        {
            row.ResultJson = resultJson;
            row.RowCount = rowCount;
            row.ErrorCount = errorCount;

            // The job scope's token, which a requested stop leaves running, carries the write.
            _ = await this.EnlistSaveAsync(host, [row], new EnlistSaveOptions { Concurrency = ConcurrencyMode.Check }).ConfigureAwait(false);
            _ = await host.PersistAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>A result as the Import row stores it.</summary>
        /// <typeparam name="TResult">The result's type.</typeparam>
        /// <param name="result">The result.</param>
        /// <returns>The JSON.</returns>
        private static string Json<TResult>(TResult result)
        {
            return JsonSerializer.Serialize(result, TellmaJson.Options);
        }
    }

    /// <summary>The errors of a failed background import, as its Import row stores them.</summary>
    /// <param name="Errors">The errors, capped at the reported maximum.</param>
    /// <param name="TotalErrors">How many errors were found.</param>
    /// <param name="CommittedRowRanges">The Excel rows earlier chunks committed, as maximal runs; empty when none did.</param>
    internal sealed record ImportFailure(IReadOnlyList<ImportError> Errors, int TotalErrors, IReadOnlyList<RowRange> CommittedRowRanges);
}
