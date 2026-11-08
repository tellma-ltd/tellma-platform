// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.entity-shapes</c>: the leaf registered for each entity implements every shape
    ///     another feature requires of it. The remedy lists the interface's members, so the fix is a
    ///     copy away.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class EntityShapesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.EntityShapes;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];
            foreach (ShapeRequirement requirement in record.ShapeRequirements)
            {
                Type leaf = record.LeafOf(requirement.EntityType);
                if (requirement.ShapeType.IsAssignableFrom(leaf))
                {
                    continue;
                }

                string entity = TypeDisplay.Short(requirement.EntityType);
                string shape = TypeDisplay.Short(requirement.ShapeType);
                string members = string.Join("; ", TypeDisplay.RequiredMembers(requirement.ShapeType));
                string membersClause = members.Length == 0 ? string.Empty : $" {shape} requires: {members}.";

                // Without a substitution the leaf is the pack's own default, which the distribution
                // cannot edit; the remedy is a leaf of its own.
                string fix = leaf == requirement.EntityType
                    ? $"Derive a leaf from {entity} that implements {shape}, and register it with UseEntity<{entity}, TLeaf>().{membersClause}"
                    : $"Implement {shape} on {TypeDisplay.Short(leaf)}.{membersClause}";

                problems.Add(new CompositionProblem(
                    Name,
                    $"The feature '{requirement.Owner.Name}' requires the entity {TypeDisplay.Qualified(requirement.EntityType)} to implement {TypeDisplay.Qualified(requirement.ShapeType)}, but its leaf {TypeDisplay.Qualified(leaf)} does not.",
                    fix));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
