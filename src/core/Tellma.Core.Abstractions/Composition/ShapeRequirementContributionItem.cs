// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A shape a feature requires of another feature's entity: the leaf class registered for
    ///     <see cref="EntityType" /> must implement the interface <see cref="ShapeType" />.
    /// </summary>
    /// <remarks>
    ///     This is how a pack adds columns to an entity it does not own. The pack declares an
    ///     interface carrying the members it needs and requires it of the owner's entity; the
    ///     distribution implements the interface on its leaf. A leaf without the shape fails startup,
    ///     and the reported remedy lists the interface's members to add.
    /// </remarks>
    /// <param name="EntityType">The entity whose registered leaf must carry the shape.</param>
    /// <param name="ShapeType">The interface the leaf must implement.</param>
    public sealed record ShapeRequirementContributionItem(Type EntityType, Type ShapeType) : FeatureContributionItem;
}
