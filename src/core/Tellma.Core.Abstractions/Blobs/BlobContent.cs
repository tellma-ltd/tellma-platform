// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     One object opened by <see cref="IBlobStore.OpenReadAsync" />: a seekable stream positioned
    ///     at the start, and the object's length.
    /// </summary>
    /// <remarks>
    ///     The caller owns the content and disposes it, which disposes the stream. A seekable stream
    ///     of known length is what lets a download answer range requests.
    /// </remarks>
    /// <param name="Content">The object's bytes; seekable, positioned at 0.</param>
    /// <param name="Length">The object's length in bytes.</param>
    public sealed record BlobContent(Stream Content, long Length) : IDisposable, IAsyncDisposable
    {
        /// <summary>Disposes <see cref="Content" />.</summary>
        public void Dispose()
        {
            Content.Dispose();
        }

        /// <summary>Disposes <see cref="Content" /> asynchronously.</summary>
        /// <returns>A task that completes when the stream is disposed.</returns>
        public ValueTask DisposeAsync()
        {
            return Content.DisposeAsync();
        }
    }
}
