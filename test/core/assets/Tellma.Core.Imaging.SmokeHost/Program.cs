// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Imaging.SmokeHost
{
    /// <summary>
    ///     Composes the image processor on the generic host, as a distribution does, runs its
    ///     startup check, renders an honest PDF and a hostile one (nested forms that expand
    ///     exponentially inside PDFium), and reports, one <c>key=value</c> line each, what happened
    ///     and whether PDFium was ever loaded into this process. Exits 0 when everything held.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>--measure N</c> also renders the honest PDF N more times and reports the spread of
    ///         a whole thumbnail's cost, worker start included.
    ///     </para>
    ///     <para>
    ///         <c>--orphan &lt;stand-in&gt;</c> instead starts one thumbnail in a worker that never
    ///         answers (the suite's stand-in), reports the worker's process id, and waits to be
    ///         killed, so the suite can see the worker die with its host.
    ///     </para>
    /// </remarks>
    internal static class Program
    {
        /// <summary>The host's entry point.</summary>
        /// <param name="args">Nothing, <c>--measure N</c> or <c>--orphan &lt;stand-in&gt;</c>.</param>
        /// <returns>0 when everything held; 1 otherwise.</returns>
        private static async Task<int> Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--orphan")
            {
                await OrphanAsync(args[1]);
                return 1;
            }

            int measure = args.Length >= 2 && args[0] == "--measure" ? int.Parse(args[1], CultureInfo.InvariantCulture) : 0;
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Services.AddTellmaSkia();
            using IHost host = builder.Build();
            bool held = true;

            // Section: the startup checks, as the realised gate runs them.
            foreach (IStartupCheck check in host.Services.GetServices<IStartupCheck>())
            {
                IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(host.Services, CancellationToken.None);
                Report($"check.{check.Name}", problems.Count == 0 ? "passed" : string.Join(" | ", problems.Select(static p => $"{p.Problem} Fix: {p.Fix}")));
                held &= problems.Count == 0;
            }

            IImageProcessor processor = host.Services.GetRequiredService<IImageProcessor>();

            // Section: an honest page, 200 x 100 points, left half black: a 256 x 128 thumbnail.
            var clock = Stopwatch.StartNew();
            byte[]? honest = await ThumbnailAsync(processor, HalfBlackPage(200, 100));
            Report("honest.milliseconds", clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            Report("honest.bytes", honest?.Length.ToString(CultureInfo.InvariantCulture) ?? "none");
            held &= honest is { Length: > 0 };

            // Section: a hostile page of about 1.5 KB that expands to millions of objects inside
            // PDFium. Its worker must be stopped, the host must stay small, and no thumbnail result.
            long hostBefore = PrivateMemory();
            clock.Restart();
            byte[]? hostile = await ThumbnailAsync(processor, FormFanOut(depth: 7, breadth: 10));
            Report("hostile.milliseconds", clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
            Report("hostile.thumbnail", hostile is null ? "none" : "rendered");
            long growth = PrivateMemory() - hostBefore;
            Report("hostile.host-memory-growth-mib", (growth / (1024 * 1024)).ToString(CultureInfo.InvariantCulture));
            held &= hostile is null && growth < 64L * 1024 * 1024;

            // Section: the cost of a whole thumbnail, worker start included.
            if (measure > 0)
            {
                var times = new List<long>();
                for (int i = 0; i < measure; i++)
                {
                    clock.Restart();
                    held &= await ThumbnailAsync(processor, HalfBlackPage(612, 792)) is not null;
                    times.Add(clock.ElapsedMilliseconds);
                }

                times.Sort();
                Report("measure.milliseconds", string.Create(CultureInfo.InvariantCulture, $"min {times[0]}, median {times[times.Count / 2]}, max {times[^1]} over {times.Count}"));
            }

            // Section: PDFium and PDFtoImage must never have been loaded into the host.
            bool pdfium = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Any(static module => module.ModuleName.Contains("pdfium", StringComparison.OrdinalIgnoreCase));
            bool pdfToImage = AppDomain.CurrentDomain.GetAssemblies().Any(static assembly => assembly.GetName().Name == "PDFtoImage");
            Report("host.pdfium-loaded", pdfium ? "yes" : "no");
            Report("host.pdftoimage-loaded", pdfToImage ? "yes" : "no");
            held &= !pdfium && !pdfToImage;

            Report("result", held ? "held" : "failed");
            return held ? 0 : 1;
        }

        /// <summary>
        ///     Starts a thumbnail in a worker that never answers, with no budget, reports the
        ///     worker's process id once it is ready, and waits forever.
        /// </summary>
        /// <param name="standIn">The stand-in worker assembly.</param>
        /// <returns>Never completes.</returns>
        private static async Task OrphanAsync(string standIn)
        {
            using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            using var client = new Workers.PdfWorkerClient(new Workers.PdfWorkerLauncher(standIn, ["hang-after-request"]), SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);
            client.Ready += static worker => Report("worker.pid", worker.Id.ToString(CultureInfo.InvariantCulture));
            using var processor = new SkiaImageProcessor(
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
                TimeProvider.System,
                new SkiaImageProcessorOptions(),
                client);
            using var stream = new MemoryStream(HalfBlackPage(100, 100), writable: false);
            _ = await processor.ThumbnailAsync("smoke", stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, CancellationToken.None);
            await Task.Delay(Timeout.Infinite);
        }

        /// <summary>Renders a 256-pixel WebP thumbnail of a PDF under the platform's 5-second budget.</summary>
        /// <param name="processor">The processor.</param>
        /// <param name="pdf">The PDF.</param>
        /// <returns>The thumbnail, or null.</returns>
        private static async Task<byte[]?> ThumbnailAsync(IImageProcessor processor, byte[] pdf)
        {
            using var stream = new MemoryStream(pdf, writable: false);
            return await processor.ThumbnailAsync("smoke", stream, BlobContentTypes.Pdf, new ThumbnailPolicy(256), TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        /// <summary>Writes one report line.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        private static void Report(string key, string value)
        {
            Console.Out.WriteLine($"{key}={value}");
        }

        /// <summary>This process's private memory, in bytes.</summary>
        /// <returns>The memory.</returns>
        private static long PrivateMemory()
        {
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }

        /// <summary>A one-page PDF whose left half is black.</summary>
        /// <param name="width">The page width in points.</param>
        /// <param name="height">The page height in points.</param>
        /// <returns>The PDF.</returns>
        private static byte[] HalfBlackPage(int width, int height)
        {
            string content = string.Create(CultureInfo.InvariantCulture, $"0 0 0 rg 0 0 {width / 2} {height} re f");
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents 4 0 R >>"),
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"),
            ]);
        }

        /// <summary>A one-page PDF whose page draws a form that draws the next <paramref name="breadth" /> times, <paramref name="depth" /> levels down.</summary>
        /// <param name="depth">The nesting depth.</param>
        /// <param name="breadth">How often each form draws the next.</param>
        /// <returns>The PDF.</returns>
        private static byte[] FormFanOut(int depth, int breadth)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /XObject << /F 5 0 R >> >> /Contents 4 0 R >>",
                "<< /Length 5 >>\nstream\n/F Do\nendstream",
            };

            for (int level = 0; level < depth; level++)
            {
                bool leaf = level == depth - 1;
                string body = leaf ? "0 0 1 1 re f" : string.Concat(Enumerable.Repeat("/F Do\n", breadth));
                string resources = leaf ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Resources << /XObject << /F {6 + level} 0 R >> >>");
                objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100]{resources} /Length {body.Length} >>\nstream\n{body}\nendstream"));
            }

            return Build([.. objects]);
        }

        /// <summary>Lays out numbered objects, the cross-reference table and the trailer.</summary>
        /// <param name="objects">The objects, numbered from 1.</param>
        /// <returns>The PDF.</returns>
        private static byte[] Build(string[] objects)
        {
            var text = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                offsets.Add(text.Length);
                text.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            int xref = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(text.ToString());
        }
    }
}
