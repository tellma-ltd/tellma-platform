// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The time budgets and slots of image work, on a fake clock: work past its budget returns at
    ///     once while its slot stays held until the native work ends; work that cannot get a slot in
    ///     time never starts; and once a call returns, the caller's stream is never touched again.
    ///     <see cref="PdfWorkerBudgetTests" /> covers the budgets of PDF workers.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class TimeBudgetTests
    {
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

        // How long a step that should be immediate may take on a loaded machine, in real time.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task A_read_in_flight_holds_the_call_until_it_returns_and_the_stream_is_never_touched_after()
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time);

            // A large noise PNG whose data the decoder pulls in many reads; the stream blocks the first
            // read past its first 16 KiB, so a read is inside the caller's stream when the budget ends.
            using var input = new BlockingStream(ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png), blockAfter: 16 * 1024);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, input, BlobContentTypes.Png, new ThumbnailPolicy(64), Budget, TestContext.Current.CancellationToken);
            Assert.True(input.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            // The budget fires on another thread: the cancellation runs the call's continuation
            // inline, and that continuation waits for the read in flight.
            Task advanced = AdvanceElsewhere(time);

            // The call cannot return while a read of the caller's stream is in flight.
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.False(thumbnail.IsCompleted);

            input.Release.Set();
            await advanced.WaitAsync(Patience, TestContext.Current.CancellationToken);
            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            input.MarkReturned();
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);

            // The abandoned decode unwinds; it never reads or repositions the caller's stream again.
            await WaitUntilAsync(() => harness.Processor.FreeImageSlots == 4);
            Assert.Equal(0, input.TouchesAfterReturn);
        }

        [Fact]
        public async Task An_image_thumbnail_waiting_for_a_slot_past_the_budget_yields_null_and_never_starts()
        {
            var time = new FakeTimeProvider();
            using var blocker = new BlockingStream(ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png), blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(time, concurrency: 1, openInput: _ => blocker);
            Task<ProcessedImage> holder = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], BlobKindPolicy.Photo.Image!, null, Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(blocker.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            using var stream = new MemoryStream(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 32), writable: false);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Png, new ThumbnailPolicy(64), Budget, TestContext.Current.CancellationToken);
            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.Single(harness.EventNames, static name => name == "BlobImageRender");

            blocker.Release.Set();
            await holder.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Normalisation_past_its_budget_is_refused_as_timeout_and_keeps_its_slot_until_the_decode_ends()
        {
            var time = new FakeTimeProvider();
            byte[] png = ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png);
            using var input = new BlockingStream(png, blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(time, concurrency: 2, openInput: _ => input);

            Task<ProcessedImage> processed = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, png, BlobKindPolicy.Photo.Image!, null, Budget, TestContext.Current.CancellationToken);
            Assert.True(input.Blocked.Wait(Patience, TestContext.Current.CancellationToken));
            Assert.Equal(1, harness.Processor.FreeImageSlots);

            Task advanced = AdvanceElsewhere(time);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            input.Release.Set();
            await advanced.WaitAsync(Patience, TestContext.Current.CancellationToken);

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => processed.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal(ImageRejectedException.TimeoutReason, rejection.Reason);
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);

            // The slot comes back only when the abandoned decode has really ended.
            await WaitUntilAsync(() => harness.Processor.FreeImageSlots == 2);
        }

        [Fact]
        public async Task An_upload_that_finds_every_slot_busy_for_its_budget_is_refused_as_busy_and_never_starts()
        {
            var time = new FakeTimeProvider();
            byte[] png = ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png);
            using var input = new BlockingStream(png, blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(time, concurrency: 1, openInput: _ => input);

            Task<ProcessedImage> first = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, png, BlobKindPolicy.Photo.Image!, null, Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
            Assert.True(input.Blocked.Wait(Patience, TestContext.Current.CancellationToken));
            Task<ProcessedImage> second = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, png, BlobKindPolicy.Photo.Image!, null, Budget, TestContext.Current.CancellationToken);

            time.Advance(Budget);

            ImageProcessingBusyException busy = await Assert.ThrowsAsync<ImageProcessingBusyException>(
                () => second.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal(Budget, busy.RetryAfter);
            Assert.Single(harness.EventNames, static name => name == "BlobImageRender");
            Assert.Contains("BlobImageBusy", harness.EventNames);
            Assert.Empty(harness.FailureReasons);

            input.Release.Set();
            await first.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task A_cancelled_upload_stops_its_decode_and_counts_no_failure()
        {
            byte[] png = ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png);
            using var input = new BlockingStream(png, blockAfter: 16 * 1024);
            using var harness = new ProcessorHarness(openInput: _ => input);
            using var cancellation = new CancellationTokenSource();

            Task<ProcessedImage> processed = harness.Processor.ProcessAsync(
                ProcessorHarness.Kind, png, BlobKindPolicy.Photo.Image!, null, ProcessorHarness.Generous, cancellation.Token);
            Assert.True(input.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            // The cancellation's callbacks wait for the read in flight, so they finish only once
            // the read is released.
            Task cancelled = cancellation.CancelAsync();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            input.Release.Set();
            await cancelled.WaitAsync(Patience, TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processed.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Empty(harness.FailureReasons);
            await WaitUntilAsync(() => harness.Processor.FreeImageSlots == 4);
        }

        /// <summary>
        ///     Advances the fake clock by the budget on a pool thread: a budget's cancellation runs the
        ///     call's continuation inline, which may wait for a read the suite has yet to release.
        /// </summary>
        /// <param name="time">The clock.</param>
        /// <returns>The advance, complete once every continuation it ran has finished.</returns>
        private static Task AdvanceElsewhere(FakeTimeProvider time)
        {
            return Task.Run(() => time.Advance(Budget), TestContext.Current.CancellationToken);
        }

        /// <summary>Polls until <paramref name="condition" /> holds, failing after the patience runs out.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        private static Task WaitUntilAsync(Func<bool> condition)
        {
            return TestWorkers.WaitUntilAsync(condition, Patience);
        }
    }
}
