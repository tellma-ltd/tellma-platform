// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     The worker's one job: walk page 1 of a PDF against the caps, render it fitted to the
    ///     thumbnail square on white, and encode it as a lossless PNG for the host to re-encode.
    /// </summary>
    internal static class PdfRenderer
    {
        /// <summary>Renders page 1 of <paramref name="pdf" /> as a PNG whose longer side is <paramref name="size" />.</summary>
        /// <param name="pdf">The document.</param>
        /// <param name="size">The thumbnail square's side, in pixels.</param>
        /// <param name="rasterizer">Renders the page; PDFium in the worker.</param>
        /// <returns>The PNG.</returns>
        /// <exception cref="WorkerFailure">The PDF cannot be opened, is encrypted, or page 1 exceeds a cap.</exception>
        public static byte[] Render(byte[] pdf, int size, IPdfRasterizer rasterizer)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            ArgumentNullException.ThrowIfNull(rasterizer);

            // Section: the walk refuses what the render would pay too much for, before it runs.
            PdfPreflight.Run(pdf);

            // Section: render and encode. PNG is lossless, so the host re-encodes exactly the pixels
            // PDFium drew, in the policy's format, as it would have from a bitmap rendered in-process.
            using SKBitmap bitmap = rasterizer.RenderFirstPage(pdf, size);
            using SKPixmap pixmap = bitmap.PeekPixels() ?? throw new WorkerFailure("The rendered page has no pixels.");
            using SKData png = pixmap.Encode(SKPngEncoderOptions.Default) ?? throw new WorkerFailure("The rendered page could not be encoded.");
            return png.ToArray();
        }
    }
}
