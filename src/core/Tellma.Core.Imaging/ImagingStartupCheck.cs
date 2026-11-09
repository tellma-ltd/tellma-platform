// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Text;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     <c>core.imaging</c>: the processor's options are valid, Skia loads and works in this
    ///     process, and a PDF worker starts, enters its sandbox, takes its limits and renders a tiny
    ///     generated PDF.
    /// </summary>
    /// <remarks>
    ///     A missing or mismatched native library, or a deployment a worker cannot start in (no
    ///     installed runtime, no dependency manifest, a kernel that refuses the worker's
    ///     system-call filter), otherwise surfaces on the first upload, as a missing thumbnail or a
    ///     server error, rather than as a startup failure that names the fix.
    /// </remarks>
    /// <param name="launcher">Starts the probe's worker; the PDF worker when null.</param>
    internal sealed class ImagingStartupCheck(PdfWorkerLauncher? launcher = null) : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "core.imaging";

        /// <summary>How long the probe's worker may take to start and render, on a host still starting.</summary>
        public static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(30);

        // The probe's thumbnail side, in pixels.
        private const int ProbeSize = 16;

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public async Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // Section: the processor's options, which are otherwise validated only when the first
            // upload constructs the processor, so a bad value would surface as a server error then.
            SkiaImageProcessorOptions options;
            try
            {
                options = services.GetService<IOptions<SkiaImageProcessorOptions>>()?.Value ?? new SkiaImageProcessorOptions();
            }
            catch (OptionsValidationException exception)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The image processor's options are invalid: {string.Join(" ", exception.Failures)}",
                    $"Set {SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.ImageConcurrency)} to a positive number of concurrent image operations and {SkiaImageProcessorOptions.SectionName}:{nameof(SkiaImageProcessorOptions.PdfWorkerMemoryLimit)} to at least {SkiaImageProcessorOptions.MinimumPdfWorkerMemoryLimit} bytes, or remove them to take the defaults."));
                options = new SkiaImageProcessorOptions();
            }

            // Section: Skia, in this process.
            string? skia = Probe(ProbeSkia);
            if (skia is not null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The SkiaSharp native library does not work in this process: {skia}",
                    "Ship the SkiaSharp native assets for this platform with the application (runtimes/<rid>/native/libSkiaSharp), on the version the SkiaSharp package pins, or register another IImageProcessor instead of calling AddTellmaSkia."));
            }

            // Section: PDFium, in a worker, through the whole path a thumbnail takes.
            CompositionProblem? worker = await ProbeWorkerAsync(Math.Max(options.PdfWorkerMemoryLimit, SkiaImageProcessorOptions.MinimumPdfWorkerMemoryLimit), cancellationToken).ConfigureAwait(false);
            if (worker is not null)
            {
                problems.Add(worker);
            }

            return problems;
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

        /// <summary>Runs one in-process probe, turning any failure into its description.</summary>
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

        /// <summary>Renders the one-page probe document in a worker, under the processor's limits.</summary>
        /// <param name="memoryLimit">The worker's memory limit.</param>
        /// <param name="cancellationToken">Cancels the probe.</param>
        /// <returns>Null on success; otherwise the problem, with its fix.</returns>
        private async Task<CompositionProblem?> ProbeWorkerAsync(long memoryLimit, CancellationToken cancellationToken)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(ProbeBudget);
            using var client = new PdfWorkerClient(launcher ?? new PdfWorkerLauncher(), memoryLimit);
            byte[] pdf = OnePagePdf();
            try
            {
                // The conversation blocks; it runs on a thread of its own.
                RenderedPage page = await Task.Factory.StartNew(
                    () => client.Render(pdf, ProbeSize, ProbeBudget, budget.Token),
                    budget.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).ConfigureAwait(false);
                return page.Width == ProbeSize && page.Height == ProbeSize
                    ? null
                    : WorkerProblem($"The PDF worker rendered the probe page at {page.Width} x {page.Height} instead of {ProbeSize} x {ProbeSize}.", RenderFix);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return WorkerProblem($"The PDF worker did not render the probe page within {ProbeBudget.TotalSeconds} seconds.", StartFix);
            }
            catch (PdfWorkerException exception)
            {
                return exception.Failure switch
                {
                    PdfWorkerFailure.Start => WorkerProblem(exception.Message, StartFix),
                    PdfWorkerFailure.Exited => WorkerProblem($"The PDF worker ended before it rendered the probe page: {exception.Message}", ExitFix),
                    PdfWorkerFailure.Protocol or PdfWorkerFailure.Failed => WorkerProblem($"The PDF worker did not render the probe page: {exception.Message}", RenderFix),
                    _ => WorkerProblem($"The PDF worker failed: {exception.Message}", RenderFix),
                };
            }
            catch (ImageFailure failure)
            {
                return WorkerProblem($"The PDF worker refused the probe page: {failure.Message}", RenderFix);
            }
        }

        /// <summary>A problem with the PDF worker.</summary>
        /// <param name="description">What failed.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private CompositionProblem WorkerProblem(string description, string fix)
        {
            return new CompositionProblem(Name, description, fix);
        }

        /// <summary>The fix for a worker that cannot start.</summary>
        private const string StartFix =
            "PDF thumbnails render in a worker process that `dotnet exec` starts from the host's own files: run the host framework-dependent on an installed .NET runtime, with its .deps.json and .runtimeconfig.json and Tellma.Core.Imaging.PdfWorker.dll beside its assemblies, as `dotnet build` and `dotnet publish` lay them out (not as a single file and not self-contained).";

        /// <summary>The fix for a worker that ended before it answered.</summary>
        private const string ExitFix =
            "The worker's own error, quoted above, names the cause. On Linux it installs a seccomp system-call filter before it reads any upload, and it does not run where the kernel or the container runtime refuses one; Docker's default profile allows it. A worker that ran out of memory needs a larger Tellma:Blobs:PdfWorkerMemoryLimit.";

        /// <summary>The fix for a worker that started but did not render.</summary>
        private const string RenderFix =
            "Ship the PDFium native assets for this platform with the application (runtimes/<rid>/native/pdfium or libpdfium) on the version the PDFtoImage package pins, or register another IImageProcessor instead of calling AddTellmaSkia.";
    }
}
