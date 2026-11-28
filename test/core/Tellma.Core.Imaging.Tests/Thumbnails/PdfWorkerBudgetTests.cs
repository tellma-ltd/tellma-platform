// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The budget and the slots of a PDF thumbnail, on a fake clock: a worker past the budget is
    ///     killed and the call returns null at once, while its PDF worker slot comes back only once
    ///     the worker's process is gone, a crashed worker that Windows Error Reporting still holds
    ///     included; a PDF that cannot get a slot in time starts no worker; image
    ///     work never waits behind PDF workers; every worker has a CPU limit; and the caller's
    ///     cancellation kills the worker too.
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
            using var harness = new ProcessorHarness(time, TestWorkers.StandIn(mode), pdfConcurrency: 2);
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;
            Assert.Equal(1, harness.Processor.FreePdfSlots);
            Assert.Equal(4, harness.Processor.FreeImageSlots);

            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.DoesNotContain("BlobPdfWorkerFailed", harness.EventNames);

            // The slot comes back with the worker's death, and never before it.
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 2, Patience);
            Assert.True(IsGone(id), "The worker outlived its slot.");
            Assert.Equal(0, harness.Pdf.LiveWorkers);
        }

        [Fact]
        public async Task A_crashed_worker_that_windows_error_reporting_still_holds_is_killed_at_the_budget()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows hands a crashed process to its error reporting before the process ends.");
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time, TestWorkers.StandIn("fail-fast-after-request"), pdfConcurrency: 1);
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;

            // Section: the stand-in fails fast once it has the request. Windows Error Reporting then
            // keeps the dying process, its pipes still open, until its report is written: under a
            // second on one machine, half a minute on another. A moment of real time lets the crash
            // begin; the call then ends at the worker's exit or at the budget, whichever is first.
            _ = await Task.WhenAny(thumbnail, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Contains(
                Assert.Single(harness.FailureReasons),
                (string?[])[BlobTelemetryTagValues.ImageTimeoutReason, BlobTelemetryTagValues.ImageDecodeFailedReason]);

            // Section: either way the worker is gone, and its slot is back.
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 1 && harness.Pdf.LiveWorkers == 0, Patience);
            Assert.True(IsGone(id), "The worker outlived its slot.");
        }

        [Fact]
        public async Task A_worker_that_never_becomes_ready_is_killed_at_the_budget()
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time, TestWorkers.StandIn("hang-before-ready"), pdfConcurrency: 2);

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            await TestWorkers.WaitUntilAsync(() => harness.Pdf.LiveWorkers == 1, Patience);
            int id = harness.Pdf.LiveWorkerIds.Single();

            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 2 && harness.Pdf.LiveWorkers == 0, Patience);
            Assert.True(IsGone(id));
        }

        [Fact]
        public async Task A_pdf_still_waiting_for_a_slot_when_the_budget_runs_out_yields_null_and_starts_no_worker()
        {
            // The one PDF worker slot is held by a PDF whose bytes are still being read.
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time, pdfConcurrency: 1);
            using var blocker = new BlockingStream(DocumentFixtures.HalfBlackPage(100, 100), blockAfter: 64);
            Task<byte[]?> holder = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, blocker, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(blocker.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            Task<byte[]?> thumbnail = PdfThumbnailAsync(harness);
            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.Single(harness.EventNames, static name => name == "BlobImageRender");
            Assert.Equal(0, harness.Pdf.LiveWorkers);

            blocker.Release.Set();
            Assert.NotNull(await holder.WaitAsync(Patience, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task An_infinite_budget_waits_for_a_slot_and_then_renders()
        {
            using var harness = new ProcessorHarness(pdfConcurrency: 1);
            using var blocker = new BlockingStream(DocumentFixtures.HalfBlackPage(100, 100), blockAfter: 64);
            Task<byte[]?> holder = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, blocker, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(blocker.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.False(thumbnail.IsCompleted);
            Assert.Equal(0, harness.Pdf.LiveWorkers);

            blocker.Release.Set();
            Assert.NotNull(await holder.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.NotNull(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Image_work_never_waits_behind_pdf_workers()
        {
            // Every PDF worker slot is held by a worker that never answers; an upload and an image
            // thumbnail still run at once, on their own slots.
            using var harness = new ProcessorHarness(new FakeTimeProvider(), TestWorkers.StandIn("hang-after-request"), concurrency: 1, pdfConcurrency: 1);
            var ready = new TaskCompletionSource<WorkerProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Pdf.Ready += worker => ready.TrySetResult(worker);
            using var cancellation = new CancellationTokenSource();
            using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            Task<byte[]?> pdf = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, cancellation.Token);
            int id = (await ready.Task.WaitAsync(Patience, TestContext.Current.CancellationToken)).Id;
            Assert.Equal(0, harness.Processor.FreePdfSlots);

            byte[] png = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 32);
            ProcessedImage upload = await harness.ProcessAsync(png, BlobKindPolicy.Photo.Image!).WaitAsync(Patience, TestContext.Current.CancellationToken);
            byte[]? thumbnail = await harness.ThumbnailAsync(png, BlobContentTypes.Png).WaitAsync(Patience, TestContext.Current.CancellationToken);

            Assert.NotEmpty(upload.Primary);
            Assert.NotNull(thumbnail);
            Assert.False(pdf.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pdf.WaitAsync(Patience, TestContext.Current.CancellationToken));
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 1 && IsGone(id), Patience);
        }

        [Theory]
        [InlineData(5, 6)]
        [InlineData(58, 59)]
        [InlineData(59, 60)]
        [InlineData(60, 60)]
        [InlineData(600, 60)]
        public void A_worker_always_has_a_cpu_limit_of_its_budget_and_a_second_at_most_a_minute(int budgetSeconds, int limitSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(limitSeconds), SkiaImageProcessor.CpuLimit(TimeSpan.FromSeconds(budgetSeconds)));
        }

        [Fact]
        public void A_worker_of_an_infinite_budget_still_has_a_cpu_limit_of_a_minute()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), SkiaImageProcessor.CpuLimit(Timeout.InfiniteTimeSpan));
        }

        [Fact]
        public async Task A_render_within_the_budget_returns_its_thumbnail()
        {
            using var harness = new ProcessorHarness(new FakeTimeProvider());

            Assert.NotNull(await PdfThumbnailAsync(harness));
            Assert.Empty(harness.FailureReasons);
            Assert.Equal(4, harness.Processor.FreePdfSlots);
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
            await TestWorkers.WaitUntilAsync(() => harness.Processor.FreePdfSlots == 4 && harness.Pdf.LiveWorkers == 0, Patience);
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
            // A zombie has ended and only waits for its parent to collect its exit code. An
            // orphan's new parent may never do that (the first process of a container that waits
            // for no child), and it then stays listed for good.
            if (OperatingSystem.IsLinux() && IsZombie(id))
            {
                return true;
            }

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

        /// <summary>Whether a Linux process has ended and not yet been collected by its parent.</summary>
        /// <param name="id">The process id.</param>
        /// <returns>True for a zombie; false for a running process, and for one no longer listed.</returns>
        private static bool IsZombie(int id)
        {
            try
            {
                // The state follows the command name, which is in parentheses and may hold any text.
                string stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{id}/stat"));
                return stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z');
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
