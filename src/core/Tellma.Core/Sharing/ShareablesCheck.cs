// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §5.1's startup checks, against the built model: the share entity has exactly one
    // foreign-key navigation to the shareable entity, it is neither a stack nor a child collection,
    // and the shareable's stack is served by a service derived from ShareableService<TEntity, TShare>.
    // The owner is server-owned: the spec has it [Derived], but a derived column is in every SET list
    // and no column update may assign one, while an update must keep the stored owner and transfer
    // must assign it, which is exactly server ownership.
    /// <summary>
    ///     <c>core.shareables</c>: every shareable entity's pair is whole: its owner is server-owned,
    ///     its share entity references it once and is neither a stack nor a child collection, and its
    ///     stack's service derives from the shareable service over the pair.
    /// </summary>
    /// <param name="record">The realised composition, whose shareables and stacks are checked.</param>
    internal sealed class ShareablesCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Shareables;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            if (record.Shareables.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            IEntityMetadataProvider metadata = services.GetRequiredService<IEntityMetadataProvider>();
            foreach (ContributedShareable shareable in record.Shareables)
            {
                Check(shareable, metadata, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Checks one pair.</summary>
        /// <param name="shareable">The pair.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="problems">Where the problems are reported.</param>
        private void Check(ContributedShareable shareable, IEntityMetadataProvider metadata, List<CompositionProblem> problems)
        {
            string described = $"The shareable {TypeDisplay.Qualified(shareable.EntityType)} of the feature '{shareable.Owner.Name}'";
            EntityMetadata? entity = Find(metadata, shareable.EntityType);
            EntityMetadata? share = Find(metadata, shareable.ShareType);
            if (entity is null || share is null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} or its share entity {TypeDisplay.Qualified(shareable.ShareType)} is not in the model.",
                    "Contribute the shareable entity as a stack (contribution.Entity<TEntity, TService>()); the share entity is mapped by the shareable declaration."));
                return;
            }

            // --- The owner, kept by every save and assigned only by a transfer ---
            PropertyMetadata? owner = entity.Properties.FirstOrDefault(static property => property.Name == nameof(IShareable.OwnerId));
            if (owner is not { Ownership: PropertyOwnership.ServerOwned })
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} declares its OwnerId as {owner?.Ownership.ToString() ?? "unmapped"}, not server-owned, so a save could change its owner or a transfer could not assign one.",
                    $"Mark {TypeDisplay.Short(shareable.EntityType)}.OwnerId [ServerOwned]."));
            }

            // --- Exactly one reference from a grant to its row ---
            int references = ShareableShape.RowReferences(entity, share).Count;
            if (references != 1)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is shared through {share.Name}, which has {references} foreign keys to {entity.Name}; a grant references its row through exactly one.",
                    $"Keep exactly one property of {TypeDisplay.Short(shareable.ShareType)} marked [References(typeof({TypeDisplay.Short(shareable.EntityType)}))]."));
            }

            // --- A share entity is a table beside the row: no stack and no child collection ---
            if (record.Entities.Exists(stack => record.LeafOf(stack.EntityType) == share.ClrType))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is shared through {share.Name}, which a feature also contributes as a stack; grants are written only by the sharing actions.",
                    $"Remove the stack of {TypeDisplay.Short(shareable.ShareType)}."));
            }

            if (metadata.All.FirstOrDefault(parent => parent.Children.Any(child => child.Child.ClrType == share.ClrType)) is EntityMetadata parent)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is shared through {share.Name}, which is a child collection of {parent.Name}; grants are saved by the sharing actions, never with a row.",
                    $"Give {TypeDisplay.Short(shareable.ShareType)} an [References] to its row instead of a [ParentKey], and remove the collection."));
            }

            // --- The shareable's stack, served by the shareable service over the pair ---
            ContributedEntity? stack = record.Entities.Find(candidate => record.LeafOf(candidate.EntityType) == entity.ClrType);
            if (stack is null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} has no stack.",
                    $"Contribute its stack with a service derived from ShareableService<{TypeDisplay.Short(shareable.EntityType)}, {TypeDisplay.Short(shareable.ShareType)}>."));
            }
            else if (!ShareableShape.DerivesFrom(stack.ServiceType, typeof(ShareableService<,>), shareable.EntityType, shareable.ShareType))
            {
                string service = stack.ServiceType is null ? "the platform's default service" : TypeDisplay.Qualified(stack.ServiceType);
                problems.Add(new CompositionProblem(
                    Name,
                    $"{described} is served by {service}, which does not derive from ShareableService<{TypeDisplay.Short(shareable.EntityType)}, {TypeDisplay.Short(shareable.ShareType)}>, so its owners and standard rows go unguarded.",
                    $"Derive the stack's service from ShareableService<{TypeDisplay.Short(shareable.EntityType)}, {TypeDisplay.Short(shareable.ShareType)}> and declare the share, publish and transfer actions over IShareCommands."));
            }
        }

        /// <summary>The metadata of an entity class, or null when the model has none.</summary>
        /// <param name="metadata">The model's entity metadata.</param>
        /// <param name="type">The class.</param>
        /// <returns>The metadata, or null.</returns>
        private static EntityMetadata? Find(IEntityMetadataProvider metadata, Type type)
        {
            try
            {
                return metadata.Get(type);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
