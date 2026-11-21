// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>One blob kind: its final policy, the column that references it, and the securable that governs it.</summary>
    /// <param name="Kind">The kind, such as <c>user-image</c>.</param>
    /// <param name="Policy">
    ///     The kind's policy: the attribute's preset, narrowed by the attribute, then transformed by
    ///     the features' and the host's blob-kind transforms in that order.
    /// </param>
    /// <param name="OwnerResource">
    ///     The securable resource whose <c>Read</c> grant governs a download: the stack's resource for
    ///     a root's column; for a child's column, the child's own resource when the child is a query
    ///     root of its own, else the stack's.
    /// </param>
    /// <param name="OwnerEntity">The entity that carries the column, such as <c>core.User</c>.</param>
    /// <param name="OwnerTable">The table of <paramref name="OwnerEntity" />.</param>
    /// <param name="OwnerColumn">The blob-reference column, such as <c>ImageId</c>.</param>
    /// <param name="OwnerPath">
    ///     The dotted to-one path from <paramref name="OwnerEntity" /> to the entity
    ///     <paramref name="OwnerResource" /> names: empty when they coincide, <c>Invoice</c> for a
    ///     child, <c>Line.Invoice</c> for a grandchild.
    /// </param>
    public sealed record BlobKindDescriptor(
        string Kind,
        BlobKindPolicy Policy,
        string OwnerResource,
        string OwnerEntity,
        TableName OwnerTable,
        string OwnerColumn,
        string OwnerPath);
}
