// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Diagnostics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     What a failure around the native work does to the processor: a thumbnail never fails an
    ///     upload, the known fan-out of forms is refused by the walk, and a failure
    ///     between taking an image slot and starting the work gives the slot back. The suite runs
    ///     alone, so the cost it reports is the preflight's, not a queue behind other renders.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class ResilienceTests
    {
        private const int Slots = 2;

        [Fact]
        public async Task A_rasterizer_that_throws_yields_no_thumbnail_rather_than_an_error()
        {
            using var harness = new ProcessorHarness(pdf: new ThrowingRasterizer());

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf));
            Assert.Equal([ImageRejectedException.DecodeFailedReason], harness.FailureReasons);
            Assert.Contains("BlobImageUnexpectedFailure", harness.EventNames);
        }

        [Fact]
        public async Task The_fan_out_of_forms_is_refused_by_the_walk_not_ended_by_a_budget()
        {
            // The walk follows PDFium's load of page 1, which expands every form instance before the
            // object cap can act, and that load cannot be interrupted. This fixture of about 1.4 KB
            // expands to some 210,000 objects: it is refused, but its cost is the residual risk
            // itself. Measured: about 1 s on a Windows workstation, and 3 to 11 s of single-threaded
            // CPU in a Linux container, so its cost is reported rather than asserted against the
            // 5 s thumbnail budget; the refusal, not a budget, must end the call.
            using var harness = new ProcessorHarness();
            using var stream = new MemoryStream(DocumentFixtures.FormFanOut(depth: 6, breadth: 10), writable: false);
            using var process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();

            byte[]? thumbnail = await harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(64), ProcessorHarness.Generous, TestContext.Current.CancellationToken);

            TimeSpan elapsed = clock.Elapsed;
            process.Refresh();
            TimeSpan cpu = process.TotalProcessorTime - cpuBefore;
            TestContext.Current.TestOutputHelper?.WriteLine($"The fan-out preflight took {elapsed} and {cpu} of process CPU time.");
            Assert.Null(thumbnail);
            Assert.Equal([ImageRejectedException.DecodeFailedReason], harness.FailureReasons);
        }

        [Fact]
        public async Task A_failure_before_the_normalisation_starts_gives_its_slot_back()
        {
            SkiaImageProcessor processor = Create(out ServiceProvider services);
            using (services)
            {
                byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 16, 16);

                for (int i = 0; i < Slots + 1; i++)
                {
                    ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(() => processor.ProcessAsync(
                        ProcessorHarness.Kind, input, BlobKindPolicy.Photo.Image!, null, ProcessorHarness.Generous, TestContext.Current.CancellationToken));
                    Assert.Equal(ImageRejectedException.DecodeFailedReason, rejection.Reason);
                }

                Assert.Equal(Slots, processor.FreeImageSlots);
            }
        }

        [Fact]
        public async Task A_failure_before_an_image_thumbnail_starts_gives_its_slot_back()
        {
            SkiaImageProcessor processor = Create(out ServiceProvider services);
            using (services)
            {
                byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 16, 16);

                for (int i = 0; i < Slots + 1; i++)
                {
                    using var stream = new MemoryStream(input, writable: false);
                    Assert.Null(await processor.ThumbnailAsync(
                        ProcessorHarness.Kind, stream, BlobContentTypes.Png, new ThumbnailPolicy(16), ProcessorHarness.Generous, TestContext.Current.CancellationToken));
                }

                Assert.Equal(Slots, processor.FreeImageSlots);
            }
        }

        /// <summary>A processor whose logger throws on the render event, the last step before the work starts.</summary>
        /// <param name="services">The meter factory's container, for the caller to dispose.</param>
        /// <returns>The processor.</returns>
        private static SkiaImageProcessor Create(out ServiceProvider services)
        {
            services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            return new SkiaImageProcessor(
                new ThrowingLogger(),
                services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
                TimeProvider.System,
                new SkiaImageProcessorOptions { ImageConcurrency = Slots },
                PdfiumRasterizer.Instance);
        }

        /// <summary>A rasterizer that fails the way no anticipated PDF failure does.</summary>
        private sealed class ThrowingRasterizer : IPdfRasterizer
        {
            /// <inheritdoc />
            public SKBitmap RenderFirstPage(GatedReadStream input, int size)
            {
                throw new InvalidOperationException("The rasterizer failed unexpectedly.");
            }
        }

        /// <summary>A logger, enabled at every level, that throws when the render event is written.</summary>
        private sealed class ThrowingLogger : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (eventId.Name == "BlobImageRender")
                {
                    throw new InvalidOperationException("The logging provider failed.");
                }
            }
        }
    }
}
