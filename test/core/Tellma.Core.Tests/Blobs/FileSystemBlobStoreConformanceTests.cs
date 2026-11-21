// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>The shared blob-store contract, run against the file-system store in a temporary directory.</summary>
    public class FileSystemBlobStoreConformanceTests : BlobStoreConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IBlobStoreHarness> CreateHarnessAsync()
        {
            return ValueTask.FromResult<IBlobStoreHarness>(new FileSystemBlobStoreHarness());
        }
    }
}
