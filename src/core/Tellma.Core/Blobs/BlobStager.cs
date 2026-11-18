// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Blobs
{
    // spec 0017 §3.2: the staging steps, in order.
    /// <summary>
    ///     Stages uploads: resolves the kind, checks the length, reads the bytes,
    ///     determines the content type, normalises an image or renders a thumbnail, records the
    ///     <see cref="BlobState.Staged" /> row in one guarded round trip, then writes the objects
    ///     create-only. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Image kinds are read into memory, bounded by the kind's size limit, since the
    ///         processor decodes from memory. Other kinds stay in memory up to
    ///         <see cref="MemoryThreshold" /> and otherwise stream to a delete-on-close file in the
    ///         scratch directory, hashed on the way.
    ///     </para>
    ///     <para>
    ///         The row is written before the bytes. A store failure leaves a staged row without bytes,
    ///         which expires into the sweep (a missing object is success for its delete), and the
    ///         caller sees the store's exception, never the id.
    ///     </para>
    /// </remarks>
    /// <param name="kinds">The kind registry.</param>
    /// <param name="store">The blob store.</param>
    /// <param name="processor">The image processor, or null when the host registers none.</param>
    /// <param name="runner">Runs the stage statement behind the connect prologue.</param>
    /// <param name="context">The scope's request context: the tenant and whether the scope is the platform's own.</param>
    /// <param name="options">The blob options.</param>
    /// <param name="scratch">Where large bodies are buffered.</param>
    /// <param name="metrics">The upload instruments.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class BlobStager(
        IBlobKindRegistry kinds,
        IBlobStore store,
        IImageProcessor? processor,
        IGuardedBatchRunner runner,
        IRequestContextAccessor context,
        BlobOptions options,
        ScratchDirectory scratch,
        BlobUploadMetrics metrics,
        ILogger<BlobStager> logger)
    {
        /// <summary>The largest body of a kind that is not an image kind kept in memory: 64 KiB.</summary>
        public const int MemoryThreshold = 64 * 1024;

        // The read buffer of a body streamed to the scratch directory.
        private const int CopyBufferSize = 81920;

        /// <inheritdoc cref="IBlobService.StageAsync" />
        public async Task<BlobDescriptor> StageAsync(BlobStageRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Kind is null || request.Content is null)
            {
                throw new ArgumentException("An upload names its kind and carries its content.", nameof(request));
            }

            long started = Stopwatch.GetTimestamp();

            // --- 1. The kind, and the display-only file name ---
            if (kinds.Find(request.Kind) is not BlobKindDescriptor kind)
            {
                // Never measured: the name is the client's, so it would open the kind tag.
                BlobServiceLog.Rejected(logger, BlobName.IsValidKind(request.Kind) ? request.Kind : "-", BlobRejectedException.UnknownKind);
                throw new BlobRejectedException(BlobRejectedException.UnknownKind, new Dictionary<string, object?>());
            }

            BlobKindPolicy policy = kind.Policy;
            string? fileName = BlobFileNames.Sanitize(request.FileName);

            // --- 2. The declared length, against the kind's and the global limit ---
            long limit = Math.Min(policy.MaxSize, options.MaxUploadSize);
            if (request.Length < 0)
            {
                throw Rejected(kind.Kind, BlobRejectedException.LengthRequired, BlobTelemetryTagValues.UploadRejectedSize, []);
            }

            if (request.Length > limit)
            {
                throw TooLarge(kind.Kind, limit);
            }

            // --- 3. The bytes: in memory, or through a delete-on-close scratch file, hashed on the way ---
            await using UploadBody body = await ReadAsync(kind.Kind, request.Content, request.Length, limit, buffer: policy.Image is not null, cancellationToken).ConfigureAwait(false);

            // --- 4. The content type, from the bytes ---
            ContentTypeDecision decision = BlobContentSniffer.Determine(body.Head, request.DeclaredContentType, policy.AllowedContentTypes);
            if (!decision.IsAccepted)
            {
                throw Rejected(
                    kind.Kind,
                    BlobRejectedException.UnsupportedType,
                    BlobTelemetryTagValues.UploadRejectedType,
                    [new(BlobRejectedException.ContentTypeArgument, decision.ContentType)]);
            }

            // --- 5 and 6. The stored bytes: a normalised image, or the upload itself with an optional thumbnail ---
            StoredContent stored = policy.Image is ImagePolicy image
                ? await NormaliseAsync(kind.Kind, body, image, policy.Thumbnail, cancellationToken).ConfigureAwait(false)
                : await RenderThumbnailAsync(kind.Kind, body, decision.ContentType, policy.Thumbnail, cancellationToken).ConfigureAwait(false);

            // --- 7. The storage key: 32 random hex digits under the kind ---
            string storageKey = BlobName.Primary(kind.Kind, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            string? thumbnailName = stored.Thumbnail is null ? null : BlobName.Variant(storageKey, ThumbnailPolicy.VariantName);

            // --- 8. The row, in one round trip: the quota (unless the platform's own scope) and the insert ---
            StagedBlob staged = await InsertAsync(kind.Kind, storageKey, stored, fileName, policy, cancellationToken).ConfigureAwait(false);

            // --- 9. The objects, after the row: the primary and its thumbnail in one create-only call ---
            await WriteAsync(kind.Kind, staged.Id, storageKey, thumbnailName, stored, body, cancellationToken).ConfigureAwait(false);

            // --- 10. The descriptor ---
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            metrics.Staged(kind.Kind, request.Length, body.Length, elapsed.TotalSeconds);
            BlobServiceLog.Staged(logger, kind.Kind, staged.Id, stored.Size, stored.ContentType, (long)elapsed.TotalMilliseconds);
            return new BlobDescriptor(staged.Id, kind.Kind, stored.ContentType, stored.Size, stored.Width, stored.Height, fileName, staged.ExpiresAt);
        }

        /// <summary>
        ///     Reads exactly <paramref name="length" /> bytes of the body, refusing a body that runs past
        ///     it as too large and one that ends before it as a wrong length.
        /// </summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="content">The body.</param>
        /// <param name="length">The declared length, within the limit.</param>
        /// <param name="limit">The kind's effective limit, for a refusal's argument.</param>
        /// <param name="buffer">Whether the body is read into memory whatever its length, as an image is.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The body; the caller disposes it.</returns>
        private async Task<UploadBody> ReadAsync(
            string kind, Stream content, long length, long limit, bool buffer, CancellationToken cancellationToken)
        {
            // --- In memory: the declared length is within the kind's limit, so the array is bounded ---
            if (buffer || length <= MemoryThreshold)
            {
                byte[] bytes = new byte[length];
                int read = await content.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (read < bytes.Length)
                {
                    throw Rejected(kind, BlobRejectedException.LengthRequired, BlobTelemetryTagValues.UploadRejectedSize, []);
                }

                // One byte more is a body past its declared length.
                byte[] probe = new byte[1];
                return await content.ReadAsync(probe, cancellationToken).ConfigureAwait(false) > 0
                    ? throw TooLarge(kind, limit)
                    : UploadBody.InMemory(bytes);
            }

            // --- Streamed: to a scratch file, hashing and keeping the head on the way ---
            FileStream file = scratch.CreateTemporaryFile();
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] head = new byte[BlobContentSniffer.SniffLength];
                int headLength = 0;
                long total = 0;
                byte[] chunk = new byte[CopyBufferSize];
                while (true)
                {
                    int read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    // The limit is enforced while reading: a body past its declared length stops here.
                    total += read;
                    if (total > length)
                    {
                        throw TooLarge(kind, limit);
                    }

                    if (headLength < head.Length)
                    {
                        int copied = Math.Min(read, head.Length - headLength);
                        Array.Copy(chunk, 0, head, headLength, copied);
                        headLength += copied;
                    }

                    hash.AppendData(chunk, 0, read);
                    await file.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                if (total < length)
                {
                    throw Rejected(kind, BlobRejectedException.LengthRequired, BlobTelemetryTagValues.UploadRejectedSize, []);
                }

                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                return UploadBody.InFile(file, total, head[..headLength], hash.GetHashAndReset());
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        // spec 0017 §3.6: normalisation.
        /// <summary>
        ///     Normalises an image through the processor: what is stored is the processor's output,
        ///     never the client's bytes, and the thumbnail comes from it.
        /// </summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="body">The upload, in memory.</param>
        /// <param name="image">The kind's image policy.</param>
        /// <param name="thumbnail">The kind's thumbnail policy, or null for none.</param>
        /// <param name="cancellationToken">Cancels the processing.</param>
        /// <returns>The bytes to store.</returns>
        private async Task<StoredContent> NormaliseAsync(
            string kind, UploadBody body, ImagePolicy image, ThumbnailPolicy? thumbnail, CancellationToken cancellationToken)
        {
            // The startup gate refuses an image kind without a processor; a host that skipped the
            // gate fails here, loudly, rather than storing unnormalised bytes.
            IImageProcessor normaliser = processor ?? throw Failed(kind, new InvalidOperationException(
                $"The blob kind '{kind}' normalises images, but no image processor is registered; call AddTellmaSkia() or register one through tellma.Blobs(b => b.ImageProcessor<T>())."));

            ProcessedImage processed;
            try
            {
                processed = await normaliser.ProcessAsync(kind, body.Bytes, image, thumbnail, options.ImageProcessTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (ImageRejectedException rejected)
            {
                throw Rejected(
                    kind,
                    BlobRejectedException.ImageRejected,
                    BlobTelemetryTagValues.UploadRejectedImage,
                    [new(BlobRejectedException.ReasonArgument, rejected.Reason)]);
            }
            catch (ImageProcessingBusyException)
            {
                // Not the file's fault: the client may retry, so it is counted apart from refusals.
                metrics.Count(kind, BlobTelemetryTagValues.UploadBusy);
                throw;
            }

            // The hash, size, dimensions and type recorded are those of the bytes that are stored.
            return new StoredContent(
                processed.Primary,
                processed.ContentType,
                processed.Primary.LongLength,
                SHA256.HashData(processed.Primary),
                processed.Width,
                processed.Height,
                processed.Thumbnail,
                thumbnail);
        }

        /// <summary>
        ///     Renders the thumbnail of a file that is not an image kind, when the kind has one and a
        ///     processor is registered. A file it cannot render, or a render that throws, has none,
        ///     which never refuses or fails the upload.
        /// </summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="body">The upload.</param>
        /// <param name="contentType">The determined media type.</param>
        /// <param name="thumbnail">The kind's thumbnail policy, or null for none.</param>
        /// <param name="cancellationToken">Cancels the render.</param>
        /// <returns>The bytes to store: the upload itself, and the thumbnail when one was rendered.</returns>
        private async Task<StoredContent> RenderThumbnailAsync(
            string kind, UploadBody body, string contentType, ThumbnailPolicy? thumbnail, CancellationToken cancellationToken)
        {
            byte[]? rendered = null;
            if (thumbnail is not null && processor is not null)
            {
                // The processor never touches the stream once it returns, so the same body is
                // rewound for the store afterwards.
                Stream input = body.OpenRead();
                try
                {
                    rendered = await processor.ThumbnailAsync(kind, input, contentType, thumbnail, options.ThumbnailTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    // A thumbnail is never a reason to refuse or fail an upload (spec 0017 §3.2
                    // step 6): a processor that throws, or cancels on its own budget, leaves the
                    // file without one, and the warning names the kind and type for the operator.
                    BlobServiceLog.ThumbnailFailed(logger, exception, kind, contentType);
                    rendered = null;
                }
                finally
                {
                    await body.ReleaseAsync(input).ConfigureAwait(false);
                }
            }

            return new StoredContent(null, contentType, body.Length, body.Sha256, null, null, rendered, rendered is null ? null : thumbnail);
        }

        /// <summary>Inserts the staged row through the guarded runner, translating the quota's refusal.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="storageKey">The primary's object name.</param>
        /// <param name="stored">The bytes to store.</param>
        /// <param name="fileName">The sanitised file name.</param>
        /// <param name="policy">The kind's policy, for its staging lifetime.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The row's id and deadline.</returns>
        private async Task<StagedBlob> InsertAsync(
            string kind, string storageKey, StoredContent stored, string? fileName, BlobKindPolicy policy, CancellationToken cancellationToken)
        {
            // --- The scope: the platform's own is exempt from the quota (spec 0017 §3.4) ---
            bool system = context.Current.IsSystem;
            StagingQuota? quota = system ? null : new StagingQuota(options.MaxStagedPerUser, options.MaxStagedBytesPerUser);
            TimeSpan ttl = policy.StagingTtl ?? options.StagingTtl;
            int ttlSeconds = (int)Math.Ceiling(ttl.TotalSeconds);
            string variants = stored.Thumbnail is null ? string.Empty : ThumbnailPolicy.VariantName;

            BatchResult<StagedBlob>? result = null;
            try
            {
                return await runner.RunAsync(
                    BatchPurpose.Persist,
                    (user, batch) =>
                    {
                        // The uploader is the user the prologue confirmed: the scope's user, or the
                        // system user in the platform's own scope. A recomposed batch appends anew.
                        DataBatch data = batch as DataBatch
                            ?? throw new InvalidOperationException("The guarded runner composed a batch that no tenant database created.");
                        StagedRow row = new(
                            user.UserId, kind, storageKey, stored.ContentType, fileName, stored.Size, stored.Sha256, stored.Width, stored.Height, variants, ttlSeconds);
                        result = BlobStatements.Stage(data, row, quota);
                        return ValueTask.CompletedTask;
                    },
                    (_, _) => result!.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BatchAssertionFailedException refused)
                when (refused.Number == TellmaSqlErrors.Invariant && refused.Code == BlobRejectedException.StagingQuotaExceeded)
            {
                throw Rejected(
                    kind,
                    BlobRejectedException.StagingQuotaExceeded,
                    BlobTelemetryTagValues.UploadQuota,
                    [new(BlobRejectedException.MaxCountArgument, options.MaxStagedPerUser), new(BlobRejectedException.MaxBytesArgument, options.MaxStagedBytesPerUser)]);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not BlobRejectedException)
            {
                throw Failed(kind, exception);
            }
        }

        /// <summary>Writes the primary and any thumbnail in one create-only call.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="id">The staged row's id, for the failure's log.</param>
        /// <param name="storageKey">The primary's object name.</param>
        /// <param name="thumbnailName">The thumbnail's object name, or null for none.</param>
        /// <param name="stored">The bytes to store.</param>
        /// <param name="body">The upload, which is the primary unless the image was normalised.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the objects are written.</returns>
        private async Task WriteAsync(
            string kind, int id, string storageKey, string? thumbnailName, StoredContent stored, UploadBody body, CancellationToken cancellationToken)
        {
            Stream primary = stored.Primary is byte[] normalised ? new MemoryStream(normalised, writable: false) : body.OpenRead();
            try
            {
                List<BlobWrite> writes = [new BlobWrite(storageKey, stored.ContentType, primary, stored.Size)];
                if (stored.Thumbnail is byte[] thumbnail && thumbnailName is not null)
                {
                    writes.Add(new BlobWrite(
                        thumbnailName,
                        BlobContentTypes.ForImageFormat(stored.ThumbnailPolicy!.Format),
                        new MemoryStream(thumbnail, writable: false),
                        thumbnail.LongLength));
                }

                await store.WriteAsync(context.Current.RequireTenant().Id, writes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The row stays staged and expires into the sweep; the caller never sees its id.
                BlobServiceLog.StoreWriteFailed(logger, exception, kind, id);
                metrics.Count(kind, BlobTelemetryTagValues.UploadFailed);
                throw;
            }
            finally
            {
                await body.ReleaseAsync(primary).ConfigureAwait(false);
            }
        }

        /// <summary>Counts and logs a refusal and creates its exception.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="code">The refusal's code.</param>
        /// <param name="outcome">The upload outcome it counts as.</param>
        /// <param name="arguments">The refusal's arguments.</param>
        /// <returns>The exception, for the caller to throw.</returns>
        private BlobRejectedException Rejected(string kind, string code, string outcome, KeyValuePair<string, object?>[] arguments)
        {
            metrics.Count(kind, outcome);
            BlobServiceLog.Rejected(logger, kind, code);
            return new BlobRejectedException(code, new Dictionary<string, object?>(arguments, StringComparer.Ordinal));
        }

        /// <summary>Counts and logs a refusal for size.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="limit">The kind's effective limit.</param>
        /// <returns>The exception, for the caller to throw.</returns>
        private BlobRejectedException TooLarge(string kind, long limit)
        {
            return Rejected(kind, BlobRejectedException.TooLarge, BlobTelemetryTagValues.UploadRejectedSize, [new(BlobRejectedException.MaxBytesArgument, limit)]);
        }

        /// <summary>Counts an upload that failed for a reason not the file's, and passes the failure on.</summary>
        /// <param name="kind">The upload's kind.</param>
        /// <param name="exception">The failure.</param>
        /// <returns>The failure, for the caller to throw.</returns>
        private Exception Failed(string kind, Exception exception)
        {
            metrics.Count(kind, BlobTelemetryTagValues.UploadFailed);
            return exception;
        }

        /// <summary>The bytes that are stored, and what is recorded about them.</summary>
        /// <param name="Primary">The normalised primary; null when the upload itself is stored.</param>
        /// <param name="ContentType">The stored primary's media type.</param>
        /// <param name="Size">The stored primary's length.</param>
        /// <param name="Sha256">The stored primary's SHA-256.</param>
        /// <param name="Width">The stored image's width, or null.</param>
        /// <param name="Height">The stored image's height, or null.</param>
        /// <param name="Thumbnail">The thumbnail, or null for none.</param>
        /// <param name="ThumbnailPolicy">The thumbnail's policy, when there is a thumbnail.</param>
        private sealed record StoredContent(
            byte[]? Primary,
            string ContentType,
            long Size,
            byte[] Sha256,
            int? Width,
            int? Height,
            byte[]? Thumbnail,
            ThumbnailPolicy? ThumbnailPolicy);
    }
}
