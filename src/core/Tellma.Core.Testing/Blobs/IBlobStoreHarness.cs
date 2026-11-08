// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Testing.Blobs
{
    /// <summary>Which conformance cases a store can express on its own storage.</summary>
    [Flags]
    public enum BlobStoreCapabilities
    {
        /// <summary>Nothing beyond the contract every store meets.</summary>
        None = 0,

        /// <summary>
        ///     The store leaves incomplete artefacts (temporary files) that
        ///     <see cref="IBlobStore.PurgeIncompleteAsync" /> removes, and the harness can plant them.
        ///     A store whose writes are atomic on the service side has none.
        /// </summary>
        IncompleteArtefacts = 1,
    }

    /// <summary>
    ///     A blob store under conformance test, plus the out-of-band access to its storage the suite
    ///     needs to observe what the contract alone cannot: whether a tenant's container or
    ///     directory exists, removing one behind the store's back, and planting incomplete artefacts.
    /// </summary>
    /// <remarks>
    ///     A harness owns its storage. Each one gives the suite tenants no other harness uses, so
    ///     cases may run in parallel against one shared emulator, and its disposal removes what the
    ///     case left behind.
    /// </remarks>
    public interface IBlobStoreHarness : IAsyncDisposable
    {
        /// <summary>The store under test.</summary>
        IBlobStore Store { get; }

        /// <summary>A tenant whose storage no other harness touches; it may or may not exist yet.</summary>
        int TenantId { get; }

        /// <summary>A second such tenant, distinct from <see cref="TenantId" />.</summary>
        int OtherTenantId { get; }

        /// <summary>Which optional cases this store supports.</summary>
        BlobStoreCapabilities Capabilities { get; }

        /// <summary>Whether the tenant's container or directory exists, observed without the store.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>True when it exists.</returns>
        ValueTask<bool> TenantStorageExistsAsync(int tenantId);

        /// <summary>Removes the tenant's container or directory and everything in it, behind the store's back.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>A task that completes when the storage is gone.</returns>
        ValueTask RemoveTenantStorageAsync(int tenantId);

        /// <summary>
        ///     Plants one incomplete artefact in the tenant's storage, as a crashed write would leave
        ///     it, last modified at <paramref name="lastModified" />. Called only when
        ///     <see cref="Capabilities" /> has <see cref="BlobStoreCapabilities.IncompleteArtefacts" />.
        /// </summary>
        /// <param name="tenantId">The tenant, whose storage exists.</param>
        /// <param name="lastModified">The artefact's modification time.</param>
        /// <returns>A handle that names the artefact to <see cref="IncompleteArtefactExistsAsync" />.</returns>
        ValueTask<string> PlantIncompleteArtefactAsync(int tenantId, DateTimeOffset lastModified);

        /// <summary>Whether a planted artefact still exists.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handle">The handle <see cref="PlantIncompleteArtefactAsync" /> returned.</param>
        /// <returns>True when it still exists.</returns>
        ValueTask<bool> IncompleteArtefactExistsAsync(int tenantId, string handle);

        /// <summary>How many incomplete artefacts the tenant's storage holds, planted or left by a write.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The count; 0 for a store without incomplete artefacts.</returns>
        ValueTask<int> CountIncompleteArtefactsAsync(int tenantId);
    }
}
