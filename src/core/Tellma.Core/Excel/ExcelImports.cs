// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;

namespace Tellma.Core.Excel
{
    // Spec 0019 §7.1: an import file travels as the caller's own staged upload, never in a body. The
    // upload is resolved in the operation's one round trip, which the connect prologue rides, then
    // copied to a delete-on-close scratch file, so every read of the workbook is local and no
    // connection is held across file I/O. A synchronous operation leaves the upload staged.
    /// <summary>
    ///     The import runner of a scope: the request's checks, the upload's intake, the importer over
    ///     it, and the synchronous import's one save with its lookups, hydration and stamp retries.
    /// </summary>
    internal sealed class ExcelImports
    {
        /// <summary>The resource a missing upload is reported under.</summary>
        public const string BlobResource = "core.Blob";

        /// <summary>The argument that marks an unconfirmed warning, which the import can run again with confirmed.</summary>
        public const string ConfirmableArgument = "confirmable";

        /// <summary>The bytes each read of the upload's copy moves.</summary>
        private const int CopyBufferSize = 81_920;

        /// <summary>The importer.</summary>
        private readonly IExcelImporter _importer;

        /// <summary>The request's context.</summary>
        private readonly ExcelContextFactory _contexts;

        /// <summary>Resolves the caller's upload.</summary>
        private readonly IBlobService _blobs;

        /// <summary>Opens the upload's bytes.</summary>
        private readonly IBlobStore _store;

        /// <summary>The request's tenant.</summary>
        private readonly IRequestContextAccessor _accessor;

        /// <summary>The directory the upload is copied to.</summary>
        private readonly ScratchDirectory _scratch;

        /// <summary>The largest package an import accepts, in bytes.</summary>
        private readonly long _maxFileBytes;

        /// <summary>The codec's options: the errors an import reports and the saves a stamp conflict retries.</summary>
        private readonly ExcelOptions _options;

        /// <summary>The codec's instruments.</summary>
        private readonly ExcelMetrics _metrics;

        /// <summary>The codec's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the runner.</summary>
        /// <param name="importer">The importer.</param>
        /// <param name="contexts">The request's context.</param>
        /// <param name="blobs">Resolves the caller's upload.</param>
        /// <param name="store">Opens the upload's bytes.</param>
        /// <param name="accessor">The request's tenant.</param>
        /// <param name="scratch">The directory the upload is copied to.</param>
        /// <param name="options">The codec's options.</param>
        /// <param name="kinds">The blob kinds, whose import-file policy caps an upload's size.</param>
        /// <param name="metrics">The codec's instruments.</param>
        /// <param name="loggers">The logger factory.</param>
        public ExcelImports(
            IExcelImporter importer,
            ExcelContextFactory contexts,
            IBlobService blobs,
            IBlobStore store,
            IRequestContextAccessor accessor,
            ScratchDirectory scratch,
            IOptions<ExcelOptions> options,
            IBlobKindRegistry kinds,
            ExcelMetrics metrics,
            ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(kinds);
            ArgumentNullException.ThrowIfNull(loggers);
            _options = options.Value;
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _logger = loggers.CreateLogger(ExcelLog.Category);
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            _scratch = scratch ?? throw new ArgumentNullException(nameof(scratch));
            _maxFileBytes = ExcelImporter.MaxPackageBytes(options.Value, kinds);
        }

        /// <summary>Inspects the caller's upload against a stack's entity.</summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="limits">The stack's limits.</param>
        /// <param name="request">The upload and the overrides.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="BadRequestException">The request's file id or overrides are malformed, or name a collection the entity lacks.</exception>
        /// <exception cref="NotFoundException">The file is not the caller's own staged upload of an import file.</exception>
        /// <exception cref="ImportException">The workbook is refused, or an override names what it lacks.</exception>
        public async Task<ImportPlan> InspectAsync(EntityMetadata entity, StackLimits limits, InspectImportRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(request);

            // --- The request's faults, before the upload is read ---
            ImportRequests.Check(entity, request.FileId, request.SheetMappings, request.ColumnMappings);

            // --- The upload, copied locally; the context after the round trip that read the tenant's settings ---
            await using FileStream workbook = await OpenAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            ExcelContext context = _contexts.Create();
            return await _importer.InspectAsync(workbook, entity, limits, request, context, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Imports the caller's upload into a stack in one transaction: the whole workbook read,
        ///     its references looked up and its stored rows read in one round trip each, and every
        ///     row saved together, so any error anywhere commits nothing.
        /// </summary>
        /// <typeparam name="TEntity">The stack's leaf.</typeparam>
        /// <param name="service">The stack's service, whose row source reads the lookups and which hydrates and saves the rows.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="limits">The stack's limits.</param>
        /// <param name="request">The upload, the mode, the row key and the overrides.</param>
        /// <param name="cancellationToken">Cancels the read and the save.</param>
        /// <returns>What the import committed.</returns>
        /// <exception cref="BadRequestException">The request is malformed, or names a row key or collection the entity or file lacks.</exception>
        /// <exception cref="NotFoundException">The file is not the caller's own staged upload of an import file.</exception>
        /// <exception cref="ForbiddenException">The caller may not save a row the import writes.</exception>
        /// <exception cref="ImportException">
        ///     The workbook is refused, is above the synchronous limits, or a row fails its mapping,
        ///     resolution, validation or save; nothing was committed.
        /// </exception>
        public async Task<ImportOutcome> ImportAsync<TEntity>(
            IStackExcelRows service,
            EntityMetadata entity,
            StackLimits limits,
            ImportRequest request,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(limits);
            ArgumentNullException.ThrowIfNull(request);

            // --- The request's faults, before the upload is read ---
            ImportRequests.Check(entity, request);
            string mode = ExcelMetrics.ModeOf(request.Mode);
            long started = Stopwatch.GetTimestamp();
            try
            {
                (ImportOutcome outcome, int rows, int lookups) = await RunAsync<TEntity>(service, entity, limits, request, cancellationToken).ConfigureAwait(false);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
                _metrics.Imported(entity.Name, request.Mode, background: false, rows, elapsed.TotalSeconds, lookups);
                ExcelLog.ImportCompleted(_logger, entity.Name, mode, outcome.Inserted, outcome.Updated, (long)elapsed.TotalMilliseconds);
                return outcome;
            }
            catch (ImportException refused)
            {
                // --- A refusal of any stage: counted by the codes reported, logged by the first ---
                _metrics.ImportRefused(entity.Name, refused.Errors);
                ExcelLog.ImportFailed(_logger, entity.Name, mode, refused.TotalErrors, refused.Errors[0].Code);
                throw;
            }
        }

        /// <summary>
        ///     Resolves the caller's staged upload and copies its bytes to a scratch file, refusing an
        ///     upload above the package limit before or while it is copied.
        /// </summary>
        /// <param name="fileId">The upload's id.</param>
        /// <param name="cancellationToken">Cancels the read and the copy.</param>
        /// <returns>The scratch file, positioned at its start; gone when it is disposed.</returns>
        /// <exception cref="NotFoundException">The file is not the caller's own staged upload, or its bytes are gone.</exception>
        /// <exception cref="ImportException">The upload is above the package limit.</exception>
        public async Task<FileStream> OpenAsync(int fileId, CancellationToken cancellationToken)
        {
            // --- The caller's own staged upload, else nothing the caller may tell apart ---
            BlobDownload? download = await _blobs.ResolveAsync(Import.FileKind, fileId, variant: null, cancellationToken).ConfigureAwait(false);
            if (download is not { State: BlobState.Staged })
            {
                throw Missing(fileId);
            }

            if (download.Size > _maxFileBytes)
            {
                throw ImportFaults.TooLarge(download.Size, _maxFileBytes);
            }

            int tenantId = _accessor.Current.RequireTenant().Id;
            BlobContent content = await _store.OpenReadAsync(tenantId, download.StorageName, cancellationToken).ConfigureAwait(false)
                ?? throw Missing(fileId);

            // --- The copy, bounded by the limit whatever length the store reports ---
            await using (content.ConfigureAwait(false))
            {
                FileStream copy = ScratchFiles.Create(_scratch.Path);
                try
                {
                    await CopyAsync(content.Content, copy, cancellationToken).ConfigureAwait(false);
                    copy.Position = 0;
                    return copy;
                }
                catch
                {
                    await copy.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }

        /// <summary>The synchronous import's stages: the parse, the lookups, the hydration, then the save.</summary>
        /// <typeparam name="TEntity">The stack's leaf.</typeparam>
        /// <param name="service">The stack's service.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="limits">The stack's limits.</param>
        /// <param name="request">The request, already checked.</param>
        /// <param name="cancellationToken">Cancels the read and the save.</param>
        /// <returns>The outcome, the parent rows read and the lookup queries run.</returns>
        private async Task<(ImportOutcome Outcome, int Rows, int Lookups)> RunAsync<TEntity>(
            IStackExcelRows service,
            EntityMetadata entity,
            StackLimits limits,
            ImportRequest request,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            // --- The upload, copied locally and read whole; the context after the round trip that read the tenant's settings ---
            ImportSession session;
            FileStream workbook = await OpenAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            await using (workbook.ConfigureAwait(false))
            {
                ExcelContext context = _contexts.Create();
                session = (await _importer.ParseAsync<TEntity>(workbook, entity, limits, request, context, synchronous: true, cancellationToken).ConfigureAwait(false)).Core;
            }

            if (session.Order.Count == 0)
            {
                return (new ImportOutcome(0, 0, 0, 0, 0, [], session.Warnings), 0, 0);
            }

            // --- The lookups in one round trip, then the stored rows the import overlays in another ---
            int last = session.Order.Count - 1;
            IReadOnlyList<ExcelQuery> queries = session.LookupsFor(0, last);
            Dictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows = [];
            if (queries.Count > 0)
            {
                IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>> read = await service.ExcelRowSource(new ExportSource.All()).ReadAsync(queries, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < queries.Count; index++)
                {
                    lookupRows[queries[index]] = read[index];
                }
            }

            IReadOnlyList<long> ids = session.HydrationIds(0, last, lookupRows);
            IReadOnlyList<object> hydrated = ids.Count == 0 ? [] : await service.HydrateAsync(ids, cancellationToken).ConfigureAwait(false);

            // --- The save, the whole file in one transaction ---
            ImportChunkFacts facts = await SaveAsync(service, entity, session, lookupRows, hydrated, cancellationToken).ConfigureAwait(false);
            RowRange committed = new(session.SheetRows.Min(), session.SheetRows.Max());
            ImportOutcome outcome = new(facts.Inserted, facts.Updated, facts.ChildrenInserted, facts.ChildrenUpdated, facts.ChildrenDeleted, [committed], session.Warnings);
            return (outcome, session.Order.Count, queries.Count);
        }

        // Spec 0019 §11.2-§11.3: a stamp conflict on rows whose expected stamps came from hydration is
        // retried with those rows read again; any other failure of the save is mapped to the cells
        // that produced it, and the import fails whole.
        /// <summary>
        ///     Resolves and saves every row of a session in one transaction, reading again and saving
        ///     again the rows that changed after they were read, within the retries allowed.
        /// </summary>
        /// <param name="service">The stack's service.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="session">The parsed import.</param>
        /// <param name="lookupRows">Each lookup with the rows it read.</param>
        /// <param name="hydrated">The stored rows the import overlays.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>What the save committed.</returns>
        /// <exception cref="ImportException">Resolution or the save found an error.</exception>
        private async Task<ImportChunkFacts> SaveAsync(
            IStackExcelRows service,
            EntityMetadata entity,
            ImportSession session,
            IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
            IReadOnlyList<object> hydrated,
            CancellationToken cancellationToken)
        {
            SaveOptions options = new()
            {
                ReturnEntities = false,
                Details = DetailsRequest.None,
                Concurrency = ConcurrencyMode.Check,
                Source = SaveSource.Import,
            };

            int last = session.Order.Count - 1;
            for (int attempt = 0; ; attempt++)
            {
                // --- Resolution: a pure function of the rows read, run again on each retry ---
                ImportChunk chunk = session.Resolve(0, last, lookupRows, hydrated, new Dictionary<long, long>());
                if (chunk.Errors.Count > 0)
                {
                    throw session.Refusal(chunk.Errors, _options.MaxReportedErrors);
                }

                try
                {
                    await service.SaveImportAsync(chunk.Entities, options, cancellationToken).ConfigureAwait(false);
                    return chunk.Facts;
                }
                catch (ConcurrencyException conflict) when (attempt < _options.ImportConcurrencyRetries && Retryable(conflict, RowsById(entity, chunk)))
                {
                    // --- The rows changed since hydration: read them again and resolve over the fresh rows ---
                    _metrics.ImportRetried(entity.Name);
                    ExcelLog.ImportChunkRetried(_logger, importId: null, session.SheetRows[0], attempt + 1, conflict.Conflicts.Count);
                    hydrated = await RehydrateAsync(service, entity, hydrated, [.. conflict.Conflicts.Select(static row => row.Id)], cancellationToken).ConfigureAwait(false);
                }
                catch (ValidationException invalid)
                {
                    throw session.Refusal([.. invalid.Errors.Select(error => session.Translate(error, chunk.CoordinateMap))], _options.MaxReportedErrors);
                }
                catch (ConfirmationRequiredException unconfirmed)
                {
                    throw session.Refusal(Unconfirmed(session, unconfirmed, RowsById(entity, chunk)), _options.MaxReportedErrors);
                }
                catch (ConcurrencyException conflict)
                {
                    throw session.Refusal(Conflicts(session, conflict, RowsById(entity, chunk)), _options.MaxReportedErrors);
                }
                catch (NotFoundException missing) when (string.Equals(missing.Resource, service.Descriptor.Resource, StringComparison.Ordinal))
                {
                    throw session.Refusal(Missing(session, missing, RowsById(entity, chunk)), _options.MaxReportedErrors);
                }
            }
        }

        /// <summary>
        ///     The chunk's parent rows by every id a failed save may name them by: the id each was
        ///     resolved with, a stored row's or a new row's temporary one, and the id the pipeline
        ///     assigned a new row before its validators ran.
        /// </summary>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="chunk">The chunk, after its save was attempted.</param>
        /// <returns>The rows.</returns>
        private static Dictionary<long, ImportChunkRow> RowsById(EntityMetadata entity, ImportChunk chunk)
        {
            Dictionary<long, ImportChunkRow> rows = [];
            foreach (ImportChunkRow row in chunk.Facts.Rows)
            {
                rows[row.Id] = row;
            }

            foreach (ImportChunkRow row in chunk.Facts.Rows)
            {
                if (entity.Key.Getter(chunk.Entities[row.Position]) is object id)
                {
                    _ = rows.TryAdd(Convert.ToInt64(id, CultureInfo.InvariantCulture), row);
                }
            }

            return rows;
        }

        /// <summary>Whether a stamp conflict is retried: no row is gone, and every expected stamp came from hydration rather than the sheet.</summary>
        /// <param name="conflict">The conflict.</param>
        /// <param name="rows">The chunk's parent rows by their payload ids.</param>
        /// <returns>True when it is.</returns>
        private static bool Retryable(ConcurrencyException conflict, Dictionary<long, ImportChunkRow> rows)
        {
            return conflict.Conflicts.All(row => !row.IsMissing && rows.TryGetValue(row.Id, out ImportChunkRow? sheet) && !sheet.SheetStamp);
        }

        /// <summary>The stored rows with the changed ones read again; a row now gone is left out, for resolution to report.</summary>
        /// <param name="service">The stack's service.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="hydrated">The stored rows read before.</param>
        /// <param name="changed">The ids of the rows that changed.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The stored rows.</returns>
        private static async Task<IReadOnlyList<object>> RehydrateAsync(
            IStackExcelRows service,
            EntityMetadata entity,
            IReadOnlyList<object> hydrated,
            IReadOnlyList<long> changed,
            CancellationToken cancellationToken)
        {
            HashSet<long> stale = [.. changed];
            IReadOnlyList<object> fresh = await service.HydrateAsync(changed, cancellationToken).ConfigureAwait(false);
            return [.. hydrated.Where(row => !stale.Contains(Convert.ToInt64(entity.Key.Getter(row), CultureInfo.InvariantCulture))), .. fresh];
        }

        /// <summary>The errors of unconfirmed warnings: one per warning and row it names, at that row, marked confirmable.</summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="unconfirmed">The warnings.</param>
        /// <param name="rows">The chunk's parent rows by their payload ids.</param>
        /// <returns>The errors.</returns>
        private static List<ImportError> Unconfirmed(ImportSession session, ConfirmationRequiredException unconfirmed, Dictionary<long, ImportChunkRow> rows)
        {
            List<ImportError> errors = [];
            foreach (ConfirmationRequest request in unconfirmed.Requests)
            {
                Dictionary<string, object?> arguments = new(request.Arguments, StringComparer.Ordinal) { [ConfirmableArgument] = true };
                if (request.Ids.Count == 0)
                {
                    errors.Add(new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, request.Key, arguments));
                    continue;
                }

                foreach (long id in request.Ids)
                {
                    errors.Add(rows.TryGetValue(id, out ImportChunkRow? row)
                        ? session.AtRow(row, column: null, request.Key, arguments)
                        : new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, request.Key, arguments));
                }
            }

            return errors;
        }

        /// <summary>
        ///     The errors of a stamp conflict: a changed row at its stamp column when the sheet gave
        ///     the stamp, else at the row, with who changed it and when; a row gone as not found.
        /// </summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="conflict">The conflict.</param>
        /// <param name="rows">The chunk's parent rows by their payload ids.</param>
        /// <returns>The errors.</returns>
        private static List<ImportError> Conflicts(ImportSession session, ConcurrencyException conflict, Dictionary<long, ImportChunkRow> rows)
        {
            List<ImportError> errors = [];
            foreach (ConcurrencyConflict changed in conflict.Conflicts)
            {
                if (!rows.TryGetValue(changed.Id, out ImportChunkRow? row))
                {
                    // A row the import did not name is placed nowhere in the workbook.
                    errors.Add(new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, ExcelErrorCodes.ImportConcurrencyConflict, ChangedBy(changed)));
                }
                else if (changed.IsMissing)
                {
                    errors.Add(NotFound(session, row));
                }
                else
                {
                    errors.Add(session.AtRow(row, row.SheetStamp ? row.StampColumn : null, ExcelErrorCodes.ImportConcurrencyConflict, ChangedBy(changed)));
                }
            }

            return errors;
        }

        /// <summary>The errors of rows deleted or hidden between their read and the save: each at its row key.</summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="missing">The not-found outcome.</param>
        /// <param name="rows">The chunk's parent rows by their payload ids.</param>
        /// <returns>The errors.</returns>
        private static List<ImportError> Missing(ImportSession session, NotFoundException missing, Dictionary<long, ImportChunkRow> rows)
        {
            List<ImportError> errors = [];
            foreach (string text in missing.Ids)
            {
                errors.Add(long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id) && rows.TryGetValue(id, out ImportChunkRow? row)
                    ? NotFound(session, row)
                    : new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, ExcelErrorCodes.ImportRowNotFound, new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["key"] = session.Shape.RowKey,
                        ["value"] = text,
                    }));
            }

            return errors;
        }

        /// <summary>A row the save no longer found, at its row key.</summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="row">The row.</param>
        /// <returns>The error.</returns>
        private static ImportError NotFound(ImportSession session, ImportChunkRow row)
        {
            return session.AtRow(row, row.KeyColumn, ExcelErrorCodes.ImportRowNotFound, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["key"] = session.Shape.RowKey,
                ["value"] = session.KeyValue(row),
            });
        }

        /// <summary>The arguments of a changed row: when it changed and who changed it, by name when the save read one.</summary>
        /// <param name="changed">The conflict.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> ChangedBy(ConcurrencyConflict changed)
        {
            return new(StringComparer.Ordinal)
            {
                ["modifiedAt"] = changed.ModifiedAt,
                ["modifiedBy"] = changed.ModifiedByName ?? (object?)changed.ModifiedById,
            };
        }

        /// <summary>Copies a stream, refusing more bytes than the package limit.</summary>
        /// <param name="source">The upload's bytes.</param>
        /// <param name="destination">The scratch file.</param>
        /// <param name="cancellationToken">Cancels the copy.</param>
        /// <returns>A task that completes when the copy has.</returns>
        /// <exception cref="ImportException">The upload is above the package limit.</exception>
        private async Task CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    copied += read;
                    if (copied > _maxFileBytes)
                    {
                        throw ImportFaults.TooLarge(copied, _maxFileBytes);
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>The refusal of a file that is not the caller's own staged upload.</summary>
        /// <param name="fileId">The file's id.</param>
        /// <returns>The exception.</returns>
        private static NotFoundException Missing(int fileId)
        {
            return new NotFoundException(BlobResource, [fileId.ToString(CultureInfo.InvariantCulture)]);
        }
    }
}
