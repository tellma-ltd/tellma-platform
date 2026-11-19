// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>One upload for <see cref="IBlobService.StageAsync" /> to stage.</summary>
    /// <remarks>
    ///     The service reads <see cref="Content" /> once, from its current position, and never disposes
    ///     it; the caller owns the stream.
    /// </remarks>
    /// <param name="Kind">The blob kind the file is uploaded as, such as <c>user-image</c>.</param>
    /// <param name="Content">The uploaded bytes.</param>
    /// <param name="Length">
    ///     The number of bytes <paramref name="Content" /> yields, as the client declared it; negative
    ///     when unknown, which is refused. A body that runs past it is refused as too large.
    /// </param>
    /// <param name="DeclaredContentType">
    ///     The client's declared media type, or null. The server determines the stored type from the
    ///     bytes; the declaration decides only between zip-based documents and among text types.
    /// </param>
    /// <param name="FileName">The client's file name, or null; sanitised and kept for display only.</param>
    public sealed record BlobStageRequest(string Kind, Stream Content, long Length, string? DeclaredContentType, string? FileName);
}
