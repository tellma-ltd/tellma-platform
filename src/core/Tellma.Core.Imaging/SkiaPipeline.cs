// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     The Skia half of the processor: identifies an image, enforces the pixel cap before any
    ///     decode, decodes the first frame at the smallest scale that covers the output, applies the
    ///     orientation and the fit in one draw, and re-encodes without metadata.
    /// </summary>
    /// <remarks>
    ///     Every native object lives in a <c>using</c> scope that ends inside the method creating
    ///     it; only encoded bytes, which are managed arrays, leave the pipeline. A decode's memory is
    ///     bounded by its size, which the pixel cap bounds: the decoded frame takes four bytes per
    ///     pixel, its mipmaps a third more, and the output a size the policy fixes.
    /// </remarks>
    internal static class SkiaPipeline
    {
        /// <summary>The quality of lossy WebP output, out of 100.</summary>
        public const int WebPQuality = 80;

        /// <summary>The quality of JPEG output, out of 100.</summary>
        public const int JpegQuality = 85;

        // Trilinear sampling: mipmaps keep a large reduction free of aliasing, and at scale one the
        // draw reproduces the source exactly.
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        /// <summary>
        ///     Normalises an upload: decode, orient, fit, encode; and, when asked, the thumbnail of the
        ///     normalised image.
        /// </summary>
        /// <param name="input">The input, gated by the caller's cancellation.</param>
        /// <param name="contentType">The media type the input's signature declares.</param>
        /// <param name="policy">The normalisation policy.</param>
        /// <param name="thumbnail">The thumbnail to render, or null.</param>
        /// <param name="cancellationToken">Stops the work between steps.</param>
        /// <returns>The processed image.</returns>
        /// <exception cref="ImageFailure">The input cannot be normalised.</exception>
        public static ProcessedImage Process(
            GatedReadStream input, string contentType, ImagePolicy policy, ThumbnailPolicy? thumbnail, CancellationToken cancellationToken)
        {
            // Section: identify, cap, plan, decode.
            using SKCodec codec = Open(input, contentType, policy.MaxInputPixels, cancellationToken);
            SKEncodedOrigin origin = codec.EncodedOrigin;
            SKSizeI encoded = codec.Info.Size;
            SKSizeI oriented = ImageGeometry.Orient(encoded, origin);
            FitPlan plan = ImageGeometry.Fit(oriented, policy.Fit, policy.MaxDimension);

            using SKBitmap decoded = Decode(codec, contentType, plan.Scale, input, cancellationToken);
            using SKImage source = SKImage.FromBitmap(decoded) ?? throw ImageFailure.DecodeFailed("The decoded frame could not be wrapped as an image.");
            cancellationToken.ThrowIfCancellationRequested();

            // Section: orient and fit in one draw, then encode the primary.
            bool flatten = policy.Format == ImageFormat.Jpeg;
            SKRect sourceRect = DecodedRect(plan.Crop, oriented, origin, encoded, decoded.Info.Size);
            using SKImage primary = Draw(source, sourceRect, origin, plan.Output, IsOpaque(codec), flatten);
            byte[] primaryBytes = Encode(primary, policy.Format);
            cancellationToken.ThrowIfCancellationRequested();

            // Section: the thumbnail comes from the normalised image, never from the input again.
            byte[]? thumbnailBytes = null;
            if (thumbnail is not null)
            {
                SKSizeI size = ImageGeometry.Contain(plan.Output, thumbnail.Size);
                using SKImage thumb = Draw(
                    primary,
                    SKRect.Create(0, 0, primary.Width, primary.Height),
                    SKEncodedOrigin.TopLeft,
                    size,
                    primary.AlphaType == SKAlphaType.Opaque,
                    thumbnail.Format == ImageFormat.Jpeg);
                thumbnailBytes = Encode(thumb, thumbnail.Format);
            }

            return new ProcessedImage(
                primaryBytes, plan.Output.Width, plan.Output.Height, thumbnailBytes, BlobContentTypes.ForImageFormat(policy.Format));
        }

        /// <summary>
        ///     Renders the thumbnail of a stored image: decode under the cap, orient, fit within the
        ///     thumbnail's square keeping the aspect ratio, encode.
        /// </summary>
        /// <param name="input">The input, gated by the call's time budget and cancellation.</param>
        /// <param name="contentType">The media type the input's signature declares.</param>
        /// <param name="policy">The thumbnail policy.</param>
        /// <param name="maxPixels">The pixel cap.</param>
        /// <param name="cancellationToken">Stops the work between steps.</param>
        /// <returns>The encoded thumbnail.</returns>
        /// <exception cref="ImageFailure">The input cannot be rendered.</exception>
        public static byte[] Thumbnail(
            GatedReadStream input, string contentType, ThumbnailPolicy policy, long maxPixels, CancellationToken cancellationToken)
        {
            using SKCodec codec = Open(input, contentType, maxPixels, cancellationToken);
            SKEncodedOrigin origin = codec.EncodedOrigin;
            SKSizeI encoded = codec.Info.Size;
            SKSizeI oriented = ImageGeometry.Orient(encoded, origin);
            FitPlan plan = ImageGeometry.Fit(oriented, ImageFit.Contain, policy.Size);

            using SKBitmap decoded = Decode(codec, contentType, plan.Scale, input, cancellationToken);
            using SKImage source = SKImage.FromBitmap(decoded) ?? throw ImageFailure.DecodeFailed("The decoded frame could not be wrapped as an image.");
            cancellationToken.ThrowIfCancellationRequested();

            SKRect sourceRect = DecodedRect(plan.Crop, oriented, origin, encoded, decoded.Info.Size);
            using SKImage thumb = Draw(source, sourceRect, origin, plan.Output, IsOpaque(codec), policy.Format == ImageFormat.Jpeg);
            return Encode(thumb, policy.Format);
        }

        /// <summary>Encodes a rendered bitmap (a PDF page) as a thumbnail in the policy's format.</summary>
        /// <param name="bitmap">The rendered page.</param>
        /// <param name="format">The thumbnail's format.</param>
        /// <returns>The encoded thumbnail.</returns>
        /// <exception cref="ImageFailure">The bitmap cannot be encoded.</exception>
        public static byte[] EncodeBitmap(SKBitmap bitmap, ImageFormat format)
        {
            bitmap.SetImmutable();
            using SKImage image = SKImage.FromBitmap(bitmap) ?? throw ImageFailure.DecodeFailed("The rendered page could not be wrapped as an image.");

            // Redraw onto an untagged surface of the same size: the encoders then write no colour
            // profile, and a JPEG is flattened like every other JPEG.
            using SKImage normalised = Draw(
                image,
                SKRect.Create(0, 0, image.Width, image.Height),
                SKEncodedOrigin.TopLeft,
                new SKSizeI(image.Width, image.Height),
                opaque: false,
                flattenOnWhite: format == ImageFormat.Jpeg);
            return Encode(normalised, format);
        }

        /// <summary>
        ///     Creates the codec over the input, checks that it decodes the format the signature
        ///     declared, and refuses an image whose header declares more pixels than the cap, before
        ///     any pixel is decoded.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <param name="contentType">The sniffed media type.</param>
        /// <param name="maxPixels">The pixel cap.</param>
        /// <param name="cancellationToken">The work's cancellation, which a closed gate reports.</param>
        /// <returns>The codec; the caller disposes it.</returns>
        /// <exception cref="ImageFailure">The input is not that format, is malformed, or is over the cap.</exception>
        private static SKCodec Open(GatedReadStream input, string contentType, long maxPixels, CancellationToken cancellationToken)
        {
            // The codec owns the managed wrapper it creates around the view and disposes it with
            // itself; disposing the view only closes the gate.
            var created = SKCodec.Create(input, out SKCodecResult result);
            if (created is null)
            {
                // A gate closed while the header was read is the cancellation, not a malformed file.
                if (input.IsClosed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw ImageFailure.DecodeFailed($"The image header could not be read ({result}).");
            }

            SKCodec codec = created;

            try
            {
                if (codec.EncodedFormat != ImageSignatures.FormatOf(contentType))
                {
                    throw ImageFailure.Format($"The bytes begin like {contentType}, but decode as {codec.EncodedFormat}.");
                }

                SKImageInfo info = codec.Info;
                if (info.Width <= 0 || info.Height <= 0)
                {
                    throw ImageFailure.DecodeFailed($"The image declares an empty size ({info.Width} x {info.Height}).");
                }

                // The pixel cap is the whole decompression-bomb defence: a decoder allocates from
                // the header, so it is checked on the header alone. The product is taken in 64 bits.
                return (long)info.Width * info.Height > maxPixels
                    ? throw ImageFailure.TooManyPixels(info.Width, info.Height, maxPixels)
                    : codec;
            }
            catch
            {
                codec.Dispose();
                throw;
            }
        }

        /// <summary>
        ///     Decodes the first frame: at the smallest supported scale that covers the plan for JPEG
        ///     and WebP, at full size for PNG and GIF, converted to sRGB.
        /// </summary>
        /// <param name="codec">The opened codec.</param>
        /// <param name="contentType">The sniffed media type.</param>
        /// <param name="scale">The plan's scale.</param>
        /// <param name="input">The gated input, to tell a cancelled read from a truncated file.</param>
        /// <param name="cancellationToken">The work's cancellation.</param>
        /// <returns>The decoded frame; the caller disposes it.</returns>
        /// <exception cref="ImageFailure">The frame cannot be decoded.</exception>
        private static SKBitmap Decode(
            SKCodec codec, string contentType, double scale, GatedReadStream input, CancellationToken cancellationToken)
        {
            SKSizeI encoded = codec.Info.Size;
            bool scales = contentType is BlobContentTypes.Jpeg or BlobContentTypes.WebP;
            SKSizeI size = scales ? ImageGeometry.ChooseDecodeSize(encoded, scale, codec.GetScaledDimensions) : encoded;

            // Premultiplied RGBA in sRGB: the decoder converts a tagged colour profile, so what is
            // drawn and encoded later is plain sRGB and the output needs no profile.
            SKAlphaType alpha = IsOpaque(codec) ? SKAlphaType.Opaque : SKAlphaType.Premul;
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, alpha, SKColorSpace.CreateSrgb());

            var bitmap = new SKBitmap();
            try
            {
                if (!bitmap.TryAllocPixels(info))
                {
                    throw ImageFailure.DecodeFailed($"No memory for a {size.Width} x {size.Height} frame.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Frame 0 only: an animation's later frames are never decoded.
                SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels(), bitmap.RowBytes, new SKCodecOptions(0));

                // A closed gate reads as a truncated file; report it as the cancellation it is.
                if (input.IsClosed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (result != SKCodecResult.Success)
                {
                    throw ImageFailure.DecodeFailed($"The image could not be decoded ({result}).");
                }

                bitmap.SetImmutable();
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>The crop rectangle in the decoded frame's coordinates.</summary>
        /// <param name="crop">The crop in the oriented space.</param>
        /// <param name="oriented">The oriented size.</param>
        /// <param name="origin">The encoded origin.</param>
        /// <param name="encoded">The stored size.</param>
        /// <param name="decoded">The decoded frame's size.</param>
        /// <returns>The source rectangle of the draw.</returns>
        private static SKRect DecodedRect(SKRectI crop, SKSizeI oriented, SKEncodedOrigin origin, SKSizeI encoded, SKSizeI decoded)
        {
            SKRectI rect = ImageGeometry.ToEncoded(crop, oriented, origin);
            float scaleX = (float)decoded.Width / encoded.Width;
            float scaleY = (float)decoded.Height / encoded.Height;
            return new SKRect(rect.Left * scaleX, rect.Top * scaleY, rect.Right * scaleX, rect.Bottom * scaleY);
        }

        /// <summary>
        ///     Draws <paramref name="sourceRect" /> of an image, oriented, onto a new output of
        ///     <paramref name="output" />, and snapshots it.
        /// </summary>
        /// <param name="source">The image to draw.</param>
        /// <param name="sourceRect">The part to draw, in the image's coordinates.</param>
        /// <param name="origin">The orientation to apply.</param>
        /// <param name="output">The oriented output size.</param>
        /// <param name="opaque">Whether the source has no alpha; the output then has none either.</param>
        /// <param name="flattenOnWhite">Whether transparency is composited over white (for JPEG output).</param>
        /// <returns>The snapshot; the caller disposes it.</returns>
        /// <exception cref="ImageFailure">The output surface cannot be created.</exception>
        private static SKImage Draw(
            SKImage source, SKRect sourceRect, SKEncodedOrigin origin, SKSizeI output, bool opaque, bool flattenOnWhite)
        {
            // No colour space on the output: pixels are already sRGB, and an untagged image makes
            // every encoder skip the colour profile. An opaque source keeps an opaque output, so an
            // opaque upload gets an opaque alpha channel; transparency is never flattened except
            // for JPEG, which has none.
            var info = new SKImageInfo(output.Width, output.Height, SKColorType.Rgba8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info)
                ?? throw ImageFailure.DecodeFailed($"No memory for a {output.Width} x {output.Height} output.");

            SKCanvas canvas = surface.Canvas;
            canvas.Clear(opaque || flattenOnWhite ? SKColors.White : SKColors.Transparent);

            // The orientation matrix maps the encoded-orientation rectangle at the output's scale
            // onto the oriented output; the crop and the scale ride the source and destination.
            bool swaps = ImageGeometry.SwapsAxes(origin);
            var destination = SKRect.Create(0, 0, swaps ? output.Height : output.Width, swaps ? output.Width : output.Height);
            canvas.SetMatrix(ImageGeometry.OrientationMatrix(origin, output.Width, output.Height));

            using var paint = new SKPaint { IsAntialias = false };
            canvas.DrawImage(source, sourceRect, destination, Sampling, paint);
            canvas.Flush();

            return surface.Snapshot();
        }

        /// <summary>Encodes an image; the output carries no metadata, since the image has none.</summary>
        /// <param name="image">A raster image.</param>
        /// <param name="format">The format.</param>
        /// <returns>The encoded bytes.</returns>
        /// <exception cref="ImageFailure">The encoder failed.</exception>
        private static byte[] Encode(SKImage image, ImageFormat format)
        {
            using SKPixmap pixmap = image.PeekPixels() ?? throw ImageFailure.DecodeFailed("The rendered image has no raster pixels.");
            using SKData? data = format switch
            {
                ImageFormat.WebP => pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebPQuality)),
                ImageFormat.Jpeg => pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)),
                ImageFormat.Png => pixmap.Encode(SKPngEncoderOptions.Default),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a defined image format."),
            };

            return data?.ToArray() ?? throw ImageFailure.DecodeFailed($"The {format} encoder failed.");
        }

        /// <summary>Whether the codec reports the image as having no alpha.</summary>
        /// <param name="codec">The codec.</param>
        /// <returns>True for an opaque image.</returns>
        private static bool IsOpaque(SKCodec codec)
        {
            return codec.Info.AlphaType == SKAlphaType.Opaque;
        }
    }
}
