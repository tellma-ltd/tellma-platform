// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>One object for <see cref="IBlobStore.WriteAsync" /> to write.</summary>
    /// <remarks>
    ///     The store reads <see cref="Content" /> from its current position and never disposes it;
    ///     the caller owns the stream.
    /// </remarks>
    /// <param name="Name">The object's name; it must satisfy <see cref="BlobName.IsValid" />.</param>
    /// <param name="ContentType">The object's media type, which a store that keeps metadata stores
    ///     with it; the blob row stays the authority on what is served.</param>
    /// <param name="Content">The bytes, read once from the current position to the end.</param>
    /// <param name="Length">The exact number of bytes <paramref name="Content" /> yields; not negative.</param>
    public sealed record BlobWrite(string Name, string ContentType, Stream Content, long Length);
}
