// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     Where an image's pixels go: the crop and output size of a fit, the decode size that still
    ///     covers it, and the transforms that apply the encoded orientation.
    /// </summary>
    /// <remarks>
    ///     Three coordinate spaces meet here. The <em>encoded</em> space is the image as stored,
    ///     before its orientation is applied; the <em>oriented</em> space is the image as displayed;
    ///     the <em>decoded</em> space is the encoded space at the scale the decoder produced. Fits
    ///     are planned in the oriented space, because that is what a person sees; pixels are read in
    ///     the decoded space.
    /// </remarks>
    internal static class ImageGeometry
    {
        /// <summary>Whether an origin turns the image a quarter turn, so width and height swap.</summary>
        /// <param name="origin">The encoded origin.</param>
        /// <returns>True for the four transposing origins.</returns>
        public static bool SwapsAxes(SKEncodedOrigin origin)
        {
            return origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        }

        /// <summary>The displayed size of an image stored at <paramref name="encoded" />.</summary>
        /// <param name="encoded">The stored size.</param>
        /// <param name="origin">The encoded origin.</param>
        /// <returns>The oriented size.</returns>
        public static SKSizeI Orient(SKSizeI encoded, SKEncodedOrigin origin)
        {
            return SwapsAxes(origin) ? new SKSizeI(encoded.Height, encoded.Width) : encoded;
        }

        /// <summary>
        ///     The size that fits <paramref name="size" /> within a square of
        ///     <paramref name="maxDimension" />, keeping the aspect ratio and never upscaling; neither
        ///     side rounds below one pixel.
        /// </summary>
        /// <param name="size">The size to fit; both sides positive.</param>
        /// <param name="maxDimension">The square's side; positive.</param>
        /// <returns>The fitted size.</returns>
        public static SKSizeI Contain(SKSizeI size, int maxDimension)
        {
            int longer = Math.Max(size.Width, size.Height);
            if (longer <= maxDimension)
            {
                return size;
            }

            double scale = (double)maxDimension / longer;
            return new SKSizeI(
                Math.Clamp((int)Math.Round(size.Width * scale), 1, maxDimension),
                Math.Clamp((int)Math.Round(size.Height * scale), 1, maxDimension));
        }

        /// <summary>Plans a fit in the oriented space.</summary>
        /// <param name="oriented">The displayed size; both sides positive.</param>
        /// <param name="fit">The fit.</param>
        /// <param name="maxDimension">The largest side of the output; positive.</param>
        /// <returns>The crop and the output size.</returns>
        public static FitPlan Fit(SKSizeI oriented, ImageFit fit, int maxDimension)
        {
            if (fit == ImageFit.CoverSquare)
            {
                // The centred square on the shorter side, scaled down to the maximum, never up.
                int side = Math.Min(oriented.Width, oriented.Height);
                int left = (oriented.Width - side) / 2;
                int top = (oriented.Height - side) / 2;
                int output = Math.Min(side, maxDimension);
                return new FitPlan(SKRectI.Create(left, top, side, side), new SKSizeI(output, output));
            }

            return new FitPlan(SKRectI.Create(0, 0, oriented.Width, oriented.Height), Contain(oriented, maxDimension));
        }

        /// <summary>
        ///     The decode size for a codec that decodes at reduced scale (JPEG, WebP): the codec's
        ///     nearest supported size to the plan's scale, provided it still covers the output, else
        ///     the full size.
        /// </summary>
        /// <param name="encoded">The stored size.</param>
        /// <param name="scale">The plan's scale, output over crop; at most one.</param>
        /// <param name="scaledDimensions">The codec's answer for a desired scale.</param>
        /// <returns>The size to decode at.</returns>
        public static SKSizeI ChooseDecodeSize(SKSizeI encoded, double scale, Func<float, SKSizeI> scaledDimensions)
        {
            if (scale >= 1)
            {
                return encoded;
            }

            // A candidate may fall short of the exact product by less than a pixel; anything
            // smaller would make the final draw an upscale, and anything larger than the stored
            // size is not a reduction at all.
            SKSizeI candidate = scaledDimensions((float)scale);
            int neededWidth = (int)Math.Floor(encoded.Width * scale);
            int neededHeight = (int)Math.Floor(encoded.Height * scale);
            bool covers = candidate.Width >= Math.Max(1, neededWidth) && candidate.Height >= Math.Max(1, neededHeight);
            bool reduces = candidate.Width <= encoded.Width && candidate.Height <= encoded.Height;
            return covers && reduces ? candidate : encoded;
        }

        /// <summary>
        ///     Maps a rectangle of the oriented space back into the encoded space, inverting the
        ///     orientation.
        /// </summary>
        /// <param name="oriented">The rectangle in the oriented space.</param>
        /// <param name="orientedSize">The oriented image's size.</param>
        /// <param name="origin">The encoded origin.</param>
        /// <returns>The rectangle in the encoded space.</returns>
        public static SKRectI ToEncoded(SKRectI oriented, SKSizeI orientedSize, SKEncodedOrigin origin)
        {
            int x = oriented.Left;
            int y = oriented.Top;
            int w = oriented.Width;
            int h = oriented.Height;
            int width = orientedSize.Width;
            int height = orientedSize.Height;

            // Each case inverts the forward map of OrientationMatrix for an axis-aligned rectangle.
            return origin switch
            {
                SKEncodedOrigin.TopRight => SKRectI.Create(width - x - w, y, w, h),
                SKEncodedOrigin.BottomRight => SKRectI.Create(width - x - w, height - y - h, w, h),
                SKEncodedOrigin.BottomLeft => SKRectI.Create(x, height - y - h, w, h),
                SKEncodedOrigin.LeftTop => SKRectI.Create(y, x, h, w),
                SKEncodedOrigin.RightTop => SKRectI.Create(y, width - x - w, h, w),
                SKEncodedOrigin.RightBottom => SKRectI.Create(height - y - h, width - x - w, h, w),
                SKEncodedOrigin.LeftBottom => SKRectI.Create(height - y - h, x, h, w),
                SKEncodedOrigin.TopLeft => oriented,
                _ => oriented,
            };
        }

        /// <summary>
        ///     The transform that draws an image in its encoded orientation into an oriented output:
        ///     a point of the encoded space, at the output's scale, maps to where it is displayed.
        /// </summary>
        /// <remarks>Skia does not orient decoded images itself; this is its encoded-origin matrix.</remarks>
        /// <param name="origin">The encoded origin.</param>
        /// <param name="width">The oriented output's width.</param>
        /// <param name="height">The oriented output's height.</param>
        /// <returns>The transform.</returns>
        public static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height)
        {
            // Rows are (scaleX, skewX, transX) and (skewY, scaleY, transY): x' = sx*x + kx*y + tx.
            return origin switch
            {
                SKEncodedOrigin.TopRight => Make(-1, 0, width, 0, 1, 0),
                SKEncodedOrigin.BottomRight => Make(-1, 0, width, 0, -1, height),
                SKEncodedOrigin.BottomLeft => Make(1, 0, 0, 0, -1, height),
                SKEncodedOrigin.LeftTop => Make(0, 1, 0, 1, 0, 0),
                SKEncodedOrigin.RightTop => Make(0, -1, width, 1, 0, 0),
                SKEncodedOrigin.RightBottom => Make(0, -1, width, -1, 0, height),
                SKEncodedOrigin.LeftBottom => Make(0, 1, 0, -1, 0, height),
                SKEncodedOrigin.TopLeft => SKMatrix.Identity,
                _ => SKMatrix.Identity,
            };

            static SKMatrix Make(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY)
            {
                return new SKMatrix(scaleX, skewX, transX, skewY, scaleY, transY, 0, 0, 1);
            }
        }
    }

    /// <summary>A fit planned in the oriented space.</summary>
    /// <param name="Crop">The part of the oriented image the output shows.</param>
    /// <param name="Output">The output's size.</param>
    internal readonly record struct FitPlan(SKRectI Crop, SKSizeI Output)
    {
        /// <summary>The output's scale relative to the crop; at most one, since fits never upscale.</summary>
        public double Scale => (double)Output.Width / Crop.Width;
    }
}
