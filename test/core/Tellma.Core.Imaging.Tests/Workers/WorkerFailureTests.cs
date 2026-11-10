// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     A worker that fails in any way is a missing thumbnail, never a failed upload or a hurt
    ///     host: whether it crashes or exits before or after it is ready, breaks the protocol, or
    ///     answers with a malformed or deceptive image, the call yields null, the failure is counted
    ///     and logged as a worker failure, the slot comes back, no worker survives, and the
    ///     processor serves the next call.
    /// </summary>
    public sealed class WorkerFailureTests
    {
        private const int Slots = 2;

        public static TheoryData<string, string> Failures => new()
        {
            { "exit-before-ready", nameof(PdfWorkerFailure.Exited) },
            { "crash-before-ready", nameof(PdfWorkerFailure.Exited) },
            { "bad-ready", nameof(PdfWorkerFailure.Protocol) },
            { "crash-after-request", nameof(PdfWorkerFailure.Exited) },
            { "exit-after-request", nameof(PdfWorkerFailure.Exited) },
            { "unknown-status", nameof(PdfWorkerFailure.Protocol) },
            { "oversized", nameof(PdfWorkerFailure.Protocol) },
            { "truncated", nameof(PdfWorkerFailure.Exited) },
            { "stray-bytes", nameof(PdfWorkerFailure.Protocol) },
            { "not-png", nameof(PdfWorkerFailure.Protocol) },
            { "wrong-size", nameof(PdfWorkerFailure.Protocol) },
            { "extra-chunk", nameof(PdfWorkerFailure.Protocol) },
            { "corrupt-png", nameof(PdfWorkerFailure.Protocol) },
            { "failed", nameof(PdfWorkerFailure.Failed) },
        };

        [Theory]
        [MemberData(nameof(Failures))]
        public async Task A_failing_worker_yields_no_thumbnail_and_leaves_the_host_serving(string mode, string failure)
        {
            using var harness = new ProcessorHarness(pdf: TestWorkers.StandIn(mode), pdfConcurrency: Slots);

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf));

            // Section: counted as a file that could not be rendered, and logged as the worker's failure.
            Assert.Equal([ImageRejectedException.DecodeFailedReason], harness.FailureReasons);
            FakeLogRecord warning = Assert.Single(harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobPdfWorkerFailed");
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(failure, warning.GetStructuredStateValue("Failure"));
            Assert.DoesNotContain('\n', warning.Message);

            // Section: the slot is back, no worker survives, and the host serves the next call.
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == Slots && harness.Pdf.LiveWorkers == 0);
            Assert.NotNull(await harness.ThumbnailAsync(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 32), BlobContentTypes.Png));
        }

        [Fact]
        public async Task A_worker_whose_answer_is_a_valid_png_of_the_right_size_is_trusted_only_that_far()
        {
            // The stand-in answers with a blank square page; the processor re-encodes it like any
            // page, so whatever a compromised worker draws, it is a thumbnail-sized image and no more.
            using var harness = new ProcessorHarness(pdf: TestWorkers.StandIn("good"));

            byte[]? thumbnail = await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf, size: 64, format: ImageFormat.Png);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal(SKEncodedImageFormat.Png, output.Format);
            Assert.Equal((64, 64), (output.Width, output.Height));
            Assert.Empty(harness.FailureReasons);
        }

        [Fact]
        public async Task A_worker_that_fails_itself_reports_its_detail_as_one_safe_line()
        {
            using var harness = new ProcessorHarness(pdf: TestWorkers.StandIn("failed"));

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf));

            FakeLogRecord warning = Assert.Single(harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobPdfWorkerFailed");
            Assert.Contains("OutOfMemoryException: the test worker ran out?of memory", warning.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_worker_that_cannot_start_yields_no_thumbnail()
        {
            using var harness = new ProcessorHarness(
                pdf: new PdfWorkerClient(new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing.dll")), SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit),
                pdfConcurrency: Slots);

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf));

            FakeLogRecord warning = Assert.Single(harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobPdfWorkerFailed");
            Assert.Equal(nameof(PdfWorkerFailure.Start), warning.GetStructuredStateValue("Failure"));
            Assert.Equal(Slots, harness.Processor.FreePdfSlots);
        }

        [Fact]
        public async Task Concurrent_pdf_thumbnails_run_in_separate_workers_bounded_by_the_slots()
        {
            // Three thumbnails on two PDF worker slots: at most two workers ever run at once, and all
            // three render.
            using var harness = new ProcessorHarness(pdfConcurrency: Slots);
            int most = 0;
            harness.Pdf.Ready += _ => InterlockedMax(ref most, harness.Pdf.LiveWorkers);

            byte[]?[] thumbnails = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf)));

            Assert.All(thumbnails, Assert.NotNull);
            Assert.InRange(most, 1, Slots);
            Assert.Equal(Slots, harness.Processor.FreePdfSlots);
        }

        /// <summary>Raises <paramref name="target" /> to <paramref name="value" /> when it is larger.</summary>
        /// <param name="target">The running maximum.</param>
        /// <param name="value">The candidate.</param>
        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
