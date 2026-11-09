// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using System.Diagnostics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The budget of a PDF thumbnail, on a fake clock: a worker past the budget is killed and the
    ///     call returns null at once, while its slot comes back only once the worker's process is
    ///     gone; a PDF that cannot get a slot in time starts no worker; and the caller's cancellation
    ///     kills the worker too.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class PdfWorkerBudgetTests
    {
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

        // How long a step that should be immediate may take on a loaded machine, in real time.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        [Theory]
        [InlineData("hang-after-request")]
        [InlineData("spin")]
        public async Task A_worker_past_the_budget_is_killed_the_call_returns_null_and_its_slot_comes_back_once_it_is_gone(string mode)
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time, TestWorkers.StandIn(mode), concurrency: 2);
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;
            Assert.Equal(1, harness.Processor.FreeImageSlots);

            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.DoesNotContain("BlobPdfWorkerFailed", harness.EventNames);

            // The slot comes back with the worker's death, and never before it.
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreeImageSlots == 2, Patience);
            Assert.True(IsGone(id), "The worker outlived its slot.");
            Assert.Equal(0, harness.Pdf.LiveWorkers);
        }

        [Fact]
        public async Task A_worker_that_never_becomes_ready_is_killed_at_the_budget()
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time, TestWorkers.StandIn("hang-before-ready"), concurrency: 2);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            await TestWorkers.WaitUntilAsync(() => harness.Pdf.LiveWorkers == 1, Patience);
            int id = harness.Pdf.LiveWorkerIds.Single();

            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreeImageSlots == 2 && harness.Pdf.LiveWorkers == 0, Patience);
            Assert.True(IsGone(id));
        }

        [Fact]
        public async Task A_pdf_still_waiting_for_a_slot_when_the_budget_runs_out_yields_null_and_starts_no_worker()
        {
            var time = new FakeTimeProvider();
            using var blocker = new BlockingStream(ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png), blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(time, concurrency: 1, openInput: _ => blocker);
            Task<ProcessedImage> holder = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], BlobKindPolicy.Photo.Image!, null, Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(blocker.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.Single(harness.EventNames, static name => name == "BlobImageRender");
            Assert.Equal(0, harness.Pdf.LiveWorkers);

            blocker.Release.Set();
            await holder.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task An_infinite_budget_waits_for_a_slot_and_then_renders()
        {
            using var blocker = new BlockingStream(ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png), blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(concurrency: 1, openInput: _ => blocker);
            Task<ProcessedImage> holder = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], BlobKindPolicy.Photo.Image!, null, Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(blocker.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.False(thumbnail.IsCompleted);
            Assert.Equal(0, harness.Pdf.LiveWorkers);

            blocker.Release.Set();
            await holder.WaitAsync(Patience, TestContext.Current.CancellationToken);
            Assert.NotNull(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_render_within_the_budget_returns_its_thumbnail()
        {
            using var harness = new ProcessorHarness(new FakeTimeProvider());

            Assert.NotNull(await PdfThumbnailAsync(harness));
            Assert.Empty(harness.FailureReasons);
            Assert.Equal(4, harness.Processor.FreeImageSlots);
        }

        [Fact]
        public async Task The_callers_cancellation_surfaces_as_a_cancellation_and_kills_the_worker()
        {
            using var harness = new ProcessorHarness(new FakeTimeProvider(), TestWorkers.StandIn("hang-after-request"));
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);
            using var cancellation = new CancellationTokenSource();
            using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);

            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Budget, cancellation.Token);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Empty(harness.FailureReasons);
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreeImageSlots == 4 && harness.Pdf.LiveWorkers == 0, Patience);
            Assert.True(IsGone(id));
        }

        [Fact]
        public async Task Disposing_the_processor_kills_its_running_workers()
        {
            using var harness = new ProcessorHarness(new FakeTimeProvider(), TestWorkers.StandIn("hang-after-request"));
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;
            harness.Processor.Dispose();

            // The killed worker is a worker that ended before it answered: no thumbnail.
            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            await TestWorkers.WaitUntilAsync(() => IsGone(id), Patience);
            Assert.Contains("BlobPdfWorkerFailed", harness.EventNames);
        }

        /// <summary>Starts a PDF thumbnail under the five-second budget.</summary>
        /// <param name="harness">The harness.</param>
        /// <returns>The call.</returns>
        private static Task<byte[]?> PdfThumbnailAsync(ProcessorHarness harness)
        {
            var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            return harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Budget, TestContext.Current.CancellationToken);
        }

        /// <summary>Whether a process has exited (or no longer exists).</summary>
        /// <param name="id">The process id.</param>
        /// <returns>True once it is gone.</returns>
        internal static bool IsGone(int id)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }
}
