// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging.Tests.Internals
{
    /// <summary>The fit arithmetic, the decode-scale choice and the orientation transforms.</summary>
    public sealed class ImageGeometryTests
    {
        [Theory]
        [InlineData(3000, 1500, 1600, 1600, 800)]
        [InlineData(1, 5000, 400, 1, 400)]
        [InlineData(5000, 1, 400, 400, 1)]
        [InlineData(100, 100, 400, 100, 100)]
        [InlineData(799, 401, 400, 400, 201)]
        public void Contain_keeps_the_aspect_ratio_never_upscales_and_never_rounds_to_zero(
            int width, int height, int max, int expectedWidth, int expectedHeight)
        {
            Assert.Equal(new SKSizeI(expectedWidth, expectedHeight), ImageGeometry.Contain(new SKSizeI(width, height), max));
        }

        [Fact]
        public void CoverSquare_crops_the_centred_square()
        {
            FitPlan wide = ImageGeometry.Fit(new SKSizeI(801, 600), ImageFit.CoverSquare, 512);
            FitPlan tall = ImageGeometry.Fit(new SKSizeI(600, 800), ImageFit.CoverSquare, 512);

            Assert.Equal(SKRectI.Create(100, 0, 600, 600), wide.Crop);
            Assert.Equal(SKRectI.Create(0, 100, 600, 600), tall.Crop);
            Assert.Equal(new SKSizeI(512, 512), wide.Output);
            Assert.Equal(512d / 600, wide.Scale, 6);
        }

        [Fact]
        public void A_reduced_decode_size_is_taken_only_when_it_covers_the_output()
        {
            var encoded = new SKSizeI(4000, 3000);

            Assert.Equal(new SKSizeI(500, 375), ImageGeometry.ChooseDecodeSize(encoded, 0.125, static _ => new SKSizeI(500, 375)));
            Assert.Equal(encoded, ImageGeometry.ChooseDecodeSize(encoded, 0.2, static _ => new SKSizeI(500, 375)));
            Assert.Equal(encoded, ImageGeometry.ChooseDecodeSize(encoded, 1, static _ => throw new InvalidOperationException("Not asked at scale one.")));
            Assert.Equal(encoded, ImageGeometry.ChooseDecodeSize(encoded, 0.5, static _ => new SKSizeI(8000, 6000)));
        }

        public static TheoryData<SKEncodedOrigin> Origins =>
        [
            SKEncodedOrigin.TopLeft,
            SKEncodedOrigin.TopRight,
            SKEncodedOrigin.BottomRight,
            SKEncodedOrigin.BottomLeft,
            SKEncodedOrigin.LeftTop,
            SKEncodedOrigin.RightTop,
            SKEncodedOrigin.RightBottom,
            SKEncodedOrigin.LeftBottom,
        ];

        [Theory]
        [MemberData(nameof(Origins))]
        public void Mapping_a_rectangle_back_to_the_encoded_space_inverts_the_orientation_matrix(SKEncodedOrigin origin)
        {
            // An asymmetric rectangle in a 60 x 40 encoded image, mapped forward by the matrix and
            // back by ToEncoded, must come back where it started.
            var encodedSize = new SKSizeI(60, 40);
            SKSizeI oriented = ImageGeometry.Orient(encodedSize, origin);
            var encodedRect = SKRectI.Create(5, 7, 11, 13);
            SKMatrix forward = ImageGeometry.OrientationMatrix(origin, oriented.Width, oriented.Height);
            SKRect mapped = forward.MapRect(encodedRect);
            var orientedRect = SKRectI.Round(mapped);

            Assert.True(SKRectI.Create(0, 0, oriented.Width, oriented.Height).Contains(orientedRect));
            Assert.Equal(encodedRect, ImageGeometry.ToEncoded(orientedRect, oriented, origin));
        }

        [Theory]
        [MemberData(nameof(Origins))]
        public void The_orientation_matrix_maps_the_encoded_image_exactly_onto_the_oriented_output(SKEncodedOrigin origin)
        {
            var encodedSize = new SKSizeI(60, 40);
            SKSizeI oriented = ImageGeometry.Orient(encodedSize, origin);

            SKRect mapped = ImageGeometry.OrientationMatrix(origin, oriented.Width, oriented.Height)
                .MapRect(SKRect.Create(0, 0, encodedSize.Width, encodedSize.Height));

            Assert.Equal(SKRect.Create(0, 0, oriented.Width, oriented.Height), mapped);
        }

        [Theory]
        [InlineData(200f, 100f, 256, 256, 128)]
        [InlineData(612f, 792f, 256, 198, 256)]
        [InlineData(10f, 10f, 96, 96, 96)]
        [InlineData(100000f, 1f, 256, 256, 1)]
        public void A_pdf_page_is_fitted_to_the_thumbnail_square_scaling_up_or_down(
            float width, float height, int size, int expectedWidth, int expectedHeight)
        {
            Assert.Equal((expectedWidth, expectedHeight), PdfWorker.PdfiumRasterizer.Fit(width, height, size));
        }
    }
}
