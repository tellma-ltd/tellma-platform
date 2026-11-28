// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     Thumbnails of stored files that were not normalised: images under the default pixel cap,
    ///     a PDF's first page, and the files that yield no thumbnail and no rejection, a PDF on a
    ///     host that renders no PDF thumbnails among them.
    /// </summary>
    public sealed class ThumbnailTests : IDisposable
    {
        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        public static TheoryData<SKEncodedImageFormat, string> Images => new()
        {
            { SKEncodedImageFormat.Jpeg, BlobContentTypes.Jpeg },
            { SKEncodedImageFormat.Png, BlobContentTypes.Png },
            { SKEncodedImageFormat.Gif, BlobContentTypes.Gif },
            { SKEncodedImageFormat.Webp, BlobContentTypes.WebP },
        };

        [Theory]
        [MemberData(nameof(Images))]
        public async Task An_image_is_fitted_within_the_thumbnail_square(SKEncodedImageFormat format, string contentType)
        {
            byte[] input = ImageFixtures.QuadrantImage(format, 1024, 512);

            byte[]? thumbnail = await _harness.ThumbnailAsync(input, contentType, size: 256);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal(SKEncodedImageFormat.Webp, output.Format);
            Assert.Equal((256, 128), (output.Width, output.Height));
        }

        [Fact]
        public async Task An_image_thumbnail_is_oriented_and_carries_no_metadata()
        {
            using SKBitmap bitmap = ImageFixtures.Quadrants(64, 32);
            byte[] input = ImageFixtures.WithExif(ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Jpeg), 6, "TellmaSecretCamera");

            byte[]? thumbnail = await _harness.ThumbnailAsync(input, BlobContentTypes.Jpeg, size: 256, format: ImageFormat.Png);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal((32, 64), (output.Width, output.Height));
            Processing.ProcessTests.AssertQuadrants(output, [SKColors.Blue, SKColors.Red, SKColors.Yellow, SKColors.Lime]);
            Assert.False(ImageInspector.ContainsAscii(thumbnail!, "TellmaSecretCamera"));
        }

        [Fact]
        public async Task A_small_image_is_not_upscaled()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 40, 20);

            byte[]? thumbnail = await _harness.ThumbnailAsync(input, BlobContentTypes.Png, size: 256);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal((40, 20), (output.Width, output.Height));
        }

        [Fact]
        public async Task An_image_over_the_default_pixel_cap_yields_no_thumbnail()
        {
            byte[] input = ImageFixtures.DeclaringSize(SKEncodedImageFormat.Png, 10_000, 6_000);

            Assert.Null(await _harness.ThumbnailAsync(input, BlobContentTypes.Png));
            Assert.Equal([ImageRejectedException.TooManyPixelsReason], _harness.FailureReasons);
        }

        [Theory]
        [InlineData(200, 100, 256, 128)]
        [InlineData(100, 200, 128, 256)]
        [InlineData(612, 792, 198, 256)]
        [InlineData(50, 50, 256, 256)]
        public async Task A_pdf_renders_page_one_fitted_to_the_thumbnail_size(int pageWidth, int pageHeight, int width, int height)
        {
            byte[] input = DocumentFixtures.HalfBlackPage(pageWidth, pageHeight);

            byte[]? thumbnail = await _harness.ThumbnailAsync(input, BlobContentTypes.Pdf, size: 256);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal(SKEncodedImageFormat.Webp, output.Format);
            Assert.Equal((width, height), (output.Width, output.Height));

            // The page's left half is filled black; its right half is the white background.
            Assert.True(ImageInspector.Near(SKColors.Black, output.At(width / 4, height / 2)), $"Found {output.At(width / 4, height / 2)}.");
            Assert.True(ImageInspector.Near(SKColors.White, output.At(width * 3 / 4, height / 2)), $"Found {output.At(width * 3 / 4, height / 2)}.");
        }

        [Theory]
        [InlineData(ImageFormat.Jpeg, SKEncodedImageFormat.Jpeg)]
        [InlineData(ImageFormat.Png, SKEncodedImageFormat.Png)]
        public async Task A_pdf_thumbnail_takes_the_policy_format(ImageFormat format, SKEncodedImageFormat encoded)
        {
            byte[] input = DocumentFixtures.HalfBlackPage(200, 100);

            byte[]? thumbnail = await _harness.ThumbnailAsync(input, BlobContentTypes.Pdf, size: 96, format: format);

            Assert.Equal(encoded, ImageInspector.Decode(thumbnail!).Format);
        }

        [Fact]
        public async Task An_encrypted_pdf_yields_no_thumbnail()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.PasswordProtected(), BlobContentTypes.Pdf));
            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
        }

        [Fact]
        public async Task An_unparsable_pdf_yields_no_thumbnail()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.Garbage(), BlobContentTypes.Pdf));
            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
        }

        [Fact]
        public async Task A_pdf_without_pages_yields_no_thumbnail()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.NoPages(), BlobContentTypes.Pdf));
        }

        [Fact]
        public async Task A_truncated_pdf_yields_no_thumbnail()
        {
            byte[] pdf = DocumentFixtures.HalfBlackPage(200, 100);

            Assert.Null(await _harness.ThumbnailAsync(pdf[..(pdf.Length / 3)], BlobContentTypes.Pdf));
        }

        [Theory]
        [InlineData(BlobContentTypes.Docx)]
        [InlineData(BlobContentTypes.Zip)]
        public async Task An_ooxml_file_or_zip_yields_no_thumbnail_and_no_failure(string contentType)
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.WordDocument(), contentType));
            Assert.Empty(_harness.FailureReasons);
            Assert.Empty(_harness.Duration.GetMeasurementSnapshot());
        }

        [Theory]
        [InlineData(BlobContentTypes.Text)]
        [InlineData(BlobContentTypes.Csv)]
        [InlineData(BlobContentTypes.Json)]
        public async Task A_text_file_yields_no_thumbnail_and_no_failure(string contentType)
        {
            Assert.Null(await _harness.ThumbnailAsync("a,b\n1,2\n"u8.ToArray(), contentType));
            Assert.Empty(_harness.FailureReasons);
        }

        [Theory]
        [InlineData("DisabledOutsideDevelopment")]
        [InlineData("DisabledOnMusl")]
        public async Task A_pdf_on_a_host_that_renders_no_pdf_thumbnails_yields_none_starts_no_worker_and_counts_no_failure(string support)
        {
            // The workers' assembly is missing: starting one would be counted and logged as a failure.
            using var workers = new PdfWorkerClient(
                new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing", "Tellma.Core.Imaging.PdfWorker.dll")),
                SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);
            using var harness = new ProcessorHarness(pdf: workers, pdfThumbnails: Enum.Parse<PdfThumbnailSupport>(support));

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(200, 100), BlobContentTypes.Pdf));

            Assert.Empty(harness.FailureReasons);
            Assert.Empty(harness.EventNames);
            Assert.Empty(harness.Duration.GetMeasurementSnapshot());

            // Images are untouched by it.
            Assert.NotNull(await harness.ThumbnailAsync(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 16, 16), BlobContentTypes.Png));
        }

        [Fact]
        public async Task A_missing_worker_on_a_host_that_renders_pdf_thumbnails_is_a_counted_failure()
        {
            // The same missing assembly, where workers do start: the control for the test above.
            using var workers = new PdfWorkerClient(
                new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing", "Tellma.Core.Imaging.PdfWorker.dll")),
                SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);
            using var harness = new ProcessorHarness(pdf: workers);

            Assert.Null(await harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(200, 100), BlobContentTypes.Pdf));

            Assert.Equal([BlobTelemetryTagValues.ImageDecodeFailedReason], harness.FailureReasons);
            Assert.Contains("BlobPdfWorkerFailed", harness.EventNames);
        }

        [Fact]
        public async Task Bytes_that_do_not_match_the_stored_type_yield_no_thumbnail_without_native_code()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 16, 16);

            Assert.Null(await _harness.ThumbnailAsync(input, BlobContentTypes.Jpeg));
            Assert.Null(await _harness.ThumbnailAsync(input, BlobContentTypes.Pdf));
            Assert.Equal([ImageRejectedException.FormatReason, ImageRejectedException.FormatReason], _harness.FailureReasons);
            Assert.DoesNotContain("BlobImageRender", _harness.EventNames);
        }

        [Fact]
        public async Task The_stored_type_is_matched_ignoring_case_and_parameters()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 16, 16);

            Assert.NotNull(await _harness.ThumbnailAsync(input, " Image/PNG; charset=binary"));
        }

        [Fact]
        public async Task The_input_is_read_from_its_current_position()
        {
            byte[] image = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 16);
            using var stream = new MemoryStream([.. new byte[100], .. image], writable: false);
            stream.Position = 100;

            byte[]? thumbnail = await _harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Png, new ThumbnailPolicy(256), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.Equal((32, 16), (ImageInspector.Decode(thumbnail!).Width, ImageInspector.Decode(thumbnail!).Height));
        }

        [Fact]
        public async Task The_input_stream_is_left_open()
        {
            byte[] image = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 16);
            using var stream = new MemoryStream(image, writable: false);

            await _harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, stream, BlobContentTypes.Png, new ThumbnailPolicy(256), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            using var pdf = new MemoryStream(DocumentFixtures.HalfBlackPage(100, 100), writable: false);
            await _harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, pdf, BlobContentTypes.Pdf, new ThumbnailPolicy(64), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.True(stream.CanRead);
            Assert.True(pdf.CanRead);
        }

        [Fact]
        public async Task Arguments_are_validated()
        {
            SkiaImageProcessor processor = _harness.Processor;
            CancellationToken token = TestContext.Current.CancellationToken;
            using var stream = new MemoryStream([1, 2, 3]);
            var policy = new ThumbnailPolicy(64);
            var timeout = TimeSpan.FromSeconds(1);

            await Assert.ThrowsAsync<ArgumentException>(() => processor.ThumbnailAsync(" ", stream, BlobContentTypes.Png, policy, timeout, token));
            await Assert.ThrowsAsync<ArgumentNullException>(() => processor.ThumbnailAsync("k", null!, BlobContentTypes.Png, policy, timeout, token));
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ThumbnailAsync("k", stream, " ", policy, timeout, token));
            await Assert.ThrowsAsync<ArgumentNullException>(() => processor.ThumbnailAsync("k", stream, BlobContentTypes.Png, null!, timeout, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ThumbnailAsync("k", stream, BlobContentTypes.Png, new ThumbnailPolicy(0), timeout, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ThumbnailAsync("k", stream, BlobContentTypes.Png, policy, TimeSpan.Zero, token));
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ThumbnailAsync("k", new NonSeekableStream(), BlobContentTypes.Png, policy, timeout, token));
        }

        /// <summary>A readable stream that cannot seek.</summary>
        private sealed class NonSeekableStream : MemoryStream
        {
            /// <inheritdoc />
            public override bool CanSeek => false;
        }
    }
}
