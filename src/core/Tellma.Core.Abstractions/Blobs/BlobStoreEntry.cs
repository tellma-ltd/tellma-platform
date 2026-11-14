// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>One object reported by <see cref="IBlobStore.ListAsync" />.</summary>
    /// <param name="Name">The object's name; it satisfies <see cref="BlobName.IsValid" />.</param>
    /// <param name="Length">The object's length in bytes.</param>
    /// <param name="LastModified">When the object was last written, in UTC.</param>
    public sealed record BlobStoreEntry(string Name, long Length, DateTimeOffset LastModified);
}
