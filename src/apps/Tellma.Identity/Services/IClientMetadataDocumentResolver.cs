// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     Resolves a document client's metadata from the URL that is its client id: from the
    ///     cache while the last answer stands, otherwise by fetching the document (and, for
    ///     <c>private_key_jwt</c>, its key set) under the fetch guards. Every network fetch is
    ///     audited and counted.
    /// </summary>
    public interface IClientMetadataDocumentResolver
    {
        /// <summary>Resolves a client id's document.</summary>
        /// <param name="clientId">The client id as presented, parsed.</param>
        /// <param name="cancellationToken">Stops waiting; a fetch already sent completes for later callers.</param>
        /// <returns>The document, or null when the client id is not a document client's.</returns>
        /// <exception cref="ClientMetadata.ClientMetadataException">
        ///     The client id is a document client's, but its document cannot be resolved: nothing
        ///     standing is cached, and the fetch was refused or failed.
        /// </exception>
        Task<ClientMetadataDocument?> ResolveAsync(Uri clientId, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Fetches a client id's document again ahead of its expiry — when a request names a
        ///     redirect URI the cached copy does not list, which a changed document may — and
        ///     serves the cached copy if that fetch fails.
        /// </summary>
        /// <param name="clientId">The client id as presented, parsed.</param>
        /// <param name="cancellationToken">Stops waiting; a fetch already sent completes for later callers.</param>
        /// <returns>The fresh document, the standing cached one after a failed fetch, or null when the client id is not a document client's.</returns>
        /// <exception cref="ClientMetadata.ClientMetadataException">
        ///     The document cannot be resolved: the fetch failed and nothing standing is cached.
        /// </exception>
        Task<ClientMetadataDocument?> RevalidateAsync(Uri clientId, CancellationToken cancellationToken = default);
    }
}
