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
            byte[] pdf = DocumentFixtures.HalfBlackPage(200, 100);
            byte[] encrypted = DocumentFixtures.PasswordProtected();

            async Task RoundAsync()
            {
                await harness.ProcessAsync(photo, BlobKindPolicy.Avatar.Image!, BlobKindPolicy.Avatar.Thumbnail);
                await harness.ProcessAsync(gif, BlobKindPolicy.Signature.Image!);
                await Assert.ThrowsAsync<ImageRejectedException>(() => harness.ProcessAsync(bomb, BlobKindPolicy.Photo.Image!));
                await Assert.ThrowsAsync<ImageRejectedException>(() => harness.ProcessAsync(truncated, BlobKindPolicy.Photo.Image!));
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
