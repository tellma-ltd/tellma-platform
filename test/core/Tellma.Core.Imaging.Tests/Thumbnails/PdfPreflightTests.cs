// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Testing;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Thumbnails
{
    /// <summary>
    ///     The worker's walk of page 1 before PDFium renders it, end to end: an image declaring too
    ///     many pixels (fewer for JPEG 2000 and JBIG2), an oversized page and an oversized file each
    ///     yield no thumbnail, refused rather than failed; honest PDFs, an encrypted one that needs
    ///     no password, and a multi-page one still render page 1.
    /// </summary>
    public sealed class PdfPreflightTests : IDisposable
    {
        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        public static TheoryData<string, byte[], string> Refused => new()
        {
            { "a 20000 x 20000 flate image", DocumentFixtures.DeclaringImage(20_000, 20_000, "FlateDecode"), "20000 x 20000" },
            { "a 5000 x 5000 JPEG 2000 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JPXDecode"), "5000 x 5000" },
            { "a 5000 x 5000 JBIG2 image", DocumentFixtures.DeclaringImage(5_000, 5_000, "JBIG2Decode"), "5000 x 5000" },
            { "a page wider than 14400 points", DocumentFixtures.HalfBlackPage(20_000, 100), "points" },
        };

        [Theory]
        [MemberData(nameof(Refused))]
        public async Task A_page_over_a_cap_yields_no_thumbnail(string name, byte[] pdf, string detail)
        {
            Assert.True(await _harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf) is null, name);

            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
            FakeLogRecord failed = Assert.Single(_harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobImageFailed");
            Assert.Contains(detail, failed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("BlobPdfWorkerFailed", _harness.EventNames);
        }

        [Fact]
        public async Task An_image_within_the_cap_renders_through_the_worker()
        {
            Assert.NotNull(await _harness.ThumbnailAsync(DocumentFixtures.DeclaringImage(4_000, 4_000, "FlateDecode"), BlobContentTypes.Pdf));
            Assert.Empty(_harness.FailureReasons);
        }

        [Fact]
        public async Task A_file_over_the_input_cap_is_not_read_at_all()
        {
            byte[] pdf = DocumentFixtures.LongerThan(WorkerProtocol.MaxInputBytes);

            Assert.Null(await _harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf));
            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
            Assert.DoesNotContain("BlobImageRender", _harness.EventNames);
        }

        [Fact]
        public async Task A_thumbnail_larger_than_the_worker_renders_is_not_asked_for()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf, size: WorkerProtocol.MaxThumbnailSize + 1));
            Assert.Equal([ImageRejectedException.DecodeFailedReason], _harness.FailureReasons);
            Assert.DoesNotContain("BlobImageRender", _harness.EventNames);
        }

        [Fact]
        public async Task A_password_protected_pdf_is_refused_for_its_password()
        {
            Assert.Null(await _harness.ThumbnailAsync(DocumentFixtures.PasswordProtected(), BlobContentTypes.Pdf));

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
    }
}
