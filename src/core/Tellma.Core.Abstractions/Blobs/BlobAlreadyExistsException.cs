// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     A create-only write met an object that already exists under the same name. The existing
    ///     object is left untouched.
    /// </summary>
    /// <remarks>
    ///     Storage keys are random, so this signals a bug or a replayed write rather than a race any
    ///     caller should expect. Like <see cref="BlobStoreException" />, it is never mapped to a
    ///     client-facing error.
    /// </remarks>
    /// <param name="name">The name of the existing object.</param>
    public sealed class BlobAlreadyExistsException(string name)
        : Exception($"A blob named '{name}' already exists; blob writes are create-only.")
    {
        /// <summary>The name of the existing object.</summary>
        public string Name { get; } = name;
    }
}
