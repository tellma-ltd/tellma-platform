// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     A blob the caller may download, as <see cref="IBlobService.ResolveAsync" /> resolved it: the
    ///     object to open in the store, and what a response serving it carries.
    /// </summary>
    /// <param name="Id">The blob's id.</param>
    /// <param name="Kind">The blob's kind.</param>
    /// <param name="State">
    ///     <see cref="BlobState.Committed" /> for an attached blob, whose bytes never change, or
    ///     <see cref="BlobState.Staged" /> for an upload its uploader previews before a save attaches it.
    /// </param>
    /// <param name="StorageName">The full object name to open: the primary's, or the requested variant's.</param>
    /// <param name="ContentType">The served media type: the primary's as the server determined it, or the variant's format.</param>
    /// <param name="Size">The primary's length in bytes, as the row records it; a variant's length is the stored object's.</param>
    /// <param name="FileName">The sanitised original file name, for the download's name; null when the upload named none.</param>
    /// <param name="IsImage">Whether the served media type is an image type.</param>
    /// <param name="ETag">
    ///     The strong entity tag, quotes included: <c>"{id}"</c> for the primary and
    ///     <c>"{id}-{variant}"</c> for a variant. The bytes behind an id never change, so the tag never
    ///     needs the store.
    /// </param>
    public sealed record BlobDownload(
        int Id,
        string Kind,
        BlobState State,
        string StorageName,
        string ContentType,
        long Size,
        string? FileName,
        bool IsImage,
        string ETag);
}
