// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Collections;
using System.Reflection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Internals
{
    /// <summary>
    ///     No native Skia object outlives a call: every codec, bitmap, image, surface, pixmap and
    ///     data block the processor creates is disposed on success, on refusal and on failure.
    /// </summary>
    /// <remarks>
    ///     SkiaSharp registers every live native wrapper in an internal handle dictionary and
    ///     removes it on disposal. Counting its entries before and after a batch of calls, without
    ///     forcing a collection in between, sees an undisposed wrapper as a leak; a collection could
    ///     only hide a leak, never invent one. The suite runs alone, so no other suite's objects
    ///     move the count.
    /// </remarks>
    [Collection(SerialSuites.Name)]
    public sealed class NativeHandleTests
    {
        [Fact]
        public async Task Processing_and_thumbnails_leave_no_native_object_behind()
        {
            using var harness = new ProcessorHarness();
            byte[] photo = ImageFixtures.WithExif(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 300, 200), 6, "Make");
            byte[] gif = ImageFixtures.Gif(16, 16, [[0], [1]]);
            byte[] bomb = ImageFixtures.DeclaringSize(SKEncodedImageFormat.Png, 10_000, 6_000);
            byte[] truncated = ImageFixtures.TruncatedJpeg(0.5);
            byte[] unreadable = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[256]];
            byte[] webp = ImageFixtures.AnimatedWebP(32, 32, [SKColors.Red, SKColors.Blue]);
            byte[] bigImagePdf = DocumentFixtures.DeclaringImage(20_000, 20_000, "FlateDecode");
            byte[] pdf = DocumentFixtures.HalfBlackPage(200, 100);
            byte[] encrypted = DocumentFixtures.PasswordProtected();

            async Task RoundAsync()
            {
                await harness.ProcessAsync(photo, BlobKindPolicy.Avatar.Image!, BlobKindPolicy.Avatar.Thumbnail);
                await harness.ProcessAsync(gif, BlobKindPolicy.Signature.Image!);
                await Assert.ThrowsAsync<ImageRejectedException>(() => harness.ProcessAsync(bomb, BlobKindPolicy.Photo.Image!));
                await Assert.ThrowsAsync<ImageRejectedException>(() => harness.ProcessAsync(truncated, BlobKindPolicy.Photo.Image!));
                await Assert.ThrowsAsync<ImageRejectedException>(() => harness.ProcessAsync(unreadable, BlobKindPolicy.Photo.Image!));
                await harness.ProcessAsync(webp, BlobKindPolicy.Photo.Image!, BlobKindPolicy.Photo.Thumbnail);
                Assert.Null(await harness.ThumbnailAsync(bigImagePdf, BlobContentTypes.Pdf));
                Assert.NotNull(await harness.ThumbnailAsync(photo, BlobContentTypes.Jpeg));
                Assert.NotNull(await harness.ThumbnailAsync(pdf, BlobContentTypes.Pdf));
                Assert.Null(await harness.ThumbnailAsync(encrypted, BlobContentTypes.Pdf));
                Assert.Null(await harness.ThumbnailAsync(bomb, BlobContentTypes.Png));
            }

            // A first round creates the shared singletons (the sRGB colour space and the like).
            await RoundAsync();
            int before = LiveNativeObjects();

            for (int i = 0; i < 5; i++)
            {
                await RoundAsync();
            }

            Assert.Equal(before, LiveNativeObjects());
        }

        [Fact]
        public async Task A_thumbnail_abandoned_mid_decode_leaves_no_native_object_behind()
        {
            var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            using var harness = new ProcessorHarness(time);
            byte[] png = ImageFixtures.Noise(512, 512, SKEncodedImageFormat.Png);
            CancellationToken token = TestContext.Current.CancellationToken;

            // A first, completed render creates the shared singletons.
            Assert.NotNull(await harness.ThumbnailAsync(png, BlobContentTypes.Png));
            int before = LiveNativeObjects();

            using var input = new BlockingStream(png, blockAfter: 16 * 1024);
            Task<byte[]?> thumbnail = harness.Processor.ThumbnailAsync(
                ProcessorHarness.Kind, input, BlobContentTypes.Png, new ThumbnailPolicy(64), TimeSpan.FromSeconds(5), token);
            Assert.True(input.Blocked.Wait(TimeSpan.FromSeconds(30), token));
            var advanced = Task.Run(() => time.Advance(TimeSpan.FromSeconds(5)), token);
            await Task.Delay(100, token);
            input.Release.Set();
            await advanced.WaitAsync(TimeSpan.FromSeconds(30), token);
            Assert.Null(await thumbnail.WaitAsync(TimeSpan.FromSeconds(30), token));

            // The abandoned decode has ended once its slot is back; nothing it made may survive it.
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (harness.Processor.FreeImageSlots != 4)
            {
                Assert.True(DateTime.UtcNow < deadline, "The abandoned decode did not end.");
                await Task.Delay(20, token);
            }

            Assert.Equal(before, LiveNativeObjects());
        }

        /// <summary>The number of SkiaSharp wrappers currently registered as live.</summary>
        /// <returns>The count.</returns>
        private static int LiveNativeObjects()
        {
            Type dictionary = typeof(SKObject).Assembly.GetType("SkiaSharp.HandleDictionary")
                ?? throw new InvalidOperationException("SkiaSharp no longer has its HandleDictionary; update this probe.");
            FieldInfo instances = dictionary.GetField("instances", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("SkiaSharp's HandleDictionary has no 'instances' field; update this probe.");
            // Read without SkiaSharp's own lock: the suite runs alone, so nothing registers meanwhile.
            return ((ICollection)instances.GetValue(null)!).Count;
        }
    }
}
