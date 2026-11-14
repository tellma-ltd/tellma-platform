// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using System.Text;

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>What an output decodes to.</summary>
    /// <param name="Format">The encoded format.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Height">The height.</param>
    /// <param name="FrameCount">The frame count.</param>
    /// <param name="Origin">The encoded origin.</param>
    /// <param name="AlphaType">The alpha type the codec reports.</param>
    /// <param name="Pixels">The pixels, unpremultiplied RGBA.</param>
    internal sealed record DecodedOutput(
        SKEncodedImageFormat Format, int Width, int Height, int FrameCount, SKEncodedOrigin Origin, SKAlphaType AlphaType, SKColor[] Pixels)
    {
        /// <summary>The pixel at (x, y).</summary>
        /// <param name="x">The column.</param>
        /// <param name="y">The row.</param>
        /// <returns>The colour.</returns>
        public SKColor At(int x, int y)
        {
            return Pixels[(y * Width) + x];
        }
    }

    /// <summary>Decodes processor output and inspects its container for metadata.</summary>
    internal static class ImageInspector
    {
        /// <summary>Decodes encoded bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The decoded output.</returns>
        public static DecodedOutput Decode(byte[] bytes)
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            Assert.NotNull(codec);

            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
            return new DecodedOutput(
                codec.EncodedFormat, info.Width, info.Height, codec.FrameCount, codec.EncodedOrigin, codec.Info.AlphaType, bitmap.Pixels);
        }

        /// <summary>Whether two colours agree within a tolerance per channel, for lossy encodings.</summary>
        /// <param name="expected">The expected colour.</param>
        /// <param name="actual">The actual colour.</param>
        /// <param name="tolerance">The tolerance per channel.</param>
        /// <returns>True when close.</returns>
        public static bool Near(SKColor expected, SKColor actual, int tolerance = 40)
        {
            return Math.Abs(expected.Red - actual.Red) <= tolerance
                && Math.Abs(expected.Green - actual.Green) <= tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= tolerance
                && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;
        }

        /// <summary>The marker bytes of a JPEG's segments before its first scan.</summary>
        /// <param name="jpeg">The JPEG.</param>
        /// <returns>The markers in order.</returns>
        public static IReadOnlyList<byte> JpegMarkers(byte[] jpeg)
        {
            var markers = new List<byte>();
            int offset = 2;
            while (offset + 4 <= jpeg.Length && jpeg[offset] == 0xFF)
            {
                byte marker = jpeg[offset + 1];
                markers.Add(marker);
                if (marker == 0xDA)
                {
                    break;
                }

                offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2));
            }

            return markers;
        }

        /// <summary>The chunk types of a PNG.</summary>
        /// <param name="png">The PNG.</param>
        /// <returns>The chunk types in order.</returns>
        public static IReadOnlyList<string> PngChunks(byte[] png)
        {
            var chunks = new List<string>();
            int offset = 8;
            while (offset + 8 <= png.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
                chunks.Add(Encoding.ASCII.GetString(png, offset + 4, 4));
                offset += 12 + length;
            }

            return chunks;
        }

        /// <summary>The chunk FourCCs of a WebP's RIFF container.</summary>
        /// <param name="webp">The WebP.</param>
        /// <returns>The chunk FourCCs in order.</returns>
        public static IReadOnlyList<string> WebPChunks(byte[] webp)
        {
            var chunks = new List<string>();
            int offset = 12;
            while (offset + 8 <= webp.Length)
            {
                int length = BinaryPrimitives.ReadInt32LittleEndian(webp.AsSpan(offset + 4));
                chunks.Add(Encoding.ASCII.GetString(webp, offset, 4));
                offset += 8 + length + (length & 1);
            }

            return chunks;
        }

        /// <summary>Whether <paramref name="haystack" /> contains the ASCII text <paramref name="needle" />.</summary>
        /// <param name="haystack">The bytes.</param>
        /// <param name="needle">The text.</param>
        /// <returns>True when found.</returns>
        public static bool ContainsAscii(byte[] haystack, string needle)
        {
            return haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;
        }
    }
}
