// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;
using System.Drawing;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging
{
    /// <summary>Renders the first page of a PDF into a bitmap. A seam, so the suite can stand in a slow render.</summary>
    internal interface IPdfRasterizer
    {
        /// <summary>
        ///     Renders page 1 fitted within a square of <paramref name="size" />, keeping its aspect
        ///     ratio, on white.
        /// </summary>
        /// <remarks>Not thread-safe: the caller serialises every call behind the process-wide render gate.</remarks>
        /// <param name="input">The PDF, gated.</param>
        /// <param name="size">The square's side, in pixels.</param>
        /// <returns>The rendered page; the caller disposes it.</returns>
        /// <exception cref="ImageFailure">The PDF is encrypted, malformed or has no page.</exception>
        SKBitmap RenderFirstPage(GatedReadStream input, int size);
    }

    /// <summary>The PDFium rasterizer, through PDFtoImage.</summary>
    /// <remarks>
    ///     PDFtoImage reads the stream lazily through a callback that never lets an exception reach
    ///     PDFium, and locks each native call; it does not lock a whole render, which is why the
    ///     processor holds its own gate around this one.
    /// </remarks>
    internal sealed class PdfiumRasterizer : IPdfRasterizer
    {
        /// <summary>The one instance; it holds no state.</summary>
        public static PdfiumRasterizer Instance { get; } = new();

        /// <inheritdoc />
        public SKBitmap RenderFirstPage(GatedReadStream input, int size)
        {
            // PDFtoImage supports exactly these operating systems; anywhere else there is no PDFium.
            // The platform analyzer recognises the guard only in statement form, not as a conditional.
#pragma warning disable IDE0046
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return Render(input, size);
            }
#pragma warning restore IDE0046

            throw ImageFailure.DecodeFailed("PDF rendering is not available on this operating system.");
        }

        /// <summary>Renders page 1 through PDFium.</summary>
        /// <param name="input">The PDF, gated.</param>
        /// <param name="size">The square's side, in pixels.</param>
        /// <returns>The rendered page.</returns>
        [SupportedOSPlatform("windows")]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static SKBitmap Render(GatedReadStream input, int size)
        {
            try
            {
                // Section: measure page 1, in points, and fit it. A page is vector content, so a small
                // page is scaled up to the thumbnail size as readily as a large one is scaled down.
                SizeF page = Conversion.GetPageSize(input, page: 0, leaveOpen: true, password: null);
                if (!(float.IsFinite(page.Width) && float.IsFinite(page.Height) && page.Width > 0 && page.Height > 0))
                {
                    throw ImageFailure.DecodeFailed($"Page 1 declares an unusable size ({page.Width} x {page.Height}).");
                }

                (int width, int height) = Fit(page.Width, page.Height, size);

                // Section: render. Both sides are given, so PDFtoImage renders exactly this box; the
                // fit above keeps the aspect ratio. Annotations and form fields are not drawn: a
                // thumbnail shows the page, and each is more native code exposed to the upload.
                input.Position = 0;
                var options = new RenderOptions(Width: width, Height: height, BackgroundColor: SKColors.White);
                return Conversion.ToImage(input, page: 0, leaveOpen: true, password: null, options);
            }
            catch (Exception exception) when (exception is PdfPasswordProtectedException or PdfUnsupportedSecuritySchemeException)
            {
                throw ImageFailure.DecodeFailed("The PDF is encrypted.", exception);
            }
            catch (Exception exception) when (exception is PdfException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // PdfException covers corrupt, truncated and unparsable files (and a gate closed
                // mid-load); an argument exception, a document without pages.
                throw ImageFailure.DecodeFailed("The PDF could not be rendered.", exception);
            }
        }

        /// <summary>Fits a page within a square, keeping its aspect ratio; neither side below one pixel.</summary>
        /// <param name="width">The page width.</param>
        /// <param name="height">The page height.</param>
        /// <param name="size">The square's side.</param>
        /// <returns>The pixel size.</returns>
        internal static (int Width, int Height) Fit(float width, float height, int size)
        {
            double scale = size / Math.Max(width, (double)height);
            return (
                Math.Clamp((int)Math.Round(width * scale), 1, size),
                Math.Clamp((int)Math.Round(height * scale), 1, size));
        }
    }
}
