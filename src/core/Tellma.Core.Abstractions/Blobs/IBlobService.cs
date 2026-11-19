// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     Stages uploaded files as blobs of the scope's tenant: validates and normalises the bytes,
    ///     records a <see cref="BlobState.Staged" /> row in one database round trip, and writes the
    ///     objects to the blob store. A record save then attaches the returned id. Resolves the blobs
    ///     the caller may download. Scoped; the tenant and the caller come from the scope's request
    ///     context, so it works in request and job scopes alike.
    /// </summary>
    public interface IBlobService
    {
        /// <summary>
        ///     Stages one upload. Every refusal happens before anything is stored. The row is written
        ///     before the objects, so every stored object has a row: a store failure leaves a staged
        ///     row without bytes, which expires into the sweep, and the caller never sees its id.
        /// </summary>
        /// <remarks>
        ///     The uploader is the user the scope connects as: the scope's user, or the system user in
        ///     the platform's own scope, which is exempt from the per-user staging quota. Only the
        ///     uploader may attach the blob, until its staging deadline.
        /// </remarks>
        /// <param name="request">The upload.</param>
        /// <param name="cancellationToken">Cancels the upload.</param>
        /// <returns>The staged blob, whose id a record save attaches.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="BlobRejectedException">The upload was refused; its code says why.</exception>
        /// <exception cref="ImageProcessingBusyException">The image processor had no free slot in time; the caller may retry.</exception>
        /// <exception cref="BlobStoreException">The store failed to write the objects.</exception>
        Task<BlobDescriptor> StageAsync(BlobStageRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Resolves a blob the caller may download, in one database round trip: a committed blob
        ///     whose owner row the caller may read (any connected member, for a kind any member reads),
        ///     or a staged one the caller uploaded and may still attach. Nothing is read from the store.
        /// </summary>
        /// <remarks>
        ///     A missing blob, a hidden owner, a blob of another kind, a released one and a variant the
        ///     blob does not carry all resolve to null alike, so a caller cannot tell them apart. The
        ///     download usually takes one round trip; a caller denied the owner read whose cached
        ///     permissions are older than the fast-deny window pays a re-check first. When the owner's
        ///     read is sensitive, a caller granted it is held to the same step-up a direct read is,
        ///     found or not; only the caller's own staged blob is served without it.
        /// </remarks>
        /// <param name="kind">The kind the request names.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="variant">A stored variant, such as <c>thumb</c>; null for the primary.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The blob to serve, or null when there is nothing the caller may download.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="kind" /> is null.</exception>
        /// <exception cref="Errors.BadRequestException"><paramref name="variant" /> is not a well-formed variant name.</exception>
        /// <exception cref="Tenancy.StepUpRequiredException">The owner's read is sensitive and the caller's sign-in is below the step-up bar.</exception>
        /// <exception cref="Errors.IndividualRequiredException">The owner's read is sensitive and the caller is a service account.</exception>
        Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant, CancellationToken cancellationToken = default);
    }
}
