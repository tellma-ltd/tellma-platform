// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     A <see cref="FileSystemBlobStore" /> over a fresh temporary directory, with the
    ///     out-of-band access the conformance suite needs. Disposal deletes the directory.
    /// </summary>
    internal sealed class FileSystemBlobStoreHarness : IBlobStoreHarness
    {
        // The real factory, because MetricCollector matches on the meter's scope.
        private readonly ServiceProvider _metricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <summary>Creates the store over a fresh directory under the system temporary directory.</summary>
        /// <param name="logger">The store's logger; a null logger when omitted.</param>
        public FileSystemBlobStoreHarness(ILogger<FileSystemBlobStore>? logger = null)
        {
            RootPath = Path.Combine(Path.GetTempPath(), "tellma-blobs-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            MeterFactory = _metricsProvider.GetRequiredService<IMeterFactory>();
            FileSystemStore = new FileSystemBlobStore(
                Options.Create(new FileSystemBlobStoreOptions { RootPath = RootPath }),
                new BlobStoreMetrics(MeterFactory),
                logger ?? NullLogger<FileSystemBlobStore>.Instance);
        }

        /// <summary>The store's root directory, which the store itself does not create.</summary>
        public string RootPath { get; }

        /// <summary>The meter factory the store's instruments are created from.</summary>
        public IMeterFactory MeterFactory { get; }

        /// <summary>The store, typed as itself.</summary>
        public FileSystemBlobStore FileSystemStore { get; }

        /// <inheritdoc />
        public IBlobStore Store => FileSystemStore;

        /// <inheritdoc />
        public int TenantId => 1;

        /// <inheritdoc />
        public int OtherTenantId => 2;

        /// <inheritdoc />
        public BlobStoreCapabilities Capabilities => BlobStoreCapabilities.IncompleteArtefacts;

        /// <summary>The directory of a tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The directory.</returns>
        public string TenantDirectory(int tenantId)
        {
            return Path.Combine(RootPath, "t" + tenantId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The temporary directory of a tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The directory.</returns>
        public string TemporaryDirectory(int tenantId)
        {
            return Path.Combine(TenantDirectory(tenantId), FileSystemBlobStore.TemporaryDirectoryName);
        }

        /// <summary>The path an object's name maps to.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="name">The object's name.</param>
        /// <returns>The path.</returns>
        public string ObjectPath(int tenantId, string name)
        {
            return Path.Combine([TenantDirectory(tenantId), .. name.Split('/')]);
        }

        /// <inheritdoc />
        public ValueTask<bool> TenantStorageExistsAsync(int tenantId)
        {
            return ValueTask.FromResult(Directory.Exists(TenantDirectory(tenantId)));
        }

        /// <inheritdoc />
        public ValueTask RemoveTenantStorageAsync(int tenantId)
        {
            string directory = TenantDirectory(tenantId);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public async ValueTask<string> PlantIncompleteArtefactAsync(int tenantId, DateTimeOffset lastModified)
        {
            string temporary = TemporaryDirectory(tenantId);
            Directory.CreateDirectory(temporary);
            string path = Path.Combine(temporary, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + FileSystemBlobStore.PartExtension);
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            File.SetLastWriteTimeUtc(path, lastModified.UtcDateTime);
            return path;
        }

        /// <inheritdoc />
        public ValueTask<bool> IncompleteArtefactExistsAsync(int tenantId, string handle)
        {
            return ValueTask.FromResult(File.Exists(handle));
        }

        /// <inheritdoc />
        public ValueTask<int> CountIncompleteArtefactsAsync(int tenantId)
        {
            string temporary = TemporaryDirectory(tenantId);
            return ValueTask.FromResult(
                Directory.Exists(temporary) ? Directory.GetFiles(temporary, "*" + FileSystemBlobStore.PartExtension).Length : 0);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _metricsProvider.DisposeAsync();

            // A best-effort cleanup: an antivirus scanner may hold a file for a moment on Windows,
            // and a leftover temporary directory must not fail the case that ran in it.
            for (int attempt = 0; attempt < 5 && Directory.Exists(RootPath); attempt++)
            {
                try
                {
                    Directory.Delete(RootPath, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(100 * (attempt + 1));
                }
            }
        }
    }
}
