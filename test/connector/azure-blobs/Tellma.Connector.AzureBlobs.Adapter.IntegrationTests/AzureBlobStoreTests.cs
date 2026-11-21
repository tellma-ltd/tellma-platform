// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Connector.AzureBlobs.IntegrationTests
{
    /// <summary>
    ///     What the Azure blob store adds to the blob-store contract, on the Azurite emulator: the
    ///     per-tenant container and its name, a private container ensured once, the service's own
    ///     create-only refusal, the length guard on a content that cannot seek, and the store
    ///     instruments it shares with the runtime.
    /// </summary>
    /// <param name="azurite">The emulator.</param>
    [Trait("Category", "Integration")]
    public sealed class AzureBlobStoreTests(AzuriteFixture azurite)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_tenant_s_blobs_live_in_the_container_named_by_the_prefix_and_the_tenant_id()
        {
            await using AzureBlobStoreHarness harness = Harness();
            string name = BlobStoreConformanceTests.NewName();

            await harness.Store.WriteAsync(
                harness.TenantId, [new BlobWrite(name, "image/png", new MemoryStream([1, 2, 3], writable: false), 3)], Token);

            // --- The container is {prefix}t{tenantId}, and the object keeps its name and media type ---
            BlobContainerClient container = harness.Container(harness.TenantId);
            Assert.Equal(harness.Prefix + "t42", container.Name);
            BlobProperties properties = (await container.GetBlobClient(name).GetPropertiesAsync(cancellationToken: Token)).Value;
            Assert.Equal("image/png", properties.ContentType);
            Assert.Equal(3, properties.ContentLength);
            Assert.False(await harness.TenantStorageExistsAsync(harness.OtherTenantId));
        }

        [Fact]
        public async Task Ensuring_a_tenant_creates_its_container_private_and_a_second_ensure_changes_nothing()
        {
            await using AzureBlobStoreHarness harness = Harness();

            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            string name = BlobStoreConformanceTests.NewName();
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [7])], Token);
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);

            // No anonymous access: blobs are reached only through the platform's authorised download.
            BlobContainerAccessPolicy policy = (await harness.Container(harness.TenantId).GetAccessPolicyAsync(cancellationToken: Token)).Value;
            Assert.Equal(PublicAccessType.None, policy.BlobPublicAccess);
            Assert.True(await harness.Container(harness.TenantId).GetBlobClient(name).ExistsAsync(Token));
        }

        [Fact]
        public async Task A_name_another_writer_created_is_refused_by_the_service_as_BlobAlreadyExistsException()
        {
            await using AzureBlobStoreHarness harness = Harness();
            string name = BlobStoreConformanceTests.NewName();

            // --- Created behind the store's back, so only the service's create-only condition can refuse it ---
            await harness.Store.EnsureTenantAsync(harness.TenantId, Token);
            _ = await harness.Container(harness.TenantId).GetBlobClient(name).UploadAsync(BinaryData.FromString("theirs"), Token);

            BlobAlreadyExistsException conflict = await Assert.ThrowsAsync<BlobAlreadyExistsException>(
                () => harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, BlobStoreConformanceTests.Bytes("mine"))], Token));

            Assert.Equal(name, conflict.Name);
            BlobDownloadResult kept = (await harness.Container(harness.TenantId).GetBlobClient(name).DownloadContentAsync(Token)).Value;
            Assert.Equal("theirs", kept.Content.ToString());
        }

        [Theory]
        [InlineData(10, 9)]
        [InlineData(10, 11)]
        [InlineData(0, 1)]
        public async Task A_content_that_cannot_seek_and_does_not_match_its_declared_length_fails_and_leaves_no_object(int declared, int actual)
        {
            await using AzureBlobStoreHarness harness = Harness();
            string name = BlobStoreConformanceTests.NewName();
            BlobWrite write = new(name, BlobStoreConformanceTests.ContentType, new ForwardOnlyStream(new byte[actual]), declared);

            _ = await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.WriteAsync(harness.TenantId, [write], Token));

            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, name, Token));
        }

        [Fact]
        public async Task A_content_that_cannot_seek_is_written_whole_when_it_matches_its_declared_length()
        {
            await using AzureBlobStoreHarness harness = Harness();
            string name = BlobStoreConformanceTests.NewName();
            byte[] bytes = [.. Enumerable.Range(0, 70_000).Select(static i => (byte)(i % 251))];

            await harness.Store.WriteAsync(
                harness.TenantId, [new BlobWrite(name, BlobStoreConformanceTests.ContentType, new ForwardOnlyStream(bytes), bytes.Length)], Token);

            await using BlobContent? content = await harness.Store.OpenReadAsync(harness.TenantId, name, Token);
            Assert.NotNull(content);
            using MemoryStream read = new();
            await content.Content.CopyToAsync(read, Token);
            Assert.Equal(bytes, read.ToArray());
        }

        [Fact]
        public async Task Listing_skips_an_object_whose_name_is_outside_the_grammar()
        {
            await using AzureBlobStoreHarness harness = Harness();
            string name = BlobStoreConformanceTests.NewName("ab");
            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1])], Token);
            _ = await harness.Container(harness.TenantId).GetBlobClient(BlobStoreConformanceTests.Kind + "/ab/stray").UploadAsync(BinaryData.FromString("x"), Token);

            List<string> names = [];
            await foreach (BlobStoreEntry entry in harness.Store.ListAsync(harness.TenantId, BlobStoreConformanceTests.Kind, Token))
            {
                names.Add(entry.Name);
            }

            Assert.Equal([name], names);
        }

        [Fact]
        public async Task The_store_measures_its_calls_on_the_shared_store_instruments()
        {
            await using AzureBlobStoreHarness harness = Harness();
            IMeterFactory meters = harness.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> operations = new(meters, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreOperations);
            using MetricCollector<double> duration = new(meters, BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreDuration);
            string name = BlobStoreConformanceTests.NewName();

            await harness.Store.WriteAsync(harness.TenantId, [BlobStoreConformanceTests.Write(name, [1, 2])], Token);
            await (await harness.Store.OpenReadAsync(harness.TenantId, name, Token))!.DisposeAsync();
            Assert.Null(await harness.Store.OpenReadAsync(harness.TenantId, BlobStoreConformanceTests.NewName(), Token));

            // --- One measurement per call, by operation and outcome; the write's duration carries its size class ---
            Assert.Equal(
                [
                    (BlobTelemetryTagValues.StoreWrite, BlobTelemetryTagValues.StoreOk),
                    (BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreOk),
                    (BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreNotFound),
                ],
                operations.GetMeasurementSnapshot().Select(static measurement =>
                    ((string)measurement.Tags[BlobTelemetryNames.OperationTag]!, (string)measurement.Tags[BlobTelemetryNames.OutcomeTag]!)));
            CollectedMeasurement<double> write = duration.GetMeasurementSnapshot()[0];
            Assert.Equal(BlobTelemetryTagValues.SizeUnder1MiB, write.Tags[BlobTelemetryNames.SizeClassTag]);
        }

        /// <summary>A store on the emulator, or a skip when this machine runs none.</summary>
        /// <returns>The harness.</returns>
        private AzureBlobStoreHarness Harness()
        {
            azurite.SkipWithoutEmulator();
            return new AzureBlobStoreHarness(azurite);
        }

        /// <summary>A readable stream over bytes that cannot seek and knows no length, as a network body.</summary>
        /// <param name="bytes">The bytes.</param>
        private sealed class ForwardOnlyStream(byte[] bytes) : Stream
        {
            private readonly MemoryStream _inner = new(bytes, writable: false);

            /// <inheritdoc />
            public override bool CanRead => true;

            /// <inheritdoc />
            public override bool CanSeek => false;

            /// <inheritdoc />
            public override bool CanWrite => false;

            /// <inheritdoc />
            public override long Length => throw new NotSupportedException();

            /// <inheritdoc />
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Flush()
            {
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
