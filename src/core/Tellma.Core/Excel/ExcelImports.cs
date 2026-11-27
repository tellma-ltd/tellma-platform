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
using Tellma.Core.Abstractions.Jobs;
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
    ///     it, the synchronous import's one save and a background import's chunk saves, each with its
    ///     lookups, hydration and stamp retries.
    /// </summary>
    internal sealed partial class ExcelImports
    {
        /// <summary>The resource a missing upload is reported under.</summary>
        public const string BlobResource = "core.Blob";

        /// <summary>The argument that marks an unconfirmed warning, which the import can run again with confirmed.</summary>
        public const string ConfirmableArgument = "confirmable";

        /// <summary>The argument of a background chunk's warning that names the sheet of the row it is about.</summary>
        public const string SheetArgument = "sheet";

        /// <summary>The argument of a background chunk's warning that names the Excel row it is about.</summary>
        public const string RowArgument = "row";

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
            (FileStream copy, _) = await OpenAsync(fileId, BlobState.Staged, cancellationToken).ConfigureAwait(false);
            return copy;
        }

        /// <summary>
        ///     Resolves an import file in a state and copies its bytes to a scratch file, refusing a
        ///     file above the package limit before or while it is copied.
        /// </summary>
        /// <param name="fileId">The file's id.</param>
        /// <param name="state">
        ///     The state the file must be in: staged for the caller's own upload a synchronous
        ///     operation reads, committed for the file an import row owns.
        /// </param>
        /// <param name="cancellationToken">Cancels the read and the copy.</param>
        /// <returns>The scratch file, positioned at its start and gone when it is disposed, with the file as resolved.</returns>
        /// <exception cref="NotFoundException">The file is not one the caller may read in that state, or its bytes are gone.</exception>
        /// <exception cref="ImportException">The file is above the package limit.</exception>
        private async Task<(FileStream Copy, BlobDownload Download)> OpenAsync(int fileId, BlobState state, CancellationToken cancellationToken)
        {
            // --- A file the caller may read in the state, else nothing the caller may tell apart ---
            BlobDownload? download = await _blobs.ResolveAsync(Import.FileKind, fileId, variant: null, cancellationToken).ConfigureAwait(false);
            if (download is null || download.State != state)
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
                    return (copy, download);
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

            // --- The lookups and the stored rows, then the save, the whole file in one transaction ---
            int last = session.Order.Count - 1;
            ChunkSave save = new(service, entity, session, 0, last, CommittedNewIds: new Dictionary<long, long>(), OnValidating: null, OnPersist: null, ImportId: null);
            (ImportChunkFacts facts, int lookups) = await SaveAsync(save, cancellationToken).ConfigureAwait(false);
            RowRange committed = new(session.SheetRows.Min(), session.SheetRows.Max());
            ImportOutcome outcome = new(facts.Inserted, facts.Updated, facts.ChildrenInserted, facts.ChildrenUpdated, facts.ChildrenDeleted, [committed], session.Warnings);
            return (outcome, session.Order.Count, lookups);
        }

        /// <summary>
        ///     Reads a chunk's lookups in one round trip and the stored rows it overlays in another,
        ///     then resolves and saves it.
        /// </summary>
        /// <param name="save">The chunk and how it is saved.</param>
        /// <param name="cancellationToken">Cancels the reads and the save.</param>
        /// <returns>What the save committed, and the lookup queries run.</returns>
        /// <exception cref="ImportException">Resolution or the save found an error.</exception>
        /// <exception cref="ConfirmationRequiredException">A background chunk raised warnings its job carries no consent for.</exception>
        private async Task<(ImportChunkFacts Facts, int Lookups)> SaveAsync(ChunkSave save, CancellationToken cancellationToken)
        {
            // --- The lookups in one round trip ---
            (IStackExcelRows service, _, ImportSession session, int fromRow, int toRow, _, _, _, _) = save;
            IReadOnlyList<ExcelQuery> queries = session.LookupsFor(fromRow, toRow);
            Dictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows = [];
            if (queries.Count > 0)
            {
                IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>> read = await service.ExcelRowSource(new ExportSource.All()).ReadAsync(queries, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < queries.Count; index++)
                {
                    lookupRows[queries[index]] = read[index];
                }
            }

            // --- The stored rows the chunk overlays in another, then the save ---
            IReadOnlyList<long> ids = session.HydrationIds(fromRow, toRow, lookupRows);
            IReadOnlyList<object> hydrated = ids.Count == 0 ? [] : await service.HydrateAsync(ids, cancellationToken).ConfigureAwait(false);
            ImportChunkFacts facts = await SaveAsync(save, lookupRows, hydrated, cancellationToken).ConfigureAwait(false);
            return (facts, queries.Count);
        }

        // Spec 0019 §11.2-§11.3: a stamp conflict on rows whose expected stamps came from hydration is
        // retried with those rows read again; any other failure of the save is mapped to the cells
        // that produced it, and the chunk fails whole. A synchronous import is one chunk of every row.
        // Spec 0019 §12.4-§12.5: a background chunk's save carries its checkpoint, appended by the
        // persist; its unconfirmed warnings leave the job waiting for its initiator, each at its row,
        // and a lost lease, which the checkpoint's fence raises inside the save, rolls it back.
        /// <summary>
        ///     Resolves and saves the rows of a chunk in one transaction, reading again and saving
        ///     again the rows that changed after they were read, within the retries allowed.
        /// </summary>
        /// <param name="save">The chunk and how it is saved.</param>
        /// <param name="lookupRows">Each of the chunk's lookups with the rows it read.</param>
        /// <param name="hydrated">The stored rows the chunk overlays.</param>
        /// <param name="cancellationToken">Cancels the save.</param>
        /// <returns>What the save committed.</returns>
        /// <exception cref="ImportException">Resolution or the save found an error.</exception>
        /// <exception cref="ConfirmationRequiredException">A background chunk raised warnings its job carries no consent for.</exception>
        /// <exception cref="OperationCanceledException">A background chunk's job lost its lease while the chunk was saved.</exception>
        private async Task<ImportChunkFacts> SaveAsync(
            ChunkSave save,
            IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
            IReadOnlyList<object> hydrated,
            CancellationToken cancellationToken)
        {
            (IStackExcelRows service, EntityMetadata entity, ImportSession session, int fromRow, int toRow, _, _, _, int? importId) = save;
            for (int attempt = 0; ; attempt++)
            {
                // --- Resolution: a pure function of the rows read, run again on each retry ---
                ImportChunk chunk = session.Resolve(fromRow, toRow, lookupRows, hydrated, save.CommittedNewIds);
                if (chunk.Errors.Count > 0)
                {
                    throw session.Refusal(chunk.Errors, _options.MaxReportedErrors);
                }

                // A re-save carries the same delegate over the chunk it resolved this time.
                save.OnValidating?.Invoke();
                Action<IDataBatch>? onPersist = save.OnPersist is Action<IDataBatch, ImportChunk> persist ? batch => persist(batch, chunk) : null;
                SaveOptions options = new()
                {
                    ReturnEntities = false,
                    Details = DetailsRequest.None,
                    Concurrency = ConcurrencyMode.Check,
                    Source = SaveSource.Import,
                    OnPersist = onPersist,
                };

                try
                {
                    await service.SaveImportAsync(chunk.Entities, options, cancellationToken).ConfigureAwait(false);
                    return chunk.Facts;
                }
                catch (ConcurrencyException conflict) when (attempt < _options.ImportConcurrencyRetries && Retryable(conflict, RowsById(entity, chunk)))
                {
                    // --- The rows changed since hydration: read them again and resolve over the fresh rows ---
                    _metrics.ImportRetried(entity.Name);
                    ExcelLog.ImportChunkRetried(_logger, importId, session.SheetRows[fromRow], attempt + 1, conflict.Conflicts.Count);
                    hydrated = await RehydrateAsync(service, entity, hydrated, [.. conflict.Conflicts.Select(static row => row.Id)], cancellationToken).ConfigureAwait(false);
                }
                catch (ValidationException invalid) when (save.Background && invalid.Errors.Any(static error => error.Code == JobErrorCodes.LeaseLost))
                {
                    // The checkpoint's lease fence, which the save reports as an invariant: the chunk
                    // rolled back with its checkpoint, and the next attempt resumes before it.
                    throw new OperationCanceledException("The job's lease was lost while a chunk of the import was saved; the chunk rolled back with its checkpoint.", invalid);
                }
                catch (ValidationException invalid)
                {
                    throw session.Refusal([.. invalid.Errors.Select(error => session.Translate(error, chunk.CoordinateMap))], _options.MaxReportedErrors);
                }
                catch (ConfirmationRequiredException unconfirmed) when (save.Background)
                {
                    throw new ConfirmationRequiredException(Pending(session, unconfirmed, RowsById(entity, chunk)));
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

            // The chunk's entities and its rows' facts are built side by side, so each row's entity
            // is at its index in the chunk; its position is the session's, which a later chunk
            // starts past zero.
            for (int index = 0; index < chunk.Facts.Rows.Count; index++)
            {
                if (entity.Key.Getter(chunk.Entities[index]) is object id)
                {
                    _ = rows.TryAdd(Convert.ToInt64(id, CultureInfo.InvariantCulture), chunk.Facts.Rows[index]);
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
        ///     The warnings a background chunk waits on: one request per warning and row it names,
        ///     its arguments adding the row's sheet and Excel row; a warning naming no row, or a row
        ///     the chunk does not hold, as it was raised.
        /// </summary>
        /// <param name="session">The parsed import.</param>
        /// <param name="unconfirmed">The warnings.</param>
        /// <param name="rows">The chunk's parent rows by their payload ids.</param>
        /// <returns>The requests.</returns>
        private static List<ConfirmationRequest> Pending(ImportSession session, ConfirmationRequiredException unconfirmed, Dictionary<long, ImportChunkRow> rows)
        {
            List<ConfirmationRequest> requests = [];
            foreach (ConfirmationRequest request in unconfirmed.Requests)
            {
                if (request.Ids.Count == 0)
                {
                    requests.Add(request);
                    continue;
                }

                foreach (long id in request.Ids)
                {
                    if (!rows.TryGetValue(id, out ImportChunkRow? row))
                    {
                        requests.Add(new ConfirmationRequest(request.Key, [id], request.Arguments));
                        continue;
                    }

                    ImportError placed = session.AtRow(row, column: null, request.Key, request.Arguments);
                    requests.Add(new ConfirmationRequest(request.Key, [id], new Dictionary<string, object?>(request.Arguments, StringComparer.Ordinal)
                    {
                        [SheetArgument] = placed.Sheet,
                        [RowArgument] = placed.Row,
                    }));
                }
            }

            return requests;
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
                    errors.Add(new ImportError(Sheet: null, Row: null, Column: null, Header: null, Property: null, ExcelErrorCodes.ImportConcurrencyConflict, ChangedBy(changed, exported: false)));
                }
                else if (changed.IsMissing)
                {
                    errors.Add(NotFound(session, row));
                }
                else
                {
                    errors.Add(session.AtRow(row, row.SheetStamp ? row.StampColumn : null, ExcelErrorCodes.ImportConcurrencyConflict, ChangedBy(changed, row.SheetStamp)));
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

        /// <summary>The arguments of a changed row: when it changed, who changed it (by name when the save read one), and whether the file's stamp was the one expected.</summary>
        /// <param name="changed">The conflict.</param>
        /// <param name="exported">Whether the expected stamp was the file's, so the row changed after the export rather than during the import.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> ChangedBy(ConcurrencyConflict changed, bool exported)
        {
            // "exported" tells the message whether the file's stamp was the one expected, so the
            // change came after the export, or the row was read in the import and changed during it.
            return new(StringComparer.Ordinal)
            {
                ["modifiedAt"] = changed.ModifiedAt,
                ["modifiedBy"] = changed.ModifiedByName ?? (object?)changed.ModifiedById,
                ["exported"] = exported,
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

        /// <summary>A chunk to save, and how: the stack, the session's positions, the ids earlier chunks gave, and a background chunk's checkpoint.</summary>
        /// <param name="Service">The stack's service.</param>
        /// <param name="Entity">The stack's entity.</param>
        /// <param name="Session">The parsed import.</param>
        /// <param name="FromRow">The chunk's first position.</param>
        /// <param name="ToRow">The chunk's last position.</param>
        /// <param name="CommittedNewIds">The ids an earlier chunk's new rows were given, by their negative sheet identity.</param>
        /// <param name="OnValidating">Called once the chunk resolved and before each save of it; null for none.</param>
        /// <param name="OnPersist">A background chunk's statements on its persist batch, given the chunk as resolved; null for a synchronous import.</param>
        /// <param name="ImportId">A background import's id; null for a synchronous import.</param>
        private sealed record ChunkSave(
            IStackExcelRows Service,
            EntityMetadata Entity,
            ImportSession Session,
            int FromRow,
            int ToRow,
            IReadOnlyDictionary<long, long> CommittedNewIds,
            Action? OnValidating,
            Action<IDataBatch, ImportChunk>? OnPersist,
            int? ImportId)
        {
            /// <summary>Whether the chunk is a background import's.</summary>
            public bool Background => ImportId is not null;
        }
    }
}
