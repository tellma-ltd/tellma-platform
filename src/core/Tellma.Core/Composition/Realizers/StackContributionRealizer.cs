// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="StackContributionItem" />s, in feature order, for the tenant model, which
    ///     maps each entity's leaf, and for the stack composition, which closes each stack's service
    ///     over the leaf and registers it. The same entity contributed again by the same feature with
    ///     the same service and operations is recorded once.
    /// </summary>
    /// <remarks>
    ///     Only the item's shape is judged here: a closed entity class, a concrete closed service class
    ///     when one is named, and operations the platform defines. Which registration of an entity
    ///     wins, and whether the service fits the leaf, are the startup gate's to report.
    /// </remarks>
    internal sealed class StackContributionRealizer : IContributionRealizer<StackContributionItem>
    {
        /// <summary>Every operation flag the platform defines.</summary>
        private const StackOperations Defined = StackOperations.All | StackOperations.Enlist;

        /// <inheritdoc />
        public void Realize(StackContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The entity ---
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

            // --- The service, when one is named ---
            if (item.ServiceType is Type serviceType && (!serviceType.IsClass || serviceType.IsAbstract || serviceType.ContainsGenericParameters))
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the entity {TypeDisplay.Qualified(entityType)} with the service {TypeDisplay.Qualified(serviceType)}, which is not a concrete, closed class.",
                    "Name a concrete service class closed over the entity: contribution.Entity<Center, CenterService<Center>>()."));
                return;
            }

            // --- The operations, when they override the entity's ---
            if (item.Operations is StackOperations operations && (operations & ~Defined) != 0)
            {
                context.Report(new CompositionProblem(
                    CompositionNames.Source,
                    $"Feature '{owner.Name}' contributes the entity {TypeDisplay.Qualified(entityType)} with the operations {(int)operations}, which include flags the platform does not define.",
                    "Combine the StackOperations values: Query, Details, Save, Delete, DeleteByQuery, Import, Export and Enlist."));
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
