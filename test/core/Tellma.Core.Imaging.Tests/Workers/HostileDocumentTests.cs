// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     Documents that expand inside PDFium's page load, where no cap of the walk can act: nested
    ///     forms of a kilobyte or two that grow to hundreds of thousands or millions of objects. In
    ///     the worker, the operating system's memory limit or the budget's kill stops each, within
    ///     the budget and a little more, and the host's own memory does not move. The suite runs
    ///     alone, so the costs it reports are the documents', not a queue behind other renders.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class HostileDocumentTests
    {
        // The platform's thumbnail budget.
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

        // The budget, plus the time a loaded machine may take to start the worker and to see it gone.
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

        [Theory]
        [InlineData(6)]
        [InlineData(8)]
        public async Task A_fan_out_of_forms_is_stopped_in_its_worker_by_the_memory_limit_or_the_budget_and_the_host_stays_small(int depth)
        {
            // FormFanOut(6, 10) is about 1.4 KB and expands to some 210,000 objects; each level
            // more multiplies that by ten.
            using var harness = new ProcessorHarness();
            byte[] pdf = DocumentFixtures.FormFanOut(depth, breadth: 10);
            long before = HostMemory();
            var clock = Stopwatch.StartNew();

            using var stream = new MemoryStream(pdf, writable: false);
            byte[]? thumbnail = await harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(256), Budget, TestContext.Current.CancellationToken);
            TimeSpan answered = clock.Elapsed;
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 4 && harness.Pdf.LiveWorkers == 0, Bound);
            TimeSpan gone = clock.Elapsed;
            long growth = HostMemory() - before;

            // Section: stopped, not rendered and not refused by the walk: the expansion happens before
            // the walk can count, so it is the limit or the kill that ends it.
            FakeLogRecord? worker = harness.Logger.Collector.GetSnapshot().SingleOrDefault(static r => r.Id.Name == "BlobPdfWorkerFailed");
            string outcome = worker is not null
                ? $"stopped by a limit: {worker.Message}"
                : $"killed at the budget ({string.Join(", ", harness.FailureReasons)})";
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"FormFanOut({depth}, 10), {pdf.Length} bytes: answered after {answered}, worker gone after {gone}, {outcome}; host memory moved {growth / 1024} KiB.");
            Assert.Null(thumbnail);
            Assert.True(worker is not null || harness.FailureReasons.SequenceEqual([BlobTelemetryTagValues.ImageTimeoutReason]), outcome);
            Assert.True(answered < Bound, $"The call took {answered}.");
            Assert.True(gone < Bound, $"The worker lived {gone}.");

            // Section: the host holds the 1-2 KB document and a few buffers, nothing PDFium made.
            Assert.True(growth < 64L * 1024 * 1024, $"The host's memory grew by {growth / (1024 * 1024)} MiB.");
        }

        [Fact]
        public void A_fan_out_of_forms_never_takes_more_than_the_workers_memory_limit()
        {
            // The worker's own memory, sampled while it expands a document that would take
            // gigabytes: it grows to its limit and no further. On Linux a worker dies with the
            // thread that started it, so this test keeps its thread: it sleeps and awaits nothing.
            const long limit = SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit;
            var launcher = new PdfWorkerLauncher();
            using WorkerProcess worker = launcher.Start(limit);
            _ = WorkerProtocol.ReadReady(worker.Output);
            long baseline = CommittedMemory(worker.Id);
            worker.ApplyLimits(new WorkerLimits(limit, TimeSpan.FromMinutes(5)));
            WorkerProtocol.WriteRequest(worker.Input, 64, DocumentFixtures.FormFanOut(depth: 8, breadth: 10));

            long peak = baseline;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < Budget && worker.ExitCode is null)
            {
                peak = Math.Max(peak, CommittedMemory(worker.Id));
                Thread.Sleep(25);
            }

            bool running = worker.ExitCode is null;
            worker.Kill();
            long growth = peak - baseline;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Committed memory grew by {growth / (1024 * 1024)} MiB under a {limit / (1024 * 1024)} MiB limit; after {clock.Elapsed} the worker {(running ? "was still running" : $"had exited with code {worker.ExitCode}")}.");
            Assert.InRange(growth, limit / 2, limit + (16L * 1024 * 1024));
        }

        [Fact]
        public async Task With_memory_to_spare_the_walk_refuses_a_fan_out_at_its_object_cap()
        {
            // Under a generous limit the expansion completes, and the walk then counts past its cap
            // and refuses the page before anything is drawn.
            using var harness = new ProcessorHarness(pdf: TestWorkers.Real(1024L * 1024 * 1024));

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.FormFanOut(depth: 6, breadth: 10), BlobContentTypes.Pdf));

            Assert.Equal([ImageRejectedException.DecodeFailedReason], harness.FailureReasons);
            FakeLogRecord failed = Assert.Single(harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobImageFailed");
            Assert.Contains("objects", failed.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A process's memory as its limit counts it: committed private memory on Windows, private
        ///     writable memory (<c>VmData</c>) on Linux; zero once it is gone.
        /// </summary>
        /// <param name="id">The process.</param>
        /// <returns>The memory, in bytes.</returns>
        private static long CommittedMemory(int id)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var process = Process.GetProcessById(id);
                    return process.PrivateMemorySize64;
                }

                string line = File.ReadLines(string.Create(CultureInfo.InvariantCulture, $"/proc/{id}/status")).First(static l => l.StartsWith("VmData:", StringComparison.Ordinal));
                return long.Parse(line["VmData:".Length..].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
            {
                return 0;
            }
        }

        /// <summary>The host's committed private memory after a full collection.</summary>
        /// <returns>The memory, in bytes.</returns>
        private static long HostMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }
    }
}
