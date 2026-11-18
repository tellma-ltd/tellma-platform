// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     A staged blob as the upload returns it: the id a record save attaches, and what the server
    ///     determined about the stored bytes.
    /// </summary>
    /// <param name="Id">The blob's id, which the owner's blob-reference property takes.</param>
    /// <param name="Kind">The blob's kind.</param>
    /// <param name="ContentType">The media type the server determined for the stored primary.</param>
    /// <param name="Size">The stored primary's length in bytes; for an image, the normalised image's.</param>
    /// <param name="Width">The stored image's width in pixels; null for a file that is not an image kind.</param>
    /// <param name="Height">The stored image's height in pixels; null for a file that is not an image kind.</param>
    /// <param name="FileName">The sanitised original file name, for display; null when none was given.</param>
    /// <param name="ExpiresAt">The staging deadline, by which a save must attach the blob; null once committed.</param>
    public sealed record BlobDescriptor(
        int Id, string Kind, string ContentType, long Size, int? Width, int? Height, string? FileName, DateTimeOffset? ExpiresAt);
}
