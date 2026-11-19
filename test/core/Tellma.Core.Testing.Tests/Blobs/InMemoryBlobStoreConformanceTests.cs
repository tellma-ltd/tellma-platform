// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.Testing.Tests.Blobs
{
    /// <summary>The in-memory store against the blob-store contract every store is held to.</summary>
    public class InMemoryBlobStoreConformanceTests : BlobStoreConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IBlobStoreHarness> CreateHarnessAsync()
        {
            return ValueTask.FromResult<IBlobStoreHarness>(new Harness());
        }

        /// <summary>A fresh store per case, so its two tenants are its own.</summary>
        private sealed class Harness : IBlobStoreHarness
        {
            // The concrete store, for the out-of-band access.
            private readonly InMemoryBlobStore _store = new();

            /// <inheritdoc />
            public IBlobStore Store => _store;

            /// <inheritdoc />
            public int TenantId => 1;

            /// <inheritdoc />
            public int OtherTenantId => 2;

            /// <inheritdoc />
            public BlobStoreCapabilities Capabilities => BlobStoreCapabilities.None;

            /// <inheritdoc />
            public ValueTask<bool> TenantStorageExistsAsync(int tenantId)
            {
                return ValueTask.FromResult(_store.TenantExists(tenantId));
            }

            /// <inheritdoc />
            public ValueTask RemoveTenantStorageAsync(int tenantId)
            {
                _store.RemoveTenant(tenantId);
                return ValueTask.CompletedTask;
            }

            /// <inheritdoc />
            public ValueTask<string> PlantIncompleteArtefactAsync(int tenantId, DateTimeOffset lastModified)
            {
                throw new NotSupportedException("The in-memory store's writes are atomic, so it has no incomplete artefacts.");
            }

            /// <inheritdoc />
            public ValueTask<bool> IncompleteArtefactExistsAsync(int tenantId, string handle)
            {
                throw new NotSupportedException("The in-memory store's writes are atomic, so it has no incomplete artefacts.");
            }

            /// <inheritdoc />
            public ValueTask<int> CountIncompleteArtefactsAsync(int tenantId)
            {
                return ValueTask.FromResult(0);
            }

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
