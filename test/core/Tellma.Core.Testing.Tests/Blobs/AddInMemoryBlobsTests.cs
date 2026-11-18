// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.Testing.Tests.Blobs
{
    /// <summary>The registration replaces whatever store and processor came before, and the stub scripts its refusals.</summary>
    public class AddInMemoryBlobsTests
    {
        [Fact]
        public async Task Replaces_any_registered_store_and_processor()
        {
            ServiceCollection services = new();
            services.AddSingleton<IBlobStore>(new InMemoryBlobStore());
            services.AddSingleton<IImageProcessor>(new StubImageProcessor());

            InMemoryBlobStore store = services.AddInMemoryBlobs();

            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IBlobStore));
            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IImageProcessor));
            await using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Same(store, provider.GetRequiredService<IBlobStore>());
            Assert.Same(provider.GetRequiredService<StubImageProcessor>(), provider.GetRequiredService<IImageProcessor>());
        }

        [Fact]
        public async Task The_stub_returns_the_input_sized_to_the_policy_and_scripts_its_refusals()
        {
            StubImageProcessor processor = new();
            ImagePolicy policy = new(400, ImageFit.Contain, ImageFormat.Png);
            CancellationToken token = TestContext.Current.CancellationToken;

            ProcessedImage processed = await processor.ProcessAsync("avatar", [1, 2, 3], policy, new ThumbnailPolicy(96), TimeSpan.FromSeconds(1), token);
            Assert.Equal([1, 2, 3], processed.Primary);
            Assert.Equal((400, 400), (processed.Width, processed.Height));
            Assert.Equal(BlobContentTypes.Png, processed.ContentType);
            Assert.Equal(StubImageProcessor.ThumbnailBytes, processed.Thumbnail);

            processor.RejectReason = ImageRejectedException.DecodeFailedReason;
            ImageRejectedException rejected = await Assert.ThrowsAsync<ImageRejectedException>(
                () => processor.ProcessAsync("avatar", [1], policy, null, TimeSpan.FromSeconds(1), token));
            Assert.Equal(ImageRejectedException.DecodeFailedReason, rejected.Reason);

            processor.Busy = true;
            _ = await Assert.ThrowsAsync<ImageProcessingBusyException>(
                () => processor.ProcessAsync("avatar", [1], policy, null, TimeSpan.FromSeconds(1), token));

            processor.RenderThumbnails = false;
            Assert.Null(await processor.ThumbnailAsync("attachment", new MemoryStream([1]), BlobContentTypes.Pdf, new ThumbnailPolicy(96), TimeSpan.FromSeconds(1), token));
            Assert.Equal(3, processor.ProcessCalls);
            Assert.Equal(1, processor.ThumbnailCalls);
        }
    }
}
