// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One child collection of an entity, saved with its owner.</summary>
    /// <param name="Name">The collection's name: <c>Contacts</c>.</param>
    /// <param name="Json">The collection's wire name: <c>contacts</c>.</param>
    /// <param name="Entity">The child entity's name.</param>
    /// <param name="ParentKey">The child's property holding its owner's id.</param>
    /// <param name="MaxCount">The most children one owner may have.</param>
    /// <param name="LabelKey">The resource key of the collection's label.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    public sealed record ClientChildMetadata(
        string Name,
        string Json,
        string Entity,
        string ParentKey,
        int MaxCount,
        string LabelKey,
        string? LabelFallback);
}
