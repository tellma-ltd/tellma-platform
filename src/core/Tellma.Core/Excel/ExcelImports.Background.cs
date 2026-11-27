// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.4: a background import reads the file its Import row owns, committed when the row
    // was inserted, and saves its rows in chunks, each its own transaction with the stamp retries of
    // a synchronous import and the job's checkpoint on its persist batch.
    /// <content>A background import's intake of its row's file and its chunk saves.</content>
    internal sealed partial class ExcelImports
    {
        /// <summary>
        ///     Resolves the committed file an import row owns and copies its bytes to a scratch file,
        ///     refusing a file above the package limit before or while it is copied.
        /// </summary>
        /// <param name="fileId">The file's id.</param>
        /// <param name="cancellationToken">Cancels the read and the copy.</param>
        /// <returns>The scratch file, positioned at its start and gone when it is disposed, with the file name it was uploaded under.</returns>
        /// <exception cref="NotFoundException">The file is not a committed import file the caller may read, or its bytes are gone.</exception>
        /// <exception cref="ImportException">The file is above the package limit.</exception>
        public async Task<(FileStream Workbook, string? FileName)> OpenCommittedAsync(int fileId, CancellationToken cancellationToken)
        {
            (FileStream copy, BlobDownload download) = await OpenAsync(fileId, BlobState.Committed, cancellationToken).ConfigureAwait(false);
            return (copy, download.FileName);
        }

        /// <summary>
        ///     Saves one chunk of a background import in its own transaction: its lookups in one round
        ///     trip, the stored rows it overlays in another, then its resolution and save, reading
        ///     again and saving again the rows that changed after they were read.
        /// </summary>
        /// <param name="service">The stack's service.</param>
        /// <param name="entity">The stack's entity.</param>
        /// <param name="session">The parsed import.</param>
        /// <param name="fromRow">The chunk's first position.</param>
        /// <param name="toRow">The chunk's last position.</param>
        /// <param name="committedNewIds">The ids earlier chunks' new rows were given, by their negative sheet identity.</param>
        /// <param name="onValidating">Called once the chunk resolved and before each save of it.</param>
        /// <param name="onPersist">
        ///     The chunk's statements on its persist batch, given the chunk as resolved for that save:
        ///     the job's checkpoint, which commits or rolls back with the chunk.
        /// </param>
        /// <param name="importId">The import row's id, which the logs name.</param>
        /// <param name="cancellationToken">Cancels the reads and the save.</param>
        /// <returns>What the save committed, and the lookup queries run.</returns>
        /// <exception cref="ImportException">Resolution or the save found an error; nothing of the chunk was committed.</exception>
        /// <exception cref="ConfirmationRequiredException">
        ///     The chunk raised warnings its job carries no consent for: one request per warning and
        ///     row, its arguments naming the row's sheet and Excel row.
        /// </exception>
        /// <exception cref="ForbiddenException">The caller may not save a row the chunk writes.</exception>
        /// <exception cref="OperationCanceledException">The job lost its lease while the chunk was saved.</exception>
        public Task<(ImportChunkFacts Facts, int Lookups)> SaveChunkAsync(
            IStackExcelRows service,
            EntityMetadata entity,
            ImportSession session,
            int fromRow,
            int toRow,
            IReadOnlyDictionary<long, long> committedNewIds,
            Action onValidating,
            Action<IDataBatch, ImportChunk> onPersist,
            int importId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(committedNewIds);
            ArgumentNullException.ThrowIfNull(onValidating);
            ArgumentNullException.ThrowIfNull(onPersist);
            return SaveAsync(new ChunkSave(service, entity, session, fromRow, toRow, committedNewIds, onValidating, onPersist, importId), cancellationToken);
        }
    }
}
