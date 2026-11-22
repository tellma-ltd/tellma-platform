// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Crud
{
    // A reference target's read is spec 0015 §7.4 (a save's references and visible loads) and §15
    // (a lookup of an import): a child entity without a read securable of its own is read under its
    // owner's decision, seen through its navigation to the owner's row, and a sensitive read asks for
    // the step-up a direct read asks for (spec 0014 §4.3).
    /// <summary>
    ///     The connected user's read of an entity that an operation reaches without reading it
    ///     directly: a save's reference targets and visible loads, and a row source's lookups. Every
    ///     decision is taken from the set the batch is composed under, which the batch's prologue
    ///     validates. Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="securables">The registered securables.</param>
    /// <param name="metadata">The tenant model's entity metadata, whose collections name each child's owner.</param>
    /// <param name="accessor">The scope's request context, whose assurance a sensitive read is held to.</param>
    /// <param name="stepUp">The assurance bar of the sensitive securables.</param>
    /// <param name="time">The clock the decisions are taken at.</param>
    internal sealed class TargetReads(
        AccessEvaluator evaluator,
        ISecurableRegistry securables,
        IEntityMetadataProvider metadata,
        IRequestContextAccessor accessor,
        StepUpBar stepUp,
        TimeProvider time)
    {
        /// <summary>The owner of each child entity, by the child's name; built on first use.</summary>
        private Dictionary<string, EntityOwner>? _owners;

        /// <summary>The connected user's decision on a securable.</summary>
        /// <param name="user">The connected user.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>The decision; a denial when the securable is not registered.</returns>
        public AccessDecision Decide(ConnectedUser user, string resource, string action)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(action);
            return securables.Find(resource, action) is SecurableDescriptor securable
                ? evaluator.DecideFrom(user.Access, securable, time.GetUtcNow())
                : evaluator.Unknown(resource, action);
        }

        /// <summary>
        ///     The connected user's read of an entity, as a reference to it or a lookup of it is
        ///     judged: its own read decision when a securable guards it, its owner's seen through the
        ///     navigation to the owner's row for a child entity without one, and no restriction for an
        ///     entity no grant can reach.
        /// </summary>
        /// <param name="user">The connected user.</param>
        /// <param name="target">The entity.</param>
        /// <returns>The read.</returns>
        /// <exception cref="InvalidOperationException">A child entity on the way declares no navigation through its parent key.</exception>
        public TargetRead Of(ConnectedUser user, EntityMetadata target)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(target);

            // --- A securable of its own: a stack's, or a child query surface's, whose owner the evaluator composes ---
            if (securables.ForResource(target.Name).Count > 0)
            {
                return new TargetRead(Decide(user, target.Name, AccessActions.Read), securables.Find(target.Name, AccessActions.Read));
            }

            // --- A child without one: its owner's read, the owner's filter rebased onto the child's navigation ---
            if (OwnerOf(target) is EntityOwner owner)
            {
                string navigation = owner.Navigation ?? throw new InvalidOperationException(
                    $"The child entity {target.Name} has no navigation through its parent key {owner.ParentKey.Name}, so a read of it cannot be judged under the read of {owner.Entity.Name}.");
                TargetRead above = Of(user, owner.Entity);
                AccessDecision decision = above.Decision;
                FilterTree? filter = decision.Filter is FilterTree held ? FilterTree.Via(navigation, held) : null;
                return above with { Decision = new AccessDecision(target.Name, AccessActions.Read, decision.Outcome, filter, decision.Grants, decision.Problems) };
            }

            // --- No grant reaches it ---
            return new TargetRead(new AccessDecision(target.Name, AccessActions.Read, AccessOutcome.Unrestricted, null, [], []), Securable: null);
        }

        /// <summary>The entity whose child collection holds a child entity, and the child's navigation to that entity's row.</summary>
        /// <param name="child">The child entity.</param>
        /// <returns>The owner; null for an entity no collection holds.</returns>
        public EntityOwner? OwnerOf(EntityMetadata child)
        {
            ArgumentNullException.ThrowIfNull(child);
            _owners ??= Owners(metadata);
            return _owners.GetValueOrDefault(child.Name);
        }

        /// <summary>Asks for the step-up a direct read of a sensitive securable asks for, of every caller but the platform's own work.</summary>
        /// <exception cref="StepUpRequiredException">The caller's sign-in is below the step-up bar.</exception>
        /// <exception cref="Abstractions.Errors.IndividualRequiredException">The caller is a service account, which cannot step up.</exception>
        public void EnsureStepUp()
        {
            RequestContext context = accessor.Current;
            if (!context.IsSystem)
            {
                AssuranceCheck.Ensure(context, stepUp.Requirement, time.GetUtcNow());
            }
        }

        /// <summary>Maps every child entity to the entity whose collection holds it.</summary>
        /// <param name="metadata">The tenant model's entity metadata.</param>
        /// <returns>The owners, by the child's name.</returns>
        private static Dictionary<string, EntityOwner> Owners(IEntityMetadataProvider metadata)
        {
            Dictionary<string, EntityOwner> owners = new(StringComparer.Ordinal);
            foreach (EntityMetadata entity in metadata.All)
            {
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    // A child's one parent key names one owner, whichever of the owner's collections holds it.
                    string? navigation = collection.Child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name)?.Navigation;
                    _ = owners.TryAdd(collection.Child.Name, new EntityOwner(entity, collection.ParentKey, navigation));
                }
            }

            return owners;
        }
    }

    /// <summary>The connected user's read of an entity, and the securable it was decided on.</summary>
    /// <param name="Decision">
    ///     The decision on the entity: its own, its owner's with the filter rebased onto it, or
    ///     unrestricted for an entity no grant can reach.
    /// </param>
    /// <param name="Securable">The read securable the decision was taken on, whose sensitivity asks for a step-up; null when no grant reaches the entity.</param>
    internal readonly record struct TargetRead(AccessDecision Decision, SecurableDescriptor? Securable)
    {
        /// <summary>Whether the caller reads no row of the entity, so every reference to it is not found.</summary>
        public bool IsDenied => Decision.Outcome == AccessOutcome.Denied;

        /// <summary>Whether the read is allowed under a sensitive securable, so it asks for the step-up a direct read asks for.</summary>
        public bool IsSensitive => !IsDenied && Securable is { IsSensitive: true };
    }

    /// <summary>The entity whose child collection holds a child entity, and how the child reaches that entity's row.</summary>
    /// <param name="Entity">The owning entity.</param>
    /// <param name="ParentKey">The child's parent key, which references the owner's row.</param>
    /// <param name="Navigation">The child's navigation through its parent key; null when the child declares none.</param>
    internal sealed record EntityOwner(EntityMetadata Entity, PropertyMetadata ParentKey, string? Navigation);
}
