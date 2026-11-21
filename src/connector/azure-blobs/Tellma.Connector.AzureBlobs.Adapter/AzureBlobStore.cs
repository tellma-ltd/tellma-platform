// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Connector.AzureBlobs
{
    // Spec 0017 §6.3: one client per process, one container per tenant, create-only uploads,
    // ranged seekable reads, and a no-op purge.

    /// <summary>
    ///     The <see cref="IBlobStore" /> on Azure Blob Storage: one storage account per distribution,
    ///     one container per tenant, named <c>{ContainerPrefix}t{tenantId}</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every upload is create-only (<c>If-None-Match: *</c>), so of two writers racing to one
    ///         name exactly one wins and the other sees <see cref="BlobAlreadyExistsException" />. A
    ///         blob up to <see cref="SingleUploadLimit" /> goes in one request, which the service
    ///         applies whole or not at all; a larger one is staged in blocks and committed at once,
    ///         so a reader sees an object whole or not at all.
    ///     </para>
    ///     <para>
    ///         The containers this process has ensured are remembered, so only a tenant's first write
    ///         creates its container; a write that finds the container gone (removed behind the
    ///         store's back) forgets it, creates it again and retries once.
    ///     </para>
    ///     <para>
    ///         A download stream knows the blob's length, can seek, and fetches only the ranges read,
    ///         one <see cref="AzureBlobStoreOptions.ReadBufferSize" /> per request, pinned to the
    ///         version it opened. Writes leave no incomplete artefact a reader or a listing can see,
    ///         so <see cref="PurgeIncompleteAsync" /> has nothing to remove.
    ///     </para>
    /// </remarks>
    internal sealed class AzureBlobStore : IBlobStore
    {
        /// <summary>
        ///     The largest blob uploaded in a single request: 256 MiB, above the platform's default
        ///     upload ceiling, so an upload of a seekable content is atomic on the service.
        /// </summary>
        public const long SingleUploadLimit = 256L * 1024 * 1024;

        /// <summary>The block size of a larger upload: 8 MiB.</summary>
        public const long BlockSize = 8L * 1024 * 1024;

        private readonly BlobServiceClient _client;
        private readonly AzureBlobStoreOptions _options;
        private readonly AzureBlobStoreMetrics _metrics;
        private readonly ILogger<AzureBlobStore> _logger;

        // The tenants whose container this process has ensured; a write for any other ensures first.
        private readonly ConcurrentDictionary<int, bool> _ensured = new();

        /// <summary>Creates the store and its one client.</summary>
        /// <param name="options">The store's settings, validated when they are resolved.</param>
        /// <param name="credential">The identity the account's endpoint is reached with.</param>
        /// <param name="metrics">The store instruments.</param>
        /// <param name="logger">The store's logger.</param>
        /// <exception cref="OptionsValidationException">The settings are invalid.</exception>
        /// <exception cref="InvalidOperationException">The endpoint is set, but no credential was supplied.</exception>
        public AzureBlobStore(
            IOptions<AzureBlobStoreOptions> options, AzureBlobStoreCredential credential, AzureBlobStoreMetrics metrics, ILogger<AzureBlobStore> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(credential);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);

            _options = options.Value;
            _client = CreateClient(_options, credential.Credential);
            _metrics = metrics;
            _logger = logger;
        }

        /// <summary>
        ///     Builds the client the settings describe: over the connection string, or over the
        ///     endpoint and the credential. Nothing is sent; the startup check builds one the same way
        ///     to report a connection string that does not parse.
        /// </summary>
        /// <param name="options">The settings.</param>
        /// <param name="credential">The credential, needed for the endpoint alone.</param>
        /// <returns>The client.</returns>
        /// <exception cref="InvalidOperationException">The settings name no account, or the endpoint has no credential.</exception>
        /// <exception cref="FormatException">The connection string does not parse.</exception>
        internal static BlobServiceClient CreateClient(AzureBlobStoreOptions options, TokenCredential? credential)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return new BlobServiceClient(options.ConnectionString);
            }

            Uri endpoint = options.ServiceUri
                ?? throw new InvalidOperationException($"{AzureBlobStoreOptions.SectionName}:{nameof(AzureBlobStoreOptions.ServiceUri)} is not configured.");
            TokenCredential identity = credential
                ?? throw new InvalidOperationException(
                    "The Azure blob store reaches its account's endpoint with a TokenCredential, and none was passed or registered; "
                    + "register the host's managed identity as the TokenCredential service, or pass one to AddAzureBlobStore.");
            return new BlobServiceClient(endpoint, identity);
        }

        /// <summary>The name of a tenant's container.</summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <returns><c>{ContainerPrefix}t{tenantId}</c>.</returns>
        public string ContainerName(int tenantId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            return _options.ContainerPrefix + "t" + tenantId.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public async Task EnsureTenantAsync(int tenantId, CancellationToken cancellationToken)
        {
            BlobContainerClient container = Container(tenantId);
            cancellationToken.ThrowIfCancellationRequested();

            long started = Stopwatch.GetTimestamp();
            try
            {
                await EnsureContainerAsync(tenantId, container, cancellationToken).ConfigureAwait(false);
            }
            catch (BlobStoreException)
            {
                _metrics.Record(BlobTelemetryTagValues.StoreEnsure, BlobTelemetryTagValues.StoreFailed, started);
                throw;
            }

            _metrics.Record(BlobTelemetryTagValues.StoreEnsure, BlobTelemetryTagValues.StoreOk, started);
        }

        /// <inheritdoc />
        public async Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken)
        {
            BlobContainerClient container = Container(tenantId);
            ArgumentNullException.ThrowIfNull(writes);

            // --- Every entry is checked before storage is touched: an invalid one anywhere writes nothing ---
            HashSet<string> names = new(StringComparer.Ordinal);
            long totalBytes = 0;
            for (int i = 0; i < writes.Count; i++)
            {
                BlobWrite write = writes[i] ?? throw new ArgumentNullException(nameof(writes), "A blob write list must not contain null.");
                ArgumentNullException.ThrowIfNull(write.Content, nameof(writes));
                ArgumentNullException.ThrowIfNull(write.ContentType, nameof(writes));
                if (write.Length < 0)
                {
                    throw new ArgumentException($"The blob write '{write.Name}' declares a negative length.", nameof(writes));
                }

                if (!write.Content.CanRead)
                {
                    throw new ArgumentException($"The content of the blob write '{write.Name}' is not readable.", nameof(writes));
                }

                if (!BlobName.IsValid(write.Name))
                {
                    throw new ArgumentException($"'{write.Name}' is not a valid blob name.", nameof(writes));
                }

                if (!names.Add(write.Name))
                {
                    throw new ArgumentException($"The blob '{write.Name}' appears twice in one write.", nameof(writes));
                }

                totalBytes += write.Length;
            }

            if (writes.Count == 0)
            {
                return;
            }

            // --- Every write, in parallel and best-effort; the first failure in list order is the call's ---
            // Every exception is kept rather than thrown out of the loop, which would cancel the
            // writes not yet started and leave them unattempted.
            long started = Stopwatch.GetTimestamp();
            var failures = new Exception?[writes.Count];
            await Parallel.ForAsync(
                0,
                writes.Count,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxParallelism, CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    try
                    {
                        await WriteOneAsync(tenantId, container, writes[i], token).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }).ConfigureAwait(false);

            // A cancelled call is neither a success nor a storage failure, so it is not metered.
            Exception? failure = failures.FirstOrDefault(static failure => failure is not null);
            if (failures.FirstOrDefault(static failure => failure is OperationCanceledException) is Exception cancelled)
            {
                ExceptionDispatchInfo.Throw(cancelled);
            }

            string outcome = failure switch
            {
                null => BlobTelemetryTagValues.StoreOk,
                BlobAlreadyExistsException => BlobTelemetryTagValues.StoreConflict,
                _ => BlobTelemetryTagValues.StoreFailed,
            };
            _metrics.Record(BlobTelemetryTagValues.StoreWrite, outcome, started, totalBytes);

            // Rethrown with its original stack, which names the failing write's own frames.
            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }
        }

        /// <inheritdoc />
        public async Task<BlobContent?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken)
        {
            BlobContainerClient container = Container(tenantId);
            ArgumentNullException.ThrowIfNull(name);
            ValidateName(name, nameof(name));
            cancellationToken.ThrowIfCancellationRequested();

            long started = Stopwatch.GetTimestamp();
            Stream stream;
            try
            {
                // The open reads the blob's properties (its length and version) and nothing of its
                // bytes; the stream then fetches one buffer per range it is read through, each
                // pinned to that version.
                stream = await container.GetBlobClient(name)
                    .OpenReadAsync(new BlobOpenReadOptions(allowModifications: false) { BufferSize = _options.ReadBufferSize }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                // A missing blob and a missing container read alike, and creating either is a
                // write's business, never a read's.
                _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreNotFound, started);
                return null;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreFailed, started);
                throw new BlobStoreException($"Opening the blob '{name}' of tenant {tenantId} failed.", exception);
            }

            _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreOk, started);
            return new BlobContent(stream, stream.Length);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken)
        {
            BlobContainerClient container = Container(tenantId);
            ArgumentNullException.ThrowIfNull(names);

            // --- Every name first: an invalid entry deletes nothing ---
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i] ?? throw new ArgumentNullException(nameof(names), "A blob name list must not contain null.");
                ValidateName(name, nameof(names));
            }

            if (names.Count == 0)
            {
                return;
            }

            // --- Single deletes with bounded parallelism; a missing blob or container is success ---
            long started = Stopwatch.GetTimestamp();
            var failures = new Exception?[names.Count];
            await Parallel.ForAsync(
                0,
                names.Count,
                new ParallelOptions { MaxDegreeOfParallelism = _options.MaxParallelism, CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    try
                    {
                        _ = await container.GetBlobClient(names[i])
                            .DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots, conditions: null, token)
                            .ConfigureAwait(false);
                    }
                    catch (RequestFailedException exception) when (exception.Status == 404)
                    {
                        // Already gone, or never written.
                    }
                    catch (Exception exception) when (IsStorageFailure(exception))
                    {
                        failures[i] = exception;
                    }
                }).ConfigureAwait(false);

            // --- Every delete was attempted; the first failure in list order is reported ---
            int failed = Array.FindIndex(failures, static failure => failure is not null);
            if (failed < 0)
            {
                _metrics.Record(BlobTelemetryTagValues.StoreDelete, BlobTelemetryTagValues.StoreOk, started);
                return;
            }

            _metrics.Record(BlobTelemetryTagValues.StoreDelete, BlobTelemetryTagValues.StoreFailed, started);
            int count = failures.Count(static failure => failure is not null);
            throw new BlobStoreException(
                $"Deleting {count} of {names.Count} blobs of tenant {tenantId} failed; the first was '{names[failed]}'.",
                failures[failed]);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, CancellationToken cancellationToken)
        {
            // Validated eagerly, so a bad prefix throws at the call rather than at the first MoveNext.
            BlobContainerClient container = Container(tenantId);
            ArgumentNullException.ThrowIfNull(prefix);
            return BlobName.IsValidPrefix(prefix)
                ? ListCoreAsync(tenantId, container, prefix, cancellationToken)
                : throw new ArgumentException($"'{prefix}' is not a valid blob listing prefix: a kind, or a kind and a fan-out segment.", nameof(prefix));
        }

        /// <inheritdoc />
        public Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken)
        {
            // Nothing to purge: an upload that never completed left no blob a reader or a listing
            // sees, and the service discards uncommitted blocks on its own.
            _ = Container(tenantId);
            cancellationToken.ThrowIfCancellationRequested();
            _metrics.Record(BlobTelemetryTagValues.StorePurge, BlobTelemetryTagValues.StoreOk, Stopwatch.GetTimestamp());
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Whether an exception from a client call is the storage's failure, which the store
        ///     reports as <see cref="BlobStoreException" />: anything but a cancellation and the
        ///     store's own answers. That covers a refused request, the retries' joint failure, a
        ///     transport failure, and a credential that cannot produce a token.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>True unless the exception is a cancellation, a <see cref="BlobStoreException" /> or a <see cref="BlobAlreadyExistsException" />.</returns>
        private static bool IsStorageFailure(Exception exception)
        {
            return exception is not (OperationCanceledException or BlobStoreException or BlobAlreadyExistsException);
        }

        /// <summary>Whether a refusal means the tenant's container does not exist.</summary>
        /// <param name="exception">The refusal.</param>
        /// <returns>True for a 404 naming the container.</returns>
        private static bool IsContainerMissing(RequestFailedException exception)
        {
            return exception.Status == 404 && exception.ErrorCode == BlobErrorCode.ContainerNotFound;
        }

        /// <summary>Whether a refusal of a create-only upload means the name exists.</summary>
        /// <param name="exception">The refusal.</param>
        /// <returns>True for the service's existing-blob conflict, or the failed <c>If-None-Match: *</c> precondition.</returns>
        private static bool IsAlreadyExists(RequestFailedException exception)
        {
            return (exception.Status == 409 && exception.ErrorCode == BlobErrorCode.BlobAlreadyExists) || exception.Status == 412;
        }

        /// <summary>Refuses a name outside the grammar, before storage is touched.</summary>
        /// <param name="name">The name.</param>
        /// <param name="parameterName">The argument the name came from.</param>
        /// <exception cref="ArgumentException">The name is invalid.</exception>
        private static void ValidateName(string name, string parameterName)
        {
            if (!BlobName.IsValid(name))
            {
                throw new ArgumentException($"'{name}' is not a valid blob name.", parameterName);
            }
        }

        /// <summary>The client of a tenant's container, after checking the tenant id.</summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <returns>The container's client; nothing is sent.</returns>
        private BlobContainerClient Container(int tenantId)
        {
            return _client.GetBlobContainerClient(ContainerName(tenantId));
        }

        /// <summary>Creates a tenant's container when absent, and remembers it.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="container">The tenant's container.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>A task that completes when the container exists.</returns>
        /// <exception cref="BlobStoreException">The container could not be created.</exception>
        private async Task EnsureContainerAsync(int tenantId, BlobContainerClient container, CancellationToken cancellationToken)
        {
            try
            {
                // Private: blobs are reached only through the platform's authorised download.
                _ = await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw new BlobStoreException($"Creating the blob storage of tenant {tenantId} failed.", exception);
            }

            _ensured[tenantId] = true;
            AzureBlobStoreLog.TenantEnsured(_logger, tenantId, container.Name);
        }

        /// <summary>Writes one object create-only, ensuring the container first or after finding it gone.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="container">The tenant's container.</param>
        /// <param name="write">The validated write.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the object is in place.</returns>
        /// <exception cref="BlobAlreadyExistsException">The name exists.</exception>
        /// <exception cref="BlobStoreException">The content does not match its declared length, or the storage failed.</exception>
        private async Task WriteOneAsync(int tenantId, BlobContainerClient container, BlobWrite write, CancellationToken cancellationToken)
        {
            // --- A content whose own length is known and wrong fails before anything is sent ---
            long origin = write.Content.CanSeek ? write.Content.Position : 0;
            if (write.Content.CanSeek && write.Content.Length - origin != write.Length)
            {
                long actual = write.Content.Length - origin;
                throw new BlobStoreException(
                    actual > write.Length
                        ? $"The content of the blob '{write.Name}' runs past its declared length of {write.Length} bytes."
                        : $"The content of the blob '{write.Name}' ended after {actual} of its declared {write.Length} bytes.",
                    null);
            }

            // --- The first write this process makes for the tenant creates its container ---
            if (!_ensured.ContainsKey(tenantId))
            {
                await EnsureContainerAsync(tenantId, container, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await UploadAsync(container, write, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (IsContainerMissing(exception))
            {
                // --- The container was removed behind the store's back: created again, and the write retried once ---
                _ = _ensured.TryRemove(tenantId, out _);
                AzureBlobStoreLog.TenantMissingOnWrite(_logger, tenantId);
                if (!write.Content.CanSeek)
                {
                    throw new BlobStoreException(
                        $"Writing the blob '{write.Name}' of tenant {tenantId} failed: its storage was missing, and its content cannot be read again for a retry.", exception);
                }

                write.Content.Position = origin;
                await EnsureContainerAsync(tenantId, container, cancellationToken).ConfigureAwait(false);
                try
                {
                    await UploadAsync(container, write, cancellationToken).ConfigureAwait(false);
                }
                catch (RequestFailedException retry) when (IsContainerMissing(retry))
                {
                    _ = _ensured.TryRemove(tenantId, out _);
                    throw new BlobStoreException(
                        $"Writing the blob '{write.Name}' of tenant {tenantId} failed: its storage vanished again after being created.", retry);
                }
                catch (Exception retry) when (IsStorageFailure(retry))
                {
                    throw new BlobStoreException($"Writing the blob '{write.Name}' of tenant {tenantId} failed.", retry);
                }
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw new BlobStoreException($"Writing the blob '{write.Name}' of tenant {tenantId} failed.", exception);
            }
        }

        /// <summary>Uploads one object create-only through the length guard.</summary>
        /// <param name="container">The tenant's container.</param>
        /// <param name="write">The validated write, positioned at its first byte.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>A task that completes when the object is in place.</returns>
        /// <exception cref="BlobAlreadyExistsException">The name exists.</exception>
        /// <exception cref="BlobStoreException">The content does not match its declared length.</exception>
        /// <exception cref="RequestFailedException">The service refused the upload for another reason, a missing container among them.</exception>
        private static async Task UploadAsync(BlobContainerClient container, BlobWrite write, CancellationToken cancellationToken)
        {
            LengthGuardStream content = new(write.Content, write.Length, write.Name);
            BlobUploadOptions options = new()
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = write.ContentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                TransferOptions = new StorageTransferOptions
                {
                    InitialTransferSize = SingleUploadLimit,
                    MaximumTransferSize = BlockSize,
                    MaximumConcurrency = 1,
                },
            };

            try
            {
                _ = await container.GetBlobClient(write.Name).UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (content.Mismatch is string mismatch && exception is not OperationCanceledException)
            {
                // The guard stopped the upload, whatever the transport wrapped its failure in.
                throw new BlobStoreException(mismatch, exception);
            }
            catch (RequestFailedException exception) when (IsAlreadyExists(exception))
            {
                throw new BlobAlreadyExistsException(write.Name);
            }
        }

        /// <summary>Enumerates a validated prefix's blobs, page by page.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="container">The tenant's container.</param>
        /// <param name="prefix">The validated prefix.</param>
        /// <param name="cancellationToken">Cancels the enumeration.</param>
        /// <returns>The blobs, in the service's order, which is the names' ordinal order.</returns>
        private async IAsyncEnumerable<BlobStoreEntry> ListCoreAsync(
            int tenantId, BlobContainerClient container, string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            string outcome = BlobTelemetryTagValues.StoreOk;

            // Whole segments: "conformance/" never matches the longer kind "conformance-other".
            IAsyncEnumerator<BlobItem> items = container
                .GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix + BlobName.SegmentSeparator, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    BlobItem item;
                    try
                    {
                        if (!await items.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }

                        item = items.Current;
                    }
                    catch (RequestFailedException exception) when (IsContainerMissing(exception))
                    {
                        // A tenant without storage has nothing to list, and the listing creates nothing.
                        break;
                    }
                    catch (Exception exception) when (IsStorageFailure(exception))
                    {
                        outcome = BlobTelemetryTagValues.StoreFailed;
                        throw new BlobStoreException($"Listing the blobs under '{prefix}' of tenant {tenantId} failed.", exception);
                    }

                    // Only names the contract can address: anything else in the container is not a blob of the platform's.
                    if (BlobName.IsValid(item.Name))
                    {
                        yield return new BlobStoreEntry(
                            item.Name,
                            item.Properties.ContentLength ?? 0,
                            (item.Properties.LastModified ?? DateTimeOffset.UnixEpoch).ToUniversalTime());
                    }
                }
            }
            finally
            {
                await items.DisposeAsync().ConfigureAwait(false);

                // An early stop by the consumer is a success; a cancellation is not metered.
                if (!cancellationToken.IsCancellationRequested)
                {
                    _metrics.Record(BlobTelemetryTagValues.StoreList, outcome, started);
                }
            }
        }
    }
}
