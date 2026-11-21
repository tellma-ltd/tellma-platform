// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     How a kind's uploaded image is normalised: decoded, oriented, resized and re-encoded by the
    ///     image processor, so the stored primary is always the processor's output and never the
    ///     client's bytes.
    /// </summary>
    /// <remarks>
    ///     Re-encoding is a security control: polyglot files, embedded metadata and decompression
    ///     bombs never reach storage. Because a decoder allocates from the image header,
    ///     <see cref="MaxInputPixels" /> is the whole memory bound, and it is enforced before any
    ///     pixel is decoded.
    /// </remarks>
    /// <param name="MaxDimension">The largest width or height of the stored image, in pixels; positive.</param>
    /// <param name="Fit">How the image is fitted into <paramref name="MaxDimension" />.</param>
    /// <param name="Format">The stored encoding, which determines the stored media type.</param>
    /// <param name="MaxInputPixels">The largest input the processor decodes, as width times height;
    ///     a larger header is refused before decoding.</param>
    public sealed record ImagePolicy(
        int MaxDimension, ImageFit Fit, ImageFormat Format = ImageFormat.WebP, int MaxInputPixels = ImagePolicy.DefaultMaxInputPixels)
    {
        /// <summary>
        ///     The default pixel cap, 50 megapixels; also the cap a kind with a thumbnail and no image
        ///     policy renders its thumbnails under.
        /// </summary>
        public const int DefaultMaxInputPixels = 50_000_000;
    }
}
