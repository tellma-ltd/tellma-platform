// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="StackContributionItem" />s, in feature order, for the tenant model, which
    ///     maps each entity's leaf. The same entity contributed again by the same feature is recorded
    ///     once.
    /// </summary>
    /// <remarks>
    ///     The model needs only the entity; the stack the item also declares, its service and its
    ///     operations, is recorded with it for the stack registry to build from.
    /// </remarks>
    internal sealed class StackContributionRealizer : IContributionRealizer<StackContributionItem>
    {
        /// <inheritdoc />
        public void Realize(StackContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            Type? entityType = item.EntityType;
            if (entityType is null || !entityType.IsClass || entityType.ContainsGenericParameters)
            {
                string described = entityType is null ? "no entity type" : TypeDisplay.Qualified(entityType);
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the entity {described}, which is not a closed class.",
                    "Contribute an entity as its closed class: contribution.Entity<TEntity>()."));
                return;
            }

            bool duplicate = context.Record.Entities.Exists(existing =>
                existing.EntityType == entityType
                && existing.ServiceType == item.ServiceType
                && existing.Operations == item.Operations
                && ReferenceEquals(existing.Owner, owner));

            if (!duplicate)
            {
                context.Record.Entities.Add(new ContributedEntity(entityType, item.ServiceType, item.Operations, owner));
            }
        }
    }
}
