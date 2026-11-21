// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Processing
{
    /// <summary>
    ///     Uploads that are not what they claim: headers declaring decompression bombs, files of
    ///     other formats, malformed and truncated images. Each is refused with a typed rejection, and
    ///     a refusal of the format reaches no native code.
    /// </summary>
    public sealed class HostileInputTests : IDisposable
    {
        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        public static TheoryData<SKEncodedImageFormat> AcceptedFormats =>
        [
            SKEncodedImageFormat.Jpeg,
            SKEncodedImageFormat.Png,
            SKEncodedImageFormat.Gif,
            SKEncodedImageFormat.Webp,
        ];

        [Theory]
        [MemberData(nameof(AcceptedFormats))]
        public async Task A_header_declaring_60_megapixels_is_refused_before_decoding(SKEncodedImageFormat format)
        {
            // 10000 x 6000 declared over 16 x 16 of data: a decode would fail as truncated, so
            // the too-many-pixels refusal proves the header alone was judged.
            byte[] input = ImageFixtures.DeclaringSize(format, 10_000, 6_000);

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            Assert.Equal(SkiaImageProcessor.RejectedCode, rejection.Code);
            Assert.Equal(ImageRejectedException.TooManyPixelsReason, rejection.Reason);
            Assert.Equal(10_000, rejection.Arguments[SkiaImageProcessor.WidthArgument]);
            Assert.Equal(6_000, rejection.Arguments[SkiaImageProcessor.HeightArgument]);
            Assert.Equal((long)ImagePolicy.DefaultMaxInputPixels, rejection.Arguments[SkiaImageProcessor.MaxPixelsArgument]);
        }

        [Fact]
        public async Task The_policys_own_pixel_cap_is_enforced()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 40, 40);

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, new ImagePolicy(512, ImageFit.Contain, MaxInputPixels: 1_599)));

            Assert.Equal(ImageRejectedException.TooManyPixelsReason, rejection.Reason);
        }

        [Fact]
        public async Task An_image_exactly_at_the_cap_is_accepted()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 40, 40);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(512, ImageFit.Contain, MaxInputPixels: 1_600));

            Assert.Equal((40, 40), (result.Width, result.Height));
        }

        public static TheoryData<string, byte[]> NotImages => new()
        {
            { "empty", [] },
            { "bitmap", [.. "BM"u8, .. new byte[64]] },
            { "icon", [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10, .. new byte[64]] },
            { "text", "hello, world"u8.ToArray() },
            { "html", "<!DOCTYPE html><html></html>"u8.ToArray() },
            { "svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
            { "pdf", DocumentFixtures.HalfBlackPage(100, 100) },
            { "riff-not-webp", [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8, .. new byte[32]] },
        };

        [Theory]
        [MemberData(nameof(NotImages))]
        public async Task A_file_that_is_not_an_accepted_image_is_refused_without_native_code(string name, byte[] input)
        {
            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            Assert.True(rejection.Reason == ImageRejectedException.FormatReason, name);

            // The render event precedes every native decode; its absence shows none was attempted.
            Assert.DoesNotContain("BlobImageRender", _harness.EventNames);
        }

        [Fact]
        public async Task An_image_signature_over_other_bytes_is_refused_as_undecodable()
        {
            byte[] junk = new byte[2048];
            new Random(3).NextBytes(junk);
            byte[] input = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. junk];

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            Assert.Equal(ImageRejectedException.DecodeFailedReason, rejection.Reason);
        }

        [Fact]
        public async Task A_signature_another_codec_decodes_is_refused_as_the_wrong_format()
        {
            // A JPEG's signature over a WebP: neither the signature nor the codec may be trusted alone.
            byte[] webp = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Webp, 16, 16);
            byte[] input = [0xFF, 0xD8, 0xFF, .. webp];

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            Assert.Contains(rejection.Reason, new[] { ImageRejectedException.DecodeFailedReason, ImageRejectedException.FormatReason });
        }

        [Theory]
        [InlineData(0.6)]
        [InlineData(0.1)]
        public async Task A_truncated_jpeg_is_refused_as_undecodable(double fraction)
        {
            byte[] input = ImageFixtures.TruncatedJpeg(fraction);

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            Assert.Equal(ImageRejectedException.DecodeFailedReason, rejection.Reason);
        }

        [Fact]
        public async Task A_truncated_png_is_refused_as_undecodable()
        {
            byte[] png = ImageFixtures.Noise(128, 128, SKEncodedImageFormat.Png);

            ImageRejectedException rejection = await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(png[..(png.Length / 2)], BlobKindPolicy.Photo.Image!));

            Assert.Equal(ImageRejectedException.DecodeFailedReason, rejection.Reason);
        }

        [Fact]
        public async Task A_cancelled_upload_throws_a_cancellation_and_counts_no_failure()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 40, 40);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _harness.Processor.ProcessAsync(ProcessorHarness.Kind, input, BlobKindPolicy.Photo.Image!, null, ProcessorHarness.Generous, cancellation.Token));

            Assert.Empty(_harness.FailureReasons);
        }

        [Fact]
        public async Task Arguments_are_validated()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 8, 8);
            CancellationToken token = TestContext.Current.CancellationToken;
            SkiaImageProcessor processor = _harness.Processor;

            await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync(" ", input, BlobKindPolicy.Photo.Image!, null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentNullException>(() => processor.ProcessAsync("k", null!, BlobKindPolicy.Photo.Image!, null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentNullException>(() => processor.ProcessAsync("k", input, null!, null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync("k", input, new ImagePolicy(0, ImageFit.Contain), null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync("k", input, new ImagePolicy(8, ImageFit.Contain, MaxInputPixels: 0), null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentException>(() => processor.ProcessAsync("k", input, new ImagePolicy(8, (ImageFit)9), null, ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync("k", input, BlobKindPolicy.Photo.Image!, new ThumbnailPolicy(0), ProcessorHarness.Generous, token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => processor.ProcessAsync("k", input, BlobKindPolicy.Photo.Image!, null, TimeSpan.Zero, token));
        }
    }
}
