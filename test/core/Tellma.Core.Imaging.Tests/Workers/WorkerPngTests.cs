// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     The check of a worker's PNG before any decoder sees it: what the worker's encoder writes
    ///     for a fitted page passes; any other size, format, chunk or framing is refused.
    /// </summary>
    public sealed class WorkerPngTests
    {
        [Theory]
        [InlineData(256, 128)]
        [InlineData(100, 256)]
        [InlineData(256, 256)]
        [InlineData(256, 1)]
        public void The_png_the_worker_encodes_for_a_fitted_page_passes(int width, int height)
        {
            Assert.Equal((width, height), WorkerPng.Check(Png(width, height), 256));
        }

        [Theory]
        [InlineData(255, 128)]
        [InlineData(257, 128)]
        [InlineData(128, 128)]
        public void A_png_whose_longer_side_is_not_the_thumbnail_size_is_refused(int width, int height)
        {
            Assert.Equal(PdfWorkerFailure.Protocol, Assert.Throws<PdfWorkerException>(() => WorkerPng.Check(Png(width, height), 256)).Failure);
        }

        [Fact]
        public void An_opaque_rgb_png_passes()
        {
            Assert.Equal((64, 32), WorkerPng.Check(Png(64, 32, SKAlphaType.Opaque), 64));
        }

        [Fact]
        public void Anything_but_a_png_is_refused()
        {
            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check("<html></html>"u8, 64));
            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check([], 64));
            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check(Png(64, 32).AsSpan(0, 20), 64));
        }

        [Theory]
        [InlineData(8 + 8 + 8, 16)]
        [InlineData(8 + 8 + 9, 3)]
        [InlineData(8 + 8 + 12, 1)]
        public void A_header_declaring_another_depth_colour_type_or_interlace_is_refused(int offset, byte value)
        {
            byte[] png = Png(64, 32);
            png[offset] = value;

            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check(png, 64));
        }

        [Theory]
        [InlineData("tEXt")]
        [InlineData("eXIf")]
        [InlineData("iCCP")]
        [InlineData("PLTE")]
        public void A_chunk_other_than_image_data_is_refused(string type)
        {
            byte[] png = Png(64, 32);
            byte[] chunk = [0, 0, 0, 1, .. System.Text.Encoding.ASCII.GetBytes(type), 0, 0, 0, 0, 0];

            byte[] spliced = [.. png.AsSpan(0, png.Length - 12), .. chunk, .. png.AsSpan(png.Length - 12)];
            Assert.Contains(type, Assert.Throws<PdfWorkerException>(() => WorkerPng.Check(spliced, 64)).Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Bytes_after_the_end_chunk_are_refused()
        {
            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check([.. Png(64, 32), 0], 64));
        }

        [Fact]
        public void A_chunk_longer_than_the_answer_is_refused()
        {
            byte[] png = Png(64, 32);
            int data = png.AsSpan().IndexOf("IDAT"u8) - 4;
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(data), uint.MaxValue);

            Assert.Throws<PdfWorkerException>(() => WorkerPng.Check(png, 64));
        }

        /// <summary>A PNG as the worker encodes one.</summary>
        /// <param name="width">Its width.</param>
        /// <param name="height">Its height.</param>
        /// <param name="alpha">Its alpha type; premultiplied, as PDFium renders, by default.</param>
        /// <returns>The PNG.</returns>
        private static byte[] Png(int width, int height, SKAlphaType alpha = SKAlphaType.Premul)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, alpha));
            bitmap.Erase(SKColors.White);
            using SKPixmap pixmap = bitmap.PeekPixels();
            using SKData data = pixmap.Encode(SKPngEncoderOptions.Default) ?? throw new InvalidOperationException("Skia did not encode the PNG.");
            return data.ToArray();
        }
    }
}
