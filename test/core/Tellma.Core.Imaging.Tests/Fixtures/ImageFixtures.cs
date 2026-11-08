// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using System.Text;

namespace Tellma.Core.Imaging.Tests.Fixtures
{
    /// <summary>
    ///     Generates every image the suites feed the processor, honest and hostile, so no binary
    ///     fixture is committed: Skia encodes the honest ones, and the hostile ones are honest files
    ///     with their headers patched or segments spliced in.
    /// </summary>
    internal static class ImageFixtures
    {
        /// <summary>The quadrant colours of <see cref="Quadrants" />: top-left, top-right, bottom-left, bottom-right.</summary>
        public static readonly SKColor[] QuadrantColors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow];

        /// <summary>A bitmap split into four solid quadrants of <see cref="QuadrantColors" />.</summary>
        /// <param name="width">The width; even.</param>
        /// <param name="height">The height; even.</param>
        /// <returns>The bitmap; the caller disposes it.</returns>
        public static SKBitmap Quadrants(int width, int height)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint();
            int halfWidth = width / 2;
            int halfHeight = height / 2;
            SKRectI[] quadrants =
            [
                SKRectI.Create(0, 0, halfWidth, halfHeight),
                SKRectI.Create(halfWidth, 0, width - halfWidth, halfHeight),
                SKRectI.Create(0, halfHeight, halfWidth, height - halfHeight),
                SKRectI.Create(halfWidth, halfHeight, width - halfWidth, height - halfHeight),
            ];

            for (int i = 0; i < quadrants.Length; i++)
            {
                paint.Color = QuadrantColors[i];
                canvas.DrawRect(quadrants[i], paint);
            }

            return bitmap;
        }

        /// <summary>A solid opaque bitmap.</summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="color">The colour.</param>
        /// <returns>The bitmap; the caller disposes it.</returns>
        public static SKBitmap Solid(int width, int height, SKColor color)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            bitmap.Erase(color);
            return bitmap;
        }

        /// <summary>
        ///     A bitmap whose left half is fully transparent and right half opaque black: a
        ///     signature-like image whose transparency must survive normalisation.
        /// </summary>
        /// <param name="width">The width; even.</param>
        /// <param name="height">The height.</param>
        /// <returns>The bitmap; the caller disposes it.</returns>
        public static SKBitmap HalfTransparent(int width, int height)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            bitmap.Erase(SKColors.Transparent);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(SKRect.Create(width / 2, 0, width - (width / 2), height), paint);
            return bitmap;
        }

        /// <summary>Encodes a bitmap as JPEG, PNG or lossless WebP.</summary>
        /// <param name="bitmap">The bitmap.</param>
        /// <param name="format">The format.</param>
        /// <returns>The encoded bytes.</returns>
        public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
        {
            using SKPixmap pixmap = bitmap.PeekPixels();
            using SKData? data = format == SKEncodedImageFormat.Jpeg ? pixmap.Encode(new SKJpegEncoderOptions(95))
                : format == SKEncodedImageFormat.Png ? pixmap.Encode(SKPngEncoderOptions.Default)
                : format == SKEncodedImageFormat.Webp ? pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100))
                : throw new ArgumentOutOfRangeException(nameof(format), format, "Skia encodes no other format.");

            return data?.ToArray() ?? throw new InvalidOperationException($"Skia could not encode the {format} fixture.");
        }

        /// <summary>
        ///     An image of four quadrants in the given format; a GIF, which Skia cannot encode, is a
        ///     solid red frame from <see cref="Gif" /> instead.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns>The encoded bytes.</returns>
        public static byte[] QuadrantImage(SKEncodedImageFormat format, int width, int height)
        {
            if (format == SKEncodedImageFormat.Gif)
            {
                return Gif(width, height, [[0]]);
            }

            using SKBitmap bitmap = Quadrants(width, height);
            return Encode(bitmap, format);
        }

        /// <summary>
        ///     A JPEG with an Exif segment carrying <paramref name="orientation" /> and a camera make,
        ///     spliced in right after the start-of-image marker.
        /// </summary>
        /// <param name="jpeg">A JPEG without Exif.</param>
        /// <param name="orientation">The Exif orientation, 1 to 8.</param>
        /// <param name="make">The camera make string, which must not survive processing.</param>
        /// <returns>The JPEG with Exif.</returns>
        public static byte[] WithExif(byte[] jpeg, ushort orientation, string make)
        {
            // Section: a big-endian TIFF structure with one IFD of two entries and the make string after it.
            byte[] makeBytes = Encoding.ASCII.GetBytes(make + "\0");
            const int ifdOffset = 8;
            const int entries = 2;
            int dataOffset = ifdOffset + 2 + (entries * 12) + 4;
            byte[] tiff = new byte[dataOffset + makeBytes.Length];
            "MM"u8.CopyTo(tiff);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), ifdOffset);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(ifdOffset), entries);

            // Make (0x010F), ASCII, at the data offset.
            Span<byte> make0 = tiff.AsSpan(ifdOffset + 2);
            BinaryPrimitives.WriteUInt16BigEndian(make0, 0x010F);
            BinaryPrimitives.WriteUInt16BigEndian(make0[2..], 2);
            BinaryPrimitives.WriteUInt32BigEndian(make0[4..], (uint)makeBytes.Length);
            BinaryPrimitives.WriteUInt32BigEndian(make0[8..], (uint)dataOffset);

            // Orientation (0x0112), SHORT, value left-justified in the entry.
            Span<byte> orientation0 = tiff.AsSpan(ifdOffset + 2 + 12);
            BinaryPrimitives.WriteUInt16BigEndian(orientation0, 0x0112);
            BinaryPrimitives.WriteUInt16BigEndian(orientation0[2..], 3);
            BinaryPrimitives.WriteUInt32BigEndian(orientation0[4..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(orientation0[8..], orientation);

            makeBytes.CopyTo(tiff, dataOffset);

            byte[] payload = [.. "Exif\0\0"u8, .. tiff];
            return InsertSegment(jpeg, 0xE1, payload);
        }

        /// <summary>A JPEG with a comment segment holding <paramref name="comment" /> right after its start: a polyglot.</summary>
        /// <param name="jpeg">The JPEG.</param>
        /// <param name="comment">The comment text.</param>
        /// <returns>The JPEG with the comment.</returns>
        public static byte[] WithComment(byte[] jpeg, string comment)
        {
            return InsertSegment(jpeg, 0xFE, Encoding.ASCII.GetBytes(comment));
        }

        /// <summary>
        ///     A JPEG tagged with the Display P3 colour profile, as phone cameras write: the encoder
        ///     embeds the profile.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns>The JPEG.</returns>
        public static byte[] DisplayP3Jpeg(int width, int height)
        {
            using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque, p3));
            bitmap.Erase(SKColors.Orange);
            return Encode(bitmap, SKEncodedImageFormat.Jpeg);
        }

        /// <summary>
        ///     A GIF89a of <paramref name="width" /> x <paramref name="height" /> whose frames are each
        ///     one solid palette index (0 red, 1 blue, 2 green, 3 white), looping forever.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="frames">Each frame's palette index, as a one-element array.</param>
        /// <returns>The GIF.</returns>
        public static byte[] Gif(int width, int height, int[][] frames)
        {
            var gif = new List<byte>();
            gif.AddRange("GIF89a"u8.ToArray());
            AddUInt16(gif, width);
            AddUInt16(gif, height);

            // A global colour table of four entries (size field 1), no background, no aspect.
            gif.AddRange([0x81, 0x00, 0x00]);
            gif.AddRange([0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF]);

            // The NETSCAPE2.0 extension: loop forever.
            gif.AddRange([0x21, 0xFF, 0x0B]);
            gif.AddRange("NETSCAPE2.0"u8.ToArray());
            gif.AddRange([0x03, 0x01, 0x00, 0x00, 0x00]);

            foreach (int[] frame in frames)
            {
                // Graphic control: 100 ms delay, no transparency; then the image descriptor.
                gif.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
                gif.Add(0x2C);
                AddUInt16(gif, 0);
                AddUInt16(gif, 0);
                AddUInt16(gif, width);
                AddUInt16(gif, height);
                gif.Add(0x00);
                gif.AddRange(LzwSolid(width * height, frame[0]));
            }

            gif.Add(0x3B);
            return [.. gif];
        }

        /// <summary>
        ///     A real image with its header's declared size patched to <paramref name="width" /> x
        ///     <paramref name="height" />. The pixel data is the small image's, so a decode of the
        ///     declared size would fail as truncated: refusing it as too large proves the header was
        ///     checked first.
        /// </summary>
        /// <param name="format">JPEG, PNG, GIF or WebP.</param>
        /// <param name="width">The declared width.</param>
        /// <param name="height">The declared height.</param>
        /// <returns>The patched image.</returns>
        public static byte[] DeclaringSize(SKEncodedImageFormat format, int width, int height)
        {
            byte[] image = QuadrantImage(format, 16, 16);
            if (format == SKEncodedImageFormat.Png)
            {
                // IHDR's width and height, then its CRC recomputed.
                BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(16), width);
                BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(20), height);
                BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(29), Crc32.Compute(image.AsSpan(12, 17)));
            }
            else if (format == SKEncodedImageFormat.Jpeg)
            {
                // The baseline start-of-frame: marker, length, precision, height, width.
                int sof = FindMarker(image, 0xC0);
                BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(sof + 5), (ushort)height);
                BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(sof + 7), (ushort)width);
            }
            else if (format == SKEncodedImageFormat.Gif)
            {
                // The logical screen and the first image descriptor.
                BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(6), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(8), (ushort)height);
                int descriptor = Array.IndexOf(image, (byte)0x2C, 13 + 12);
                BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(descriptor + 5), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(descriptor + 7), (ushort)height);
            }
            else if (format == SKEncodedImageFormat.Webp)
            {
                // The lossless header after "VP8L" and its length: a signature byte, then 14 bits
                // of width - 1 and 14 bits of height - 1, the top four bits kept.
                const int header = 12 + 8;
                if (image[header - 8 + 3] != (byte)'L' || image[header] != 0x2F)
                {
                    throw new InvalidOperationException("The fixture expects a simple lossless WebP.");
                }

                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(header + 1));
                bits = (bits & 0xF0000000u) | (uint)(width - 1) | ((uint)(height - 1) << 14);
                BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(header + 1), bits);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(format), format, "Not an accepted format.");
            }

            return image;
        }

        /// <summary>A photographic-noise JPEG, which does not compress away, cut short at <paramref name="fraction" />.</summary>
        /// <param name="fraction">How much of the file to keep.</param>
        /// <returns>The truncated JPEG.</returns>
        public static byte[] TruncatedJpeg(double fraction)
        {
            byte[] jpeg = Noise(128, 128, SKEncodedImageFormat.Jpeg);
            return jpeg[..(int)(jpeg.Length * fraction)];
        }

        /// <summary>A noise image, deterministic, which compresses poorly so its data spans many reads.</summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="format">The format.</param>
        /// <returns>The encoded image.</returns>
        public static byte[] Noise(int width, int height, SKEncodedImageFormat format)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
            var random = new Random(17);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                }
            }

            return Encode(bitmap, format);
        }

        /// <summary>Splices a segment in right after a JPEG's start-of-image marker.</summary>
        /// <param name="jpeg">The JPEG.</param>
        /// <param name="marker">The segment's marker byte.</param>
        /// <param name="payload">The segment's payload.</param>
        /// <returns>The JPEG with the segment.</returns>
        private static byte[] InsertSegment(byte[] jpeg, byte marker, byte[] payload)
        {
            byte[] segment = new byte[4 + payload.Length];
            segment[0] = 0xFF;
            segment[1] = marker;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
            payload.CopyTo(segment, 4);
            return [.. jpeg[..2], .. segment, .. jpeg[2..]];
        }

        /// <summary>The offset of the first segment with the given marker, walking the segments before the scan.</summary>
        /// <param name="jpeg">The JPEG.</param>
        /// <param name="marker">The marker byte.</param>
        /// <returns>The offset of the segment's 0xFF.</returns>
        private static int FindMarker(byte[] jpeg, byte marker)
        {
            int offset = 2;
            while (offset + 4 <= jpeg.Length && jpeg[offset] == 0xFF)
            {
                if (jpeg[offset + 1] == marker)
                {
                    return offset;
                }

                offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2));
            }

            throw new InvalidOperationException($"The JPEG has no segment 0xFF{marker:X2}.");
        }

        /// <summary>
        ///     LZW data for a frame of one palette index, with a minimum code size of 2: a clear code
        ///     before every pixel keeps every code three bits wide, which every decoder accepts.
        /// </summary>
        /// <param name="pixels">The pixel count.</param>
        /// <param name="index">The palette index.</param>
        /// <returns>The minimum code size byte, the data sub-blocks and the terminator.</returns>
        private static byte[] LzwSolid(int pixels, int index)
        {
            const int clear = 4;
            const int end = 5;
            var codes = new List<int>();
            for (int i = 0; i < pixels; i++)
            {
                codes.Add(clear);
                codes.Add(index);
            }

            codes.Add(end);

            // Pack the three-bit codes least significant bit first.
            var packed = new List<byte>();
            int buffer = 0;
            int count = 0;
            foreach (int code in codes)
            {
                buffer |= code << count;
                count += 3;
                while (count >= 8)
                {
                    packed.Add((byte)(buffer & 0xFF));
                    buffer >>= 8;
                    count -= 8;
                }
            }

            if (count > 0)
            {
                packed.Add((byte)(buffer & 0xFF));
            }

            // Sub-blocks of at most 255 bytes.
            var data = new List<byte> { 2 };
            for (int i = 0; i < packed.Count; i += 255)
            {
                int length = Math.Min(255, packed.Count - i);
                data.Add((byte)length);
                data.AddRange(packed.GetRange(i, length));
            }

            data.Add(0);
            return [.. data];
        }

        /// <summary>Appends a little-endian 16-bit value.</summary>
        /// <param name="bytes">The buffer.</param>
        /// <param name="value">The value.</param>
        private static void AddUInt16(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)(value >> 8));
        }
    }
}
