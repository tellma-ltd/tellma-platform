// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Crud;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Records <see cref="ValidatorContributionItem" />s, in realisation order, for the stack
    ///     composition, which closes each component over the leaf of every stack it applies to and
    ///     registers it. The same component attached again to the same entity is recorded once.
    /// </summary>
    /// <remarks>
    ///     The item's shape is judged here: an entity class, a defined kind, and a concrete component
    ///     that is either closed and implements the kind's contract for the entity, or an open
    ///     definition of exactly one type parameter. How it closes over each leaf is the startup
    ///     gate's to report.
    /// </remarks>
    internal sealed class ValidatorContributionRealizer : IContributionRealizer<ValidatorContributionItem>
    {
        /// <inheritdoc />
        public void Realize(ValidatorContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // --- The entity and the kind ---
            if (item.EntityType is not { IsClass: true, ContainsGenericParameters: false } entityType)
            {
                string described = item.EntityType is null ? "no entity" : TypeDisplay.Qualified(item.EntityType);
                Report(
                    context,
                    $"Feature '{owner.Name}' attaches a component to {described}, which is not a closed entity class.",
                    "Attach a component to an entity's closed class: contribution.Validator<TEntity, TValidator>().");
                return;
            }

            if (!Enum.IsDefined(item.Kind))
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' attaches a component of the undefined kind {(int)item.Kind} to {TypeDisplay.Qualified(entityType)}.",
                    "Use Validator, PersistEffect, DetailsContributor or NamedFilterContributor.");
                return;
            }

            // --- The component class: concrete, and closed or open over exactly one parameter ---
            Type contract = ComponentContracts.For(item.Kind, entityType);
            string kind = ComponentContracts.Describe(item.Kind);
            if (item.ComponentType is not { IsClass: true, IsAbstract: false } componentType)
            {
                string described = item.ComponentType is null ? "no component type" : TypeDisplay.Qualified(item.ComponentType);
                Report(
                    context,
                    $"Feature '{owner.Name}' attaches the {kind} {described} to {TypeDisplay.Qualified(entityType)}, which is not a concrete class.",
                    $"Attach a concrete class implementing {TypeDisplay.Short(contract)}.");
                return;
            }

            if (componentType.IsGenericTypeDefinition)
            {
                int parameters = componentType.GetGenericArguments().Length;
                if (parameters != 1)
                {
                    Report(
                        context,
                        $"Feature '{owner.Name}' attaches the open {kind} {TypeDisplay.Qualified(componentType)} to {TypeDisplay.Qualified(entityType)}, which has {parameters} type parameters; the platform closes a definition over the stack's leaf alone.",
                        "Give the component one type parameter, the entity, constrained to what it needs of the leaf.");
                    return;
                }
            }
            else if (componentType.ContainsGenericParameters)
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' attaches the {kind} {TypeDisplay.Qualified(componentType)} to {TypeDisplay.Qualified(entityType)}, which is neither closed nor an open definition.",
                    "Attach the component closed over the entity, or by its open definition: typeof(Rules<>).");
                return;
            }
            else if (!contract.IsAssignableFrom(componentType))
            {
                Report(
                    context,
                    $"Feature '{owner.Name}' attaches the {kind} {TypeDisplay.Qualified(componentType)} to {TypeDisplay.Qualified(entityType)}, but it does not implement {TypeDisplay.Short(contract)}.",
                    $"Implement {TypeDisplay.Short(contract)}, or attach the component to the entity it is written against.");
                return;
            }

            // --- Recorded once per entity, class and kind ---
            bool duplicate = context.Record.Components.Exists(existing =>
                existing.EntityType == entityType && existing.ComponentType == componentType && existing.Kind == item.Kind);
            if (!duplicate)
            {
                context.Record.Components.Add(new ContributedComponent(entityType, componentType, item.Kind, owner));
            }
        }

        /// <summary>Reports a problem of a component item.</summary>
        /// <param name="context">Where the problem is reported.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">The remedy.</param>
        private static void Report(RealizationContext context, string problem, string fix)
        {
            context.Report(new CompositionProblem(CompositionNames.Source, problem, fix));
        }
    }
}
