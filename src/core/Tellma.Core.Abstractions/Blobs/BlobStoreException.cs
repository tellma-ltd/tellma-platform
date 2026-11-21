// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>A blob store failed to perform an operation.</summary>
    /// <remarks>
    ///     An infrastructure failure, thrown by store implementations and never mapped to a
    ///     client-facing error: a request that meets it fails as an internal error with a trace id.
    ///     That is why it derives from <see cref="Exception" /> rather than from the platform's
    ///     client-facing exception base.
    /// </remarks>
    /// <param name="message">What failed, naming the operation and, where there is one, the object.</param>
    /// <param name="inner">The storage's own failure, when there is one.</param>
    public sealed class BlobStoreException(string message, Exception? inner) : Exception(message, inner);
}
