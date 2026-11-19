// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     Stages uploaded files as blobs of the scope's tenant: validates and normalises the bytes,
    ///     records a <see cref="BlobState.Staged" /> row in one database round trip, and writes the
    ///     objects to the blob store. A record save then attaches the returned id. Scoped; the tenant
    ///     and the uploader come from the scope's request context, so it works in request and job
    ///     scopes alike.
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
    }
}
