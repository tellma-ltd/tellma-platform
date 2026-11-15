// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="StackCompanionContributionItem" />s, in realisation order, for the stack
    ///     composition, which closes each companion over its stack's leaf and registers it. The same
    ///     companion attached again to the same entity is recorded once.
    /// </summary>
    /// <remarks>
    ///     Only the item's shape is judged here; whether a stack owns the entity, and whether the
    ///     companion declares an action that fits it, are the startup gate's to report.
    /// </remarks>
    internal sealed class StackCompanionContributionRealizer : IContributionRealizer<StackCompanionContributionItem>
    {
        /// <inheritdoc />
        public void Realize(StackCompanionContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The shape of the item ---
            if (item.EntityType is not { IsClass: true, ContainsGenericParameters: false } entityType)
            {
                string described = item.EntityType is null ? "no entity" : TypeDisplay.Qualified(item.EntityType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' attaches a stack companion to {described}, which is not a closed entity class.",
                    "Attach a companion to an entity's closed class: contribution.EntityCompanion<TEntity, TCompanion>()."));
                return;
            }

            if (item.CompanionType is not { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } companionType)
            {
                string described = item.CompanionType is null ? "no companion type" : TypeDisplay.Qualified(item.CompanionType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' attaches the stack companion {described} to {TypeDisplay.Qualified(entityType)}, which is not a concrete, closed class.",
                    "Attach a companion as a concrete class closed over the entity it is written against: contribution.EntityCompanion<Center, CenterOperations<Center>>()."));
                return;
            }

            // --- Recorded once per entity and class ---
            bool duplicate = context.Record.Companions.Exists(existing => existing.EntityType == entityType && existing.CompanionType == companionType);
            if (!duplicate)
            {
                context.Record.Companions.Add(new ContributedCompanion(entityType, companionType, owner));
            }
        }
    }
}
