// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Testing;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The walk of page 1 before PDFium renders it: an image declaring too many pixels (fewer for
    ///     JPEG 2000 and JBIG2), a fan-out of forms, an oversized page and an oversized file each
    ///     yield no thumbnail, and the render is never called; honest PDFs, an encrypted one that
    ///     needs no password, and a multi-page one still render page 1.
    /// </summary>
    public sealed class PdfPreflightTests : IDisposable
    {
        private readonly RecordingRasterizer _rasterizer = new();
        private readonly ProcessorHarness _harness;

        public PdfPreflightTests()
        {
            _harness = new ProcessorHarness(pdf: _rasterizer);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        public static TheoryData<string, byte[], string> Refused => new()
        {
            { "a 20000 x 20000 flate image", DocumentFixtures.DeclaringImage(20_000, 20_000, "FlateDecode"), "20000 x 20000" },
            { "a 5000 x 5000 JPEG 2000 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JPXDecode"), "5000 x 5000" },
            { "a 5000 x 5000 JBIG2 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JBIG2Decode"), "5000 x 5000" },
            { "a fan-out of forms", DocumentFixtures.FormFanOut(depth: 6, breadth: 10), "objects" },
            { "a page wider than 14400 points", DocumentFixtures.HalfBlackPage(20_000, 100), "points" },
        };

        [Theory]
        [MemberData(nameof(Refused))]
        public async Task A_page_over_a_cap_yields_no_thumbnail_and_is_never_rendered(string name, byte[] pdf, string detail)
        {
            Assert.True(await _harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf) is null, name);

            Assert.Equal(0, _rasterizer.Calls);
            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
            FakeLogRecord failed = Assert.Single(_harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobImageFailed");
            Assert.Contains(detail, failed.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_image_within_the_cap_passes_the_walk()
        {
            // 4000 x 4000 is 16 MP: within the general cap, so the walk lets it through; the render
            // of its four bytes of data then simply draws nothing.
            Assert.NotNull(await _harness.ThumbnailAsync(DocumentFixtures.DeclaringImage(4_000, 4_000, "FlateDecode"), BlobContentTypes.Pdf));
            Assert.Equal(1, _rasterizer.Calls);
        }

        [Fact]
        public async Task A_file_over_the_input_cap_is_not_read_at_all()
        {
            byte[] pdf = DocumentFixtures.LongerThan(PdfPreflight.MaxInputBytes);

            Assert.Null(await _harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf));
            Assert.Equal(0, _rasterizer.Calls);
            Assert.DoesNotContain("BlobImageRender", _harness.EventNames);
        }

        [Fact]
        public async Task A_password_protected_pdf_is_refused_for_its_password()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.PasswordProtected(), BlobContentTypes.Pdf));

            Assert.Equal(0, _rasterizer.Calls);
            FakeLogRecord failed = Assert.Single(_harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobImageFailed");
            Assert.Contains("password", failed.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_pdf_encrypted_under_an_empty_user_password_renders()
        {
            byte[]? thumbnail = await _harness.ThumbnailAsync(DocumentFixtures.EncryptedWithEmptyUserPassword(), BlobContentTypes.Pdf);

            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal((256, 128), (output.Width, output.Height));
            Assert.True(ImageInspector.Near(SKColors.Black, output.At(64, 64)), $"Found {output.At(64, 64)}: the content did not decrypt.");
            Assert.True(ImageInspector.Near(SKColors.White, output.At(192, 64)));
        }

        [Fact]
        public async Task A_multi_page_pdf_renders_page_one()
        {
            byte[]? thumbnail = await _harness.ThumbnailAsync(DocumentFixtures.TwoPages(), BlobContentTypes.Pdf);

            // Page 1 is landscape and half black; page 2 would be portrait and all black.
            DecodedOutput output = ImageInspector.Decode(thumbnail!);
            Assert.Equal((256, 128), (output.Width, output.Height));
            Assert.True(ImageInspector.Near(SKColors.White, output.At(192, 64)));
        }

        /// <summary>Counts renders and delegates them to PDFium.</summary>
        private sealed class RecordingRasterizer : IPdfRasterizer
        {
            private int _calls;

            /// <summary>How many renders were asked for.</summary>
            public int Calls => Volatile.Read(ref _calls);

            /// <inheritdoc />
            public SKBitmap RenderFirstPage(GatedReadStream input, int size)
            {
                Interlocked.Increment(ref _calls);
                return PdfiumRasterizer.Instance.RenderFirstPage(input, size);
            }
        }
    }
}
