// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     A kind's <c>thumb</c> variant, rendered once at upload when the primary can be rendered:
    ///     from the normalised image for an image kind, or from the stored primary otherwise (an
    ///     image, or a PDF's first page).
    /// </summary>
    /// <remarks>
    ///     A kind's <see cref="Format" /> is fixed once the kind has stored thumbnails, because every
    ///     stored thumbnail is served under the current format's media type.
    /// </remarks>
    /// <param name="Size">The largest width or height of the thumbnail, in pixels; positive.</param>
    /// <param name="Format">The thumbnail's encoding.</param>
    public sealed record ThumbnailPolicy(int Size, ImageFormat Format = ImageFormat.WebP)
    {
        /// <summary>The name of the thumbnail variant: <c>{primaryName}.thumb</c>.</summary>
        public const string VariantName = "thumb";
    }
}
