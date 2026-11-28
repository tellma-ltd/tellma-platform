// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The worker's renderer, run in this process where a test needs to see inside it: the walk
    ///     refuses before the render is ever called; and a page rendered through a worker is,
    ///     byte for byte, the thumbnail the processor rendered in its own process before PDF
    ///     rendering moved out of it.
    /// </summary>
    /// <remarks>
    ///     PDFium is not thread-safe, so the suites that load it into this process run alone.
    /// </remarks>
    [Collection(SerialSuites.Name)]
    public sealed class PdfRendererTests : IDisposable
    {
        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        public static TheoryData<string, byte[]> RefusedBeforeRendering => new()
        {
            { "a 20000 x 20000 flate image", DocumentFixtures.DeclaringImage(20_000, 20_000, "FlateDecode") },
            { "a 5000 x 5000 JPEG 2000 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JPXDecode") },
            { "a 5000 x 5000 JBIG2 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JBIG2Decode") },
            { "a page wider than 14400 points", DocumentFixtures.HalfBlackPage(20_000, 100) },
            { "a password-protected PDF", DocumentFixtures.PasswordProtected() },
            { "no PDF at all", DocumentFixtures.Garbage() },
        };

        [Theory]
        [MemberData(nameof(RefusedBeforeRendering))]
        public void The_worker_refuses_before_the_render_is_ever_called(string name, byte[] pdf)
        {
            var rasterizer = new RecordingRasterizer();

            Assert.Throws<WorkerFailure>(() => PdfRenderer.Render(pdf, 256, rasterizer));
            Assert.True(rasterizer.Calls == 0, name);
        }

        [Fact]
        public void An_image_within_the_cap_passes_the_walk_and_is_rendered()
        {
            // 4000 x 4000 is 16 MP: within the general cap, so the walk lets it through; the render
            // of its four bytes of data then simply draws nothing.
            var rasterizer = new RecordingRasterizer();

            byte[] png = PdfRenderer.Render(DocumentFixtures.DeclaringImage(4_000, 4_000, "FlateDecode"), 256, rasterizer);

            Assert.Equal(1, rasterizer.Calls);
            Assert.NotEmpty(png);
        }

        [Fact]
        public void The_worker_caps_images_at_the_processors_default_pixel_cap()
        {
            Assert.Equal(ImagePolicy.DefaultMaxInputPixels, PdfPreflight.MaxImagePixels);
        }

        public static TheoryData<string, byte[], int, ImageFormat> Pages => new()
        {
            { "a landscape page", DocumentFixtures.HalfBlackPage(200, 100), 256, ImageFormat.WebP },
            { "a portrait page", DocumentFixtures.HalfBlackPage(100, 200), 256, ImageFormat.WebP },
            { "a letter page", DocumentFixtures.HalfBlackPage(612, 792), 256, ImageFormat.WebP },
            { "a small page scaled up", DocumentFixtures.HalfBlackPage(50, 50), 256, ImageFormat.WebP },
            { "text and shapes as WebP", DocumentFixtures.TextAndShapes(), 256, ImageFormat.WebP },
            { "text and shapes as PNG", DocumentFixtures.TextAndShapes(), 256, ImageFormat.Png },
            { "text and shapes as JPEG", DocumentFixtures.TextAndShapes(), 96, ImageFormat.Jpeg },
            { "page 1 of two", DocumentFixtures.TwoPages(), 96, ImageFormat.Png },
            { "an encrypted page", DocumentFixtures.EncryptedWithEmptyUserPassword(), 256, ImageFormat.WebP },
        };

        [Theory]
        [MemberData(nameof(Pages))]
        public async Task A_page_rendered_in_a_worker_is_the_thumbnail_rendered_in_process_before(string name, byte[] pdf, int size, ImageFormat format)
        {
            // Section: the thumbnail as the processor rendered it in its own process: PDFium's bitmap,
            // encoded directly.
            byte[] before;
            using (SKBitmap bitmap = PdfiumRasterizer.Instance.RenderFirstPage(pdf, size))
            {
                before = SkiaPipeline.EncodeBitmap(bitmap, format);
            }

            // Section: the same document through a worker, its PNG and the host's re-encoding.
            byte[]? now = await _harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf, size, format);

            Assert.True(now is not null, name);
            Assert.Equal(before, now);
        }

        /// <summary>Counts renders and delegates them to PDFium.</summary>
        private sealed class RecordingRasterizer : IPdfRasterizer
        {
            private int _calls;

            /// <summary>How many renders were asked for.</summary>
            public int Calls => Volatile.Read(ref _calls);

            /// <inheritdoc />
            public SKBitmap RenderFirstPage(byte[] pdf, int size)
            {
                Interlocked.Increment(ref _calls);
                return PdfiumRasterizer.Instance.RenderFirstPage(pdf, size);
            }
        }
    }
}
