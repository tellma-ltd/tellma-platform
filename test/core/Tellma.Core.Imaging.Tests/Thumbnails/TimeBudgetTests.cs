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
    ///     The thumbnail time budget, on a fake clock: a render past it yields null at once, a PDF
    ///     render keeps the process-wide gate until its native call returns, a render still waiting
    ///     for the gate never starts, and a decode in flight stops reading the input.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class TimeBudgetTests
    {
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

        // How long a step that should be immediate may take on a loaded machine, in real time.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        [Fact]
        public async Task A_render_still_waiting_for_the_gate_when_the_budget_runs_out_yields_null_and_never_starts()
        {
            var time = new FakeTimeProvider();
            var rasterizer = new BlockingRasterizer(block: false);
            using var harness = new ProcessorHarness(time, rasterizer);
            await SkiaImageProcessor.PdfRenderGate.WaitAsync(TestContext.Current.CancellationToken);
            Task<byte[]?> thumbnail;
            try
            {
                thumbnail = ThumbnailAsync(harness, DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf);
                Assert.False(thumbnail.IsCompleted);

                time.Advance(Budget);

                Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            }
            finally
            {
                SkiaImageProcessor.PdfRenderGate.Release();
            }

            Assert.Equal(0, rasterizer.Calls);
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.Equal(1, SkiaImageProcessor.PdfRenderGate.CurrentCount);
        }

        [Fact]
        public async Task A_render_past_the_budget_yields_null_at_once_and_keeps_the_gate_until_the_native_call_returns()
        {
            var time = new FakeTimeProvider();
            using var rasterizer = new BlockingRasterizer(block: true);
            using var harness = new ProcessorHarness(time, rasterizer);

            Task<byte[]?> thumbnail = ThumbnailAsync(harness, DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf);
            Assert.True(rasterizer.Entered.Wait(Patience, TestContext.Current.CancellationToken));

            time.Advance(Budget);

            // The call gives up while the "native" render is still running and holding the gate.
            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal(0, SkiaImageProcessor.PdfRenderGate.CurrentCount);
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);
            Assert.Equal(BlobTelemetryTagValues.ImageThumbnail, harness.Duration.GetMeasurementSnapshot().Single().Tags[BlobTelemetryNames.OperationTag]);

            // When it returns, the gate is free for the next render.
            rasterizer.Release.Set();
            await WaitUntilAsync(static () => SkiaImageProcessor.PdfRenderGate.CurrentCount == 1);
        }

        [Fact]
        public async Task A_render_within_the_budget_returns_its_thumbnail()
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time);

            byte[]? thumbnail = await ThumbnailAsync(harness, DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf);

            Assert.NotNull(thumbnail);
            Assert.Empty(harness.FailureReasons);
        }

        [Fact]
        public async Task An_infinite_budget_waits_for_the_gate()
        {
            using var harness = new ProcessorHarness();
            using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            await SkiaImageProcessor.PdfRenderGate.WaitAsync(TestContext.Current.CancellationToken);
            Task<byte[]?> thumbnail;
            try
            {
                thumbnail = harness.Processor.ThumbnailAsync(
                    ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);
                await Task.Delay(200, TestContext.Current.CancellationToken);
                Assert.False(thumbnail.IsCompleted);
            }
            finally
            {
                SkiaImageProcessor.PdfRenderGate.Release();
            }

            Assert.NotNull(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task The_callers_cancellation_surfaces_as_a_cancellation_not_as_no_thumbnail()
        {
            using var harness = new ProcessorHarness(new FakeTimeProvider());
            using var cancellation = new CancellationTokenSource();
            await SkiaImageProcessor.PdfRenderGate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                using var stream = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
                Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                    ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), Budget, cancellation.Token);

                await cancellation.CancelAsync();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            }
            finally
            {
                SkiaImageProcessor.PdfRenderGate.Release();
            }

            Assert.Empty(harness.FailureReasons);
        }

        [Fact]
        public async Task An_image_decode_in_flight_stops_reading_the_input_once_the_budget_runs_out()
        {
            var time = new FakeTimeProvider();
            using var harness = new ProcessorHarness(time);

            // A large noise PNG whose data the decoder must pull in many reads; the stream blocks
            // the first read past its first 16 KiB, so the decode is in flight when the budget ends.
            byte[] png = ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png);
            using var input = new BlockingStream(png, blockAfter: 16 * 1024);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, input, BlobContentTypes.Png, new ThumbnailPolicy(64), Budget, TestContext.Current.CancellationToken);
            Assert.True(input.Blocked.Wait(Patience, TestContext.Current.CancellationToken));

            time.Advance(Budget);

            Assert.Null(await thumbnail.WaitAsync(Patience, TestContext.Current.CancellationToken));
            Assert.Equal([BlobTelemetryTagValues.ImageTimeoutReason], harness.FailureReasons);

            // Once the call has returned, the read in flight completes and no new one starts.
            int started = input.ReadsStarted;
            input.Release.Set();
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Equal(started, input.ReadsStarted);
        }

        /// <summary>Starts a thumbnail of <paramref name="input" /> under the five-second budget.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="input">The stored bytes.</param>
        /// <param name="contentType">The stored type.</param>
        /// <returns>The call.</returns>
        private static Task<byte[]?> ThumbnailAsync(ProcessorHarness harness, byte[] input, string contentType)
        {
            var stream = new MemoryStream(input, writable: false);
            return harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, contentType, new ThumbnailPolicy(64), Budget, TestContext.Current.CancellationToken);
        }

        /// <summary>Polls until <paramref name="condition" /> holds, failing after the patience runs out.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Patience;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>A stand-in for PDFium that blocks inside its render until released.</summary>
        /// <param name="block">Whether a render blocks.</param>
        private sealed class BlockingRasterizer(bool block) : IPdfRasterizer, IDisposable
        {
            private int _calls;

            /// <summary>Set when a render has started.</summary>
            public ManualResetEventSlim Entered { get; } = new();

            /// <summary>Set by the suite to let a blocked render return.</summary>
            public ManualResetEventSlim Release { get; } = new();

            /// <summary>How many renders started.</summary>
            public int Calls => Volatile.Read(ref _calls);

            /// <inheritdoc />
            public SKBitmap RenderFirstPage(GatedReadStream input, int size)
            {
                Interlocked.Increment(ref _calls);
                Entered.Set();
                if (block)
                {
                    Release.Wait(Patience);
                }

                var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                bitmap.Erase(SKColors.White);
                return bitmap;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                Release.Set();
                Entered.Dispose();
                Release.Dispose();
            }
        }

        /// <summary>A seekable stream whose first read past a threshold blocks until released.</summary>
        private sealed class BlockingStream(byte[] bytes, int blockAfter) : MemoryStream(bytes, writable: false)
        {
            private int _readsStarted;
            private int _blockedOnce;

            /// <summary>Set when a read is blocked.</summary>
            public ManualResetEventSlim Blocked { get; } = new();

            /// <summary>Set by the suite to let the blocked read finish.</summary>
            public ManualResetEventSlim Release { get; } = new();

            /// <summary>How many reads started.</summary>
            public int ReadsStarted => Volatile.Read(ref _readsStarted);

            // Only the array overload is overridden: a derived memory stream's span overload already
            // routes through it, and overriding both would make each call the other.

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                Interlocked.Increment(ref _readsStarted);
                if (Position >= blockAfter && Interlocked.Exchange(ref _blockedOnce, 1) == 0)
                {
                    Blocked.Set();
                    Release.Wait(Patience);
                }

                return base.Read(buffer, offset, count);
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Release.Set();
                    Blocked.Dispose();
                    Release.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
