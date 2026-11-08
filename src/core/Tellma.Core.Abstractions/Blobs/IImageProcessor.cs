// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     Decodes, validates, normalises and re-encodes untrusted images, and renders thumbnails. The
    ///     platform's one native-code seam: the runtime never references an imaging library, so a
    ///     distribution registers an implementation (or none, when no kind needs one).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every input is hostile. An implementation identifies the format from the bytes, never
    ///         from a declaration; refuses an image whose header declares more than the policy's
    ///         pixel cap before decoding a single pixel; decodes the first frame only; applies the
    ///         encoded orientation; and writes output that carries no metadata.
    ///     </para>
    ///     <para>
    ///         Implementations are singletons and must be safe to call concurrently.
    ///     </para>
    /// </remarks>
    public interface IImageProcessor
    {
        /// <summary>
        ///     Normalises an uploaded image per <paramref name="policy" /> and, when
        ///     <paramref name="thumbnail" /> is set, renders its thumbnail from the normalised image.
        /// </summary>
        /// <param name="input">The uploaded bytes; at most the kind's size limit.</param>
        /// <param name="policy">How the image is normalised.</param>
        /// <param name="thumbnail">The thumbnail to render, or null for none.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The bytes to store, their dimensions and media type, and the thumbnail.</returns>
        /// <exception cref="ImageRejectedException">The input is not an accepted image, declares too
        ///     many pixels, or cannot be decoded.</exception>
        Task<ProcessedImage> ProcessAsync(
            byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, CancellationToken cancellationToken);

        /// <summary>
        ///     Renders a thumbnail of a stored file that was not normalised: one of the four image
        ///     types, under the default pixel cap, or a PDF's first page. Returns null for anything it
        ///     cannot render — another type, an encrypted or unparsable PDF, an image over the cap, a
        ///     render that exceeds its time budget. Null is never a rejection of the file.
        /// </summary>
        /// <param name="input">The stored bytes, positioned at the start.</param>
        /// <param name="contentType">The stored media type.</param>
        /// <param name="policy">The thumbnail to render.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The encoded thumbnail, or null when the file cannot be rendered.</returns>
        Task<byte[]?> ThumbnailAsync(
            Stream input, string contentType, ThumbnailPolicy policy, CancellationToken cancellationToken);
    }
}
