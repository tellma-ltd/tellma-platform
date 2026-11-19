// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     Every blob kind of the realised composition, each with its final policy and the owner column
    ///     that references it. Built once at startup from the <c>[BlobReference]</c> properties on the
    ///     stacks' roots and the child entities of their aggregates; a singleton.
    /// </summary>
    public interface IBlobKindRegistry
    {
        /// <summary>Every kind, in the order the stacks and their child collections declare them.</summary>
        IReadOnlyList<BlobKindDescriptor> All { get; }

        /// <summary>Finds a kind by its name, which is case-sensitive.</summary>
        /// <param name="kind">The kind, such as <c>user-image</c>.</param>
        /// <returns>The kind, or null when no blob reference declares it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="kind" /> is null.</exception>
        BlobKindDescriptor? Find(string kind);
    }
}
