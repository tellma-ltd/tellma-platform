// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: every frame the platform opens is a host writes enlist with. The checks every
    // enlistment passes are the same whatever the frame: the frame admits enlistment now (its phase),
    // the writer declared the pair and the registry knows it, the writer takes part in the frame, and
    // a pair onto the writer's own stack comes only from an open frame its own API action opened.
    // Each refusal is an InvalidOperationException, an authoring bug. An admitted write becomes a
    // group of the frame, composed around the frame's own statements in enlistment order.
    /// <summary>A frame writes enlist with: a pipeline operation, an enlisted group, or an open frame.</summary>
    internal abstract class EnlistmentFrame : IWriteHost, IEnlistmentHost
    {
        /// <summary>Guards the groups.</summary>
        private readonly Lock _gate = new();

        /// <summary>The groups enlisted with this frame directly, in enlistment order.</summary>
        private readonly List<IEnlistedGroup> _groups = [];

        /// <inheritdoc />
        public abstract WriteHostKind Kind { get; }

        /// <inheritdoc />
        public abstract string Operation { get; }

        /// <inheritdoc />
        public abstract IConfirmations Confirmations { get; }

        /// <summary>The rounds of the frame's host, shared with every group enlisted inside it.</summary>
        /// <exception cref="InvalidOperationException">The frame's rounds have not started.</exception>
        public abstract EnlistmentRounds Rounds { get; }

        /// <summary>The errors the groups enlisted with this frame re-home theirs into.</summary>
        public abstract ValidationErrors Errors { get; }

        /// <summary>Who the frame's writes are recorded as coming from, unless an enlistment names its own.</summary>
        public abstract SaveSource Source { get; }

        /// <summary>Whether the frame is composing, so a front-door write from inside it is refused.</summary>
        public abstract bool IsComposing { get; }

        /// <summary>The groups enlisted with this frame directly, in enlistment order.</summary>
        public IReadOnlyList<IEnlistedGroup> Groups
        {
            get
            {
                lock (_gate)
                {
                    return [.. _groups];
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(options);

            // --- The pair, the participant and the phase; then the group, recorded in call order ---
            EnlistmentSite site = Admit(writer, typeof(TTarget), options.Order, options.MapPath, options.Source);
            IReadOnlyList<object?> boxed = rows as IReadOnlyList<object?> ?? throw new InvalidOperationException(
                $"The rows enlisted with {Operation} are of {typeof(TTarget).Name}, a value type; a stack's rows are entities.");
            IEnlistedSave group = EnlistmentBridge.Of(site.Target).Save(site, boxed, options);
            Add(group);

            // --- The target's shape and preprocessing now; the ids now or once the next round ran ---
            await group.EnlistAsync().ConfigureAwait(false);
            return new EnlistedSave<TTarget>(rows, site.Target.Resource);
        }

        /// <inheritdoc />
        public EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
            where TTarget : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);

            // --- The pair, the participant and the phase; then the group, recorded in call order ---
            EnlistmentSite site = Admit(writer, typeof(TTarget), options.Order, options.MapPath, options.Source);
            IEnlistedDelete group = EnlistmentBridge.Of(site.Target).Delete(site, ids);
            Add(group);
            group.Enlist();
            return new EnlistedDelete<TKey>(ids, site.Target.Resource);
        }

        // Spec 0015 §13.3, group ordering: the BeforeHost groups precede the host's emitter and the
        // AfterHost groups follow its effects, each in enlistment order. A group's own enlistments nest
        // around it the same way, so A enlisting B BeforeHost, and B enlisting C AfterHost, composes
        // B, C, A.
        /// <summary>Appends the groups of one order around the frame's own statements, each with its own groups around it.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="order">The groups' order: those ahead of the frame's statements, or those after them.</param>
        /// <param name="first">Whether this is the batch's first composition.</param>
        /// <returns>A task that completes when the groups are composed.</returns>
        public async ValueTask ComposeGroupsAsync(IDataBatch batch, EnlistOrder order, bool first)
        {
            ArgumentNullException.ThrowIfNull(batch);
            foreach (IEnlistedGroup group in Groups)
            {
                if (group.Order != order)
                {
                    continue;
                }

                await group.Frame.ComposeGroupsAsync(batch, EnlistOrder.BeforeHost, first).ConfigureAwait(false);
                await group.ComposeAsync(batch, first).ConfigureAwait(false);
                await group.Frame.ComposeGroupsAsync(batch, EnlistOrder.AfterHost, first).ConfigureAwait(false);
            }
        }

        /// <summary>Every group nested in the frame, in the order the persist batch composes them.</summary>
        /// <returns>The groups.</returns>
        public IEnumerable<IEnlistedGroup> Composed()
        {
            return ComposedAround(EnlistOrder.BeforeHost).Concat(ComposedAround(EnlistOrder.AfterHost));
        }

        // Spec 0015 §13.2 and §13.3: once the host committed, each group's post-commit phase runs after
        // the host's own, in the order the batch composed the groups, each failure contained.
        /// <summary>Runs the post-commit phase of every group nested in the frame, in composition order.</summary>
        /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
        /// <returns>A task that completes when every group's participants returned.</returns>
        public async Task AfterGroupsCommitAsync(BatchOutcome? committed)
        {
            foreach (IEnlistedGroup group in Composed())
            {
                await group.AfterCommitAsync(committed).ConfigureAwait(false);
            }
        }

        /// <summary>Refuses an enlistment the frame's phase does not admit now.</summary>
        /// <param name="writer">The writer's class, which the refusal names.</param>
        /// <exception cref="InvalidOperationException">The frame admits no enlistment now.</exception>
        protected abstract void EnsureAdmits(Type writer);

        /// <summary>Whether the frame admits a pair onto the writer's own stack: only an open frame its own API action opened.</summary>
        /// <param name="target">The target stack, the writer's own.</param>
        /// <returns>True when it does.</returns>
        protected virtual bool AdmitsSelfPair(StackDescriptor target)
        {
            return false;
        }

        /// <summary>The groups of one order nested in the frame, each with its own groups around it, in composition order.</summary>
        /// <param name="order">The order.</param>
        /// <returns>The groups.</returns>
        private IEnumerable<IEnlistedGroup> ComposedAround(EnlistOrder order)
        {
            foreach (IEnlistedGroup group in Groups.Where(group => group.Order == order))
            {
                foreach (IEnlistedGroup before in group.Frame.ComposedAround(EnlistOrder.BeforeHost))
                {
                    yield return before;
                }

                yield return group;
                foreach (IEnlistedGroup after in group.Frame.ComposedAround(EnlistOrder.AfterHost))
                {
                    yield return after;
                }
            }
        }

        // Spec 0015 §13.3, the pair gate: the receiver's IEnlists<TTarget> is the compile-time half;
        // the run-time half finds the pair among the registry's, so a class the composition never
        // registered enlists nothing. The writer must take part in the frame, and a self-pair (the
        // writer's own stack) comes only from an open frame its own API action opened: from a pipeline
        // frame it would re-enter the stack whose hooks are running.
        /// <summary>Admits an enlistment, or refuses it.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="target">The entity type the writer enlists.</param>
        /// <param name="order">Where the group sits around the host's statements.</param>
        /// <param name="map">The enlistment's path map; null maps every path to the host's root.</param>
        /// <param name="source">The enlistment's source; null for the frame's.</param>
        /// <returns>What the group needs of the frame.</returns>
        /// <exception cref="InvalidOperationException">The enlistment is refused.</exception>
        private EnlistmentSite Admit(object writer, Type target, EnlistOrder order, Func<ValidationPath, ValidationPath>? map, SaveSource? source)
        {
            Type type = writer.GetType();

            // --- The phase ---
            EnsureAdmits(type);

            // --- The pair, from the registry ---
            StackRegistry registry = Rounds.Services.Registry;
            EnlistmentDescriptor? pair = null;
            StackDescriptor? stack = null;
            foreach (EnlistmentDescriptor declared in registry.Enlistments)
            {
                if (declared.WriterKey.IsAssignableFrom(type) && registry.Find(declared.TargetResource) is StackDescriptor candidate && target.IsAssignableFrom(candidate.EntityType))
                {
                    pair = declared;
                    stack = candidate;
                    break;
                }
            }

            if (pair is null || stack is null)
            {
                throw new InvalidOperationException(
                    $"The class {type.Name} enlisted a write to {target.Name} with {Operation}, but the registry holds no such pair. Register the writer with the composition (as a stack's service, companion or component, a routed service or a handler) and declare IEnlists<{target.Name}> on it, for a stack that accepts enlisted writes.");
            }

            // --- The participant ---
            if (!Rounds.IsParticipant(type))
            {
                throw new InvalidOperationException(
                    $"The class {type.Name} enlisted a write to {stack.Resource} with {Operation}, but it takes no part in that frame. Enlist from the frame's own service, its components, or the method, handler or step the frame was opened for.");
            }

            // --- The self-pair, from an open frame its own API action opened only ---
            bool selfPair = registry.StackOfWriter(type) is StackDescriptor own && string.Equals(own.Resource, stack.Resource, StringComparison.Ordinal);
            return selfPair && !AdmitsSelfPair(stack)
                ? throw new InvalidOperationException(
                    $"The class {type.Name} enlisted a write to its own stack {stack.Resource} with {Operation}. A stack writes its own rows through a self-pair only from the open frame of one of its own [ApiAction] methods; from its hooks it changes its own payload instead.")
                : new EnlistmentSite(this, stack, pair, order, map, source ?? Source);
        }

        /// <summary>Records a group with the frame and with its host's rounds, in call order.</summary>
        /// <param name="group">The group.</param>
        private void Add(IEnlistedGroup group)
        {
            lock (_gate)
            {
                _groups.Add(group);
            }

            Rounds.Add(group);
        }
    }

    /// <summary>What an enlisted group needs of the frame it was enlisted with.</summary>
    /// <param name="Host">The frame the write was enlisted with.</param>
    /// <param name="Target">The target stack.</param>
    /// <param name="Pair">The declared pair, which the target's contexts carry as their enlistment.</param>
    /// <param name="Order">Where the group sits around the host's statements.</param>
    /// <param name="MapPath">Maps a path of the target's payload onto the host's; null maps every path to the host's root.</param>
    /// <param name="Source">Who the write is recorded as coming from.</param>
    internal sealed record EnlistmentSite(
        EnlistmentFrame Host,
        StackDescriptor Target,
        EnlistmentDescriptor Pair,
        EnlistOrder Order,
        Func<ValidationPath, ValidationPath>? MapPath,
        SaveSource Source);

    /// <summary>An enlisted save, whose admitted stages run when it is enlisted.</summary>
    internal interface IEnlistedSave : IEnlistedGroup
    {
        /// <summary>Runs the save's stages up to its ids: the shape, preprocessing, and the ids at once or in the host's next round.</summary>
        /// <returns>A task that completes once the rows carry their final ids.</returns>
        Task EnlistAsync();
    }

    /// <summary>An enlisted delete, whose admitted stages run when it is enlisted.</summary>
    internal interface IEnlistedDelete : IEnlistedGroup
    {
        /// <summary>Runs the delete's stages that need no round: the claim, and its validators' start when the target validates deletes.</summary>
        void Enlist();
    }
}
