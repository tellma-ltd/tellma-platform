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
    ///     Re-encoding as a security control: the encoded orientation is applied to the pixels and
    ///     then gone, no metadata or colour profile survives into any output format, and a polyglot
    ///     comes out as nothing but an image.
    /// </summary>
    public sealed class MetadataAndOrientationTests : IDisposable
    {
        private const string CameraMake = "TellmaSecretCamera";

        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        // The encoded image is 64 x 32: red, lime / blue, yellow. Each row gives the displayed
        // colours (top-left, top-right, bottom-left, bottom-right) for an Exif orientation, worked
        // out by hand from the Exif definitions rather than from the code under test.
        public static TheoryData<ushort, int, int, string> Orientations => new()
        {
            { 1, 64, 32, "R G B Y" },
            { 2, 64, 32, "G R Y B" },
            { 3, 64, 32, "Y B G R" },
            { 4, 64, 32, "B Y R G" },
            { 5, 32, 64, "R B G Y" },
            { 6, 32, 64, "B R Y G" },
            { 7, 32, 64, "Y G B R" },
            { 8, 32, 64, "G Y R B" },
        };

        [Theory]
        [MemberData(nameof(Orientations))]
        public async Task The_exif_orientation_is_applied_to_the_pixels(ushort orientation, int width, int height, string colors)
        {
            using SKBitmap bitmap = ImageFixtures.Quadrants(64, 32);
            byte[] input = ImageFixtures.WithExif(ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Jpeg), orientation, CameraMake);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(1600, ImageFit.Contain, ImageFormat.Png));

            Assert.Equal((width, height), (result.Width, result.Height));
            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal((width, height), (output.Width, output.Height));
            Assert.Equal(SKEncodedOrigin.TopLeft, output.Origin);
            ProcessTests.AssertQuadrants(output, [.. colors.Split(' ').Select(Color)]);
        }

        [Fact]
        public async Task An_oriented_cover_square_crops_the_displayed_centre()
        {
            // Rotated a quarter turn, the 64 x 32 image displays 32 x 64; its centred square spans
            // the middle of the displayed height and keeps the displayed left and right halves.
            using SKBitmap bitmap = ImageFixtures.Quadrants(64, 32);
            byte[] input = ImageFixtures.WithExif(ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Jpeg), 6, CameraMake);

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(32, ImageFit.CoverSquare, ImageFormat.Png));

            DecodedOutput output = ImageInspector.Decode(result.Primary);
            Assert.Equal((32, 32), (output.Width, output.Height));
            ProcessTests.AssertQuadrants(output, [SKColors.Blue, SKColors.Red, SKColors.Yellow, SKColors.Lime]);
        }

        [Theory]
        [InlineData(ImageFormat.WebP)]
        [InlineData(ImageFormat.Jpeg)]
        [InlineData(ImageFormat.Png)]
        public async Task No_metadata_survives_into_the_output(ImageFormat format)
        {
            using SKBitmap bitmap = ImageFixtures.Quadrants(64, 32);
            byte[] input = ImageFixtures.WithExif(ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Jpeg), 6, CameraMake);
            Assert.True(ImageInspector.ContainsAscii(input, CameraMake));

            ProcessedImage result = await _harness.ProcessAsync(
                input, new ImagePolicy(1600, ImageFit.Contain, format), new ThumbnailPolicy(16, format));

            AssertNoMetadata(result.Primary, format);
            AssertNoMetadata(result.Thumbnail!, format);
        }

        [Theory]
        [InlineData(ImageFormat.WebP)]
        [InlineData(ImageFormat.Jpeg)]
        [InlineData(ImageFormat.Png)]
        public async Task An_embedded_colour_profile_is_converted_and_not_carried(ImageFormat format)
        {
            byte[] input = ImageFixtures.DisplayP3Jpeg(40, 40);
            Assert.True(ImageInspector.ContainsAscii(input, "ICC_PROFILE"));

            ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(1600, ImageFit.Contain, format));

            AssertNoMetadata(result.Primary, format);
            Assert.False(ImageInspector.ContainsAscii(result.Primary, "ICC_PROFILE"));
        }

        [Fact]
        public async Task A_jpeg_html_polyglot_is_re_encoded_to_bytes_without_markup()
        {
            const string html = "<html><body><script>alert(document.cookie)</script></body></html>";
            using SKBitmap bitmap = ImageFixtures.Solid(32, 32, SKColors.SteelBlue);
            byte[] input = ImageFixtures.WithComment(ImageFixtures.Encode(bitmap, SKEncodedImageFormat.Jpeg), html);
            Assert.Contains((byte)'<', input.AsSpan(0, Math.Min(input.Length, 8192)).ToArray());

            foreach (ImageFormat format in Enum.GetValues<ImageFormat>())
            {
                ProcessedImage result = await _harness.ProcessAsync(input, new ImagePolicy(1600, ImageFit.Contain, format));

                // What a browser sniffs is the first bytes: no '<' at all, not just no tag.
                byte[] head = result.Primary.AsSpan(0, Math.Min(result.Primary.Length, 8192)).ToArray();
                Assert.DoesNotContain((byte)'<', head);
                Assert.False(ImageInspector.ContainsAscii(result.Primary, "script"));
            }
        }

        /// <summary>Asserts that an output carries no metadata container of its format.</summary>
        /// <param name="bytes">The output.</param>
        /// <param name="format">Its format.</param>
        private static void AssertNoMetadata(byte[] bytes, ImageFormat format)
        {
            Assert.False(ImageInspector.ContainsAscii(bytes, CameraMake));
            Assert.False(ImageInspector.ContainsAscii(bytes, "Exif"));
            switch (format)
            {
                case ImageFormat.Jpeg:
                    // Only the structural segments: no APPn (Exif, XMP, ICC, JFIF thumbnails) and no comment.
                    IReadOnlyList<byte> markers = ImageInspector.JpegMarkers(bytes);
                    Assert.DoesNotContain(markers, static marker => marker is (>= 0xE1 and <= 0xEF) or 0xFE);
                    break;

                case ImageFormat.Png:
                    IReadOnlyList<string> chunks = ImageInspector.PngChunks(bytes);
                    string[] metadata = ["eXIf", "iCCP", "sRGB", "gAMA", "cHRM", "tEXt", "iTXt", "zTXt", "tIME"];
                    Assert.Empty(chunks.Intersect(metadata));
                    break;

                case ImageFormat.WebP:
                    IReadOnlyList<string> riff = ImageInspector.WebPChunks(bytes);
                    Assert.Empty(riff.Intersect(["EXIF", "XMP ", "ICCP"]));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "Not a defined format.");
            }
        }

        /// <summary>The colour a letter of the orientation table names.</summary>
        /// <param name="letter">R, G, B or Y.</param>
        /// <returns>The colour.</returns>
        private static SKColor Color(string letter)
        {
            return letter switch
            {
                "R" => SKColors.Red,
                "G" => SKColors.Lime,
                "B" => SKColors.Blue,
                "Y" => SKColors.Yellow,
                _ => throw new ArgumentOutOfRangeException(nameof(letter), letter, "Not a quadrant colour."),
            };
        }
    }
}
