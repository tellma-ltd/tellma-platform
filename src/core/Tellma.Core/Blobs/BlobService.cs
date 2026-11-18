// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>The scope's blob service: staging through <see cref="BlobStager" />, downloads through <see cref="BlobResolver" />.</summary>
    /// <param name="stager">Stages uploads.</param>
    /// <param name="resolver">Resolves downloads.</param>
    internal sealed class BlobService(BlobStager stager, BlobResolver resolver) : IBlobService
    {
        /// <inheritdoc />
        public Task<BlobDescriptor> StageAsync(BlobStageRequest request, CancellationToken cancellationToken = default)
        {
            return stager.StageAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant, CancellationToken cancellationToken = default)
        {
            return resolver.ResolveAsync(kind, id, variant, cancellationToken);
        }
    }
}
