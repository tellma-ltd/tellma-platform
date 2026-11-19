// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One navigation of an entity: a path step to another entity's row.</summary>
    /// <param name="Name">The navigation's name, as paths spell it: <c>Parent</c>.</param>
    /// <param name="Json">The navigation's wire name: <c>parent</c>.</param>
    /// <param name="ForeignKey">The property holding the target's id, such as <c>ParentId</c>; a sibling's is the entity's own key.</param>
    /// <param name="Target">The target entity's name, such as <c>gl.Center</c>.</param>
    /// <param name="Kind">Whether it is a plain reference, a child's link to its owner, or a sibling.</param>
    /// <param name="Nullable">Whether the navigation may reach no row.</param>
    /// <param name="LabelKey">The resource key of the navigation's label.</param>
    /// <param name="LabelFallback">The humanized English a client shows when no resource declares <paramref name="LabelKey" />; null, and omitted, when one does.</param>
    public sealed record ClientNavigationMetadata(
        string Name,
        string Json,
        string ForeignKey,
        string Target,
        ClientNavigationKind Kind,
        bool Nullable,
        string LabelKey,
        string? LabelFallback);
}
