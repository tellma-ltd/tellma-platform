// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Connector.AzureBlobs.IntegrationTests
{
    /// <summary>
    ///     One case's Azure blob store on the shared emulator, composed as a host composes it, under a
    ///     container prefix of its own so cases running side by side never share a container. Disposing
    ///     it deletes the containers it may have created.
    /// </summary>
    internal sealed class AzureBlobStoreHarness : IBlobStoreHarness
    {
        private readonly ServiceProvider _services;
        private readonly BlobServiceClient _inspector;

        /// <summary>Composes a store over the emulator.</summary>
        /// <param name="azurite">The emulator.</param>
        public AzureBlobStoreHarness(AzuriteFixture azurite)
        {
            ArgumentNullException.ThrowIfNull(azurite);

            // A fresh prefix per case: a letter, eight hex digits and a hyphen, which every tenant id extends validly.
            Prefix = "c" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8] + "-";
            ServiceCollection services = [];
            _ = services.AddLogging();
            services.AddAzureBlobStore(
                options =>
                {
                    options.ConnectionString = azurite.ConnectionString;
                    options.ContainerPrefix = Prefix;
                },
                credential: null);
            _services = services.BuildServiceProvider();
            Store = _services.GetRequiredService<IBlobStore>();
            _inspector = azurite.CreateServiceClient();
        }

        /// <summary>The case's container prefix.</summary>
        public string Prefix { get; }

        /// <summary>The composed services, for a case that reads the store's instruments.</summary>
        public IServiceProvider Services => _services;

        /// <inheritdoc />
        public IBlobStore Store { get; }

        /// <inheritdoc />
        public int TenantId => 42;

        /// <inheritdoc />
        public int OtherTenantId => 43;

        /// <summary>Uploads are atomic on the service, so nothing incomplete is left to purge.</summary>
        public BlobStoreCapabilities Capabilities => BlobStoreCapabilities.None;

        /// <summary>The container a tenant's blobs live in, as the emulator holds it.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The container's client.</returns>
        public BlobContainerClient Container(int tenantId)
        {
            return _inspector.GetBlobContainerClient(Prefix + "t" + tenantId.ToString(CultureInfo.InvariantCulture));
        }

        /// <inheritdoc />
        public async ValueTask<bool> TenantStorageExistsAsync(int tenantId)
        {
            return (await Container(tenantId).ExistsAsync(TestContext.Current.CancellationToken)).Value;
        }

        /// <inheritdoc />
        public async ValueTask RemoveTenantStorageAsync(int tenantId)
        {
            _ = await Container(tenantId).DeleteIfExistsAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>Never called: the store declares no incomplete artefacts.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="lastModified">When the artefact was last modified.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public ValueTask<string> PlantIncompleteArtefactAsync(int tenantId, DateTimeOffset lastModified)
        {
            throw new NotSupportedException("The Azure blob store leaves no incomplete artefact to plant.");
        }

        /// <summary>Never called: the store declares no incomplete artefacts.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handle">The artefact's handle.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public ValueTask<bool> IncompleteArtefactExistsAsync(int tenantId, string handle)
        {
            throw new NotSupportedException("The Azure blob store leaves no incomplete artefact to look for.");
        }

        /// <summary>
        ///     The blobs of a tenant's container that hold uncommitted blocks and no committed
        ///     content: what an upload that never completed leaves behind.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The count; zero when the container does not exist.</returns>
        public async ValueTask<int> CountIncompleteArtefactsAsync(int tenantId)
        {
            BlobContainerClient container = Container(tenantId);
            if (!(await container.ExistsAsync(TestContext.Current.CancellationToken)).Value)
            {
                return 0;
            }

            // A listing that includes uncommitted blobs, less the one that does not.
            HashSet<string> committed = new(StringComparer.Ordinal);
            await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: null, TestContext.Current.CancellationToken))
            {
                _ = committed.Add(item.Name);
            }

            int incomplete = 0;
            await foreach (BlobItem item in container.GetBlobsAsync(BlobTraits.None, BlobStates.Uncommitted, prefix: null, TestContext.Current.CancellationToken))
            {
                incomplete += committed.Contains(item.Name) ? 0 : 1;
            }

            return incomplete;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (int tenantId in new[] { TenantId, OtherTenantId })
            {
                _ = await Container(tenantId).DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
            }

            await _services.DisposeAsync();
        }
    }
}
