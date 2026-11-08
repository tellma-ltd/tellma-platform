// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="ShapeRequirementContributionItem" />s for the <c>core.entity-shapes</c>
    ///     startup check, which compares each with the leaf registered for its entity.
    /// </summary>
    internal sealed class ShapeRequirementContributionRealizer : IContributionRealizer<ShapeRequirementContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ShapeRequirementContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            if (item.EntityType is null || item.ShapeType is null)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' requires a shape without naming both the entity and the shape.",
                    "Require a shape with contribution.RequiresShape<TEntity, TShape>()."));
                return;
            }

            // The shape is the members a leaf must carry, so only a closed interface can express it.
            if (!item.ShapeType.IsInterface || item.ShapeType.ContainsGenericParameters)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' requires the shape {TypeDisplay.Qualified(item.ShapeType)} of {TypeDisplay.Qualified(item.EntityType)}, but a shape must be a closed interface.",
                    $"Declare the members the feature needs on an interface, and require it with RequiresShape<{TypeDisplay.Short(item.EntityType)}, TShape>()."));
                return;
            }

            bool duplicate = context.Record.ShapeRequirements.Exists(existing =>
                existing.EntityType == item.EntityType && existing.ShapeType == item.ShapeType && ReferenceEquals(existing.Owner, owner));

            if (!duplicate)
            {
                context.Record.ShapeRequirements.Add(new ShapeRequirement(item.EntityType, item.ShapeType, owner));
            }
        }
    }
}
