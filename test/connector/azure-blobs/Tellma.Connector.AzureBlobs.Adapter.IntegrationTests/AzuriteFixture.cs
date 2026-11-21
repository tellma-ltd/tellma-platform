// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Storage.Blobs;
using Testcontainers.Azurite;

[assembly: AssemblyFixture(typeof(Tellma.Connector.AzureBlobs.IntegrationTests.AzuriteFixture))]

namespace Tellma.Connector.AzureBlobs.IntegrationTests
{
    /// <summary>
    ///     The Azurite emulator every case of the run shares: an existing one the developer names, or
    ///     a container started once. Without Docker on Windows the suite is skipped, naming why; on
    ///     any other machine a missing emulator fails the run, so CI never passes by skipping.
    /// </summary>
    public sealed class AzuriteFixture : IAsyncLifetime
    {
        /// <summary>The variable naming an existing emulator's connection string, instead of a container.</summary>
        public const string ConnectionStringVariable = "TELLMA_TEST_AZURITE";

        /// <summary>The variable overriding the emulator's image.</summary>
        public const string ImageVariable = "TELLMA_TEST_AZURITE_IMAGE";

        /// <summary>The emulator's image, pinned so a run is reproducible.</summary>
        public const string DefaultImage = "mcr.microsoft.com/azure-storage/azurite:3.38.0";

        private AzuriteContainer? _container;

        /// <summary>Why the suite is skipped on this machine; null when the emulator is running.</summary>
        public string? SkipReason { get; private set; }

        /// <summary>The emulator's connection string.</summary>
        /// <exception cref="InvalidOperationException">The fixture is not initialized, or the emulator is not running.</exception>
        public string ConnectionString
        {
            get => field ?? throw new InvalidOperationException("The Azurite fixture is not initialized, or the emulator is not running.");
            private set;
        }

        /// <summary>A client of the emulator's blob service, for a case to inspect storage behind the store's back.</summary>
        /// <returns>The client.</returns>
        public BlobServiceClient CreateServiceClient()
        {
            return new BlobServiceClient(ConnectionString);
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // --- An existing emulator the developer named ---
            string? named = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            if (!string.IsNullOrWhiteSpace(named))
            {
                ConnectionString = named;
                return;
            }

            // --- Otherwise a container. The emulator lags the client's newest service version, so
            // its version check is off: the suite pins the store's behaviour, not the emulator's ---
            string image = Environment.GetEnvironmentVariable(ImageVariable) ?? DefaultImage;
            try
            {
                // Both Build (Docker endpoint detection) and StartAsync fail when Docker is absent or stopped.
                _container = new AzuriteBuilder(image).WithInMemoryPersistence().WithCommand("--skipApiVersionCheck").Build();
                await _container.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows())
            {
                // A Windows machine without Docker runs every other suite; this one says why it did not run.
                SkipReason = "The Azure blob store suite needs the Azurite emulator: start Docker (for Testcontainers), or set "
                    + $"{ConnectionStringVariable} to a running emulator's connection string. Starting the container failed: {exception.Message}";
                return;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The Azure blob store suite requires Docker (for Testcontainers) and could not start the Azurite container. Install and "
                        + $"start Docker, or set {ConnectionStringVariable} to a running emulator's connection string. Underlying error: {exception.Message}",
                    exception);
            }

            ConnectionString = _container.GetConnectionString();
        }

        /// <summary>Skips the running case when the emulator is not running on this machine.</summary>
        public void SkipWithoutEmulator()
        {
            Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
