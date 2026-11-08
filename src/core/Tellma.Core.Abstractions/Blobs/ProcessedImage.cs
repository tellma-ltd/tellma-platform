// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     What <see cref="IImageProcessor.ProcessAsync" /> produced: the bytes stored as the blob's
    ///     primary, which the stored size, hash, dimensions and media type describe, and the
    ///     thumbnail rendered from them.
    /// </summary>
    /// <param name="Primary">The normalised image's encoded bytes.</param>
    /// <param name="Width">The normalised image's width in pixels.</param>
    /// <param name="Height">The normalised image's height in pixels.</param>
    /// <param name="Thumbnail">The thumbnail's encoded bytes, or null when none was requested.</param>
    /// <param name="ContentType">The media type of <paramref name="Primary" />, from the policy's format.</param>
    public sealed record ProcessedImage(
        byte[] Primary, int Width, int Height, byte[]? Thumbnail, string ContentType);
}
