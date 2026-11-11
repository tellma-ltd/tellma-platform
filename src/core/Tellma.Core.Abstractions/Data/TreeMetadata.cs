// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>The tree columns of a <see cref="TreeEntity{TKey}" /> and the depth its statements cap recursion at.</summary>
    /// <param name="ParentKey">The self-referencing <c>ParentId</c> property.</param>
    /// <param name="NodeColumn">The shadow <c>hierarchyid</c> column holding each row's path; never a member of the class.</param>
    /// <param name="SubtreeCount">The <c>SubtreeCount</c> property, 1 for a leaf.</param>
    /// <param name="ActiveSubtreeCount">The <c>ActiveSubtreeCount</c> property of an activatable tree; null otherwise.</param>
    /// <param name="MaxDepth">The deepest a row may nest, counting a root as depth 1 (<see cref="TreeAttribute" />).</param>
    public sealed record TreeMetadata(
        PropertyMetadata ParentKey, string NodeColumn, PropertyMetadata SubtreeCount,
        PropertyMetadata? ActiveSubtreeCount, int MaxDepth);
}
