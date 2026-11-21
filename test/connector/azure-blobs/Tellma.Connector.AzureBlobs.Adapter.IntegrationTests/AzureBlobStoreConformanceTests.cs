// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Testing.Blobs;

namespace Tellma.Connector.AzureBlobs.IntegrationTests
{
    /// <summary>The blob-store contract, run against the Azure blob store on the Azurite emulator.</summary>
    /// <param name="azurite">The emulator.</param>
    [Trait("Category", "Integration")]
    public sealed class AzureBlobStoreConformanceTests(AzuriteFixture azurite) : BlobStoreConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IBlobStoreHarness> CreateHarnessAsync()
        {
            azurite.SkipWithoutEmulator();
            return ValueTask.FromResult<IBlobStoreHarness>(new AzureBlobStoreHarness(azurite));
        }
    }
}
