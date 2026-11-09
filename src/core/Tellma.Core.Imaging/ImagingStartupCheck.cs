// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;
using System.Text;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     <c>core.imaging</c>: the processor's options are valid, and its native libraries load and
    ///     work in this process. Skia encodes and decodes a tiny image, and PDFium opens a tiny
    ///     generated PDF.
    /// </summary>
    /// <remarks>
    ///     A missing or mismatched native library otherwise surfaces on the first upload, as a server
    ///     error, rather than as a startup failure that names the fix. The probe runs behind the same
    ///     render gate as every PDF render.
    /// </remarks>
    internal sealed class ImagingStartupCheck : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "core.imaging";

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public async Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            string? skia = Probe(ProbeSkia);
            string? pdfium;
            await PdfRenderGate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                pdfium = Probe(ProbePdfium);
            }
            finally
            {
                PdfRenderGate.Semaphore.Release();
            }

            // Section: one problem per library that failed, with what failed and the fix.
            List<CompositionProblem> problems = [];

            // Section: the processor's options, which are otherwise validated only when the first
            // upload constructs the processor, so a bad value would surface as a server error then.
            try
            {
                _ = services.GetService<IOptions<SkiaImageProcessorOptions>>()?.Value;
            }
            catch (OptionsValidationException exception)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The image processor's options are invalid: {string.Join(" ", exception.Failures)}",
                    $"Set {SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.ImageConcurrency)} to a positive number of concurrent image operations, or remove it to take the default."));
            }

            if (skia is not null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The SkiaSharp native library does not work in this process: {skia}",
                    "Ship the SkiaSharp native assets for this platform with the application (runtimes/<rid>/native/libSkiaSharp), on the version the SkiaSharp package pins, or register another IImageProcessor instead of calling AddTellmaSkia."));
            }

            if (pdfium is not null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The PDFium native library does not work in this process: {pdfium}",
                    "Ship the PDFium native assets for this platform with the application (runtimes/<rid>/native/pdfium or libpdfium), or register another IImageProcessor instead of calling AddTellmaSkia."));
            }

            return problems;
        }

        /// <summary>Runs one probe, turning any failure into its description.</summary>
        /// <param name="probe">The probe; returns null on success.</param>
        /// <returns>Null on success; otherwise what failed.</returns>
        private static string? Probe(Func<string?> probe)
        {
            try
            {
                return probe();
            }
#pragma warning disable CA1031 // A failed native load surfaces as any of several exception types; each is a startup problem.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        /// <summary>Encodes a 2 x 2 image as WebP and decodes it back.</summary>
        /// <returns>Null on success; otherwise what failed.</returns>
        private static string? ProbeSkia()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
            bitmap.Erase(SKColors.Red);
            using SKPixmap pixmap = bitmap.PeekPixels();
            using SKData? encoded = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100));
            if (encoded is null)
            {
                return "the WebP encoder returned nothing.";
            }

            using var codec = SKCodec.Create(encoded);
            return codec is { EncodedFormat: SKEncodedImageFormat.Webp, Info.Width: 2, Info.Height: 2 }
                ? null
                : "the WebP decoder did not read back the probe image.";
        }

        /// <summary>Opens a one-page PDF and counts its pages.</summary>
        /// <returns>Null on success; otherwise what failed.</returns>
        private static string? ProbePdfium()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                using var pdf = new MemoryStream(OnePagePdf(), writable: false);
                int pages = Conversion.GetPageCount(pdf, leaveOpen: true, password: null);
                return pages == 1 ? null : $"the probe document reported {pages} pages instead of 1.";
            }

            return "PDFium is not available on this operating system.";
        }

        /// <summary>A minimal, well-formed one-page PDF, with a correct cross-reference table.</summary>
        /// <returns>The document's bytes.</returns>
        internal static byte[] OnePagePdf()
        {
            string[] objects =
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 8 8] >>",
            ];

            // Section: the body, recording each object's byte offset for the cross-reference table.
            var text = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                offsets.Add(text.Length);
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            // Section: the table (twenty bytes per entry) and the trailer.
            int xref = text.Length;
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(text.ToString());
        }
    }
}
