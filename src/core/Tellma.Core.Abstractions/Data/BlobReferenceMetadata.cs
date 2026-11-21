// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>A blob-reference column of an entity (<see cref="BlobReferenceAttribute" />), with its kind and policy.</summary>
    /// <param name="Property">The <c>int?</c> property holding the blob's id.</param>
    /// <param name="Kind">The blob kind the column references.</param>
    /// <param name="Policy">The kind's policy: the declaration's preset, narrowed by the declaration.</param>
    public sealed record BlobReferenceMetadata(PropertyMetadata Property, string Kind, BlobKindPolicy Policy);
}
