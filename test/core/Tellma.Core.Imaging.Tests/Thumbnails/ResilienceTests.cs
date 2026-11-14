// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     What a failure between taking an image slot and starting the work does to the processor:
    ///     the slot comes back, for a normalisation, an image thumbnail and a PDF thumbnail alike,
    ///     and no PDF worker is started. Failures of the PDF worker itself are
    ///     <see cref="Workers.WorkerFailureTests" />'s.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class ResilienceTests
    {
        private const int Slots = 2;

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

        [Fact]
        public async Task A_failure_before_a_pdf_thumbnail_starts_gives_its_slot_back_and_starts_no_worker()
        {
            SkiaImageProcessor processor = Create(out ServiceProvider services);
            using (services)
            using (processor)
            {
                byte[] input = DocumentFixtures.HalfBlackPage(100, 100);

                for (int i = 0; i < Slots + 1; i++)
                {
                    using var stream = new MemoryStream(input, writable: false);
                    Assert.Null(await processor.ThumbnailAsync(
                        ProcessorHarness.Kind, stream, BlobContentTypes.Pdf, new ThumbnailPolicy(16), ProcessorHarness.Generous, TestContext.Current.CancellationToken));
                }

                Assert.Equal(Slots, processor.FreeImageSlots);
                Assert.Equal(0, processor.PdfWorkers.LiveWorkers);
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
                new SkiaImageProcessorOptions { ImageConcurrency = Slots });
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
