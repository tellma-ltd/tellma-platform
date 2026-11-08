// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The bytes of blobs, by object name, for one tenant per call: the low-level storage behind
    ///     the blob service. One implementation is registered per process, as a singleton.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every tenant has its own container or directory, created by
    ///         <see cref="EnsureTenantAsync" /> and, as a fallback, by the first write that finds it
    ///         missing. Objects are immutable: writes are create-only, so a name, once written, holds
    ///         the same bytes until it is deleted. Every name passed to a member must satisfy
    ///         <see cref="BlobName.IsValid" />, and every member validates all of them before it
    ///         touches storage, throwing <see cref="ArgumentException" /> otherwise.
    ///     </para>
    ///     <para>
    ///         A store never consults the sandbox context: a sandbox tenant has a container of its
    ///         own like any other, and writing to the platform's own storage is not an external side
    ///         effect. The failures a store throws, <see cref="BlobAlreadyExistsException" /> and
    ///         <see cref="BlobStoreException" />, are infrastructure failures that the web layer
    ///         never maps to a client-facing error.
    ///     </para>
    ///     <para>
    ///         Every implementation must pass the shared blob-store conformance suite, the executable
    ///         form of this contract.
    ///     </para>
    /// </remarks>
    public interface IBlobStore
    {
        /// <summary>
        ///     Creates the tenant's container or directory when it does not exist. Idempotent. Called
        ///     when a tenant is provisioned, and by a write that finds the tenant's storage missing.
        /// </summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>A task that completes when the tenant's storage exists.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="BlobStoreException">The storage could not be created.</exception>
        Task EnsureTenantAsync(int tenantId, CancellationToken cancellationToken);

        /// <summary>
        ///     Writes every object, create-only and in parallel. Best-effort: a failure leaves the
        ///     writes that succeeded in place, and the call then throws the failure of the first
        ///     failing write in list order.
        /// </summary>
        /// <remarks>
        ///     A write into a tenant whose container or directory is missing creates it once and
        ///     retries once; a second failure throws. Each write's content must yield exactly its
        ///     declared length; one that yields fewer or more bytes fails and leaves no object.
        /// </remarks>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="writes">The objects; their names valid and distinct. Empty is a no-op.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>A task that completes when every object is written.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="writes" /> or an entry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="ArgumentException">A name is invalid or appears twice, or a length is negative.</exception>
        /// <exception cref="BlobAlreadyExistsException">An object of that name already exists.</exception>
        /// <exception cref="BlobStoreException">The storage failed.</exception>
        Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken);

        /// <summary>
        ///     Opens one object as a seekable stream of known length, or returns null when the object,
        ///     or the tenant's container or directory, does not exist. A read creates nothing.
        /// </summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="name">The object's name.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The content, which the caller disposes; null when the object does not exist.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is invalid.</exception>
        /// <exception cref="BlobStoreException">The storage failed.</exception>
        Task<BlobContent?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken);

        /// <summary>
        ///     Deletes every named object, with bounded parallelism. A missing object, container or
        ///     directory is success, and a delete creates nothing.
        /// </summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="names">The objects' names. Empty is a no-op.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>A task that completes when every object is gone.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="names" /> or an entry is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="ArgumentException">A name is invalid.</exception>
        /// <exception cref="BlobStoreException">An object could not be deleted; the others were attempted.</exception>
        Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken);

        /// <summary>
        ///     Enumerates the objects under a prefix — a kind, or a kind and a fan-out segment (see
        ///     <see cref="BlobName.IsValidPrefix" />) — in ordinal order of their names. For tooling
        ///     and reconciliation only, never on a request path. A missing container or directory
        ///     yields nothing and creates nothing.
        /// </summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="prefix">The prefix; whole segments only, so a kind never matches another kind that starts with it.</param>
        /// <param name="cancellationToken">Cancels the enumeration.</param>
        /// <returns>The objects under the prefix.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="prefix" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="ArgumentException"><paramref name="prefix" /> is invalid.</exception>
        /// <exception cref="BlobStoreException">The storage failed.</exception>
        IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, CancellationToken cancellationToken);

        /// <summary>
        ///     Housekeeping: removes the store's incomplete artefacts (a write's temporary file left by
        ///     a crash) last modified before <paramref name="olderThan" />. A no-op for a store that
        ///     leaves none.
        /// </summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="olderThan">The cut-off; artefacts modified at or after it are kept.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>A task that completes when the artefacts are gone.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tenantId" /> is not positive.</exception>
        /// <exception cref="BlobStoreException">An artefact could not be removed; the others were attempted.</exception>
        Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken);
    }
}
