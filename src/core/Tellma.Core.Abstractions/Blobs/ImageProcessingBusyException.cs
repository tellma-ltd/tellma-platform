// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The image processor had no free slot within the call's time budget: the server is busy,
    ///     and the upload is not at fault.
    /// </summary>
    /// <remarks>
    ///     Like <see cref="BlobStoreException" />, an infrastructure condition rather than a
    ///     client-facing validation error, which is why it derives from <see cref="Exception" />. The
    ///     web surface answers it with 503 Service Unavailable and a <c>Retry-After</c> of
    ///     <see cref="RetryAfter" />; the same upload is expected to succeed once load falls.
    /// </remarks>
    /// <param name="retryAfter">How long the client should wait before retrying; positive.</param>
    public sealed class ImageProcessingBusyException(TimeSpan retryAfter)
        : Exception("The image processor is busy; no slot became free within the time budget.")
    {
        /// <summary>How long the client should wait before retrying, in whole seconds, at least one.</summary>
        public TimeSpan RetryAfter { get; } = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));
    }
}
