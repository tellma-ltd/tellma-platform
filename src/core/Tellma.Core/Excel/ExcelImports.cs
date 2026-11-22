// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Buffers;
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
    /// <summary>The import runner of a scope: the request's checks, the upload's intake, and the importer over it.</summary>
    internal sealed class ExcelImports
    {
        /// <summary>The resource a missing upload is reported under.</summary>
        public const string BlobResource = "core.Blob";

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

        /// <summary>Creates the runner.</summary>
        /// <param name="importer">The importer.</param>
        /// <param name="contexts">The request's context.</param>
        /// <param name="blobs">Resolves the caller's upload.</param>
        /// <param name="store">Opens the upload's bytes.</param>
        /// <param name="accessor">The request's tenant.</param>
        /// <param name="scratch">The directory the upload is copied to.</param>
        /// <param name="options">The codec's options.</param>
        /// <param name="kinds">The blob kinds, whose import-file policy caps an upload's size.</param>
        public ExcelImports(
            IExcelImporter importer,
            ExcelContextFactory contexts,
            IBlobService blobs,
            IBlobStore store,
            IRequestContextAccessor accessor,
            ScratchDirectory scratch,
            IOptions<ExcelOptions> options,
            IBlobKindRegistry kinds)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(kinds);
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
