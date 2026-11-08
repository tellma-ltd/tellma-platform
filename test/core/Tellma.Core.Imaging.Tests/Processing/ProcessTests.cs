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
    ///     Normalisation of honest uploads: every accepted format, the two fits and their
    ///     dimensions, the thumbnail drawn from the normalised image, the output formats and their
    ///     media types, and alpha.
    /// </summary>
    public sealed class ProcessTests : IDisposable
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
        public async Task Each_accepted_format_is_normalised_to_the_policy_format(SKEncodedImageFormat format)
        {
            byte[] input = ImageFixtures.QuadrantImage(format, 64, 32);

            ProcessedImage result = await _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!);

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal(SKEncodedImageFormat.Webp, output.Format);
            Assert.Equal(BlobContentTypes.WebP, result.ContentType);
            Assert.Equal((64, 32), (result.Width, result.Height));
            Assert.Equal((64, 32), (output.Width, output.Height));
        }

        [Theory]
        [InlineData(ImageFormat.WebP, BlobContentTypes.WebP, SKEncodedImageFormat.Webp)]
        [InlineData(ImageFormat.Jpeg, BlobContentTypes.Jpeg, SKEncodedImageFormat.Jpeg)]
        [InlineData(ImageFormat.Png, BlobContentTypes.Png, SKEncodedImageFormat.Png)]
        public async Task The_output_and_its_content_type_follow_the_policy_format(ImageFormat format, string contentType, SKEncodedImageFormat encoded)
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 40, 40);

            ProcessedImage result = await _harness.ProcessAsync(
                input, new ImagePolicy(100, ImageFit.Contain, format), new ThumbnailPolicy(20, format));

            Assert.Equal(contentType, result.ContentType);
            Assert.Equal(encoded, ImageInspector.Decode(result.Primary).Format);
            Assert.Equal(encoded, ImageInspector.Decode(result.Thumbnail!).Format);
        }

        [Theory]
        [InlineData(3000, 1500, 1600, 1600, 800)]
        [InlineData(1500, 3000, 1600, 800, 1600)]
        [InlineData(1601, 1601, 1600, 1600, 1600)]
        [InlineData(300, 200, 1600, 300, 200)]
        public async Task Contain_fits_within_the_maximum_keeping_the_aspect_ratio_without_upscaling(
            int width, int height, int maxDimension, int expectedWidth, int expectedHeight)
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, width, height);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(maxDimension, ImageFit.Contain));

            Assert.Equal((expectedWidth, expectedHeight), (result.Width, result.Height));
            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal((expectedWidth, expectedHeight), (output.Width, output.Height));
        }

        [Theory]
        [InlineData(800, 600, 512, 512)]
        [InlineData(600, 800, 512, 512)]
        [InlineData(300, 200, 512, 200)]
        public async Task CoverSquare_crops_the_centred_square_and_scales_it_down_to_the_maximum(
            int width, int height, int maxDimension, int expectedSide)
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, width, height);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(maxDimension, ImageFit.CoverSquare));

            Assert.Equal((expectedSide, expectedSide), (result.Width, result.Height));
            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal((expectedSide, expectedSide), (output.Width, output.Height));
        }

        [Fact]
        public async Task CoverSquare_keeps_the_centre_of_the_image()
        {
            // A wide image: a red band, a blue centre square, a red band. The crop is all blue.
            using SKBitmap bitmap = ImageFixtures.Solid(300, 100, SKColors.Red);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = SKColors.Blue })
            {
                canvas.DrawRect(SKRect.Create(100, 0, 100, 100), paint);
            }

            byte[] input = ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Png);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(50, ImageFit.CoverSquare, ImageFormat.Png));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.All([output.At(2, 2), output.At(47, 2), output.At(2, 47), output.At(47, 47)], static color =>
                Assert.True(ImageInspector.Near(SKColors.Blue, color, 10), $"Expected blue, found {color}."));
        }

        [Fact]
        public async Task The_avatar_thumbnail_is_a_square_of_its_size()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 1200, 900);
            BlobKindPolicy avatar = BlobKindPolicy.Avatar;

            ProcessedImage result = await _harness.ProcessAsync(input, avatar.Image!, avatar.Thumbnail);

            Assert.Equal((512, 512), (result.Width, result.Height));
            DecodedOutput thumbnail = ImageInspector.Decode(result.Thumbnail!);
            Assert.Equal((96, 96), (thumbnail.Width, thumbnail.Height));
            Assert.Equal(SKEncodedImageFormat.Webp, thumbnail.Format);
        }

        [Fact]
        public async Task A_contain_thumbnail_keeps_the_aspect_ratio_of_the_normalised_image()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 3000, 1500);
            BlobKindPolicy photo = BlobKindPolicy.Photo;

            ProcessedImage result = await _harness.ProcessAsync(input, photo.Image!, photo.Thumbnail);

            Assert.Equal((1600, 800), (result.Width, result.Height));
            DecodedOutput thumbnail = ImageInspector.Decode(result.Thumbnail!);
            Assert.Equal((256, 128), (thumbnail.Width, thumbnail.Height));
        }

        [Fact]
        public async Task A_thumbnail_is_never_larger_than_the_normalised_image()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 60, 40);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(1600, ImageFit.Contain), new ThumbnailPolicy(256));

            DecodedOutput thumbnail = ImageInspector.Decode(result.Thumbnail!);
            Assert.Equal((60, 40), (thumbnail.Width, thumbnail.Height));
        }

        [Fact]
        public async Task No_thumbnail_is_rendered_when_none_is_asked_for()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 60, 40);

            ProcessedImage result = await _harness.ProcessAsync(input, BlobKindPolicy.Signature.Image!, thumbnail: null);

            Assert.Null(result.Thumbnail);
        }

        [Fact]
        public async Task A_reduced_scale_decode_still_draws_the_whole_image()
        {
            // A JPEG decoded at a fraction of its size must still map every quadrant to its place.
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 4000, 2000);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(200, ImageFit.Contain, ImageFormat.Png));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal((200, 100), (output.Width, output.Height));
            AssertQuadrants(output, ImageFixtures.QuadrantColors);
        }

        [Fact]
        public async Task The_signature_preset_keeps_transparency_in_its_png()
        {
            using SKBitmap bitmap = ImageFixtures.HalfTransparent(200, 100);
            byte[] input = ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Png);

            ProcessedImage result = await _harness.ProcessAsync(input, BlobKindPolicy.Signature.Image!);

            Assert.Equal(BlobContentTypes.Png, result.ContentType);
            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal(0, output.At(10, 50).Alpha);
            Assert.Equal(255, output.At(190, 50).Alpha);
            Assert.True(ImageInspector.Near(SKColors.Black, output.At(190, 50), 5));
        }

        [Fact]
        public async Task WebP_output_keeps_transparency()
        {
            using SKBitmap bitmap = ImageFixtures.HalfTransparent(200, 100);
            byte[] input = ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Png);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(200, ImageFit.Contain, ImageFormat.WebP));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal(0, output.At(10, 50).Alpha);
            Assert.Equal(255, output.At(190, 50).Alpha);
        }

        [Theory]
        [InlineData(ImageFormat.WebP)]
        [InlineData(ImageFormat.Png)]
        public async Task An_opaque_input_has_an_opaque_output(ImageFormat format)
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 120, 80);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(100, ImageFit.Contain, format));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.All(output.Pixels, static pixel => Assert.Equal(255, pixel.Alpha));
        }

        [Fact]
        public async Task Jpeg_output_composites_transparency_over_white()
        {
            using SKBitmap bitmap = ImageFixtures.HalfTransparent(200, 100);
            byte[] input = ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Png);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(200, ImageFit.Contain, ImageFormat.Jpeg));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.True(ImageInspector.Near(SKColors.White, output.At(10, 50), 8), $"Expected white, found {output.At(10, 50)}.");
            Assert.True(ImageInspector.Near(SKColors.Black, output.At(190, 50), 8));
        }

        [Fact]
        public async Task An_animated_gif_is_reduced_to_its_first_frame()
        {
            // Frame 0 is red (palette index 0), frame 1 blue (index 1).
            byte[] input = ImageFixtures.Gif(32, 32, [[0], [1]]);
            using (var data = SKData.CreateCopy(input))
            using (var source = SKCodec.Create(data))
            {
                Assert.Equal(2, source.FrameCount);
            }

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(64, ImageFit.Contain, ImageFormat.Png), new ThumbnailPolicy(16));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            // Skia reports a still image as zero or one frames, never more.
            Assert.InRange(output.FrameCount, 0, 1);
            Assert.All(output.Pixels, static pixel => Assert.Equal(SKColors.Red, pixel));
            Assert.InRange(ImageInspector.Decode(result.Thumbnail!).FrameCount, 0, 1);
        }

        /// <summary>Asserts the colour at the centre of each quadrant of an output.</summary>
        /// <param name="output">The decoded output.</param>
        /// <param name="expected">Top-left, top-right, bottom-left and bottom-right colours.</param>
        internal static void AssertQuadrants(DecodedOutput output, SKColor[] expected)
        {
            (int X, int Y)[] centres =
            [
                (output.Width / 4, output.Height / 4),
                (output.Width * 3 / 4, output.Height / 4),
                (output.Width / 4, output.Height * 3 / 4),
                (output.Width * 3 / 4, output.Height * 3 / 4),
            ];

            for (int i = 0; i < centres.Length; i++)
            {
                SKColor actual = output.At(centres[i].X, centres[i].Y);
                Assert.True(ImageInspector.Near(expected[i], actual), $"Quadrant {i}: expected {expected[i]}, found {actual}.");
            }
        }
    }
}
